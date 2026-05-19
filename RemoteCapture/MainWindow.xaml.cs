using RemoteCapture.Lib.CaptureSampleCore;
using RemoteCapture.Lib.ScreenCapture;
using RemoteCapture.Lib.WindowsRuntimeHelpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.UI.Composition;

namespace RemoteCapture
{
    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window
    {
        private nint hwnd;
        private Compositor compositor;
        private CompositionTarget target;
        private ContainerVisual root;

        private BasicSampleApplication sample;
        private ObservableCollection<Process> processes;
        private ObservableCollection<MonitorInfo> monitors;

        // WebSocket components
        private Lib.WebSocket.WebSocketServer webSocketServer;
        private Lib.WebSocket.WebSocketClient webSocketClient;
        private DispatcherTimer broadcastTimer;
        private int currentFps = 30;
        private int receivedFrameCount = 0;
        private bool useJpegCompression = false;
        private int jpegQuality = 75;

        public MainWindow()
        {
            InitializeComponent();

#if DEBUG
            // Force graphicscapture.dll to load.
            var picker = new GraphicsCapturePicker();
#endif
        }

        /*
        private async void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
            await StartPickerCaptureAsync();
        }
        */

        private void PrimaryMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            //WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
            StartPrimaryMonitorCapture();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var interopWindow = new WindowInteropHelper(this);
            hwnd = interopWindow.Handle;
            var presentationSource = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;
            if (presentationSource != null)
            {
                dpiX = presentationSource.CompositionTarget.TransformToDevice.M11;
                dpiY = presentationSource.CompositionTarget.TransformToDevice.M22;
            }
            var controlsWidth = (float)(ControlsGrid.ActualWidth * dpiX);

            InitComposition(controlsWidth);
            InitWindowList();
            InitMonitorList();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            //WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
        }

