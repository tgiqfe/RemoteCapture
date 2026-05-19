using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace RemoteCapture.Lib.WebSocket
{
    public class WebSocketClient : IDisposable
    {
        private ClientWebSocket _clientWebSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isConnected;

        public event EventHandler<BitmapImage> ImageReceived;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

        public bool IsConnected => _isConnected;

        public async Task ConnectAsync(string serverUrl)
        {
            if (_isConnected)
                return;

            try
            {
                _clientWebSocket = new ClientWebSocket();
                _cancellationTokenSource = new CancellationTokenSource();

                await _clientWebSocket.ConnectAsync(new Uri(serverUrl), _cancellationTokenSource.Token);
                _isConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);

                _ = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
            }
            catch (Exception)
            {
                _isConnected = false;
                _clientWebSocket?.Dispose();
                _cancellationTokenSource?.Dispose();
                throw;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[1024 * 1024 * 10]; // 10MB buffer
            var memoryStream = new MemoryStream();

            try
            {
                while (_isConnected && !token.IsCancellationRequested)
                {
                    memoryStream.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        var segment = new ArraySegment<byte>(buffer);
                        result = await _clientWebSocket.ReceiveAsync(segment, token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await DisconnectAsync();
                            return;
                        }

                        memoryStream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (memoryStream.Length > 0)
                    {
                        var imageData = memoryStream.ToArray();
                        var bitmapImage = DecodeImage(imageData);
                        if (bitmapImage != null)
                        {
                            ImageReceived?.Invoke(this, bitmapImage);
                        }
                    }
                }
            }
            catch (Exception)
            {
                await DisconnectAsync();
            }
            finally
            {
                memoryStream.Dispose();
            }
        }

        private BitmapImage DecodeImage(byte[] imageData)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                using (var stream = new MemoryStream(imageData))
                {
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                }
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch
            {
                return null;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!_isConnected)
                return;

            _isConnected = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                if (_clientWebSocket?.State == WebSocketState.Open)
                {
                    await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
                }
            }
            catch { }
            finally
            {
                _clientWebSocket?.Dispose();
                _cancellationTokenSource?.Dispose();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task SendMouseEventAsync(MouseEventMessage mouseEvent)
        {
            if (!_isConnected || _clientWebSocket?.State != WebSocketState.Open)
                return;

            try
            {
                var json = JsonSerializer.Serialize(mouseEvent);
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                await _clientWebSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // Send failed
            }
        }

        public async Task SendKeyboardEventAsync(KeyboardEventMessage keyboardEvent)
        {
            if (!_isConnected || _clientWebSocket?.State != WebSocketState.Open)
                return;

            try
            {
                var json = JsonSerializer.Serialize(keyboardEvent);
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                await _clientWebSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // Send failed
            }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(1000);
        }
    }
}
