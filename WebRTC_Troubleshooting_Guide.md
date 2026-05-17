# WebRTC画面キャプチャー トラブルシューティングガイド

## 🔍 新機能: リアルタイム診断カウンター

アプリケーションに診断用のカウンターを追加しました。これにより、問題がどの段階で発生しているかを正確に特定できます。

### 送信側（Sender）のカウンター

1. **Supplied**: キャプチャされたフレーム数（MainWindowからWebRTCPeerに供給された回数）
2. **Encoded**: FFmpegでH.264にエンコードされたフレーム数
3. **Sent**: RTPで実際に送信されたフレーム数

**正常な状態**: `Supplied ≈ Encoded ≈ Sent`（ほぼ同じ数値）

### 受信側（Receiver）のカウンター

1. **RTP**: RTPパケットとして受信したフレーム数
2. **Decoded**: FFmpegでデコードされたフレーム数
3. **Delivered**: UIに配信されたフレーム数

**正常な状態**: `RTP ≈ Decoded ≈ Delivered`（ほぼ同じ数値）

### UIでの表示

カウンター情報は接続ステータステキストに自動的に追加されます：
- 送信側: `Status: Connected | Diag: Supplied=120, Encoded=120, Sent=120`
- 受信側: `Status: Connected | Diag: RTP=115, Decoded=115, Delivered=115`

## 🚨 問題パターンと診断方法

### パターン1: 送信側で「Supplied > 0, Encoded = 0」

**症状**: フレームはキャプチャされているが、エンコードされていない

**原因**:
- FFmpegVideoEndPointが正しく開始されていない
- OnVideoFormatsNegotiatedイベントが発火していない
- FFmpegライブラリがインストールされていない、または読み込めない

**確認方法**:
```
デバッグ出力で以下を確認:
[SENDER] Video formats negotiated: H264
[SENDER] FFmpegVideoEndPoint started after format negotiation
[SENDER] Connected FFmpegVideoEndPoint.OnVideoSourceEncodedSample to PeerConnection.SendVideo
```

**解決策**:
1. FFmpegライブラリが正しくインストールされているか確認
2. SDP交換が正しく完了しているか確認（Answerを受信しているか）
3. デバッグ出力で`Video formats negotiated`が表示されるか確認

---

### パターン2: 送信側で「Encoded > 0, Sent = 0」

**症状**: フレームはエンコードされているが、RTPで送信されていない

**原因**:
- PeerConnectionが`null`になっている
- VideoLocalTrackが`null`になっている
- ICE接続が確立されていない

**確認方法**:
```
デバッグ出力で以下を確認:
[SENDER] ICE connection state changed to connected
[SENDER] WARNING: Cannot send - PeerConnection or VideoLocalTrack is null
```

**解決策**:
1. ICE接続が`connected`になっているか確認
2. `[SENDER] WARNING`メッセージがないか確認
3. SDP交換が正しく完了しているか再確認

---

### パターン3: 受信側で「RTP = 0」

**症状**: RTPパケットが全く受信されていない

**原因**:
- ICE接続が確立されていない
- ファイアウォールがWebRTCをブロックしている
- 送信側が実際には送信していない
- SDPが正しく交換されていない

**確認方法**:
```
デバッグ出力で以下を確認:
送信側: [SENDER] Sent=XXX （XXX > 0）
受信側: [RECEIVER] ICE connection state changed to connected
```

**解決策**:
1. 送信側のカウンターで`Sent > 0`を確認
2. 受信側のICE接続状態を確認
3. SDPを正しくコピー＆ペーストしたか再確認
4. ローカルネットワークでテストする
5. ファイアウォールを一時的に無効にしてテスト

---

### パターン4: 受信側で「RTP > 0, Decoded = 0」

**症状**: RTPパケットは受信しているが、デコードされていない

**原因**:
- FFmpegVideoEndPointのビデオシンクが開始されていない
- OnVideoFormatsNegotiatedイベントが発火していない
- FFmpegデコーダーの初期化に失敗している
- 受信したデータが破損している

**確認方法**:
```
デバッグ出力で以下を確認:
[RECEIVER] Video formats negotiated: H264
[RECEIVER] FFmpegVideoEndPoint video sink started after format negotiation
[RECEIVER] First RTP frame received: XXXX bytes
```

**解決策**:
1. FFmpegライブラリが正しくインストールされているか確認
2. `Video formats negotiated`が表示されるか確認
3. `video sink started`が表示されるか確認
4. ビデオフォーマットが一致しているか確認（両側でH.264）

---

### パターン5: 受信側で「Decoded > 0, Delivered = 0」

**症状**: フレームはデコードされているが、UIに表示されていない

