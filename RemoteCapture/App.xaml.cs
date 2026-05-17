using RemoteCapture.Lib.WindowsRuntimeHelpers;
using System.Configuration;
using System.Data;
using System.Windows;
using Windows.System;

namespace RemoteCapture
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // FFmpegライブラリのパスを最初に設定
            SetupFFmpegPath();

            _controller = CoreMessagingHelper.CreateDispatcherQueueControllerForCurrentThread();
        }

        private DispatcherQueueController _controller;

        /// <summary>
        /// FFmpegネイティブライブラリのパスを設定
        /// </summary>
        private static void SetupFFmpegPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                System.Diagnostics.Debug.WriteLine($"[App] Base directory: {baseDir}");

                // 環境変数PATHにFFmpeg DLLのディレクトリを追加
                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var newPath = baseDir + ";" + path;
                Environment.SetEnvironmentVariable("PATH", newPath);
                System.Diagnostics.Debug.WriteLine($"[App] Added to PATH: {baseDir}");

                // 必要なDLLが存在するか確認
                var requiredDlls = new[] { "avcodec-62.dll", "avutil-60.dll", "swscale-9.dll", "swresample-6.dll", "avformat-62.dll" };
                bool allFound = true;
                foreach (var dll in requiredDlls)
                {
                    var dllPath = System.IO.Path.Combine(baseDir, dll);
                    var exists = System.IO.File.Exists(dllPath);
                    System.Diagnostics.Debug.WriteLine($"[App] {dll}: {(exists ? "FOUND" : "NOT FOUND")} at {dllPath}");

                    if (!exists)
                    {
                        allFound = false;
                    }
                }

                if (!allFound)
                {
                    var message = $"Some required FFmpeg libraries are missing.\n\nPlease ensure FFmpeg 5.1 DLLs are in:\n{baseDir}";
                    System.Diagnostics.Debug.WriteLine($"[App] WARNING: {message}");
                    // MessageBox.Show(message, "FFmpeg Libraries Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // FFmpeg.AutoGenのRootPathを設定（DynamicallyLoadedBindingsが初期化される前に設定する必要がある）
                FFmpeg.AutoGen.ffmpeg.RootPath = baseDir;
                System.Diagnostics.Debug.WriteLine($"[App] FFmpeg.RootPath set to: {baseDir}");

                // DynamicallyLoadedBindingsを強制的に初期化するために、ネイティブ関数を1つ呼び出す
                try
                {
                    var version = FFmpeg.AutoGen.ffmpeg.av_version_info();
                    System.Diagnostics.Debug.WriteLine($"[App] FFmpeg version: {version}");
                    System.Diagnostics.Debug.WriteLine($"[App] FFmpeg initialized successfully");
                }
                catch (Exception initEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] WARNING: FFmpeg version check failed: {initEx.Message}");
                    // 初期化失敗でも続行する
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ERROR setting up FFmpeg: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");

                // 起動は継続させる（エンコーダー初期化時に詳細なエラーが出る）
                MessageBox.Show(
                    $"Warning: FFmpeg initialization encountered an issue:\n{ex.Message}\n\nThe application will continue, but video encoding may not work.",
                    "FFmpeg Setup Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

}
