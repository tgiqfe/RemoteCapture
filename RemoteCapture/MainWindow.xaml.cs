using RemoteCapture.Lib.CaptureSampleCore;
using RemoteCapture.Lib.ScreenCapture;
using RemoteCapture.Lib.WindowsRuntimeHelpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.UI.Composition;

namespace RemoteCapture
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
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

        public MainWindow()
        {
            InitializeComponent();

#if DEBUG
            // Force graphicscapture.dll to load.
            var picker = new GraphicsCapturePicker();
#endif
        }

        private async void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
            await StartPickerCaptureAsync();
        }

        private void PrimaryMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
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
            WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
        }

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

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var monitor = (MonitorInfo)comboBox.SelectedItem;

            if (monitor != null)
            {
                StopCapture();
                WindowComboBox.SelectedIndex = -1;
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
                WindowComboBox.ItemsSource = processes;
            }
            else
            {
                WindowComboBox.IsEnabled = false;
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
    }
}