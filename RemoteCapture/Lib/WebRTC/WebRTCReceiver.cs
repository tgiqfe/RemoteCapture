using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace RemoteCapture.Lib.WebRTC
{
    /// <summary>
    /// WebRTC受信側を管理するクラス
    /// </summary>
    public class WebRTCReceiver : IDisposable
    {
        private RTCPeerConnection? _peerConnection;
        private FFmpegVideoEndPoint? _videoEndPoint;

        // 診断用カウンター
        private long _rtpFramesReceived = 0;
        private long _framesDecoded = 0;
        private long _framesDelivered = 0;
        private DateTime _lastStatsLog = DateTime.UtcNow;

        /// <summary>
        /// 受信したRTPフレーム数（OnVideoFrameReceivedが発生した回数）
        /// </summary>
        public long RtpFramesReceived => _rtpFramesReceived;

        /// <summary>
        /// デコードされたフレーム数（OnVideoSinkDecodedSampleが発生した回数）
        /// </summary>
        public long FramesDecoded => _framesDecoded;

        /// <summary>
        /// 配信されたフレーム数（VideoFrameReceivedイベントを発生させた回数）
        /// </summary>
        public long FramesDelivered => _framesDelivered;

        /// <summary>
        /// 接続状態が変化したときに発生するイベント
        /// </summary>
        public event EventHandler<WebRTCConnectionState>? ConnectionStateChanged;

        /// <summary>
        /// SDPが生成されたときに発生するイベント（Answer）
        /// </summary>
        public event EventHandler<string>? LocalSdpReady;

        /// <summary>
        /// エラーが発生したときに発生するイベント
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// ビデオフレームを受信したときに発生するイベント
        /// </summary>
        public event EventHandler<VideoFrameEventArgs>? VideoFrameReceived;

        /// <summary>
        /// 現在の接続状態
        /// </summary>
        public WebRTCConnectionState ConnectionState { get; private set; } = WebRTCConnectionState.Disconnected;

        /// <summary>
        /// リモートOfferを受け取ってAnswerを生成
        /// </summary>
        public async Task<bool> SetRemoteOfferAndCreateAnswerAsync(string sdp)
        {
            try
            {
                // 既存の接続をクローズ
                await CloseAsync();

                // PeerConnectionの作成
                var config = new RTCConfiguration
                {
                    iceServers = new List<RTCIceServer>
                    {
                        new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                    },
                    X_UseRtpFeedbackProfile = true
                };

                _peerConnection = new RTCPeerConnection(config);

                // イベントハンドラの設定
                _peerConnection.oniceconnectionstatechange += (state) =>
                {
                    UpdateConnectionState(state);
                };

                _peerConnection.onconnectionstatechange += (state) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Peer connection state changed to {state}.");
                };

                // FFmpegビデオエンドポイントを作成（H.264デコード用）
                _videoEndPoint = new FFmpegVideoEndPoint();
                _videoEndPoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);

                // デコード済みフレームを受け取る
                _videoEndPoint.OnVideoSinkDecodedSample += OnDecodedVideoFrame;

                // ビデオフォーマットのネゴシエーション後にデコーダーを開始
                _peerConnection.OnVideoFormatsNegotiated += async (formats) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[RECEIVER] Video formats negotiated: {formats.First().FormatName}");
                    _videoEndPoint.SetVideoSinkFormat(formats.First());

                    // デコーダーを開始
                    await _videoEndPoint.StartVideoSink();

                    System.Diagnostics.Debug.WriteLine("[RECEIVER] FFmpegVideoEndPoint video sink started after format negotiation");
                };

                // MediaStreamTrackを作成
                var videoTrack = new MediaStreamTrack(_videoEndPoint.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
                _peerConnection.addTrack(videoTrack);

                // 受信したビデオフレームをエンドポイントに渡す
                _peerConnection.OnVideoFrameReceived += (remoteEp, timestamp, frame, format) =>
                {
                    _rtpFramesReceived++;

                    if (_rtpFramesReceived == 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RECEIVER] First RTP frame received: {frame.Length} bytes, timestamp={timestamp}");
                    }
                    else if (_rtpFramesReceived % 30 == 0)  // 30フレームごとにログ出力
                    {
                        System.Diagnostics.Debug.WriteLine($"[RECEIVER] RTP frames received: {_rtpFramesReceived}");
                    }

                    _videoEndPoint.GotVideoFrame(remoteEp, timestamp, frame, format);
                };

                System.Diagnostics.Debug.WriteLine("[RECEIVER] Video track added for receiving (H.264)");

                // Remote Offer (送信側からのOffer)を設定
                var offerInit = new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = sdp
                };

                var result = _peerConnection.setRemoteDescription(offerInit);
                if (result != SetDescriptionResultEnum.OK)
                {
                    ErrorOccurred?.Invoke(this, $"Failed to set remote offer: {result}");
                    System.Diagnostics.Debug.WriteLine($"[RECEIVER] Failed to set remote offer: {result}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("[RECEIVER] Remote offer set successfully");

                // Local Answer (このアプリからのAnswer)を作成
                var answer = _peerConnection.createAnswer();
                await _peerConnection.setLocalDescription(answer);

                // ICE候補の収集を待つ
                await Task.Delay(1500);

                // SDPを取得してイベント発生
                var answerSdp = _peerConnection.localDescription.sdp.ToString();
                LocalSdpReady?.Invoke(this, answerSdp);

                System.Diagnostics.Debug.WriteLine("[RECEIVER] Answer created and local description set");

                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error setting remote offer: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[RECEIVER] Exception in SetRemoteOfferAndCreateAnswerAsync: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private void OnDecodedVideoFrame(byte[] frame, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
        {
            try
            {
                _framesDecoded++;

                if (_framesDecoded == 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[RECEIVER] First frame decoded: {width}x{height}, {frame.Length} bytes, format={pixelFormat}");
                }

                // デコード済みフレームをイベントで通知
                var eventArgs = new VideoFrameEventArgs
                {
                    Frame = frame,
                    Width = (int)width,
                    Height = (int)height,
                    Format = null
                };

                VideoFrameReceived?.Invoke(this, eventArgs);
                _framesDelivered++;

                // 1秒ごとに統計をログ出力
                var now = DateTime.UtcNow;
                if ((now - _lastStatsLog).TotalSeconds >= 1.0)
                {
                    System.Diagnostics.Debug.WriteLine($"[RECEIVER] STATS: RTP Received={_rtpFramesReceived}, Decoded={_framesDecoded}, Delivered={_framesDelivered}");
                    _lastStatsLog = now;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RECEIVER] Error handling decoded video frame: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void UpdateConnectionState(RTCIceConnectionState state)
        {
            WebRTCConnectionState newState = state switch
            {
                RTCIceConnectionState.checking => WebRTCConnectionState.Connecting,
                RTCIceConnectionState.connected => WebRTCConnectionState.Connected,
                RTCIceConnectionState.failed => WebRTCConnectionState.Failed,
                RTCIceConnectionState.disconnected => WebRTCConnectionState.Disconnected,
                RTCIceConnectionState.closed => WebRTCConnectionState.Closed,
                _ => WebRTCConnectionState.Disconnected
            };

            if (ConnectionState != newState)
            {
                ConnectionState = newState;
                ConnectionStateChanged?.Invoke(this, newState);
                System.Diagnostics.Debug.WriteLine($"[RECEIVER] ICE connection state changed to {state}");
            }
        }

        /// <summary>
        /// 接続をクローズ
        /// </summary>
        public async Task CloseAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[RECEIVER] Closing connection. Final stats: RTP Received={_rtpFramesReceived}, Decoded={_framesDecoded}, Delivered={_framesDelivered}");

            if (_videoEndPoint != null)
            {
                await _videoEndPoint.CloseVideoSink();
                _videoEndPoint.Dispose();
                _videoEndPoint = null;
            }

            if (_peerConnection != null)
            {
                _peerConnection.Close("Closing receiver");
                _peerConnection.Dispose();
                _peerConnection = null;
            }

            // カウンターをリセット
            _rtpFramesReceived = 0;
            _framesDecoded = 0;
            _framesDelivered = 0;

            ConnectionState = WebRTCConnectionState.Closed;
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseAsync().Wait();
        }
    }

    /// <summary>
    /// 受信したビデオフレームのイベント引数
    /// </summary>
    public class VideoFrameEventArgs : EventArgs
    {
        public byte[] Frame { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public VideoFormat? Format { get; set; }
    }
}
