# RemoteCapture.Service

RemoteCapture.Serviceは、Windowsサービスとしてモニターキャプチャの送信側を実装したプロジェクトです。

## 機能

- モニターのリアルタイムキャプチャ
- WebSocketサーバーによる画像配信（JPEG圧縮）
- リモートマウス・キーボード操作の受信と実行
- 設定可能なフレームレート、画質、ポート番号

## 設定 (appsettings.json)

```json
{
  "RemoteCapture": {
	"WebSocketPort": 8080,        // WebSocketサーバーのポート番号
	"MaxClients": 4,               // 最大接続クライアント数
	"FrameRate": 30,               // フレームレート (fps)
	"JpegQuality": 75,             // JPEG画質 (1-100)
	"MonitorIndex": 0              // キャプチャするモニターのインデックス (0から開始)
  }
}
```

## Windowsサービスとしてのインストール

### 前提条件

- Windows 10/11 (バージョン 10.0.22000.0 以降)
- .NET 10.0 ランタイム
- 管理者権限

### インストール手順

1. プロジェクトをビルドして発行します:

```powershell
dotnet publish -c Release -o C:\RemoteCaptureService
```

2. Windowsサービスを作成します（**重要**: 画面キャプチャを使用するため、LocalSystemアカウントで対話デスクトップを許可する必要があります）:

```powershell
sc.exe create RemoteCaptureService binPath="C:\RemoteCaptureService\RemoteCapture.Service.exe" obj=LocalSystem type=interact type=own
```

または、現在ログインしているユーザーアカウントでサービスを実行する場合:

```powershell
sc.exe create RemoteCaptureService binPath="C:\RemoteCaptureService\RemoteCapture.Service.exe" obj=".\ユーザー名" password="パスワード"
```

**注意**: Windows Vista以降では、セッション0分離によりLocalSystemでの対話デスクトップアクセスが制限されています。最も確実な方法は、現在のユーザーアカウントでサービスを実行することです。

3. サービスを開始します:

```powershell
sc.exe start RemoteCaptureService
```

### アンインストール手順

1. サービスを停止します:

```powershell
sc.exe stop RemoteCaptureService
```

2. サービスを削除します:

```powershell
sc.exe delete RemoteCaptureService
```

## 使用方法

1. Windowsサービスとしてインストールして実行します
2. WebSocketクライアント（RemoteCapture.exeなど）で `ws://localhost:8080` に接続します
3. リアルタイムでモニター画面が配信されます
4. マウスとキーボードのイベントを送信すると、サービス側で実行されます

## ログ

サービスのログはWindowsイベントビューアーまたはコンソール出力で確認できます。

## トラブルシューティング

### サービスが起動後すぐに終了する

Windows サービスはデフォルトで「セッション0（非対話型セッション）」で実行されるため、画面キャプチャAPIにアクセスできません。

**解決策:**

1. サービスを削除します:
```powershell
sc.exe stop RemoteCaptureService
sc.exe delete RemoteCaptureService
```

2. 現在のユーザーアカウントでサービスを再作成します:
```powershell
sc.exe create RemoteCaptureService binPath="C:\RemoteCaptureService\RemoteCapture.Service.exe" obj=".\ユーザー名" password="パスワード"
sc.exe start RemoteCaptureService
```

3. サービスのログファイル（実行ファイルと同じフォルダ）でエラーの詳細を確認してください。

### サービスが起動しない

- Windowsイベントビューアーでエラーログを確認してください
- サービスのログファイルを確認してください
- モニターが接続されていることを確認してください
- ポート番号が他のアプリケーションと競合していないか確認してください

### クライアントが接続できない

- Windowsファイアウォールでポートが開放されているか確認してください
- サービスが実行中であることを確認してください (`sc.exe query RemoteCaptureService`)

### パフォーマンスが悪い

- `appsettings.json` でフレームレートやJPEG画質を調整してください
- ネットワーク帯域幅を確認してください
