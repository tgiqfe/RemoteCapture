using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteCapture.DesktopDuplication;

/// <summary>
/// DXGI Desktop Duplication APIを使用した画面キャプチャクラス
/// </summary>
public class DesktopDuplicationCapture : IDisposable
{
    private SharpDX.Direct3D11.Device? _device;
    private OutputDuplication? _outputDuplication;
    private Texture2D? _screenTexture;
    private int _adapterIndex;
    private int _outputIndex;
    private int _width;
    private int _height;
    private readonly object _captureLock = new object();
    private bool _isCapturing = false;

    /// <summary>
    /// 利用可能なモニター一覧を取得
    /// </summary>
    public static IEnumerable<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        try
        {
            System.Diagnostics.Debug.WriteLine("GetMonitors: Creating Factory1...");
            using var factory = new Factory1();
            System.Diagnostics.Debug.WriteLine($"GetMonitors: Factory created, Adapters count = {factory.Adapters1.Length}");

            for (int adapterIndex = 0; adapterIndex < factory.Adapters1.Length; adapterIndex++)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"GetMonitors: Processing adapter {adapterIndex}...");
                    using var adapter = factory.Adapters1[adapterIndex];
                    System.Diagnostics.Debug.WriteLine($"GetMonitors: Adapter {adapterIndex}, Outputs count = {adapter.Outputs.Length}");

