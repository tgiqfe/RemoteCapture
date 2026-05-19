using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace RemoteCapture.Lib.WindowsRuntimeHelpers
{
    public static class CaptureHelper
    {
        private static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        [ComImport]
        [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IInitializeWithWindow
        {
            void Initialize(nint hwnd);
        }

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IGraphicsCaptureItemInterop
        {
            nint CreateForWindow(
                [In] nint window,
                [In] ref Guid iid);
            nint CreateForMonitor(
                [In] nint monitor,
                [In] ref Guid iid);
        }

        public static void SetWindow(this GraphicsCapturePicker picker, nint hwnd)
        {
            var interop = picker.As<IInitializeWithWindow>();
            interop.Initialize(hwnd);
        }

        public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
            var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            Marshal.Release(itemPointer);

            return item;
        }

        public static GraphicsCaptureItem CreateItemForMonitor(nint hmon)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var itemPointer = interop.CreateForMonitor(hmon, GraphicsCaptureItemGuid);
            var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            Marshal.Release(itemPointer);

            return item;
        }
    }
}
