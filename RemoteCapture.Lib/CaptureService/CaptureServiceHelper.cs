using RemoteCapture.Lib.ScreenCapture;
using RemoteCapture.Lib.WindowsRuntimeHelpers;
using RemoteCapture.Lib.WebSocket;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace RemoteCapture.Lib.CaptureService
{
    public class CaptureServiceHelper : IDisposable
    {
        private GraphicsCaptureItem? _captureItem;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private IDirect3DDevice? _device;
        private SharpDX.Direct3D11.Device? _d3dDevice;
        private Texture2D? _lastFrame;
        private Windows.Graphics.SizeInt32 _lastSize;
        private readonly object _frameLock = new object();

        public static IEnumerable<MonitorInformation> GetMonitors()
        {
            var monitors = MonitorEnumerationHelper.GetMonitors();
            return monitors.Select(m => new MonitorInformation
            {
                DeviceName = m.DeviceName,
                Hmon = m.Hmon,
                IsPrimary = m.IsPrimary,
                Width = (int)m.ScreenSize.X,
                Height = (int)m.ScreenSize.Y
            });
        }

        public void StartCapture(nint monitorHandle)
        {
            _device = Direct3D11Helper.CreateDevice();
            _d3dDevice = Direct3D11Helper.CreateSharpDXDevice(_device);
            _captureItem = CaptureHelper.CreateItemForMonitor(monitorHandle);

            _framePool = Direct3D11CaptureFramePool.Create(
                _device,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _captureItem.Size);
            _lastSize = _captureItem.Size;
            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(_captureItem);
            _session.IsBorderRequired = false;
            _session.StartCapture();
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null)
                    return;

                var newSize = false;
                if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
                {
                    newSize = true;
                    _lastSize = frame.ContentSize;
                }

                using var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);

                lock (_frameLock)
                {
                    _lastFrame?.Dispose();
                    var desc = bitmap.Description;
                    desc.BindFlags = BindFlags.None;
                    desc.CpuAccessFlags = CpuAccessFlags.Read;
                    desc.Usage = ResourceUsage.Staging;
                    desc.OptionFlags = ResourceOptionFlags.None;
                    _lastFrame = new Texture2D(_d3dDevice, desc);
                    _d3dDevice!.ImmediateContext.CopyResource(bitmap, _lastFrame);
                }

                if (newSize)
                {
                    _framePool!.Recreate(
                        _device,
                        Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        _lastSize);
                }
            }
            catch
            {
                // Ignore frame capture errors
            }
        }

        public byte[]? GetCurrentFrameAsJpeg(int quality = 75)
        {
            lock (_frameLock)
            {
                if (_lastFrame == null || _d3dDevice == null)
                {
                    Logger.Debug("GetCurrentFrameAsJpeg: No frame available (_lastFrame or _d3dDevice is null)");
                    return null;
                }

                try
                {
                    Logger.Debug($"GetCurrentFrameAsJpeg: Starting JPEG encoding (size: {_lastSize.Width}x{_lastSize.Height}, quality: {quality})");

                    var dataBox = _d3dDevice.ImmediateContext.MapSubresource(
                        _lastFrame,
                        0,
                        MapMode.Read,
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
                                System.Buffer.MemoryCopy(
                                    src + y * stride,
                                    dst + y * bitmapData.Stride,
                                    bitmapData.Stride,
                                    width * 4);
                            }
                        }

                        bitmap.UnlockBits(bitmapData);

                        using var memoryStream = new MemoryStream();
                        var encoderParameters = new System.Drawing.Imaging.EncoderParameters(1);
                        encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                            System.Drawing.Imaging.Encoder.Quality, quality);

                        var jpegCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                            .First(codec => codec.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

                        bitmap.Save(memoryStream, jpegCodec, encoderParameters);
                        var result = memoryStream.ToArray();

                        Logger.Debug($"GetCurrentFrameAsJpeg: JPEG encoding completed, size: {result.Length} bytes");
                        return result;
                    }
                    finally
                    {
                        _d3dDevice.ImmediateContext.UnmapSubresource(_lastFrame, 0);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetCurrentFrameAsJpeg: Error during JPEG encoding - {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            }
        }

        public void Dispose()
        {
            lock (_frameLock)
            {
                _session?.Dispose();
                _framePool?.Dispose();
                _lastFrame?.Dispose();
                _d3dDevice?.Dispose();
            }
        }
    }

    public class MonitorInformation
    {
        public string DeviceName { get; set; } = string.Empty;
        public nint Hmon { get; set; }
        public bool IsPrimary { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public static class InputSimulator
    {
        public static void SimulateMouseEvent(MouseEventMessage mouseEvent)
        {
            MouseSimulator.ExecuteMouseEvent(mouseEvent);
        }

        public static void SimulateKeyboardEvent(KeyboardEventMessage keyboardEvent)
        {
            KeyboardSimulator.ExecuteKeyboardEvent(keyboardEvent);
        }
    }
}
