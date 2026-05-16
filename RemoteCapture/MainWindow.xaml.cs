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
        private nint _hwnd;
        private Compositor _compositor;
        private CompositionTarget _target;
        private ContainerVisual _root;

        private BasicSampleApplication _sample;
        private ObservableCollection<Process> processes;
        private ObservableCollection<MonitorInfo> _monitors;

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
            _compositor = new Compositor();

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

        private void PrimaryMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            _sample.StopCapture();
            MonitorComboBox.SelectedIndex = -1;
            
            var monitor = MonitorEnumerationHelper.
                GetMonitors().
                Where(m => m.IsPrimary).
                First();
            var item = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
            if (item != null)
            {
                _sample.StartCaptureFromItem(item);
            }
        }

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var monitor = (MonitorInfo)comboBox.SelectedItem;

            if (monitor != null)
            {
                _sample.StopCapture();
                var hmon = monitor.Hmon;
                try
                {
                    var item = CaptureHelper.CreateItemForMonitor(hmon);
                    if (item != null)
                    {
                        _sample.StartCaptureFromItem(item);
                    }
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hmon 0x{hmon.ToInt32():X8} is not valid for capture!");
                    _monitors.Remove(monitor);
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _sample.StopCapture();
            MonitorComboBox.SelectedIndex = -1;
        }

        #endregion
    }
}