        private void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"snapshot_{timestamp}.png";
                var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), fileName);

                sample.SaveSnapshot(filePath);

                MessageBox.Show($"Snapshot saved to:\n{filePath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save snapshot:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /*
        private void WindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var process = (Process)comboBox.SelectedItem;

            if (process != null)
            {
                StopCapture();
                MonitorComboBox.SelectedIndex = -1;
                var hwnd = process.MainWindowHandle;
                try
                {
                    StartHwndCapture(hwnd);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hwnd 0x{hwnd.ToInt32():X8} is not valid for capture!");
                    processes.Remove(process);
                    comboBox.SelectedIndex = -1;
                }
            }
        }
        */

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var monitor = (MonitorInfo)comboBox.SelectedItem;

            if (monitor != null)
            {
                StopCapture();
                //WindowComboBox.SelectedIndex = -1;
                var hmon = monitor.Hmon;
                try
                {
                    StartHmonCapture(hmon);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hmon 0x{hmon.ToInt32():X8} is not valid for capture!");
                    monitors.Remove(monitor);
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        private void InitComposition(float controlsWidth)
        {
            // Create the compositor.
            compositor = new Compositor();

            // Create a target for the window.
            target = compositor.CreateDesktopWindowTarget(hwnd, true);

            // Attach the root visual.
            root = compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;
            root.Size = new Vector2(-controlsWidth, 0);
            root.Offset = new Vector3(controlsWidth, 0, 0);
            target.Root = root;

            // Setup the rest of the sample application.
            sample = new BasicSampleApplication(compositor);
            root.Children.InsertAtTop(sample.Visual);
        }

        private void InitWindowList()
        {
            if (ApiInformation.IsApiContractPresent(typeof(Windows.Foundation.UniversalApiContract).FullName, 8))
            {
                var processesWithWindows = Process.GetProcesses().
                    Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle) &&
                        WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle));
                processes = new ObservableCollection<Process>(processesWithWindows);
                //WindowComboBox.ItemsSource = processes;
            }
            else
            {
                //WindowComboBox.IsEnabled = false;
            }
        }

        private void InitMonitorList()
        {
            if (ApiInformation.IsApiContractPresent(typeof(Windows.Foundation.UniversalApiContract).FullName, 8))
            {
                monitors = new ObservableCollection<MonitorInfo>(MonitorEnumerationHelper.GetMonitors());
                MonitorComboBox.ItemsSource = monitors;
            }
            else
            {
                MonitorComboBox.IsEnabled = false;
                PrimaryMonitorButton.IsEnabled = false;
            }
        }

        private async Task StartPickerCaptureAsync()
        {
            var picker = new GraphicsCapturePicker();
            picker.SetWindow(hwnd);
            var item = await picker.PickSingleItemAsync();

            if (item != null)
            {
                sample.StartCaptureFromItem(item);
            }
        }

        private void StartHwndCapture(nint hwnd)
        {
            var item = CaptureHelper.CreateItemForWindow(hwnd);
            if (item != null)
            {
                sample.StartCaptureFromItem(item);
            }
        }

        private void StartHmonCapture(nint hmon)
        {
            var item = CaptureHelper.CreateItemForMonitor(hmon);
            if (item != null)
            {
                sample.StartCaptureFromItem(item);
            }
        }

        private void StartPrimaryMonitorCapture()
        {
            var monitor = MonitorEnumerationHelper.
                GetMonitors().
                Where(m => m.IsPrimary).
                First();
            StartHmonCapture(monitor.Hmon);
        }

        private void StopCapture()
        {
            sample.StopCapture();
        }

        // WebSocket Server (Sender) Methods
        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                webSocketServer = new Lib.WebSocket.WebSocketServer(8080, 4);
                webSocketServer.ClientCountChanged += WebSocketServer_ClientCountChanged;
                await webSocketServer.StartAsync();

                broadcastTimer = new DispatcherTimer();
                broadcastTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / currentFps);
                broadcastTimer.Tick += BroadcastTimer_Tick;
                broadcastTimer.Start();

                var ipAddresses = GetLocalIPAddresses();
                var addressList = string.Join("\n", ipAddresses.Select(ip => $"ws://{ip}:8080/"));

                ServerStatusText.Text = "サーバー実行中";
                ServerAddressDisplay.Text = addressList;
                StartServerButton.IsEnabled = false;
                StopServerButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"サーバー起動エラー:\n{ex.Message}\n\n管理者権限で実行してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            StopWebSocketServer();
        }

        private void StopWebSocketServer()
        {
            broadcastTimer?.Stop();
            webSocketServer?.Stop();
            webSocketServer = null;

            ServerStatusText.Text = "停止中";
            ServerAddressDisplay.Text = "-";
            ConnectedClientsText.Text = "接続クライアント数: 0 / 4";
            StartServerButton.IsEnabled = true;
            StopServerButton.IsEnabled = false;
        }

        private List<string> GetLocalIPAddresses()
        {
            var ipAddresses = new List<string>();
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddresses.Add(ip.ToString());
                    }
                }
            }
            catch { }

            if (ipAddresses.Count == 0)
            {
                ipAddresses.Add("localhost");
            }

            return ipAddresses;
        }

        private async void BroadcastTimer_Tick(object sender, EventArgs e)
        {
            if (webSocketServer != null && sample != null)
            {
                try
                {
                    byte[] frameData = useJpegCompression 
                        ? sample.GetCurrentFrameAsJpeg(jpegQuality)
                        : sample.GetCurrentFrameAsPng();

                    if (frameData != null)
                    {
                        await webSocketServer.BroadcastImageAsync(frameData);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Broadcast error: {ex.Message}");
                }
            }
        }

        private void WebSocketServer_ClientCountChanged(object sender, int count)
        {
            Dispatcher.Invoke(() =>
            {
                ConnectedClientsText.Text = $"接続クライアント数: {count} / 4";
            });
        }

        private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            currentFps = (int)e.NewValue;
            if (FpsValueText != null)
            {
                FpsValueText.Text = currentFps.ToString();
            }

            if (broadcastTimer != null)
            {
                broadcastTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / currentFps);
            }
        }

        private void CompressionFormat_Changed(object sender, RoutedEventArgs e)
        {
            if (JpegRadioButton != null && JpegQualitySlider != null && JpegQualityLabel != null)
            {
                useJpegCompression = JpegRadioButton.IsChecked == true;
                JpegQualitySlider.IsEnabled = useJpegCompression;
                JpegQualityLabel.IsEnabled = useJpegCompression;
            }
        }

        private void JpegQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            jpegQuality = (int)e.NewValue;
            if (JpegQualityValueText != null)
            {
                JpegQualityValueText.Text = jpegQuality.ToString();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopWebSocketServer();
            webSocketClient?.Dispose();
        }

        // WebSocket Client (Receiver) Methods
        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var serverAddress = ServerAddressTextBox.Text.Trim();
            if (string.IsNullOrEmpty(serverAddress))
            {
                MessageBox.Show("サーバーアドレスを入力してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                webSocketClient = new Lib.WebSocket.WebSocketClient();
                webSocketClient.ImageReceived += WebSocketClient_ImageReceived;
                webSocketClient.Connected += WebSocketClient_Connected;
                webSocketClient.Disconnected += WebSocketClient_Disconnected;

                await webSocketClient.ConnectAsync(serverAddress);

                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"接続エラー:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                webSocketClient?.Dispose();
                webSocketClient = null;
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (webSocketClient != null)
            {
                await webSocketClient.DisconnectAsync();
                webSocketClient.Dispose();
                webSocketClient = null;
            }

            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
        }

        private void WebSocketClient_ImageReceived(object sender, System.Windows.Media.Imaging.BitmapImage e)
        {
            Dispatcher.Invoke(() =>
            {
                ReceivedImage.Source = e;
                NoImageText.Visibility = Visibility.Collapsed;
                receivedFrameCount++;
                ReceivedFramesText.Text = $"受信フレーム数: {receivedFrameCount}";
            });
        }

        private void WebSocketClient_Connected(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ClientStatusText.Text = "接続中";
                receivedFrameCount = 0;
                ReceivedFramesText.Text = "受信フレーム数: 0";
            });
        }

        private void WebSocketClient_Disconnected(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ClientStatusText.Text = "未接続";
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                NoImageText.Visibility = Visibility.Visible;
            });
        }
    }
}