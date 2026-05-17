# WebRTC送信側エンコーディング問題の分析

## 問題の概要

送信側で画面キャプチャは正常に動作しているが（55 FPS @ 3440x1440）、受信側で映像が表示されない。

## 根本原因

**送信側でH.264エンコードが実行されていない**

### ログから確認された事実

#### 送信側ログ
```
Calling SupplyFrame: 3440x1440, 19814400 bytes
[DIAGNOSTIC] WARNING: Frames supplied but NOT encoded! Check FFmpeg encoder.
[SENDER] DEBUG: _videoEndPoint is IVideoSource = False
[SENDER] ERROR: Failed to cast FFmpegVideoEndPoint to IVideoSource!
[SENDER] ERROR: _videoEndPoint interfaces: IVideoSink, IDisposable
```

#### 受信側ログ
```
[RECEIVER] Video formats negotiated: H264
[RECEIVER] FFmpegVideoEndPoint video sink started after format negotiation
[RECEIVER] ICE connection state changed to connected
```

**結論**: 
- 受信側のICE接続、H.264ネゴシエーション、デコーダー初期化はすべて成功
- 受信側でRTPフレーム受信のログが一切ない
- 送信側でエンコードが実行されていない

## 技術的な問題

### SIPSorcery WebRTC のアーキテクチャ

1. **送信側に必要なコンポーネント**:
   - `IVideoSource`: ビデオフレームのソース（エンコード済みまたは生フレーム）
   - `VideoEncoder`: H.264エンコーダー
   - `RTCPeerConnection`: WebRTC接続管理

2. **`FFmpegVideoEndPoint` の役割**:
   - `IVideoSink` として機能（受信・デコード用）
   - `IVideoSource` インターフェースを実装していない
   - **送信側のエンコーダーとしては使用できない**

3. **`FFmpegVideoEncoder` の役割**:
   - H.264エンコーダーとして機能
   - しかし、`IVideoSource` インターフェースを実装していない可能性
   - APIドキュメントが不足しており、正しい使用方法が不明

### 現在の実装の問題点

#### `CaptureVideoSource.cs`
- `IVideoSource` インターフェースを実装
- `OnVideoSourceRawSample` イベントで生フレームを提供
- **`OnVideoSourceEncodedSample` イベントを発火していない**
- H.264エンコード機能がない

#### `WebRTCPeer.cs`
- `CaptureVideoSource` を `MediaStreamTrack` に接続
- `RTCPeerConnection` は `IVideoSource.OnVideoSourceEncodedSample` イベントをリッスンしてRTP送信を行う
- しかし、`CaptureVideoSource` はエンコード済みフレームを提供しないため、何も送信されない

## 試した解決策

### 1. `FFmpegVideoEncoder` を直接使用
```csharp
private FFmpegVideoEncoder? _videoEncoder;
_videoEncoder.InitialiseEncoder(formats.First()); // ❌ パラメーターが不足
_videoEncoder.OnVideoSourceEncodedSample // ❌ このイベントが存在しない
```

**結果**: APIが不明で実装不可

### 2. `CaptureVideoSource` に `FFmpegVideoEncoder` を統合
```csharp
_encoder = new FFmpegVideoEncoder();
_encoder.ExternalVideoSourceRawSample(timestamp, width, height, pixelData, VideoPixelFormatsEnum.Bgra);
```

**結果**: このメソッドが存在しない

### 3. `FFmpegVideoEndPoint` を送信側で使用
```csharp
_videoEndPoint = new FFmpegVideoEndPoint();
_videoEndPoint.ExternalVideoSourceRawSample(...);
```

**結果**: `FFmpegVideoEndPoint` は `IVideoSink` のみを実装し、`IVideoSource` を実装していないため、エンコード済みフレームを提供できない

## 必要な解決策

### オプション 1: SIPSorcery の正しいエンコーダーAPIを見つける

**SIPSorceryMedia.FFmpeg** パッケージのドキュメントまたはサンプルコードを参照して、以下を確認する:
1. H.264エンコーダーの正しいクラス名とAPI
2. 生フレーム → H.264エンコード → `OnVideoSourceEncodedSample` イベント発火の正しいフロー
3. `RTCPeerConnection` との正しい接続方法

### オプション 2: SIPSorcery のサンプルプロジェクトを参考にする

以下のようなサンプルを探す:
- 画面キャプチャ → WebRTC送信
- カスタムビデオソース → H.264エンコード → WebRTC
- `IVideoSource` の実装例

### オプション 3: VP8エンコーダーを試す

H.264の代わりにVP8エンコーダーを使用する（SIPSorceryで VP8のほうがサポートが充実している可能性がある）

### オプション 4: 低レベルFFmpeg APIを直接使用

`FFmpeg.AutoGen` を使用して、生フレームを直接H.264にエンコードし、`OnVideoSourceEncodedSample` イベントを手動で発火させる

## 次のステップ

1. **SIPSorceryMedia.FFmpegのソースコードを確認**
   - GitHub: https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg
   - `FFmpegVideoEncoder` の正しいAPI
   - サンプルコードやユニットテスト

2. **SIPSorceryの公式サンプルを確認**
   - GitHub: https://github.com/sipsorcery-org/sipsorcery
   - WebRTC送信側の実装例

3. **代替エンコーダーの検討**
   - `VideoEncoderEndPoint`
   - `VPXVideoEncoder`（VP8/VP9）

4. **コミュニティに質問**
   - SIPSorcery GitHub Discussions
   - Stack Overflow

## 現在のコード状態

### ビルド状態
✅ **ビルド成功** - コンパイルエラーなし

### 実行時の動作
- ✅ 送信側: 画面キャプチャ正常（55 FPS）
- ✅ 送信側: WebRTC接続確立
- ✅ 受信側: WebRTC接続確立
- ✅ 受信側: H.264ネゴシエーション成功
- ✅ 受信側: デコーダー初期化成功
- ❌ 送信側: H.264エンコード **実行されていない**
- ❌ 受信側: RTPフレーム **受信していない**
- ❌ 受信側: 映像表示 **なし**

## 参考資料

### SIPSorcery関連
- [SIPSorcery GitHub](https://github.com/sipsorcery-org/sipsorcery)
- [SIPSorceryMedia.FFmpeg GitHub](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg)
- [SIPSorcery Examples](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples)

### WebRTC関連
- [WebRTC Specification](https://www.w3.org/TR/webrtc/)
- [H.264 in WebRTC](https://webrtchacks.com/h264-packetization-in-webrtc/)

