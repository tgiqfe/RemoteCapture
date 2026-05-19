using System.Runtime.InteropServices;
using Windows.UI.Composition;
using SharpDX.DXGI;
using WinRT;
namespace RemoteCapture.Lib.WindowsRuntimeHelpers
{
    public static class CompositionHelper
    {
        // Delegate for CreateDesktopWindowTarget function from VTable
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDesktopWindowTargetDelegate(
            nint thisPtr,
            nint hwnd,
            [MarshalAs(UnmanagedType.Bool)] bool isTopmost,
            out nint target);

        // Delegate for CreateCompositionSurfaceForSwapChain function from VTable
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCompositionSurfaceForSwapChainDelegate(
            nint thisPtr,
            nint swapChain,
            out nint surface);

        [ComImport]
        [Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface ICompositorInterop
        {
            void CreateCompositionSurfaceForHandle(
                nint swapChain,
                out ICompositionSurface result);

            void CreateCompositionSurfaceForSwapChain(
                nint swapChain,
                out ICompositionSurface result);

            void CreateGraphicsDevice(
                nint renderingDevice,
                out CompositionGraphicsDevice result);
        }

        [ComImport]
        [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface ICompositorDesktopInterop
        {
            void CreateDesktopWindowTarget(
                nint hwnd,
                bool isTopmost,
                out Windows.UI.Composition.Desktop.DesktopWindowTarget target);
        }

        public static CompositionTarget CreateDesktopWindowTarget(this Compositor compositor, nint hwnd, bool isTopmost)
        {
            // Get the IUnknown pointer for the Compositor
            nint compositorPtr = nint.Zero;
            try
            {
                compositorPtr = MarshalInspectable<Compositor>.FromManaged(compositor);

                // Query for ICompositorDesktopInterop interface
                Guid iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
                int hr = Marshal.QueryInterface(compositorPtr, in iid, out nint interopPtr);

                if (hr != 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                try
                {
                    // Get the VTable pointer
                    nint vTablePtr = Marshal.ReadIntPtr(interopPtr, 0);

                    // CreateDesktopWindowTarget is at offset 3 (0=QueryInterface, 1=AddRef, 2=Release, 3=CreateDesktopWindowTarget)
                    nint functionPtr = Marshal.ReadIntPtr(vTablePtr, 3 * nint.Size);

                    // Create delegate and invoke
                    var createDesktopWindowTarget = Marshal.GetDelegateForFunctionPointer<CreateDesktopWindowTargetDelegate>(functionPtr);
                    nint targetPtr;
                    hr = createDesktopWindowTarget(interopPtr, hwnd, isTopmost, out targetPtr);

                    if (hr != 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }

                    // Convert the result pointer to managed object
                    var target = MarshalInspectable<Windows.UI.Composition.Desktop.DesktopWindowTarget>.FromAbi(targetPtr);
                    Marshal.Release(targetPtr);
                    return target;
                }
                finally
                {
                    if (interopPtr != nint.Zero)
                    {
                        Marshal.Release(interopPtr);
                    }
                }
            }
            finally
            {
                if (compositorPtr != nint.Zero)
                {
                    Marshal.Release(compositorPtr);
                }
            }
        }

        public static ICompositionSurface CreateCompositionSurfaceForSwapChain(this Compositor compositor, SwapChain swapChain)
        {
            // Get the IUnknown pointer for the Compositor
            nint compositorPtr = nint.Zero;
            try
            {
                compositorPtr = MarshalInspectable<Compositor>.FromManaged(compositor);

                // Query for ICompositorInterop interface
                Guid iid = new Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330");
                int hr = Marshal.QueryInterface(compositorPtr, in iid, out nint interopPtr);

                if (hr != 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                try
                {
                    // Get the VTable pointer
                    nint vTablePtr = Marshal.ReadIntPtr(interopPtr, 0);

                    // CreateCompositionSurfaceForSwapChain is at offset 4 (0=QueryInterface, 1=AddRef, 2=Release, 3=CreateCompositionSurfaceForHandle, 4=CreateCompositionSurfaceForSwapChain)
                    nint functionPtr = Marshal.ReadIntPtr(vTablePtr, 4 * nint.Size);

                    // Create delegate and invoke
                    var createSurface = Marshal.GetDelegateForFunctionPointer<CreateCompositionSurfaceForSwapChainDelegate>(functionPtr);
                    nint surfacePtr;
                    hr = createSurface(interopPtr, swapChain.NativePointer, out surfacePtr);

                    if (hr != 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }

                    // Convert the result pointer to managed object
                    var surface = MarshalInspectable<ICompositionSurface>.FromAbi(surfacePtr);
                    Marshal.Release(surfacePtr);
                    return surface;
                }
                finally
                {
                    if (interopPtr != nint.Zero)
                    {
                        Marshal.Release(interopPtr);
                    }
                }
            }
            finally
            {
                if (compositorPtr != nint.Zero)
                {
                    Marshal.Release(compositorPtr);
                }
            }
        }
    }
}
