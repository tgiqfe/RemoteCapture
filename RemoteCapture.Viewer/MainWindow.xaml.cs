using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using RemoteCapture.Protocol;
using ProtocolMouseButton = RemoteCapture.Protocol.MouseButton;

namespace RemoteCapture.Viewer
{
    public partial class MainWindow : Window
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cancellationTokenSource;
        private int _remoteScreenWidth;
        private int _remoteScreenHeight;

        public MainWindow()
        {
            InitializeComponent();

            ScreenImage.MouseMove += ScreenImage_MouseMove;
            ScreenImage.MouseDown += ScreenImage_MouseDown;
            ScreenImage.MouseUp += ScreenImage_MouseUp;
            ScreenImage.MouseWheel += ScreenImage_MouseWheel;
            KeyDown += MainWindow_KeyDown;
            KeyUp += MainWindow_KeyUp;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var serverAddress = ServerAddressTextBox.Text;
            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                MessageBox.Show("Please enter server address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                _webSocket = new ClientWebSocket();
                _cancellationTokenSource = new CancellationTokenSource();

                var uri = new Uri($"ws://{serverAddress}/screen");
                await _webSocket.ConnectAsync(uri, _cancellationTokenSource.Token);

                StatusTextBlock.Text = "Connected";
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;

                _ = Task.Run(() => ReceiveFrames(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Connection Failed";
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await DisconnectAsync();
        }

        private async Task DisconnectAsync()
        {
            if (_webSocket != null)
            {
                _cancellationTokenSource?.Cancel();

                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                }

                _webSocket.Dispose();
                _webSocket = null;
            }

            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = "Disconnected";
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                ScreenImage.Source = null;
            });
        }

        private async Task ReceiveFrames(CancellationToken cancellationToken)
        {
            var buffer = new byte[1024 * 1024 * 10];

            try
            {
                while (_webSocket != null && _webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.Count > 0)
                    {
                        var messageType = (MessageType)buffer[0];

                        if (messageType == MessageType.FrameUpdate)
                        {
                            var jsonData = buffer.Skip(1).Take(result.Count - 1).ToArray();
                            var frameMessage = JsonSerializer.Deserialize<FrameUpdateMessage>(jsonData);

                            if (frameMessage?.ImageData != null)
                            {
                                _remoteScreenWidth = frameMessage.Width;
                                _remoteScreenHeight = frameMessage.Height;
                                Dispatcher.Invoke(() => DisplayFrame(frameMessage.ImageData));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error receiving frames: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                await DisconnectAsync();
            }
        }

        private void DisplayFrame(byte[] imageData)
        {
            try
            {
                using var memoryStream = new MemoryStream(imageData);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                bitmap.Freeze();

                ScreenImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error displaying frame: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _ = DisconnectAsync();
        }

        private async void ScreenImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_webSocket?.State == WebSocketState.Open && ScreenImage.Source != null)
            {
                var point = GetRemoteCoordinates(e.GetPosition(ScreenImage));
                await SendMouseEvent((int)point.X, (int)point.Y, ProtocolMouseButton.None, false);
            }
        }

        private async void ScreenImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_webSocket?.State == WebSocketState.Open && ScreenImage.Source != null)
            {
                ScreenImage.Focus();
                ScreenImage.CaptureMouse();

                var point = GetRemoteCoordinates(e.GetPosition(ScreenImage));
                var button = e.ChangedButton switch
                {
                    System.Windows.Input.MouseButton.Left => ProtocolMouseButton.Left,
                    System.Windows.Input.MouseButton.Right => ProtocolMouseButton.Right,
                    System.Windows.Input.MouseButton.Middle => ProtocolMouseButton.Middle,
                    _ => ProtocolMouseButton.None
                };

                await SendMouseEvent((int)point.X, (int)point.Y, button, true);
            }
        }

        private async void ScreenImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_webSocket?.State == WebSocketState.Open && ScreenImage.Source != null)
            {
                ScreenImage.ReleaseMouseCapture();

                var point = GetRemoteCoordinates(e.GetPosition(ScreenImage));
                var button = e.ChangedButton switch
                {
                    System.Windows.Input.MouseButton.Left => ProtocolMouseButton.Left,
                    System.Windows.Input.MouseButton.Right => ProtocolMouseButton.Right,
                    System.Windows.Input.MouseButton.Middle => ProtocolMouseButton.Middle,
                    _ => ProtocolMouseButton.None
                };

                await SendMouseEvent((int)point.X, (int)point.Y, button, false);
            }
        }

        private async void ScreenImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_webSocket?.State == WebSocketState.Open && ScreenImage.Source != null)
            {
                var point = GetRemoteCoordinates(e.GetPosition(ScreenImage));
                await SendMouseWheelEvent((int)point.X, (int)point.Y, e.Delta);
            }
        }

        private async void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (_webSocket?.State == WebSocketState.Open && ScreenImage.IsFocused)
            {
                e.Handled = true;
                await SendKeyboardEvent(KeyInterop.VirtualKeyFromKey(e.Key), true, e.KeyboardDevice.IsKeyDown(Key.RightAlt) || e.KeyboardDevice.IsKeyDown(Key.RightCtrl));
            }
        }

        private async void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (_webSocket?.State == WebSocketState.Open && ScreenImage.IsFocused)
            {
                e.Handled = true;
                await SendKeyboardEvent(KeyInterop.VirtualKeyFromKey(e.Key), false, e.KeyboardDevice.IsKeyDown(Key.RightAlt) || e.KeyboardDevice.IsKeyDown(Key.RightCtrl));
            }
        }

        private Point GetRemoteCoordinates(Point localPoint)
        {
            if (ScreenImage.Source == null || _remoteScreenWidth == 0 || _remoteScreenHeight == 0)
                return new Point(0, 0);

            var imageWidth = ScreenImage.Source.Width;
            var imageHeight = ScreenImage.Source.Height;

            var scaleX = _remoteScreenWidth / imageWidth;
            var scaleY = _remoteScreenHeight / imageHeight;

            return new Point((int)(localPoint.X * scaleX), (int)(localPoint.Y * scaleY));
        }

        private async Task SendMouseEvent(int x, int y, ProtocolMouseButton button, bool isPressed)
        {
            if (_webSocket?.State != WebSocketState.Open)
                return;

            var message = new MouseEventMessage
            {
                X = x,
                Y = y,
                Button = button,
                IsPressed = isPressed
            };

            await SendMessage(MessageType.MouseEvent, message);
        }

        private async Task SendMouseWheelEvent(int x, int y, int delta)
        {
            if (_webSocket?.State != WebSocketState.Open)
                return;

            var message = new MouseWheelEventMessage
            {
                X = x,
                Y = y,
                Delta = delta
            };

            await SendMessage(MessageType.MouseWheelEvent, message);
        }

        private async Task SendKeyboardEvent(int virtualKeyCode, bool isPressed, bool isExtendedKey)
        {
            if (_webSocket?.State != WebSocketState.Open)
                return;

            var message = new KeyboardEventMessage
            {
                VirtualKeyCode = virtualKeyCode,
                IsPressed = isPressed,
                IsExtendedKey = isExtendedKey
            };

            await SendMessage(MessageType.KeyboardEvent, message);
        }

        private async Task SendMessage<T>(MessageType messageType, T message)
        {
            if (_webSocket?.State != WebSocketState.Open)
                return;

            try
            {
                var messageTypeBytes = new[] { (byte)messageType };
                var jsonData = JsonSerializer.SerializeToUtf8Bytes(message);
                var fullMessage = messageTypeBytes.Concat(jsonData).ToArray();

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(fullMessage),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }
    }
}