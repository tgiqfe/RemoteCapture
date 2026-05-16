using System.Numerics;
using Windows.Foundation;

namespace RemoteCapture.Lib.ScreenCapture
{
    internal class MonitorInfo
    {
        public bool IsPrimary { get; set; }
        public Vector2 ScreenSize { get; set; }
        public Rect MonitorArea { get; set; }
        public Rect WorkArea { get; set; }
        public string DeviceName { get; set; }
        public nint Hmon { get; set; }
    }
}
