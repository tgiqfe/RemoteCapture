using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RemoteCapture.Lib.WebRTC
{
    /// <summary>
    /// キャプチャされたフレームデータをWebRTC VideoFrameに変換するVideoSource
    /// </summary>
    public class CaptureVideoSource : IVideoSource, IDisposable
    {
        private bool _isStarted = false;
        private bool _isPaused = false;
        private bool _isClosed = false;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;

#pragma warning disable CS0067 // イベントが使用されていない警告を抑制
        public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;
#pragma warning restore CS0067

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
            // H.264形式を使用
            return new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.H264, 90000) // H.264、クロック90kHz
            };
        }

        /// <summary>
        /// ビデオフォーマットを設定
        /// </summary>
        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            // この実装では動的に解像度が変わるため、特に設定は不要
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
            System.Diagnostics.Debug.WriteLine($"CaptureVideoSource.SupplyFrame called: {width}x{height}, started={_isStarted}, paused={_isPaused}, closed={_isClosed}");

            if (!_isStarted || _isPaused || _isClosed)
            {
                System.Diagnostics.Debug.WriteLine($"Frame rejected: started={_isStarted}, paused={_isPaused}, closed={_isClosed}");
                return;
            }

            if (OnVideoSourceRawSample != null)
            {
                try
                {
                    uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // BGRAフォーマットでサンプルを送信
                    OnVideoSourceRawSample?.Invoke(timestamp, width, height, pixelData, VideoPixelFormatsEnum.Bgra);
                    System.Diagnostics.Debug.WriteLine($"Video frame sent: {width}x{height}, {pixelData.Length} bytes");
                }
                catch (Exception ex)
                {
                    OnVideoSourceError?.Invoke($"Error supplying video frame: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("OnVideoSourceRawSample is null");
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
