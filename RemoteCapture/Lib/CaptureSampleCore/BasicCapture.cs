using RemoteCapture.Lib.WindowsRuntimeHelpers;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;

namespace RemoteCapture.Lib.CaptureSampleCore
{
    internal class BasicCapture : IDisposable
    {
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32 _lastSize;

        private IDirect3DDevice _device;
        private SharpDX.Direct3D11.Device _d3dDevice;
        private SharpDX.DXGI.SwapChain1 _swapChain;
        private SharpDX.Direct3D11.Texture2D _lastFrame;

        public BasicCapture(IDirect3DDevice device, GraphicsCaptureItem item)
        {
            _item = item;
            _device = device;
            _d3dDevice = Direct3D11Helper.CreateSharpDXDevice(_device);

            var dxgiFactory = new SharpDX.DXGI.Factory2();
            var description = new SharpDX.DXGI.SwapChainDescription1()
            {
                Width = _item.Size.Width,
                Height = _item.Size.Height,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SharpDX.DXGI.SampleDescription() { Count = 1, Quality = 0 },
                Usage = SharpDX.DXGI.Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = SharpDX.DXGI.Scaling.Stretch,
                SwapEffect = SharpDX.DXGI.SwapEffect.FlipSequential,
                AlphaMode = SharpDX.DXGI.AlphaMode.Premultiplied,
                Flags = SharpDX.DXGI.SwapChainFlags.None,
            };
            _swapChain = new SharpDX.DXGI.SwapChain1(dxgiFactory, _d3dDevice, ref description);

            _framePool = Direct3D11CaptureFramePool.Create(
                _device,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            _session = _framePool.CreateCaptureSession(item);
            _lastSize = item.Size;
            _framePool.FrameArrived += OnFrameArrived;
        }

        public void StartCapture()
        {
            _session.IsBorderRequired = false;
            _session.StartCapture();
        }

        public ICompositionSurface CreateSurface(Compositor compositor)
        {
            return compositor.CreateCompositionSurfaceForSwapChain(_swapChain);
        }

        public void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var newSize = false;

            using (var frame = sender.TryGetNextFrame())
            {
                if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
                {
                    newSize = true;
                    _lastSize = frame.ContentSize;
                    _swapChain.ResizeBuffers(
                        2,
                        _lastSize.Width,
                        _lastSize.Height,
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SharpDX.DXGI.SwapChainFlags.None);

                    _lastFrame?.Dispose();
                    _lastFrame = null;
                }

                using (var backBuffer = _swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0))
                using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                {
                    _d3dDevice.ImmediateContext.CopyResource(bitmap, backBuffer);

                    _lastFrame?.Dispose();
                    var desc = bitmap.Description;
                    desc.BindFlags = SharpDX.Direct3D11.BindFlags.None;
                    desc.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read;
                    desc.Usage = SharpDX.Direct3D11.ResourceUsage.Staging;
                    desc.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;
                    _lastFrame = new SharpDX.Direct3D11.Texture2D(_d3dDevice, desc);
                    _d3dDevice.ImmediateContext.CopyResource(bitmap, _lastFrame);
                }
            }

            _swapChain.Present(0, SharpDX.DXGI.PresentFlags.None);
            if (newSize)
            {
                _framePool.Recreate(
                    _device,
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _lastSize);
            }
        }

        public void SaveSnapshot(string filePath)
        {
            if (_lastFrame == null)
            {
                throw new InvalidOperationException("No frame has been captured yet.");
            }

            var dataBox = _d3dDevice.ImmediateContext.MapSubresource(
                _lastFrame,
                0,
                SharpDX.Direct3D11.MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None);

            try
            {
                var width = _lastSize.Width;
                var height = _lastSize.Height;
                var stride = dataBox.RowPitch;

                using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                unsafe
                {
                    var src = (byte*)dataBox.DataPointer;
                    var dst = (byte*)bitmapData.Scan0;

                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            src + y * stride,
                            dst + y * bitmapData.Stride,
                            bitmapData.Stride,
                            width * 4);
                    }
                }

                bitmap.UnlockBits(bitmapData);
                bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            finally
            {
                _d3dDevice.ImmediateContext.UnmapSubresource(_lastFrame, 0);
            }
        }

        public byte[] GetCurrentFrameAsPng()
        {
            if (_lastFrame == null)
            {
                return null;
            }

            var dataBox = _d3dDevice.ImmediateContext.MapSubresource(
                _lastFrame,
                0,
                SharpDX.Direct3D11.MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None);

            try
            {
                var width = _lastSize.Width;
                var height = _lastSize.Height;
                var stride = dataBox.RowPitch;

                using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                unsafe
                {
                    var src = (byte*)dataBox.DataPointer;
                    var dst = (byte*)bitmapData.Scan0;

                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            src + y * stride,
                            dst + y * bitmapData.Stride,
                            bitmapData.Stride,
                            width * 4);
                    }
                }

                bitmap.UnlockBits(bitmapData);

                using var memoryStream = new MemoryStream();
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                return memoryStream.ToArray();
            }
            finally
            {
                _d3dDevice.ImmediateContext.UnmapSubresource(_lastFrame, 0);
            }
        }

        #region Dispose Pattern

        public void Dispose()
        {
            _session?.Dispose();
            _framePool?.Dispose();
            _swapChain?.Dispose();
            _lastFrame?.Dispose();
            _d3dDevice?.Dispose();
        }

        #endregion
    }
}
