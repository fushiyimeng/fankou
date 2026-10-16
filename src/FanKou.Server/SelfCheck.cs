using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using FanKou.Protocol;

namespace FanKou.Server;

/// <summary>
/// 联机这条链路的自检：起一个真的 Kestrel + 真的 WebSocket 连接，
/// 把"建房 → 加人机 → 开局 → 出牌 → 打完一局"走一遍。
///
/// 之所以放在服务端进程里跑：协议 DTO 就在隔壁，改字段时这里会跟着编译报错，
/// 不会出现"测试还按老协议发消息、线上却变了"那种事。
/// </summary>
public static class SelfCheck
{
    public static async Task<int> RunAsync()
    {
        var lines = new List<string>();
        var passed = true;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:18081");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseWebSockets();
        var server = new RoomServer(fast: true);

        app.Map("/ws", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await server.HandleAsync(socket, context.RequestAborted);
        });

        await app.StartAsync();

        try
        {
            passed &= await CheckAsync("单人 + 三家人机，能打完一局", lines, () => SingleHumanAsync());
            passed &= await CheckAsync("两个真人 + 两家人机，能打完一局", lines, () => TwoHumansAsync());
            passed &= await CheckAsync("换座位、指定座位加人机、观战位看不到手牌", lines, () => SeatsAndWatchersAsync());
            passed &= await CheckAsync("房号互不相同、加入不存在的房间会报错", lines, () => RoomCodesAsync());
        }
        finally
        {
            await app.StopAsync();
        }

        Console.WriteLine("联机自检");
        Console.WriteLine();
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine(passed ? "自检通过" : "自检失败");
        return passed ? 0 : 1;
    }

    private static async Task<bool> CheckAsync(string name, List<string> lines, Func<Task<string>> body)
    {
        try
        {
            lines.Add($"✅ {name}：{await body()}");
            return true;
        }
        catch (Exception ex)
        {
            lines.Add($"❌ {name}：{ex.Message}");
            return false;
        }
    }

    private static async Task<string> SingleHumanAsync()
    {
        await using var a = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "房主");

        var joined = await a.CreateRoomAsync();
        var code = joined.Code!;

        // 人机得指定座位（南被房主占了，剩下北 3 / 东 0 / 西 2）。
        foreach (var seat in new[] { 3, 0, 2 })
        {
            a.Send(new ClientMessage { T = "addBot", Seat = seat });
            await a.WaitForAsync("joined");
        }

        a.Send(new ClientMessage { T = "start" });

        var rounds = await PlayAsync(a, TimeSpan.FromSeconds(120));
        return $"房号 {code}，打完 {rounds} 局";
    }

    private static async Task<string> TwoHumansAsync()
    {
        await using var a = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "甲");
        await using var b = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "乙");

        var joined = await a.CreateRoomAsync();
        var code = joined.Code!;

        var second = await b.JoinRoomAsync(code);

        if (second.Seat == joined.Seat)
        {
            throw new InvalidOperationException("两个人被分到了同一个座位");
        }

        if (second.Seat is not (1 or 2 or 3))
        {
            throw new InvalidOperationException($"第二个人的座位是 {second.Seat}，应该是北/东/西之一");
        }

        // 两个真人占了南(1)和北(3)，剩下东 0 / 西 2 补人机。
        foreach (var seat in new[] { 0, 2 })
        {
            a.Send(new ClientMessage { T = "addBot", Seat = seat });
            await a.WaitForAsync("joined");
        }

        a.Send(new ClientMessage { T = "start" });

        var leak = 0;
        void Inspect(StateDto state)
        {
            if (!state.BottomRevealed && state.Bottom.Length > 0)
            {
                Interlocked.Exchange(ref leak, 1);
            }
        }

        // 两个人必须同时在跑：一个在等另一个的话，两边都收不到消息。
        var rounds = await Task.WhenAll(
            PlayAsync(a, TimeSpan.FromSeconds(120), Inspect),
            PlayAsync(b, TimeSpan.FromSeconds(120), Inspect));

        var roundsA = rounds[0];
        var roundsB = rounds[1];

        if (Volatile.Read(ref leak) == 1)
        {
            throw new InvalidOperationException("锅底还没翻开就发给客户端了");
        }

        return $"房号 {code}，甲坐 {SeatName(joined.Seat ?? -1)}、乙坐 {SeatName(second.Seat ?? -1)}，两边各打完 {Math.Min(roundsA, roundsB)} 局";
    }

    private static async Task<string> SeatsAndWatchersAsync()
    {
        await using var a = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "甲");
        await using var b = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "乙");
        await using var watcher = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "看客");

        var joined = await a.CreateRoomAsync();
        var code = joined.Code!;

        await b.JoinRoomAsync(code);
        await watcher.JoinRoomAsync(code, watch: true);

        if (watcher.Seat != -1 || watcher.WatchIndex < 0)
        {
            throw new InvalidOperationException($"观战的人不该坐到牌桌上（seat={watcher.Seat} watch={watcher.WatchIndex}）");
        }

        // 甲从南换到东（空位）
        a.Send(new ClientMessage { T = "switch", Seat = 0 });
        var moved = await a.WaitForAsync("joined");

        if (moved.Seat != 0)
        {
            throw new InvalidOperationException($"换到东之后座位是 {moved.Seat}");
        }

        // 房主把人机**加到指定的空位**（南=1、西=2）
        a.Send(new ClientMessage { T = "addBot", Seat = 1 });
        await a.WaitForAsync("joined");
        a.Send(new ClientMessage { T = "addBot", Seat = 2 });
        var withBots = await a.WaitForAsync("joined");

        if (withBots.Seats?.Count(s => s.Kind == "ai") != 2)
        {
            throw new InvalidOperationException("人机没有被加到指定的座位上");
        }

        a.Send(new ClientMessage { T = "start" });
        await a.WaitForAsync("room");

        // 观战的人收到的状态里，手牌和待决策都必须是空的
        var sawState = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var message = await watcher.ReceiveAsync(TimeSpan.FromSeconds(10));

            if (message?.T != "state" || message.State is not { } state)
            {
                continue;
            }

            sawState = true;

            if (state.Hand.Length != 0)
            {
                throw new InvalidOperationException($"观战的人收到了 {state.Hand.Length} 张手牌");
            }

            if (state.Pending is not null)
            {
                throw new InvalidOperationException("观战的人收到了待决策");
            }

            break;
        }

        if (!sawState)
        {
            throw new InvalidOperationException("观战的人一直没收到状态");
        }

        return $"甲从南换到东、人机指定加到南/西，看客坐观战位 {watcher.WatchIndex}，整局看不到任何手牌";
    }

    private static async Task<string> RoomCodesAsync()
    {
        await using var a = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "甲");
        await using var b = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "乙");
        await using var c = await Client.ConnectAsync("ws://127.0.0.1:18081/ws", "丙");

        var first = (await a.CreateRoomAsync()).Code!;
        var second = (await b.CreateRoomAsync()).Code!;

        if (first == second)
        {
            throw new InvalidOperationException($"两个房间拿到了同一个房号 {first}");
        }

        foreach (var code in new[] { first, second })
        {
            if (code.Length != 4 || !code.All(char.IsAsciiDigit))
            {
                throw new InvalidOperationException($"房号 {code} 不是 4 位数字");
            }
        }

        c.Send(new ClientMessage { T = "join", Code = "0001", Name = "丙" });
        var reply = await c.WaitForAsync("error");

        return $"两个房号 {first} / {second}，加入 0001 → 「{reply.Message}」";
    }

    /// <summary>一直打到出结算为止；轮到自己的每一步都替自己走。</summary>
    private static async Task<int> PlayAsync(Client client, TimeSpan timeout, Action<StateDto>? inspect = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var rounds = 0;
        StateDto? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var message = await client.ReceiveAsync(TimeSpan.FromSeconds(20));

            if (message is null)
            {
                break;
            }

            if (message.T == "error")
            {
                throw new InvalidOperationException($"服务端报错：{message.Message}");
            }

            if (message.T == "result")
            {
                rounds++;

                // 结算之后就告一段落，别把整盘 16 局都打完。
                return rounds;
            }

            if (message.T != "state" || message.State is not { } state)
            {
                continue;
            }

            last = state;
            inspect?.Invoke(state);

            if (state.Hand.Length == 0 && !state.BottomRevealed)
            {
                // 还没发到自己手上，正常。
            }

            switch (state.Pending?.Kind)
            {
                case "call":
                    client.Send(new ClientMessage { T = "call", Option = state.Pending.Options.Length - 1 });
                    break;

                case "bottom":
                {
                    // 明牌不能扣——真实界面拦着，自检这边也得自己长眼。
                    var locked = state.Pending.Locked.ToHashSet();
                    var picks = state.Pending.Pool.Where(id => !locked.Contains(id)).Take(8).ToArray();
                    client.Send(new ClientMessage { T = "bottom", Cards = picks });
                    break;
                }

                case "play" when state.Pending.Candidates.Length > 0:
                    client.Send(new ClientMessage { T = "play", Cards = state.Pending.Candidates[0].Cards });
                    break;
            }
        }

        throw new InvalidOperationException(
            $"没打完一局（已结算 {rounds} 局）。最后看到：第 {last?.TrickNumber} 轮、"
            + $"手牌 {last?.Hand.Length} 张、待决策「{last?.Pending?.Kind ?? "无"}」、"
            + $"状态「{last?.Status}」");
    }

    private static string SeatName(int seat) => seat switch
    {
        0 => "东",
        1 => "南",
        2 => "西",
        3 => "北",
        _ => "?",
    };
}

