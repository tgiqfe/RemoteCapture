using System.Net.WebSockets;
using RemoteCapture.Lib.GDI;

namespace RemoteCapture.Lib.WebSocket;

public class ScreenStreamServer
{
    private readonly int _fps;
    private readonly int _quality;
    private readonly bool _captureLogonUI;

    public ScreenStreamServer(int fps = 30, int quality = 75, bool captureLogonUI = false)
    {
        _fps = fps;
        _quality = quality;
        _captureLogonUI = captureLogonUI;
    }

    public async Task StreamScreenAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        var delayMs = 1000 / _fps;

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var imageData = ScreenCapture.CaptureScreenWithCursor(_quality, _captureLogonUI);

                var sizeBytes = BitConverter.GetBytes(imageData.Length);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(sizeBytes),
                    WebSocketMessageType.Binary,
                    false,
                    cancellationToken);

                await webSocket.SendAsync(
                    new ArraySegment<byte>(imageData),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);

                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error streaming screen: {ex.Message}");
                break;
            }
        }

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Closing",
                CancellationToken.None);
        }
    }
}