                    for (int outputIndex = 0; outputIndex < adapter.Outputs.Length; outputIndex++)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"GetMonitors: Processing output {outputIndex} of adapter {adapterIndex}...");
                            using var output = adapter.Outputs[outputIndex];
                            var bounds = output.Description.DesktopBounds;

                            System.Diagnostics.Debug.WriteLine($"GetMonitors: Output {outputIndex} - {output.Description.DeviceName}, Bounds: {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");

                            var monitor = new MonitorInfo
                            {
                                DeviceName = output.Description.DeviceName,
                                AdapterIndex = adapterIndex,
                                OutputIndex = outputIndex,
                                IsPrimary = adapterIndex == 0 && outputIndex == 0,
                                Width = bounds.Right - bounds.Left,
                                Height = bounds.Bottom - bounds.Top,
                                Left = bounds.Left,
                                Top = bounds.Top
                            };

                            monitors.Add(monitor);
                            System.Diagnostics.Debug.WriteLine($"GetMonitors: Added monitor: {monitor.DeviceName}");
                        }
                        catch (Exception ex)
                        {
                            // 個別の出力で失敗しても続行
                            System.Diagnostics.Debug.WriteLine($"GetMonitors: Failed to get output {outputIndex} from adapter {adapterIndex}: {ex.GetType().Name} - {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 個別のアダプターで失敗しても続行
                    System.Diagnostics.Debug.WriteLine($"GetMonitors: Failed to get adapter {adapterIndex}: {ex.GetType().Name} - {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"GetMonitors: Total monitors found = {monitors.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetMonitors: Critical error: {ex.GetType().Name} - {ex.Message}");
            throw new InvalidOperationException("Failed to enumerate monitors using DXGI", ex);
        }

        return monitors;
    }

    /// <summary>
    /// キャプチャを開始
    /// </summary>
    /// <param name="adapterIndex">アダプターインデックス</param>
    /// <param name="outputIndex">出力インデックス</param>
    public void StartCapture(int adapterIndex, int outputIndex)
    {
        lock (_captureLock)
        {
            if (_isCapturing)
            {
                throw new InvalidOperationException("Capture is already running");
            }

            try
            {
                _adapterIndex = adapterIndex;
                _outputIndex = outputIndex;

                using var factory = new Factory1();
                using var adapter = factory.GetAdapter1(adapterIndex);

                _device = new SharpDX.Direct3D11.Device(adapter);

                using var output = adapter.GetOutput(outputIndex);
                using var output1 = output.QueryInterface<Output1>();

                var bounds = output.Description.DesktopBounds;
                _width = bounds.Right - bounds.Left;
                _height = bounds.Bottom - bounds.Top;

                _outputDuplication = output1.DuplicateOutput(_device);

                var textureDesc = new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = _width,
                    Height = _height,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Staging
                };

                _screenTexture = new Texture2D(_device, textureDesc);
                _isCapturing = true;
            }
            catch (SharpDXException ex)
            {
                // SharpDX固有のエラー
                Dispose();
                throw new InvalidOperationException(
                    $"Failed to initialize Desktop Duplication for adapter {adapterIndex}, output {outputIndex}. " +
                    $"HRESULT: 0x{ex.ResultCode.Code:X8}. " +
                    $"This may occur if running in Session 0 without desktop access or if another process is already capturing.", ex);
            }
            catch (Exception ex)
            {
                // その他のエラー
                Dispose();
                throw new InvalidOperationException(
                    $"Failed to start capture for adapter {adapterIndex}, output {outputIndex}", ex);
            }
        }
    }

    /// <summary>
    /// 現在のフレームをJPEGバイト配列として取得
    /// </summary>
    /// <param name="quality">JPEG品質 (1-100)</param>
    /// <returns>JPEGバイト配列、取得できない場合はnull</returns>
    public byte[]? GetCurrentFrameAsJpeg(int quality = 75)
    {
        lock (_captureLock)
        {
            if (!_isCapturing || _outputDuplication == null || _device == null || _screenTexture == null)
            {
                return null;
            }

            try
            {
                // フレームを取得（タイムアウト0ミリ秒 = すぐに返す）
                var result = _outputDuplication.TryAcquireNextFrame(0, out var frameInfo, out var desktopResource);

                if (result.Failure || desktopResource == null)
                {
                    return null;
                }

                try
                {
                    using var tempTexture = desktopResource.QueryInterface<Texture2D>();

                    // CPUアクセス可能なテクスチャにコピー
                    _device.ImmediateContext.CopyResource(tempTexture, _screenTexture);

                    // テクスチャデータをBitmapに変換
                    var dataBox = _device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                    try
                    {
                        using var bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
                        var bitmapData = bitmap.LockBits(
                            new Rectangle(0, 0, _width, _height),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppArgb);

                        try
                        {
                            // データをコピー
                            var sourcePtr = dataBox.DataPointer;
                            var destPtr = bitmapData.Scan0;
                            var rowPitch = dataBox.RowPitch;
                            var destRowPitch = bitmapData.Stride;

                            for (int y = 0; y < _height; y++)
                            {
                                Utilities.CopyMemory(
                                    destPtr + y * destRowPitch,
                                    sourcePtr + y * rowPitch,
                                    Math.Min(rowPitch, destRowPitch));
                            }
                        }
                        finally
                        {
                            bitmap.UnlockBits(bitmapData);
                        }

                        // BitmapをJPEGに変換
                        using var ms = new MemoryStream();
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                        var jpegCodec = ImageCodecInfo.GetImageEncoders()
                            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

                        bitmap.Save(ms, jpegCodec, encoderParams);
                        return ms.ToArray();
                    }
                    finally
                    {
                        _device.ImmediateContext.UnmapSubresource(_screenTexture, 0);
                    }
                }
                finally
                {
                    desktopResource?.Dispose();
                    _outputDuplication.ReleaseFrame();
                }
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
            {
                // タイムアウト（新しいフレームがない）は正常
                return null;
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code)
            {
                // アクセスロスト（解像度変更など）
                // 再初期化が必要
                StopCapture();
                return null;
            }
        }
    }

    /// <summary>
    /// キャプチャを停止
    /// </summary>
    public void StopCapture()
    {
        lock (_captureLock)
        {
            _isCapturing = false;
            _outputDuplication?.Dispose();
            _outputDuplication = null;
            _screenTexture?.Dispose();
            _screenTexture = null;
            _device?.Dispose();
            _device = null;
        }
    }

    public void Dispose()
    {
        StopCapture();
        GC.SuppressFinalize(this);
    }
}
