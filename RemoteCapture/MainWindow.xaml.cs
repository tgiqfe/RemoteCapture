using RemoteCapture.Lib.CaptureSampleCore;
using RemoteCapture.Lib.ScreenCapture;
using RemoteCapture.Lib.WebRTC;
using RemoteCapture.Lib.WindowsRuntimeHelpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using WinUIComposition = Windows.UI.Composition;

namespace RemoteCapture
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private nint _hwnd;
        private WinUIComposition.Compositor _compositor;
        private WinUIComposition.CompositionTarget _target;
        private WinUIComposition.ContainerVisual _root;

        private BasicSampleApplication _sample;
        private ObservableCollection<Process> processes;
        private ObservableCollection<MonitorInfo> _monitors;

        // フレームデータ監視用
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.UtcNow;
        private double _currentFps = 0;

        // WebRTC
        private WebRTCPeer? _webRtcPeer;
        private WebRTCReceiver? _webRtcReceiver;

        // モード管理
        private bool _isSenderMode = true;

        public MainWindow()
        {
            InitializeComponent();

#if DEBUG
            // Force graphicscapture.dll to load.
            var picker = new GraphicsCapturePicker();
#endif
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var interopWindow = new WindowInteropHelper(this);
            _hwnd = interopWindow.Handle;
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
            InitMonitorList();
        }

        private void InitComposition(float controlsWidth)
        {
            // Create the compositor.
            _compositor = new WinUIComposition.Compositor();

            // Create a target for the window.
            _target = _compositor.CreateDesktopWindowTarget(_hwnd, true);

            // Attach the root visual.
            _root = _compositor.CreateContainerVisual();
            _root.RelativeSizeAdjustment = Vector2.One;
            _root.Size = new Vector2(-controlsWidth, 0);
            _root.Offset = new Vector3(controlsWidth, 0, 0);
            _target.Root = _root;

            // Setup the rest of the sample application.
            _sample = new BasicSampleApplication(_compositor);

            // フレームデータイベントを購読
            _sample.FrameDataAvailable += OnFrameDataAvailable;

            _root.Children.InsertAtTop(_sample.Visual);
        }

        private void InitMonitorList()
        {
            if (ApiInformation.IsApiContractPresent(typeof(Windows.Foundation.UniversalApiContract).FullName, 8))
            {
                _monitors = new ObservableCollection<MonitorInfo>(MonitorEnumerationHelper.GetMonitors());
                MonitorComboBox.ItemsSource = _monitors;
            }
            else
            {
                MonitorComboBox.IsEnabled = false;
                PrimaryMonitorButton.IsEnabled = false;
            }
        }

        #region Action

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SenderModeRadio == null || ReceiverModeRadio == null)
                return;

            _isSenderMode = SenderModeRadio.IsChecked == true;

            // UI表示切り替え
            if (SenderControls != null)
                SenderControls.Visibility = _isSenderMode ? Visibility.Visible : Visibility.Collapsed;
            if (ReceiverControls != null)
                ReceiverControls.Visibility = _isSenderMode ? Visibility.Collapsed : Visibility.Visible;
            if (SenderSdpArea != null)
                SenderSdpArea.Visibility = _isSenderMode ? Visibility.Visible : Visibility.Collapsed;
            if (ReceiverSdpArea != null)
                ReceiverSdpArea.Visibility = _isSenderMode ? Visibility.Collapsed : Visibility.Visible;

            // プレビューエリアの切り替え
            if (SenderPreviewArea != null)
                SenderPreviewArea.Visibility = _isSenderMode ? Visibility.Visible : Visibility.Collapsed;
            if (ReceiverVideoImage != null)
                ReceiverVideoImage.Visibility = _isSenderMode ? Visibility.Collapsed : Visibility.Visible;
            if (ReceiverWaitingMessage != null)
                ReceiverWaitingMessage.Visibility = _isSenderMode ? Visibility.Collapsed : Visibility.Visible;

            // 既存の接続をクリア
            StopCurrentMode();

            Debug.WriteLine($"Mode switched to: {(_isSenderMode ? "Sender" : "Receiver")}");
        }

        private void StopCurrentMode()
        {
            if (_isSenderMode)
            {
                _sample?.StopCapture();
                _webRtcPeer?.CloseAsync().Wait();
                _webRtcPeer = null;
            }
            else
            {
                _webRtcReceiver?.CloseAsync().Wait();
                _webRtcReceiver = null;
            }
        }

        private void PrimaryMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            _sample.StopCapture();
            MonitorComboBox.SelectedIndex = -1;

            Debug.WriteLine("Primary Monitor button clicked");
            var monitor = MonitorEnumerationHelper.
                GetMonitors().
                Where(m => m.IsPrimary).
                First();
            var item = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
            if (item != null)
            {
                Debug.WriteLine($"Starting capture from primary monitor: {monitor.DeviceName}");
                _sample.StartCaptureFromItem(item);
                Debug.WriteLine("Capture started");
            }
            else
            {
                Debug.WriteLine("Failed to create capture item");
            }
        }

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var monitor = (MonitorInfo)comboBox.SelectedItem;

            if (monitor != null)
            {
                Debug.WriteLine($"Monitor selected: {monitor.DeviceName}");
                _sample.StopCapture();
                var hmon = monitor.Hmon;
                try
                {
                    var item = CaptureHelper.CreateItemForMonitor(hmon);
                    if (item != null)
                    {
                        Debug.WriteLine($"Starting capture from monitor: {monitor.DeviceName}");
                        _sample.StartCaptureFromItem(item);
                        Debug.WriteLine("Capture started");
                    }
                    else
                    {
                        Debug.WriteLine("Failed to create capture item");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hmon 0x{hmon.ToInt32():X8} is not valid for capture! Exception: {ex.Message}");
                    _monitors.Remove(monitor);
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCurrentMode();
            MonitorComboBox.SelectedIndex = -1;

            // フレーム情報をリセット
            ResetFrameDataDisplay();

            // 受信情報もリセット
            if (!_isSenderMode)
            {
                ReceiverOfferSdpTextBox.Clear();
                ReceiverLocalSdpTextBox.Clear();
                ReceivedResolutionText.Text = "Resolution: -";
                ReceivedFpsText.Text = "FPS: -";
                ReceivedLastUpdateText.Text = "Last Update: -";
            }
        }

        private void FrameRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var selectedItem = (ComboBoxItem)comboBox.SelectedItem;

            if (selectedItem != null && _sample != null)
            {
                var fps = double.Parse(selectedItem.Tag.ToString());
                _sample.TargetFrameRate = fps;

                Debug.WriteLine($"Target frame rate set to: {(fps > 0 ? fps.ToString() : "Unlimited")} FPS");
            }
        }

        private void EnablePreviewCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_sample != null)
            {
                _sample.EnablePreview = EnablePreviewCheckBox.IsChecked == true;
                Debug.WriteLine($"Preview enabled: {_sample.EnablePreview}");
            }
        }

        private async void SetOfferButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSenderMode)
            {
                var offerSdp = ReceiverOfferSdpTextBox.Text.Trim();
                if (string.IsNullOrEmpty(offerSdp))
                {
                    MessageBox.Show("Please paste the Offer SDP first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // WebRTC受信を初期化
                if (_webRtcReceiver == null)
                {
                    _webRtcReceiver = new WebRTCReceiver();
                    _webRtcReceiver.ConnectionStateChanged += OnReceiverConnectionStateChanged;
                    _webRtcReceiver.LocalSdpReady += OnReceiverLocalSdpReady;
                    _webRtcReceiver.ErrorOccurred += OnReceiverErrorOccurred;
                    _webRtcReceiver.VideoFrameReceived += OnReceiverVideoFrameReceived;
                }

                SetOfferButton.IsEnabled = false;
                var success = await _webRtcReceiver.SetRemoteOfferAndCreateAnswerAsync(offerSdp);
                SetOfferButton.IsEnabled = true;

                if (!success)
                {
                    MessageBox.Show("Failed to set remote offer. Check debug output.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Receiver Event Handlers

        private void OnReceiverConnectionStateChanged(object sender, WebRTCConnectionState state)
        {
            Dispatcher.Invoke(() =>
            {
                ReceiverConnectionStatusText.Text = $"Status: {state}";
                Debug.WriteLine($"Receiver connection state: {state}");
            });
        }

        private void OnReceiverLocalSdpReady(object sender, string sdp)
        {
            Dispatcher.Invoke(() =>
            {
                ReceiverLocalSdpTextBox.Text = sdp;
                Debug.WriteLine($"Receiver Answer SDP ready ({sdp.Length} chars)");
            });
        }

        private void OnReceiverErrorOccurred(object sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Receiver error: {error}", "WebRTC Receiver Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Receiver error: {error}");
            });
        }

        private void OnReceiverVideoFrameReceived(object sender, VideoFrameEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 待機メッセージを非表示
                    if (ReceiverWaitingMessage != null)
                        ReceiverWaitingMessage.Visibility = Visibility.Collapsed;

                    // 受信映像情報を更新
                    ReceivedResolutionText.Text = $"Resolution: {e.Width}x{e.Height}";
                    ReceivedLastUpdateText.Text = $"Last Update: {DateTime.Now:HH:mm:ss.fff}";

                    // フレームデータをBitmapに変換して表示
                    // FFmpegから来るフォーマットを確認してピクセルフォーマットを決定
                    int stride = e.Width * 4; // BGRA = 4 bytes per pixel
                    var bitmap = BitmapSource.Create(
                        e.Width,
                        e.Height,
                        96, // dpiX
                        96, // dpiY
                        PixelFormats.Bgra32,
                        null,
                        e.Frame,
                        stride
                    );

                    ReceiverVideoImage.Source = bitmap;

                    Debug.WriteLine($"Displayed receiver frame: {e.Width}x{e.Height}, {e.Frame.Length} bytes");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error displaying receiver frame: {ex.Message}");
            }
        }

        #endregion

        #region Frame Data Monitoring

        private void OnFrameDataAvailable(object sender, CapturedFrameEventArgs e)
        {
            // フレームカウントを増加
            _frameCount++;

            // FPS計算（1秒ごとに更新）
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _currentFps = _frameCount / elapsed;
                _frameCount = 0;
                _lastFpsUpdate = now;
            }

            // UIスレッドで表示を更新
            try
            {
                Dispatcher.Invoke(() =>
                {
                    ResolutionText.Text = $"Resolution: {e.Width} x {e.Height}";
                    DataSizeText.Text = $"Data Size: {e.PixelData.Length:N0} bytes ({e.PixelData.Length / 1024.0 / 1024.0:F2} MB)";
                    FpsText.Text = $"FPS: {_currentFps:F1}";
                    LastUpdateText.Text = $"Last Update: {e.Timestamp:HH:mm:ss.fff}";
                });

                // デバッグ出力（最初のフレームと1秒ごと）
                if (_frameCount == 1 || elapsed >= 1.0)
                {
                    Debug.WriteLine($"Frame: {e.Width}x{e.Height}, Size: {e.PixelData.Length} bytes, FPS: {_currentFps:F1}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating UI: {ex.Message}");
            }

            // WebRTCでフレームを送信
            if (_webRtcPeer != null)
            {
                if (_frameCount == 1)
                {
                    Debug.WriteLine($"Calling SupplyFrame: {e.Width}x{e.Height}, {e.PixelData.Length} bytes");
                }
                _webRtcPeer.SupplyFrame(e.PixelData, e.Width, e.Height);
            }
        }

        private void ResetFrameDataDisplay()
        {
            _frameCount = 0;
            _currentFps = 0;
            _lastFpsUpdate = DateTime.UtcNow;

            ResolutionText.Text = "Resolution: -";
            DataSizeText.Text = "Data Size: -";
            FpsText.Text = "FPS: -";
            LastUpdateText.Text = "Last Update: -";
        }

        #endregion

        #region WebRTC

        private async void CreateOfferButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // WebRTCPeerの初期化
                if (_webRtcPeer == null)
                {
                    _webRtcPeer = new WebRTCPeer();

                    // イベントハンドラの設定
                    _webRtcPeer.ConnectionStateChanged += OnWebRtcConnectionStateChanged;
                    _webRtcPeer.LocalSdpReady += OnLocalSdpReady;
                    _webRtcPeer.ErrorOccurred += OnWebRtcError;
                }

                CreateOfferButton.IsEnabled = false;
                ConnectionStatusText.Text = "Status: Creating Offer...";

                // Offerの作成
                var success = await _webRtcPeer.CreateOfferAsync();

                if (!success)
                {
                    MessageBox.Show("Failed to create WebRTC offer", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    CreateOfferButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating offer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CreateOfferButton.IsEnabled = true;
                ConnectionStatusText.Text = "Status: Error";
            }
        }

        private async void SetAnswerButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("=== SetAnswerButton_Click called ===");
            try
            {
                Debug.WriteLine($"Current mode: _isSenderMode={_isSenderMode}");

                var remoteSdp = RemoteSdpTextBox.Text.Trim();
                Debug.WriteLine($"Remote SDP length: {remoteSdp.Length}");

                if (string.IsNullOrEmpty(remoteSdp))
                {
                    Debug.WriteLine("Remote SDP is empty");
                    MessageBox.Show("Please paste the remote SDP answer", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_webRtcPeer == null)
                {
                    Debug.WriteLine("WebRTC peer is null");
                    MessageBox.Show("Please create an offer first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SetAnswerButton.IsEnabled = false;
                ConnectionStatusText.Text = "Status: Setting Remote Answer...";

                Debug.WriteLine("Calling SetRemoteDescriptionAsync with answer...");
                var success = await _webRtcPeer.SetRemoteDescriptionAsync(remoteSdp, "answer");
                Debug.WriteLine($"SetRemoteDescriptionAsync result: {success}");

                if (success)
                {
                    ConnectionStatusText.Text = "Status: Connecting...";
                    Debug.WriteLine("Remote answer set successfully, waiting for connection...");
                }
                else
                {
                    MessageBox.Show("Failed to set remote answer", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetAnswerButton.IsEnabled = true;
                    ConnectionStatusText.Text = "Status: Error";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SetAnswerButton_Click: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error setting answer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetAnswerButton.IsEnabled = true;
                ConnectionStatusText.Text = "Status: Error";
            }
        }

        private void OnWebRtcConnectionStateChanged(object? sender, WebRTCConnectionState state)
        {
            Dispatcher.Invoke(() =>
            {
                ConnectionStatusText.Text = $"Status: {state}";

                // 接続状態に応じて色を変更
                switch (state)
                {
                    case WebRTCConnectionState.Connected:
                        ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                        break;
                    case WebRTCConnectionState.Failed:
                        ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                        CreateOfferButton.IsEnabled = true;
                        SetAnswerButton.IsEnabled = true;
                        break;
                    case WebRTCConnectionState.Disconnected:
                    case WebRTCConnectionState.Closed:
                        ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Gray);
                        CreateOfferButton.IsEnabled = true;
                        SetAnswerButton.IsEnabled = true;
                        break;
                    default:
                        ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                        break;
                }
            });
        }

        private void OnLocalSdpReady(object? sender, string sdp)
        {
            Dispatcher.Invoke(() =>
            {
                LocalSdpTextBox.Text = sdp;
                MessageBox.Show("SDP Offer created! Copy the Local SDP and send it to the remote peer.", "SDP Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void OnWebRtcError(object? sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                Debug.WriteLine($"WebRTC Error: {error}");
                MessageBox.Show($"WebRTC Error: {error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        #endregion
    }
}