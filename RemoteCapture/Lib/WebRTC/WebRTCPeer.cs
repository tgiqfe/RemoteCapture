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
        private CaptureVideoSource? _captureVideoSource;

        // 診断用カウンター
        private long _framesSupplied = 0;
        private long _framesEncoded = 0;
        private long _framesSent = 0;
        private DateTime _lastStatsLog = DateTime.UtcNow;

        /// <summary>
        /// 供給されたフレーム数（ExternalVideoSourceRawSampleが呼ばれた回数）
        /// </summary>
        public long FramesSupplied => _framesSupplied;

        /// <summary>
        /// エンコードされたフレーム数（OnVideoSourceEncodedSampleが発生した回数）
        /// </summary>
        public long FramesEncoded => _framesEncoded;

        /// <summary>
        /// 送信されたフレーム数（SendVideoが呼ばれた回数）
        /// </summary>
        public long FramesSent => _framesSent;

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

                // CaptureVideoSourceを作成（FFmpegVideoEncoderを内部で使用）
                _captureVideoSource = new CaptureVideoSource();

                // エンコード済みサンプルをRTPで送信
                _captureVideoSource.OnVideoSourceEncodedSample += (timestamp, sample) =>
                {
                    _framesEncoded++;

                    if (_peerConnection != null)
                    {
                        _peerConnection.SendVideo(timestamp, sample);

                        if (_framesEncoded == 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SENDER] First encoded frame sent: timestamp={timestamp}, size={sample.Length}");
                        }
                    }
                };

                System.Diagnostics.Debug.WriteLine("[SENDER] CaptureVideoSource created and OnVideoSourceEncodedSample connected to SendVideo");

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

                // RTCPレポートでフレーム送信をカウント
                _peerConnection.OnSendReport += (mediaType, report) =>
                {
                    if (report != null && mediaType == SDPMediaTypesEnum.video && report.SenderReport != null)
                    {
                        var packetsSent = report.SenderReport.PacketCount;
                        if (packetsSent != _framesSent)
                        {
                            _framesSent = packetsSent;
                            _framesEncoded = packetsSent; // パケット送信 = エンコード成功と仮定

                            // 1秒ごとに統計をログ出力
                            var now = DateTime.UtcNow;
                            if ((now - _lastStatsLog).TotalSeconds >= 1.0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SENDER] STATS: Supplied={_framesSupplied}, Encoded={_framesEncoded}, Sent={_framesSent}");
                                _lastStatsLog = now;
                            }
                        }
                    }
                };

                // ビデオフォーマットのネゴシエーション後にビデオソースを開始
                _peerConnection.OnVideoFormatsNegotiated += async (formats) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SENDER] Video formats negotiated: {formats.First().FormatName}");

                    _captureVideoSource.SetVideoSourceFormat(formats.First());
                    await _captureVideoSource.StartVideo();

                    System.Diagnostics.Debug.WriteLine("[SENDER] CaptureVideoSource started");
                };

                // MediaStreamTrackを追加
                _videoTrack = new MediaStreamTrack(
                    _captureVideoSource.GetVideoSourceFormats(), 
                    MediaStreamStatusEnum.SendOnly);
                _peerConnection.addTrack(_videoTrack);

                System.Diagnostics.Debug.WriteLine($"[SENDER] Video track added with CaptureVideoSource");

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
                    System.Diagnostics.Debug.WriteLine("[SENDER] Error: PeerConnection is null");
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

                System.Diagnostics.Debug.WriteLine($"[SENDER] Setting remote description (type={descriptionType})...");
                var result = _peerConnection.setRemoteDescription(remoteDescription);

                if (result == SetDescriptionResultEnum.OK)
                {
                    System.Diagnostics.Debug.WriteLine("[SENDER] Remote description set successfully");
                    return true;
                }
                else
                {
                    ErrorOccurred?.Invoke(this, $"Failed to set remote description: {result}");
                    System.Diagnostics.Debug.WriteLine($"[SENDER] Failed to set remote description: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error setting remote description: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SENDER] Exception in SetRemoteDescriptionAsync: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// キャプチャされたフレームデータを供給
        /// </summary>
        public void SupplyFrame(byte[] pixelData, int width, int height)
        {
            if (_captureVideoSource != null && _peerConnection != null)
            {
                _framesSupplied++;

                if (_framesSupplied == 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[SENDER] First frame supplied: {width}x{height}, {pixelData.Length} bytes");
                }

                _captureVideoSource.SupplyFrame(pixelData, width, height);
            }
            else
            {
                if (_captureVideoSource == null)
                    System.Diagnostics.Debug.WriteLine("[SENDER] Warning: CaptureVideoSource is null, cannot supply frame");
                if (_peerConnection == null)
                    System.Diagnostics.Debug.WriteLine("[SENDER] Warning: PeerConnection is null, cannot supply frame");
            }
        }

        /// <summary>
        /// 接続をクローズ
        /// </summary>
        public async Task CloseAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SENDER] Closing connection. Final stats: Supplied={_framesSupplied}, Encoded={_framesEncoded}, Sent={_framesSent}");

                if (_captureVideoSource != null)
                {
                    await _captureVideoSource.CloseVideo();
                    _captureVideoSource.Dispose();
                    _captureVideoSource = null;
                }

                if (_peerConnection != null)
                {
                    _peerConnection.close();
                    _peerConnection.Dispose();
                    _peerConnection = null;
                }

                // カウンターをリセット
                _framesSupplied = 0;
                _framesEncoded = 0;
                _framesSent = 0;

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
            System.Diagnostics.Debug.WriteLine($"[SENDER] ICE connection state changed to {iceState}");

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
