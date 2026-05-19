using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using RemoteCapture.Lib.WebSocket;

namespace RemoteCapture.Viewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ScreenStreamClient? _client;
        private bool _isConnected = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TextBox_IPAddress.Text = "localhost";
            TextBox_PortNumber.Text = "5000";
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _client?.Disconnect();
        }

        private async void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                _client?.Disconnect();
                _isConnected = false;
                Button_Connect.Content = "Connect";
                TextBox_IPAddress.IsEnabled = true;
                TextBox_PortNumber.IsEnabled = true;
                return;
            }

            var ipAddress = TextBox_IPAddress.Text.Trim();
            var portNumber = TextBox_PortNumber.Text.Trim();

            if (string.IsNullOrEmpty(ipAddress))
            {
                MessageBox.Show("IPアドレスを入力してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(portNumber))
            {
                MessageBox.Show("ポート番号を入力してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Button_Connect.IsEnabled = false;
            Button_Connect.Content = "接続中...";
            TextBox_IPAddress.IsEnabled = false;
            TextBox_PortNumber.IsEnabled = false;

            try
            {
                _client?.Disconnect();

                _client = new ScreenStreamClient();
                _client.ImageReceived += OnImageReceived;
                _client.ErrorOccurred += OnErrorOccurred;

                var uri = $"ws://{ipAddress}:{portNumber}/screen";
                await _client.ConnectAsync(uri);

                _isConnected = true;
                Button_Connect.Content = "切断";
                Button_Connect.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"接続エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Button_Connect.Content = "Connect";
                Button_Connect.IsEnabled = true;
                TextBox_IPAddress.IsEnabled = true;
                TextBox_PortNumber.IsEnabled = true;
            }
        }

        private void OnImageReceived(object? sender, byte[] imageData)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var bitmapImage = new BitmapImage();
                    using var ms = new MemoryStream(imageData);

                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    ScreenImage.Source = bitmapImage;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"画像表示エラー: {ex.Message}");
                }
            });
        }

        private void OnErrorOccurred(object? sender, string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"接続エラー: {errorMessage}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                _isConnected = false;
                Button_Connect.Content = "Connect";
                Button_Connect.IsEnabled = true;
                TextBox_IPAddress.IsEnabled = true;
                TextBox_PortNumber.IsEnabled = true;
            });
        }
    }
}