**原因**:
- `VideoFrameReceived`イベントハンドラが登録されていない
- イベントハンドラ内でエラーが発生している
- UIスレッドの問題
- Image コントロールのVisibilityが`Collapsed`になっている

**確認方法**:
```
デバッグ出力で以下を確認:
[RECEIVER] Decoded frame received: 1920x1080, XXXX bytes
MainWindow: Displayed receiver frame: 1920x1080
Error displaying receiver frame: XXX （エラーメッセージ）
```

**解決策**:
1. `VideoFrameReceived`イベントが購読されているか確認
2. ReceiverVideoImageのVisibilityが`Visible`になっているか確認
3. ReceiverModeRadioが選択されているか確認
4. デバッグ出力でエラーメッセージを確認

---

## 📋 診断手順（ステップバイステップ）

### ステップ1: アプリケーションを起動してキャプチャ開始

1. **送信側**で「Sender」モードを選択
2. 「Use Primary Monitor」をクリック
3. Visual Studioの出力ウィンドウで以下を確認：
   ```
   Frame: 1920x1080, Size: XXXX bytes, FPS: XX.X
   ```
   ↓ これが表示されれば**キャプチャは成功**

### ステップ2: WebRTC接続を確立

4. **送信側**で「Create Offer (SDP)」をクリック
5. Local SDPをコピー
6. 出力ウィンドウで以下を確認：
   ```
   [SENDER] Video track added with H.264 FFmpeg encoder
   ```

7. **受信側（別アプリまたは別PC）**で「Receiver」モードを選択
8. コピーしたOfferをテキストボックスに貼り付け
9. 「Set Offer & Create Answer」をクリック
10. Answer SDPをコピー

11. **送信側**に戻って、Answer SDPを貼り付け
12. 「Set Remote Answer」をクリック

### ステップ3: 接続状態を確認

13. 両側で接続ステータスが`Connected`になるか確認
14. 出力ウィンドウで以下を確認：
    ```
    送信側: [SENDER] ICE connection state changed to connected
    受信側: [RECEIVER] ICE connection state changed to connected
    ```

### ステップ4: カウンターで診断

15. **送信側**のステータステキストを確認：
    ```
    Status: Connected | Diag: Supplied=120, Encoded=120, Sent=120
    ```
    - 3つの数値がほぼ同じ → ✅ 正常
    - Supplied > Encoded → ⚠️ エンコーダーの問題（パターン1参照）
    - Encoded > Sent → ⚠️ 送信の問題（パターン2参照）

16. **受信側**のステータステキストを確認：
    ```
    Status: Connected | Diag: RTP=115, Decoded=115, Delivered=115
    ```
    - 3つの数値がほぼ同じ → ✅ 正常
    - RTP = 0 → ⚠️ 受信の問題（パターン3参照）
    - RTP > Decoded → ⚠️ デコーダーの問題（パターン4参照）
    - Decoded > Delivered → ⚠️ UI表示の問題（パターン5参照）

### ステップ5: 出力ウィンドウで詳細確認

17. Visual Studioの出力ウィンドウで`[DIAGNOSTIC] WARNING`を検索
18. 警告がある場合、該当するパターンの解決策を試す

---

## 🛠️ よくある問題と即座の解決策

### 問題: 「FFmpeg関連のDLLが見つからない」エラー

**解決策**:
1. NuGetパッケージマネージャーで`SIPSorceryMedia.FFmpeg`を確認
2. FFmpegネイティブライブラリをインストール：
   ```
   Install-Package SIPSorceryMedia.FFmpeg
   ```
3. プロジェクトをクリーンしてリビルド

### 問題: ICE接続が`failed`になる

**解決策**:
1. 同じPC上で送信側と受信側を別々のアプリインスタンスで起動してテスト
2. STUNサーバーが利用可能か確認（`stun.l.google.com:19302`）
3. ファイアウォールを一時的に無効化してテスト
4. 必要に応じてTURNサーバーを追加

### 問題: 映像が表示されるが遅延が大きい

**解決策**:
1. フレームレートを下げる（30FPS または 15FPS）
2. ネットワーク品質を確認
3. デバッグ出力を無効化してパフォーマンス向上

---

## 📊 正常動作時のログ例

### 送信側（Sender）の正常なログ
```
[SENDER] Video track added with H.264 FFmpeg encoder
[SENDER] Video formats negotiated: H264
[SENDER] FFmpegVideoEndPoint started after format negotiation
[SENDER] Connected FFmpegVideoEndPoint.OnVideoSourceEncodedSample to PeerConnection.SendVideo
[SENDER] Setting remote description (type=answer)...
[SENDER] Remote description set successfully
[SENDER] ICE connection state changed to connected
[SENDER] First frame supplied: 1920x1080, 8294400 bytes
[SENDER] First frame sent: timestamp=1234567890, length=12345
[SENDER] STATS: Supplied=30, Encoded=30, Sent=30
[SENDER] STATS: Supplied=60, Encoded=60, Sent=60
```

