using FanKou.Server;

// 双扣联机服务端：一个 WebSocket 端点 + 房间管理。
//
// 只绑 IPv4 的 0.0.0.0：N100 上 IPv6 是没有防火墙的，
// 用 http://*:8081 会把端口直接晾在公网上。IPv4 这边有 NAT 挡着，
// 容器和局域网连得到就够了，对外统一走 nginx 的 443。
// 联机自检：起一个临时实例，自己连自己，把整条路走一遍。
if (args.Contains("--selfcheck"))
{
    Environment.ExitCode = await FanKou.Server.SelfCheck.RunAsync();
    return;
}

var urls = Environment.GetEnvironmentVariable("FANKOU_URLS") ?? "http://0.0.0.0:8081";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(urls);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

// 本地/联机自检时把节拍压到最小，正常跑用真节拍。
var fast = Environment.GetEnvironmentVariable("FANKOU_FAST") == "1";
var server = new RoomServer(fast);

app.MapGet("/health", () => Results.Text($"ok rooms={server.RoomCount}"));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await server.HandleAsync(socket, context.RequestAborted);
});

app.Run();
