using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteCapture.Lib.WebSocket
{
    public class WebSocketServer : IDisposable
    {
        private readonly int _port;
        private readonly int _maxClients;
        private HttpListener _httpListener;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, System.Net.WebSockets.WebSocket> _clients;
        private bool _isRunning;

        public event EventHandler<int> ClientCountChanged;

        public int ConnectedClientCount => _clients.Count;
        public bool IsRunning => _isRunning;

        public WebSocketServer(int port = 8080, int maxClients = 4)
        {
            _port = port;
            _maxClients = maxClients;
            _clients = new ConcurrentDictionary<string, System.Net.WebSockets.WebSocket>();
        }

        public async Task StartAsync()
        {
            if (_isRunning)
                return;

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_port}/");
            _httpListener.Start();
            _isRunning = true;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        if (context.Request.IsWebSocketRequest)
                        {
                            _ = ProcessWebSocketRequestAsync(context, token);
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            context.Response.Close();
                        }
                    }
                    catch (Exception)
                    {
                        // Listener stopped
                    }
                }
            }, token);
        }

        private async Task ProcessWebSocketRequestAsync(HttpListenerContext context, CancellationToken token)
        {
            if (_clients.Count >= _maxClients)
            {
                context.Response.StatusCode = 503; // Service Unavailable
                context.Response.Close();
                return;
            }

            HttpListenerWebSocketContext webSocketContext = null;
            try
            {
                webSocketContext = await context.AcceptWebSocketAsync(null);
                var clientId = Guid.NewGuid().ToString();
                var webSocket = webSocketContext.WebSocket;

                _clients.TryAdd(clientId, webSocket);
                ClientCountChanged?.Invoke(this, _clients.Count);

                var buffer = new byte[1024];
                while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                }

                _clients.TryRemove(clientId, out _);
                ClientCountChanged?.Invoke(this, _clients.Count);
            }
            catch (Exception)
            {
                // Connection failed
            }
        }

        public async Task BroadcastImageAsync(byte[] imageData)
        {
            if (!_isRunning || imageData == null || imageData.Length == 0)
                return;

            var tasks = new List<Task>();
            foreach (var client in _clients.Values)
            {
                if (client.State == WebSocketState.Open)
                {
                    tasks.Add(SendImageToClientAsync(client, imageData));
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task SendImageToClientAsync(System.Net.WebSockets.WebSocket client, byte[] imageData)
        {
            try
            {
                var segment = new ArraySegment<byte>(imageData);
                await client.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // Send failed, client will be cleaned up in ProcessWebSocketRequestAsync
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            foreach (var client in _clients.Values)
            {
                try
                {
                    if (client.State == WebSocketState.Open)
                    {
                        client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None).Wait(1000);
                    }
                    client.Dispose();
                }
                catch { }
            }
            _clients.Clear();

            _httpListener?.Stop();
            _httpListener?.Close();
            _cancellationTokenSource?.Dispose();

            ClientCountChanged?.Invoke(this, 0);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
