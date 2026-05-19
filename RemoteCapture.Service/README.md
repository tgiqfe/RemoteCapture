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

### 重要: 実行アカウントについて

RemoteCapture.Serviceは **Windows Graphics Capture API** を使用して画面をキャプチャします。このAPIはアクティブユーザーセッション（対話型セッション）でのみ動作するため、サービスは以下のいずれかの方法で実行する必要があります:

1. **推奨**: 現在ログインしているユーザーアカウントでサービスを実行
2. LocalSystemアカウント + 対話デスクトップの許可（Windows Vista以降では制限があります）

**注意**: 
- LocalSystemアカウント（セッション0）では、通常の画面キャプチャは動作しません
- ログイン画面のキャプチャは技術的に困難です
- 最も確実な方法は、**現在のユーザーアカウントでサービスを実行**することです

### インストール手順

1. プロジェクトをビルドして発行します:

```powershell
dotnet publish -c Release -o C:\RemoteCaptureService
```

2. **推奨**: 現在ログインしているユーザーアカウントでサービスを作成します:

```powershell
sc.exe create RemoteCaptureService binPath="C:\RemoteCaptureService\RemoteCapture.Service.exe" obj=".\ユーザー名" password="パスワード" start=auto
```

または、ドメインアカウントの場合:

```powershell
sc.exe create RemoteCaptureService binPath="C:\RemoteCaptureService\RemoteCapture.Service.exe" obj="ドメイン\ユーザー名" password="パスワード" start=auto
```

**参考**: LocalSystemアカウントで実行する場合（非推奨、画面キャプチャが動作しない可能性があります）:

```powershell
sc.exe create RemoteCaptureService binPath="C:\RemoteCaptureService\RemoteCapture.Service.exe" obj=LocalSystem type=own
```

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

### ❌ エラー: "Service is running in session 0, but active user session is 1"

このエラーは、Windowsサービスがセッション0（非対話型セッション）で実行されているため、アクティブユーザーセッション（セッション1）の画面をキャプチャできないことを意味します。

**根本原因:**

Windows Graphics Capture APIは、**同一セッション内のデスクトップしかキャプチャできません**。Windowsサービスは、実行アカウントを変更しても常にセッション0で起動されるため、ユーザーセッション（セッション1以降）の画面をキャプチャすることはできません。

**推奨解決策: タスクスケジューラを使用**

最も確実な方法は、Windowsサービスではなく**タスクスケジューラ**を使用してユーザーログオン時に自動起動する方法です:

1. 既存のサービスを削除します（インストールしている場合）:
```powershell
sc.exe stop RemoteCaptureService
sc.exe delete RemoteCaptureService
```

2. タスクスケジューラでタスクを作成します:

**方法A: PowerShellコマンドで作成**
```powershell
$action = New-ScheduledTaskAction -Execute "C:\RemoteCaptureService\RemoteCapture.Service.exe"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0

Register-ScheduledTask -TaskName "RemoteCapture" -Action $action -Trigger $trigger -Principal $principal -Settings $settings
```

**方法B: GUIで作成**
1. タスクスケジューラを開く（`taskschd.msc`）
2. 「基本タスクの作成」をクリック
3. 名前: `RemoteCapture`
4. トリガー: **ログオン時**
5. 操作: **プログラムの開始**
6. プログラム: `C:\RemoteCaptureService\RemoteCapture.Service.exe`
7. タスクのプロパティで以下を設定:
   - 「最上位の特権で実行する」にチェック
   - 「ユーザーがログオンしているときのみ実行する」を選択

**利点:**
- ✅ ユーザーセッションで実行されるため、画面キャプチャが正常に動作
- ✅ ユーザーログオン時に自動起動
- ✅ サービスと同様にバックグラウンドで動作

**欠点:**
- ❌ ユーザーがログオフすると停止
- ❌ ログイン画面のキャプチャは不可能

### サービスが起動後すぐに終了する

Windows サービスはデフォルトで「セッション0（非対話型セッション）」で実行されるため、画面キャプチャAPIにアクセスできません。

**⚠️ 注意: サービスとしての実行には技術的制限があります**

ユーザーアカウントでサービスを作成しても、Windowsサービスは常にセッション0で起動されます。これは回避できないWindowsの仕様です。

**非推奨の回避策（動作保証なし）:**

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

**推奨**: 上記の「タスクスケジューラを使用」する方法を使用してください。

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
