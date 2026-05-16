using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace RemoteCapture.Lib.WindowsRuntimeHelpers
{
    internal static class Direct3D11Helper
    {
        static Guid IInspectable = new Guid("AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90");
        static Guid ID3D11Resource = new Guid("dc8e63f3-d12b-4952-b47b-5e45026a862d");
        static Guid IDXGIAdapter3 = new Guid("645967A4-1392-4310-A798-8053CE3E93FD");
        static Guid ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");
        static Guid ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        internal interface IDirect3DDxgiInterfaceAccess
        {
            nint GetInterface(
                [In] ref Guid iid);
        }

        [DllImport(
            "d3d11.dll",
            EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
            SetLastError = true,
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        static extern uint CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

        [DllImport(
            "d3d11.dll",
            EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface",
            SetLastError = true,
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        static extern uint CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, out nint graphicsSurface);

        public static IDirect3DDevice CreateDevice(bool useWARP = false)
        {
            var d3dDevice = new SharpDX.Direct3D11.Device(
                useWARP ? SharpDX.Direct3D.DriverType.Software : SharpDX.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport);
            var device = CreateDirect3DDeviceFromSharpDXDevice(d3dDevice);
            return device;
        }

        public static IDirect3DDevice CreateDirect3DDeviceFromSharpDXDevice(SharpDX.Direct3D11.Device d3dDevice)
        {
            IDirect3DDevice device = null;

            // Acquire the DXGI interface for the Direct3D device.
            using (var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device3>())
            {
                // Wrap the native device using a WinRT interop object.
                uint hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out nint pUnknown);
                if (hr == 0)
                {
                    device = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                    Marshal.Release(pUnknown);
                }
            }

            return device;
        }

        public static IDirect3DSurface CreateDirect3DSurfaceFromSharpDXTexture(Texture2D texture)
        {
            IDirect3DSurface surface = null;

            // Acquire the DXGI interface for the Direct3D surface.
            using (var dxgiSurface = texture.QueryInterface<Surface>())
            {
                // Wrap the native device using a WinRT interop object.
                uint hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out nint pUnknown);
                if (hr == 0)
                {
                    surface = MarshalInterface<IDirect3DSurface>.FromAbi(pUnknown);
                    Marshal.Release(pUnknown);
                }
            }
            return surface;
        }

        public static SharpDX.Direct3D11.Device CreateSharpDXDevice(IDirect3DDevice device)
        {
            var access = device.As<IDirect3DDxgiInterfaceAccess>();
            var d3dPointer = access.GetInterface(ID3D11Device);
            var d3dDevice = new SharpDX.Direct3D11.Device(d3dPointer);
            return d3dDevice;
        }

        public static Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var d3dPointer = access.GetInterface(ID3D11Texture2D);
            var d3dSurface = new Texture2D(d3dPointer);
            return d3dSurface;
        }

        public static byte[] CopyTexture2DToByteArray(SharpDX.Direct3D11.Device device, Texture2D texture)
        {
            var textureDesc = texture.Description;

            // CPU読み取り可能なステージングテクスチャを作成
            var stagingTextureDesc = new Texture2DDescription
            {
                Width = textureDesc.Width,
                Height = textureDesc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = textureDesc.Format,
                Usage = ResourceUsage.Staging,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None
            };

            using (var stagingTexture = new Texture2D(device, stagingTextureDesc))
            {
                // GPUからCPUアクセス可能なテクスチャにコピー
                device.ImmediateContext.CopyResource(texture, stagingTexture);

                // データをマップして読み取り
                var dataBox = device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                try
                {
                    // ピクセルデータのサイズを計算 (BGRA8 = 4 bytes per pixel)
                    int bytesPerPixel = 4; // Format.B8G8R8A8_UNorm
                    int totalBytes = textureDesc.Width * textureDesc.Height * bytesPerPixel;
                    byte[] pixelData = new byte[totalBytes];

                    // 行ごとにデータをコピー (RowPitchを考慮)
                    int rowPitch = dataBox.RowPitch;
                    int rowWidth = textureDesc.Width * bytesPerPixel;

                    for (int y = 0; y < textureDesc.Height; y++)
                    {
                        Marshal.Copy(
                            dataBox.DataPointer + (y * rowPitch),
                            pixelData,
                            y * rowWidth,
                            rowWidth);
                    }

                    return pixelData;
                }
                finally
                {
                    device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                }
            }
        }
    }
}