### 受信側（Receiver）の正常なログ
```
[RECEIVER] Video track added for receiving (H.264)
[RECEIVER] Remote offer set successfully
[RECEIVER] Answer created and local description set
[RECEIVER] Video formats negotiated: H264
[RECEIVER] FFmpegVideoEndPoint video sink started after format negotiation
[RECEIVER] ICE connection state changed to connected
[RECEIVER] First RTP frame received: 12345 bytes, timestamp=1234567890
[RECEIVER] First frame decoded: 1920x1080, 8294400 bytes, format=Bgra
[RECEIVER] STATS: RTP Received=30, Decoded=30, Delivered=30
[RECEIVER] STATS: RTP Received=60, Decoded=60, Delivered=60
```

---

## 🎯 まとめ

1. **UIのカウンター**を確認して、どの段階で問題が発生しているかを特定
2. **デバッグ出力**で詳細なエラーメッセージを確認
3. 該当する**問題パターン**の解決策を試す
4. それでも解決しない場合は、ログ全体を確認して追加のエラーがないかチェック

このガイドに従えば、受信側で映像が表示されない問題を確実に診断・解決できます。

### 1. **送信側 (WebRTCPeer.cs) の問題**

#### 問題1: FFmpegVideoEndPointの重複作成
- **場所**: CreateOfferAsync()メソッド
- **問題**: 64行目と98行目で2回FFmpegVideoEndPointを作成していた
- **影響**: 最初のインスタンスが無駄になり、リソースが無駄遣いされていた
- **修正**: 重複を削除し、1回のみ作成

#### 問題2: MediaStreamTrackの追加が欠落
- **場所**: CreateOfferAsync()メソッド
- **問題**: 最初の修正でMediaStreamTrackの追加コードが削除されていた
- **影響**: ビデオトラックがPeerConnectionに追加されず、映像が送信されない
- **修正**: OnVideoFormatsNegotiatedイベント設定後にMediaStreamTrackを追加

#### 問題3: OnVideoFormatsNegotiatedイベントの接続タイミング
- **場所**: CreateOfferAsync()メソッド
- **問題**: ビデオトラックを追加した後にOnVideoFormatsNegotiatedを設定していた
- **影響**: ネゴシエーションが既に完了している場合、イベントハンドラが実行されない
- **修正**: ビデオトラック追加**前**にイベントハンドラを設定

### 2. **受信側 (WebRTCReceiver.cs) の問題**

#### 問題4: ビデオフォーマットのネゴシエーション処理がない
- **場所**: SetRemoteOfferAndCreateAnswerAsync()メソッド
- **問題**: 受信側でOnVideoFormatsNegotiatedイベントハンドラが設定されていなかった
- **影響**: デコーダーが開始されず、受信した映像をデコードできない
- **修正**: OnVideoFormatsNegotiatedイベントハンドラを追加し、デコーダーを開始

#### 問題5: ビデオシンクの開始処理がない
- **場所**: SetRemoteOfferAndCreateAnswerAsync()メソッド
- **問題**: FFmpegVideoEndPointのビデオシンク（デコーダー）を開始する処理がなかった
- **影響**: デコーダーが動作せず、映像を表示できない
- **修正**: OnVideoFormatsNegotiatedイベント内で`await _videoEndPoint.StartVideoSink()`を呼び出し

### 3. **ログの改善**

すべてのログメッセージに`[SENDER]`または`[RECEIVER]`のプレフィックスを追加し、どちら側の処理かを明確にしました。

## 診断方法

### デバッグ出力の確認手順

アプリケーションを実行して、Visual StudioのOutput（出力）ウィンドウでDebugログを確認してください。

#### 送信側の正常な流れ:
```
[SENDER] Video track added with H.264 FFmpeg encoder
[SENDER] Video formats negotiated: H264
[SENDER] FFmpegVideoEndPoint started after format negotiation
[SENDER] Connected FFmpegVideoEndPoint.OnVideoSourceEncodedSample to PeerConnection.SendVideo
[SENDER] Calling FFmpegVideoEndPoint.ExternalVideoSourceRawSample(1920x1080, 8294400 bytes)
[SENDER] VideoLocalTrack exists, StreamStatus=SendOnly
[SENDER] Sent encoded video sample: timestamp=1234567890, length=12345
[SENDER] ICE connection state changed to connected
```

