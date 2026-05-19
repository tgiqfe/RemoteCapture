using System.Net.WebSockets;

namespace RemoteCapture.Lib.WebSocket;

public class ScreenStreamClient
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<byte[]>? ImageReceived;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(string uri)
    {
        _webSocket = new ClientWebSocket();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            await _webSocket.ConnectAsync(new Uri(uri), _cancellationTokenSource.Token);
            await ReceiveLoopAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    public void Disconnect()
    {
        _cancellationTokenSource?.Cancel();
        _webSocket?.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        if (_webSocket == null || _cancellationTokenSource == null)
            return;

        while (_webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var sizeBuffer = new byte[4];
                var sizeResult = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(sizeBuffer),
                    _cancellationTokenSource.Token);

                if (sizeResult.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var imageSize = BitConverter.ToInt32(sizeBuffer, 0);
                var imageBuffer = new byte[imageSize];
                var totalReceived = 0;

                while (totalReceived < imageSize)
                {
                    var imageResult = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(imageBuffer, totalReceived, imageSize - totalReceived),
                        _cancellationTokenSource.Token);

                    totalReceived += imageResult.Count;

                    if (imageResult.EndOfMessage)
                    {
                        break;
                    }
                }

                ImageReceived?.Invoke(this, imageBuffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex.Message);
                break;
            }
        }
    }
}