/// <summary>自检用的一只 WebSocket 客户端。</summary>
public sealed class Client : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _buffer = new byte[64 * 1024];

    public int Seat { get; private set; } = -1;

    public int WatchIndex { get; private set; } = -1;

    public static async Task<Client> ConnectAsync(string url, string name)
    {
        var client = new Client();
        await client._socket.ConnectAsync(new Uri(url), CancellationToken.None);
        client.Name = name;
        return client;
    }

    public string Name { get; private set; } = "玩家";

    public void Send(ClientMessage message) =>
        _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, Wire.Json), WebSocketMessageType.Text, true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public async Task<ServerMessage> CreateRoomAsync()
    {
        Send(new ClientMessage { T = "create", Name = Name });
        return await WaitForAsync("joined");
    }

    public async Task<ServerMessage> JoinRoomAsync(string code, bool watch = false)
    {
        Send(new ClientMessage { T = "join", Code = code, Name = Name, Watch = watch });
        return await WaitForAsync("joined");
    }

    public async Task<ServerMessage> WaitForAsync(string type)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var message = await ReceiveAsync(TimeSpan.FromSeconds(15));
            if (message is null)
            {
                break;
            }

            if (message.T == type)
            {
                if (message.Seat is { } seat)
                {
                    Seat = seat;
                }

                if (message.WatchIndex is { } watchIndex)
                {
                    WatchIndex = watchIndex;
                }

                return message;
            }
        }

        throw new InvalidOperationException($"没等到 {type} 消息");
    }

    public async Task<ServerMessage?> ReceiveAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var stream = new MemoryStream();

        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(_buffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                stream.Write(_buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ServerMessage>(Encoding.UTF8.GetString(stream.ToArray()), Wire.Json);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // 收摊而已。
        }

        _socket.Dispose();
    }
}
