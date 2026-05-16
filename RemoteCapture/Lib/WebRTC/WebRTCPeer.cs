using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RemoteCapture.Lib.WebRTC
{
    /// <summary>
    /// WebRTC接続の状態
    /// </summary>
    public enum WebRTCConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed,
        Closed
    }

    /// <summary>
    /// WebRTC PeerConnectionを管理するクラス
    /// </summary>
    public class WebRTCPeer : IDisposable
    {
        private RTCPeerConnection? _peerConnection;
        private MediaStreamTrack? _videoTrack;
        private FFmpegVideoEndPoint? _videoEndPoint;

        /// <summary>
        /// 接続状態が変化したときに発生するイベント
        /// </summary>
        public event EventHandler<WebRTCConnectionState>? ConnectionStateChanged;

        /// <summary>
        /// SDPが生成されたときに発生するイベント
        /// </summary>
        public event EventHandler<string>? LocalSdpReady;

        /// <summary>
        /// エラーが発生したときに発生するイベント
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// 現在の接続状態
        /// </summary>
        public WebRTCConnectionState ConnectionState { get; private set; } = WebRTCConnectionState.Disconnected;

        /// <summary>
        /// PeerConnectionを初期化してOfferを作成
        /// </summary>
        public async Task<bool> CreateOfferAsync()
        {
            try
            {
                // 既存の接続をクローズ
                await CloseAsync();

                // FFmpegビデオエンドポイントを作成（H.264エンコーダー + VideoSourceの両方の役割）
                _videoEndPoint = new FFmpegVideoEndPoint();
                _videoEndPoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);

                // MediaEndPointsを作成
                var mediaEndPoints = new MediaEndPoints
                {
                    VideoSource = _videoEndPoint as IVideoSource,
                    VideoSink = _videoEndPoint as IVideoSink
                };

                // PeerConnectionの作成（MediaEndPointsを渡す）
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

                // FFmpegVideoEndPointを作成
                _videoEndPoint = new FFmpegVideoEndPoint();

                // MediaStreamTrackを追加
                _videoTrack = new MediaStreamTrack(
                    _videoEndPoint.GetVideoSourceFormats(), 
                    MediaStreamStatusEnum.SendOnly);
                _peerConnection.addTrack(_videoTrack);

                // ビデオフォーマットのネゴシエーション後にエンコーダーを開始
                _peerConnection.OnVideoFormatsNegotiated += async (formats) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Video formats negotiated: {formats.First().FormatName}");
                    _videoEndPoint.SetVideoSourceFormat(formats.First());

                    // エンコーダーを開始
                    await _videoEndPoint.StartVideo();

                    System.Diagnostics.Debug.WriteLine("FFmpegVideoEndPoint started after format negotiation");

                    // FFmpegVideoEndPointとRTCPeerConnectionの送信パイプラインを接続
                    if (_videoEndPoint is IVideoSource videoSource)
                    {
                        // ビデオソースのエンコード済みサンプルをRTPで送信
                        videoSource.OnVideoSourceEncodedSample += (timestamp, sample) =>
                        {
                            if (_peerConnection != null && _peerConnection.VideoLocalTrack != null)
                            {
                                _peerConnection.SendVideo((uint)timestamp, sample);
                                System.Diagnostics.Debug.WriteLine($"Sent encoded video sample: timestamp={timestamp}, length={sample.Length}");
                            }
                        };

                        System.Diagnostics.Debug.WriteLine("Connected FFmpegVideoEndPoint.OnVideoSourceEncodedSample to PeerConnection.SendVideo");
                    }
                };

                System.Diagnostics.Debug.WriteLine($"Video track added with H.264 FFmpeg encoder");

                // Offerの作成
                var offer = _peerConnection.createOffer();
                await _peerConnection.setLocalDescription(offer);

                // ICE候補の収集を待つ
                await Task.Delay(1500); // ICE候補が収集されるまで待つ

                // SDPを取得してイベント発生
                var sdp = _peerConnection.localDescription.sdp.ToString();
                LocalSdpReady?.Invoke(this, sdp);

                ConnectionState = WebRTCConnectionState.Connecting;
                ConnectionStateChanged?.Invoke(this, ConnectionState);

                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to create offer: {ex.Message}");
                ConnectionState = WebRTCConnectionState.Failed;
                ConnectionStateChanged?.Invoke(this, ConnectionState);
                return false;
            }
        }

        /// <summary>
        /// リモートSDPを設定（Answerを受信）
        /// </summary>
        public async Task<bool> SetRemoteDescriptionAsync(string sdp, string type)
        {
            try
            {
                if (_peerConnection == null)
                {
                    ErrorOccurred?.Invoke(this, "PeerConnection is not initialized");
                    System.Diagnostics.Debug.WriteLine("Error: PeerConnection is null");
                    return false;
                }

                var descriptionType = type.ToLower() == "answer" 
                    ? RTCSdpType.answer 
                    : RTCSdpType.offer;

                var remoteDescription = new RTCSessionDescriptionInit
                {
                    type = descriptionType,
                    sdp = sdp
                };

                System.Diagnostics.Debug.WriteLine($"Setting remote description (type={descriptionType})...");
                var result = _peerConnection.setRemoteDescription(remoteDescription);

                if (result == SetDescriptionResultEnum.OK)
                {
                    System.Diagnostics.Debug.WriteLine("Remote description set successfully");
                    return true;
                }
                else
                {
                    ErrorOccurred?.Invoke(this, $"Failed to set remote description: {result}");
                    System.Diagnostics.Debug.WriteLine($"Failed to set remote description: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error setting remote description: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception in SetRemoteDescriptionAsync: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// キャプチャされたフレームデータを供給
        /// </summary>
        public void SupplyFrame(byte[] pixelData, int width, int height)
        {
            if (_videoEndPoint != null && _peerConnection != null)
            {
                System.Diagnostics.Debug.WriteLine($"WebRTCPeer: Calling FFmpegVideoEndPoint.ExternalVideoSourceRawSample({width}x{height}, {pixelData.Length} bytes)");

                uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _videoEndPoint.ExternalVideoSourceRawSample(timestamp, width, height, pixelData, VideoPixelFormatsEnum.Bgra);

                // エンコード済みサンプルがあるか確認
                if (_peerConnection.VideoLocalTrack != null)
                {
                    System.Diagnostics.Debug.WriteLine($"VideoLocalTrack exists, StreamStatus={_peerConnection.VideoLocalTrack.StreamStatus}");
                }
            }
            else
            {
                if (_videoEndPoint == null)
                    System.Diagnostics.Debug.WriteLine("Warning: FFmpegVideoEndPoint is null, cannot supply frame");
                if (_peerConnection == null)
                    System.Diagnostics.Debug.WriteLine("Warning: PeerConnection is null, cannot supply frame");
            }
        }

        /// <summary>
        /// 接続をクローズ
        /// </summary>
        public async Task CloseAsync()
        {
            try
            {
                if (_videoEndPoint != null)
                {
                    await _videoEndPoint.CloseVideo();
                    _videoEndPoint.Dispose();
                    _videoEndPoint = null;
                }

                if (_peerConnection != null)
                {
                    _peerConnection.close();
                    _peerConnection.Dispose();
                    _peerConnection = null;
                }

                ConnectionState = WebRTCConnectionState.Closed;
                ConnectionStateChanged?.Invoke(this, ConnectionState);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error closing connection: {ex.Message}");
            }
        }

        /// <summary>
        /// ICE接続状態に基づいて接続状態を更新
        /// </summary>
        private void UpdateConnectionState(RTCIceConnectionState iceState)
        {
            System.Diagnostics.Debug.WriteLine($"ICE connection state changed to {iceState}");

            switch (iceState)
            {
                case RTCIceConnectionState.connected:
                    ConnectionState = WebRTCConnectionState.Connected;
                    break;
                case RTCIceConnectionState.disconnected:
                    ConnectionState = WebRTCConnectionState.Disconnected;
                    break;
                case RTCIceConnectionState.failed:
                    ConnectionState = WebRTCConnectionState.Failed;
                    break;
                case RTCIceConnectionState.closed:
                    ConnectionState = WebRTCConnectionState.Closed;
                    break;
                default:
                    ConnectionState = WebRTCConnectionState.Connecting;
                    break;
            }

            ConnectionStateChanged?.Invoke(this, ConnectionState);
        }

        public void Dispose()
        {
            CloseAsync().Wait();
        }
    }
}
