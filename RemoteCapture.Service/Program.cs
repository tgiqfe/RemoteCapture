using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RemoteCapture.Protocol;

// ログファイルの設定
var logPath = Path.Combine(Path.GetTempPath(), "RemoteCapture.Service.log");
var logStream = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };

void Log(string message)
{
    var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    Console.WriteLine(logMessage);
    logStream.WriteLine(logMessage);
}

Log("=== RemoteCapture.Service Starting ===");
Log($"Log file: {logPath}");
Log($"Process ID: {Environment.ProcessId}");
Log($"Session ID: {System.Diagnostics.Process.GetCurrentProcess().SessionId}");
Log($"User: {Environment.UserName}");
Log($"Is Interactive: {Environment.UserInteractive}");
Log("======================================");

// グローバルログ関数として使えるように
Console.SetOut(new LogWriter(Console.Out, logStream));

var builder = WebApplication.CreateBuilder(args);

// Windowsサービスとして実行できるように設定
builder.Host.UseWindowsService();

// サービス起動時のタイムアウトを防ぐためのライフタイム設定
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.UseWebSockets();

app.Map("/screen", async context =>
{
    Log($"[WebSocket] New connection from {context.Connection.RemoteIpAddress}");

    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await HandleScreenShare(webSocket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

Log("[Server] Starting WebSocket server on http://0.0.0.0:5000");

// サービスとして実行する場合はRunAsync、コンソールの場合はRunを使用
await app.RunAsync("http://0.0.0.0:5000");

async Task HandleScreenShare(WebSocket webSocket)
{
    using var screenCapture = new RemoteCapture.Service.ScreenCapture();
    screenCapture.Initialize();

    var (width, height) = screenCapture.GetScreenSize();
    var inputInjector = new RemoteCapture.Service.InputInjector(width, height);

    var receiveTask = Task.Run(async () =>
    {
        var buffer = new byte[4096];
        while (webSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.Count > 0)
                {
                    var messageType = (MessageType)buffer[0];
                    var jsonData = buffer.Skip(1).Take(result.Count - 1).ToArray();

                    switch (messageType)
                    {
                        case MessageType.MouseEvent:
                            var mouseEvent = JsonSerializer.Deserialize<MouseEventMessage>(jsonData);
                            if (mouseEvent != null)
                            {
                                inputInjector.InjectMouseEvent(mouseEvent);
                            }
                            break;

                        case MessageType.MouseWheelEvent:
                            var wheelEvent = JsonSerializer.Deserialize<MouseWheelEventMessage>(jsonData);
                            if (wheelEvent != null)
                            {
                                inputInjector.InjectMouseWheelEvent(wheelEvent);
                            }
                            break;

                        case MessageType.KeyboardEvent:
                            var keyEvent = JsonSerializer.Deserialize<KeyboardEventMessage>(jsonData);
                            if (keyEvent != null)
                            {
                                inputInjector.InjectKeyboardEvent(keyEvent);
                            }
                            break;
                    }
                }
            }
            catch
            {
                break;
            }
        }
    });

    try
    {
        while (webSocket.State == WebSocketState.Open)
        {
            var imageData = screenCapture.CaptureScreen();

            var frameMessage = new FrameUpdateMessage
            {
                Width = width,
                Height = height,
                ImageData = imageData
            };

            var messageTypeBytes = new[] { (byte)MessageType.FrameUpdate };
            var jsonData = JsonSerializer.SerializeToUtf8Bytes(frameMessage);
            var fullMessage = messageTypeBytes.Concat(jsonData).ToArray();

            await webSocket.SendAsync(
                new ArraySegment<byte>(fullMessage),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            await Task.Delay(33);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }

    await receiveTask;
}

// ログファイル用のTextWriter
class LogWriter : TextWriter
{
    private readonly TextWriter _console;
    private readonly TextWriter _file;

    public LogWriter(TextWriter console, TextWriter file)
    {
        _console = console;
        _file = file;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void WriteLine(string? value)
    {
        _console.WriteLine(value);
        _file.WriteLine(value);
    }

    public override void Write(string? value)
    {
        _console.Write(value);
        _file.Write(value);
    }
}
