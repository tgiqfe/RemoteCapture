using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RemoteCapture.Lib.WebRTC
{
    /// <summary>
    /// キャプチャされたフレームデータをWebRTC VideoFrameに変換するVideoSource
    /// FFmpegVideoEncoderを使用してH.264エンコードを行います
    /// </summary>
    public class CaptureVideoSource : IVideoSource, IDisposable
    {
        private bool _isStarted = false;
        private bool _isPaused = false;
        private bool _isClosed = false;
        private FFmpegVideoEncoder? _encoder;
        private VideoCodecsEnum _negotiatedCodec = VideoCodecsEnum.H264;
        private uint _frameCount = 0;
        private int _width = 0;
        private int _height = 0;
        private bool _isEncoderInitialised = false;
        private bool _encoderInitFailed = false;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;

#pragma warning disable CS0067
        public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;
#pragma warning restore CS0067

        public CaptureVideoSource()
        {
            try
            {
                _encoder = new FFmpegVideoEncoder();
                System.Diagnostics.Debug.WriteLine("[CaptureVideoSource] Created with FFmpegVideoEncoder");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] ERROR creating FFmpegVideoEncoder: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Stack trace: {ex.StackTrace}");
                _encoder = null;
            }
        }

        /// <summary>
        /// ビデオソースがエンコード済みサンプルを提供できるかどうか
        /// </summary>
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;

        /// <summary>
        /// ビデオソースが生のサンプルを提供できるかどうか
        /// </summary>
        public bool IsVideoSourcePaused() => _isPaused;

        /// <summary>
        /// サポートされているビデオフォーマットのリスト
        /// </summary>
        public List<VideoFormat> GetVideoSourceFormats()
        {
            // H.264形式を使用（デフォルトのFormat IDを使用）
            return new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.H264, 96) // H.264、Format ID 96 (RTPで一般的に使用される動的ペイロードタイプ)
            };
        }

        /// <summary>
        /// ビデオフォーマットを設定
        /// </summary>
        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            _negotiatedCodec = videoFormat.Codec;
            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Video format set: {videoFormat.FormatName}, Codec: {_negotiatedCodec}");
        }

        /// <summary>
        /// ビデオソースを開始
        /// </summary>
        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _isPaused = false;
                System.Diagnostics.Debug.WriteLine($"CaptureVideoSource started: _isStarted={_isStarted}, _isPaused={_isPaused}, _isClosed={_isClosed}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("CaptureVideoSource already started");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// ビデオソースを一時停止
        /// </summary>
        public Task PauseVideo()
        {
            _isPaused = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// ビデオソースを再開
        /// </summary>
        public Task ResumeVideo()
        {
            _isPaused = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// ビデオソースをクローズ
        /// </summary>
        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _isStarted = false;

                _encoder?.Dispose();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 外部からキャプチャされたフレームデータを供給
        /// </summary>
        /// <param name="pixelData">BGRA形式のピクセルデータ</param>
        /// <param name="width">フレーム幅</param>
        /// <param name="height">フレーム高さ</param>
        public void SupplyFrame(byte[] pixelData, int width, int height)
        {
            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] SupplyFrame called: {width}x{height}, {pixelData.Length} bytes, _isStarted={_isStarted}, _isPaused={_isPaused}, _isClosed={_isClosed}");

            if (!_isStarted || _isPaused || _isClosed)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Skipping frame: not started or paused or closed");
                return;
            }

            if (_encoder == null)
            {
                System.Diagnostics.Debug.WriteLine("[CaptureVideoSource] ERROR: Encoder is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Encoder check passed. _isEncoderInitialised={_isEncoderInitialised}");

            try
            {
                // エンコーダーの初期化（最初のフレーム時のみ）
                if (!_isEncoderInitialised || _width != width || _height != height)
                {
                    // FFmpegのコーデックIDを取得
                    var codecId = FFmpegConvert.GetAVCodecID(_negotiatedCodec);
                    if (codecId.HasValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Initializing encoder: {width}x{height}, codec={_negotiatedCodec}, codecId={codecId.Value}");

                        try
                        {
                            // 既に初期化されている場合は再作成
                            if (_encoder != null)
                            {
                                _encoder.Dispose();
                                _encoder = new FFmpegVideoEncoder();
                            }

                            _encoder.InitialiseEncoder(codecId.Value, width, height, 30); // 30 FPS

                            // 初期化成功後にのみサイズを設定
                            _width = width;
                            _height = height;
                            _isEncoderInitialised = true;
                            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Encoder initialized successfully");
                        }
                        catch (Exception initEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] ERROR: Failed to initialize encoder: {initEx.Message}");
                            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Stack trace: {initEx.StackTrace}");
                            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Inner exception: {initEx.InnerException?.Message}");
                            _isEncoderInitialised = false;
                            // _encoderInitFailed = true; // デバッグのため一時的に無効化
                            OnVideoSourceError?.Invoke($"Failed to initialize video encoder: {initEx.Message}");
                            return;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] ERROR: Could not get codec ID for {_negotiatedCodec}");
                        return;
                    }
                }

                // FFmpegVideoEncoder.EncodeVideoでエンコード
                // EncodeVideoは内部でコーデックIDを再取得するため、より直接的なEncodeメソッドを使用
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Calling Encode (unsafe): {width}x{height}, pixelData length={pixelData.Length}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] _encoder null check: {_encoder == null}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] _isEncoderInitialised: {_isEncoderInitialised}");

                byte[]? encodedSample = null;
                var encCodecId = FFmpegConvert.GetAVCodecID(_negotiatedCodec);
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Codec ID for {_negotiatedCodec}: {(encCodecId.HasValue ? encCodecId.Value.ToString() : "null")}");

                if (encCodecId.HasValue)
                {
                    unsafe
                    {
                        fixed (byte* pSample = pixelData)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] About to call _encoder.Encode with codecId={encCodecId.Value}, width={width}, height={height}, fps=30, keyFrame={_frameCount == 0}");

                            if (_encoder == null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] ERROR: _encoder is null before Encode call!");
                                return;
                            }

                            // BGRAからYUV420Pへの変換を含むエンコード
                            encodedSample = _encoder.Encode(
                                encCodecId.Value,
                                pSample,
                                width,
                                height,
                                30, // FPS
                                _frameCount == 0, // 最初のフレームはキーフレーム
                                AVPixelFormat.AV_PIX_FMT_BGRA // 入力ピクセルフォーマット
                            );

                            System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] _encoder.Encode completed");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] ERROR: Could not get codec ID for encoding");
                }

                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Encode returned: {(encodedSample == null ? "null" : encodedSample.Length + " bytes")}");

                if (encodedSample != null && encodedSample.Length > 0)
                {
                    // タイムスタンプを生成（ミリ秒単位）
                    uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // エンコード済みサンプルを発火
                    OnVideoSourceEncodedSample?.Invoke(timestamp, encodedSample);

                    _frameCount++;
                    if (_frameCount == 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] First encoded frame: size={encodedSample.Length} bytes");
                    }
                }
            }
            catch (NullReferenceException nex)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] NullReferenceException: {nex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Stack trace: {nex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] _encoder is null: {_encoder == null}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] _isEncoderInitialised: {_isEncoderInitialised}");
                OnVideoSourceError?.Invoke($"Null reference error encoding video frame: {nex.Message}");
            }
            catch (Exception ex)
            {
                OnVideoSourceError?.Invoke($"Error encoding video frame: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Encoding error: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CaptureVideoSource] Stack trace: {ex.StackTrace}");
            }
        }

        public void Dispose()
        {
            CloseVideo().Wait();
        }

        public void RestrictFormats(Func<VideoFormat, bool> filter)
        {
            // この実装では特に制限は設定しない
        }

        public void ForceKeyFrame()
        {
            // キーフレーム要求の処理（必要に応じて実装）
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            // 外部からの直接呼び出し用（通常は使用しない）
        }

        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
        {
            // 高速サンプリング用（この実装では未使用）
        }
    }
}
