using RemoteCapture.Lib.WindowsRuntimeHelpers;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Text;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;

namespace RemoteCapture.Lib.CaptureSampleCore
{
    /// <summary>
    /// キャプチャされたフレームデータを格納するクラス
    /// </summary>
    public class CapturedFrameEventArgs : EventArgs
    {
        public byte[] PixelData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime Timestamp { get; set; }
    }

    internal class BasicCapture : IDisposable
    {
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32 _lastSize;

        private IDirect3DDevice _device;
        private SharpDX.Direct3D11.Device _d3dDevice;
        private SharpDX.DXGI.SwapChain1 _swapChain;

        /// <summary>
        /// キャプチャされたフレームデータが利用可能になったときに発生するイベント
        /// </summary>
        public event EventHandler<CapturedFrameEventArgs> FrameDataAvailable;

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
                }

                using (var backBuffer = _swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0))
                using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                {
                    _d3dDevice.ImmediateContext.CopyResource(bitmap, backBuffer);

                    // ピクセルデータを抽出してイベントを発生
                    if (FrameDataAvailable != null)
                    {
                        try
                        {
                            byte[] pixelData = Direct3D11Helper.CopyTexture2DToByteArray(_d3dDevice, bitmap);

                            var eventArgs = new CapturedFrameEventArgs
                            {
                                PixelData = pixelData,
                                Width = _lastSize.Width,
                                Height = _lastSize.Height,
                                Timestamp = DateTime.UtcNow
                            };

                            FrameDataAvailable?.Invoke(this, eventArgs);
                        }
                        catch (Exception ex)
                        {
                            // ピクセルデータの抽出エラーは無視して続行
                            System.Diagnostics.Debug.WriteLine($"Error extracting pixel data: {ex.Message}");
                        }
                    }
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

        #region Dispose Pattern

        public void Dispose()
        {
            _session?.Dispose();
            _framePool?.Dispose();
            _swapChain?.Dispose();
            _d3dDevice?.Dispose();
        }

        #endregion
    }
}
