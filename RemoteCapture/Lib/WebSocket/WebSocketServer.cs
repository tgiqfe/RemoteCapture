using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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
        public event EventHandler<MouseEventMessage> MouseEventReceived;
        public event EventHandler<KeyboardEventMessage> KeyboardEventReceived;

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

            RemoteCapture.Lib.Logger.Info($"WebSocketServer starting on port {_port}");
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://+:{_port}/");
            _httpListener.Start();
            _isRunning = true;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            RemoteCapture.Lib.Logger.Info("WebSocketServer started successfully");

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
                RemoteCapture.Lib.Logger.Warning($"Max clients ({_maxClients}) reached. Rejecting connection.");
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
                RemoteCapture.Lib.Logger.Info($"Client connected: {clientId}. Total clients: {_clients.Count}");

                var buffer = new byte[1024 * 64]; // Increased buffer for text messages
                var messageStream = new MemoryStream();

                while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    try
                    {
                        messageStream.SetLength(0);
                        WebSocketReceiveResult result;

                        do
                        {
                            var segment = new ArraySegment<byte>(buffer);
                            result = await webSocket.ReceiveAsync(segment, token);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                                break;
                            }

                            messageStream.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Text && messageStream.Length > 0)
                        {
                            var messageBytes = messageStream.ToArray();
                            var messageText = Encoding.UTF8.GetString(messageBytes);

                            RemoteCapture.Lib.Logger.Debug($"WebSocketServer received text message: {messageText.Substring(0, Math.Min(100, messageText.Length))}...");

                            try
                            {
                                // Try to deserialize as MouseEventMessage
                                var mouseEvent = JsonSerializer.Deserialize<MouseEventMessage>(messageText);
                                if (mouseEvent != null)
                                {
                                    RemoteCapture.Lib.Logger.Debug($"Deserialized MouseEvent: {mouseEvent.EventType}");
                                    MouseEventReceived?.Invoke(this, mouseEvent);
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                RemoteCapture.Lib.Logger.Warning($"Failed to deserialize as MouseEvent: {ex.Message}");
                            }

                            try
                            {
                                // Try to deserialize as KeyboardEventMessage
                                var keyboardEvent = JsonSerializer.Deserialize<KeyboardEventMessage>(messageText);
                                if (keyboardEvent != null)
                                {
                                    KeyboardEventReceived?.Invoke(this, keyboardEvent);
                                }
                            }
                            catch { }
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