#### 受信側の正常な流れ:
```
[RECEIVER] Video track added for receiving (H.264)
[RECEIVER] Remote offer set successfully
[RECEIVER] Answer created and local description set
[RECEIVER] Video formats negotiated: H264
[RECEIVER] FFmpegVideoEndPoint video sink started after format negotiation
[RECEIVER] ICE connection state changed to connected
[RECEIVER] RTP video frame received: 12345 bytes, timestamp=1234567890
[RECEIVER] Decoded frame received: 1920x1080, 8294400 bytes, format=Bgra
```

### 問題発生時のチェックポイント

#### 1. **送信側でフレームが送信されない場合**
- `[SENDER] Video formats negotiated`が出力されているか確認
- `[SENDER] FFmpegVideoEndPoint started after format negotiation`が出力されているか確認
- `[SENDER] Sent encoded video sample`が出力されているか確認
- `[SENDER] WARNING: VideoLocalTrack is null`が出ていないか確認

#### 2. **受信側でフレームが表示されない場合**
- `[RECEIVER] Video formats negotiated`が出力されているか確認
- `[RECEIVER] FFmpegVideoEndPoint video sink started after format negotiation`が出力されているか確認
- `[RECEIVER] RTP video frame received`が出力されているか確認
- `[RECEIVER] Decoded frame received`が出力されているか確認

#### 3. **ICE接続の問題**
- 送信側と受信側の両方で`ICE connection state changed to connected`が出力されるか確認
- `ICE connection state changed to failed`が出た場合はネットワーク設定を確認
  - STUNサーバーに接続できているか
  - ファイアウォールがWebRTCをブロックしていないか
  - ローカルネットワークでテストしているか

#### 4. **フォーマットネゴシエーションの問題**
- 送信側と受信側の両方で`Video formats negotiated`が出力されるか確認
- H.264コーデックが利用可能か確認（FFmpegライブラリが正しくインストールされているか）

### トラブルシューティングコマンド

Visual Studioの出力ウィンドウで以下のフィルターを使用すると便利です:

1. **送信側のみ表示**: フィルター `[SENDER]`
2. **受信側のみ表示**: フィルター `[RECEIVER]`
3. **警告のみ表示**: フィルター `WARNING`
4. **エラーのみ表示**: フィルター `Error` または `Exception`

### よくある問題と解決策

#### 問題: 「VideoLocalTrack is null」
- **原因**: PeerConnectionが正しく初期化されていない、またはビデオトラックが追加されていない
- **解決策**: CreateOfferAsync()が完了しているか確認。SDPの交換が正しく行われているか確認

#### 問題: 「FFmpegVideoEndPoint is null」
- **原因**: WebRTCPeerが初期化されていない
- **解決策**: 画面キャプチャを開始する前にCreateOfferAsync()を呼び出す

#### 問題: 「RTP video frame received」が出るが「Decoded frame received」が出ない
- **原因**: デコーダーが開始されていない、またはフォーマットネゴシエーションが失敗
- **解決策**: OnVideoFormatsNegotiatedイベントが発火しているか確認。FFmpegが正しくインストールされているか確認

#### 問題: ICE接続が「failed」になる
- **原因**: ネットワーク接続の問題、または両端が異なるネットワークにいる
- **解決策**: 
  - ローカルネットワークでテストする
  - TURNサーバーを設定する（現在はSTUNのみ）
  - ファイアウォール設定を確認

## 修正箇所の詳細

### WebRTCPeer.cs の変更点
1. 重複したFFmpegVideoEndPoint作成を削除
2. OnVideoFormatsNegotiatedイベントハンドラをビデオトラック追加前に設定
3. MediaStreamTrackの追加を復元
4. すべてのログに`[SENDER]`プレフィックスを追加
5. エラー時のログを強化

### WebRTCReceiver.cs の変更点
1. OnVideoFormatsNegotiatedイベントハンドラを追加
2. ビデオシンクの開始処理（StartVideoSink）を追加
3. すべてのログに`[RECEIVER]`プレフィックスを追加
4. エラー時のログを強化

## 次のステップ

これらの修正により、以下のことが可能になります:

1. **送信側**: 
   - キャプチャしたフレームがH.264でエンコードされる
   - エンコードされたフレームがRTPで送信される
   - 接続状態が正しく追跡される

2. **受信側**:
   - RTPパケットを受信する
   - H.264でデコードする
   - デコードされたフレームをUIに表示する

3. **デバッグ**:
   - 詳細なログで問題箇所を特定できる
   - 送信側と受信側のログを区別できる

アプリケーションを実行して、上記のデバッグ出力を確認することで、どこに問題があるかを正確に特定できます。
