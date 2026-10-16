using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FanKou.Core.Cards;
using FanKou.Protocol;

namespace FanKou.Web.Services;

/// <summary>
/// 联机客户端：一条 WebSocket，收服务端推来的房间信息和对局状态。
/// 它**不跑任何规则**——引擎在服务端，这边只负责显示和把动作发回去。
/// </summary>
public sealed class OnlineClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _buffer = new byte[64 * 1024];

    private CancellationTokenSource? _cts;

    /// <summary>收到任何消息都会响；页面拿它重画。</summary>
    public event Action? Changed;

    public string? Code { get; private set; }

    public int Seat { get; private set; } = -1;

    public bool IsHost { get; private set; }

    public bool Started { get; private set; }

    public SeatInfo[] Seats { get; private set; } = [];

    public WatcherInfo[] Watchers { get; private set; } = [];

    /// <summary>-1 = 坐在牌桌上；0..2 = 在第几个观战位。</summary>
    public int WatchIndex { get; private set; } = -1;

    public bool IsWatcher => WatchIndex >= 0;

    public StateDto? State { get; private set; }

    public RoundDto? Result { get; private set; }

    public string? Error { get; private set; }

    public bool Closed { get; private set; }

    public static string BuildUrl(string baseUri)
    {
        var uri = new Uri(new Uri(baseUri), "ws");
        var scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        return $"{scheme}://{uri.Authority}/ws";
    }

    public async Task ConnectAsync(string url)
    {
        await _socket.ConnectAsync(new Uri(url), CancellationToken.None);
        _cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_cts.Token);
    }

    public void Create(string name) => Send(new ClientMessage { T = "create", Name = name });

    public void Join(string code, string name, bool watch = false) =>
        Send(new ClientMessage { T = "join", Code = code, Name = name, Watch = watch });

    /// <summary>把人机加到指定座位。</summary>
    public void AddBot(int seat) => Send(new ClientMessage { T = "addBot", Seat = seat });

    public void RemoveBot(int seat) => Send(new ClientMessage { T = "removeBot", Seat = seat });

    /// <summary>换到某个牌桌座位（有人就跟他换，是人机就顶掉）。</summary>
    public void SwitchTo(int seat) => Send(new ClientMessage { T = "switch", Seat = seat });

    /// <summary>下桌去观战。</summary>
    public void GoWatch(int index) => Send(new ClientMessage { T = "watch", WatchIndex = index });

    public void StartGame() => Send(new ClientMessage { T = "start" });

    public void NextRound() => Send(new ClientMessage { T = "next" });

    public void Call(int option) => Send(new ClientMessage { T = "call", Option = option });

    public void Bottom(IEnumerable<Card> cards) => Send(new ClientMessage { T = "bottom", Cards = [.. cards.Select(c => c.Id)] });

    public void Play(IEnumerable<Card> cards) => Send(new ClientMessage { T = "play", Cards = [.. cards.Select(c => c.Id)] });

    public void ClearResult() => Result = null;

    private void Send(ClientMessage message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Wire.Json);
        _ = SendAsync(payload);
    }

    private async Task SendAsync(byte[] payload)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // 掉线了，下面的接收循环会收尾。
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();

                while (true)
                {
                    var result = await _socket.ReceiveAsync(_buffer, token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Closed = true;
                        Changed?.Invoke();
                        return;
                    }

                    stream.Write(_buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                var message = JsonSerializer.Deserialize<ServerMessage>(stream.ToArray(), Wire.Json);
                if (message is not null)
                {
                    Apply(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常收摊。
        }
        catch (Exception)
        {
            Closed = true;
            Changed?.Invoke();
        }
    }

    private void Apply(ServerMessage message)
    {
        switch (message.T)
        {
            case "joined":
                Code = message.Code;
                Seat = message.Seat ?? -1;
                IsHost = message.Host ?? false;
                Started = message.Started ?? false;
                Seats = message.Seats ?? [];
                Watchers = message.Watchers ?? [];
                WatchIndex = message.WatchIndex ?? -1;
                Result = null;
                break;

            case "room":
                Code = message.Code ?? Code;
                Seat = message.Seat ?? Seat;
                IsHost = message.Host ?? IsHost;
                Started = message.Started ?? Started;
                Seats = message.Seats ?? [];
                Watchers = message.Watchers ?? [];
                WatchIndex = message.WatchIndex ?? WatchIndex;
                break;

            case "state":
                State = message.State;
                break;

            case "result":
                Result = message.Result;
                break;

            case "error":
                Error = message.Message;
                break;
        }

        Changed?.Invoke();
    }

    public void ClearError() => Error = null;

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
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
        _cts?.Dispose();
    }
}
