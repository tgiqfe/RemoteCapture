# RemoteCapture.Service

RemoteCapture.Serviceは、コンソールアプリケーションとしても、Windowsサービスとしても実行できます。

## コンソールアプリケーションとして実行

```powershell
.\RemoteCapture.Service.exe
```

通常のコンソールアプリとして実行されます。Ctrl+Cで終了できます。

## Windowsサービスとして実行

### 重要: GDIスクリーンキャプチャの制限

このサービスはGDI (`Graphics.CopyFromScreen()`) を使用してスクリーンキャプチャを行います。

### 2つの実行方法

#### 方法1: ユーザーアカウントでサービスを実行（推奨・簡単）

ログオンしているユーザーアカウントでサービスを実行します。

**メリット**:
- 設定が簡単
- ユーザーデスクトップを直接キャプチャ可能

**デメリット**:
- ユーザーがログオフするとサービスが停止する可能性がある
- LogonUI（UAC画面、ロック画面等）はキャプチャできない

#### 方法2: SYSTEMアカウント + アクティブセッションで実行（高度）

NT AUTHORITY\SYSTEMアカウントで、アクティブユーザーセッション内で実行します。

**メリット**:
- ユーザーデスクトップをキャプチャ可能
- LogonUI/Winlogon（UAC、ログイン画面、Ctrl+Alt+Del画面等）もキャプチャ可能
- ユーザーログオフの影響を受けない

**デメリット**:
- 設定が複雑
- サードパーティツール（PsExec、PowerRunAsSystem等）が必要

### サービスの登録と設定手順

#### 方法1の手順: ユーザーアカウントでサービス実行

##### 1. サービスの登録

管理者権限でPowerShellを開き、以下のコマンドを実行します：

```powershell
# サービスを作成
$servicePath = "C:\Path\To\RemoteCapture.Service.exe"
New-Service -Name "RemoteCaptureService" `
			-BinaryPathName $servicePath `
			-DisplayName "RemoteCapture Service" `
			-Description "Screen capture service via WebSocket" `
			-StartupType Manual
```

##### 2. サービスのログオンアカウント設定

**GUIから設定**:

1. `Win + R` → `services.msc` を実行
2. 「RemoteCapture Service」を探して右クリック → プロパティ
3. 「ログオン」タブを選択
4. 「アカウント」を選択
5. 「参照」ボタンをクリック
6. ログオンしているユーザー名を入力（例: `.\UserName`）
7. パスワードを入力
8. 「OK」をクリック

**PowerShellから設定**:

```powershell
# サービスの実行アカウントを変更
$serviceName = "RemoteCaptureService"
$username = ".\YourUserName"  # ドメインユーザーの場合は "DOMAIN\UserName"
$password = "YourPassword"

# sc.exeを使用してログオンアカウントを設定
sc.exe config $serviceName obj= $username password= $password
```

##### 3. サービスの開始

```powershell
Start-Service -Name "RemoteCaptureService"
```

#### 方法2の手順: SYSTEMアカウント + アクティブセッション

この方法では、PowerRunAsSystemなどのツールを使用します。

##### 前提条件

PowerRunAsSystemをインストール：

```powershell
Install-Module -Name PowerRunAsSystem
```

##### 実行手順

1. **管理者権限でPowerShellを起動**

2. **SYSTEMアカウントのPowerShellセッションを開始**:

```powershell
Invoke-InteractiveSystemPowerShell
```

新しいPowerShellウィンドウが「NT AUTHORITY\System」として開きます。

3. **SYSTEMセッションからサービスを起動**:

新しく開いたSYSTEM権限のPowerShellで：

```powershell
cd "C:\Path\To\RemoteCapture.Service"
.\RemoteCapture.Service.exe
```

この状態で、ユーザーデスクトップとLogonUI（UAC画面、ロック画面等）の両方をキャプチャできます。

##### 代替方法: PsExecを使用

Sysinternals PsExecを使用する場合：

```powershell
# PsExecダウンロード
# https://docs.microsoft.com/en-us/sysinternals/downloads/psexec

# アクティブセッションでSYSTEMとして実行
PsExec.exe -s -i 1 "C:\Path\To\RemoteCapture.Service.exe"
```

`-i 1` の数字はセッションIDです。現在のセッションIDを確認するには：

```powershell
query user
```

#### 4. サービスの状態確認

```powershell
Get-Service -Name "RemoteCaptureService"
```

### サービスの削除

```powershell
# サービスの停止
Stop-Service -Name "RemoteCaptureService"

# サービスの削除
sc.exe delete "RemoteCaptureService"
```

## ポート設定

デフォルトでは `http://0.0.0.0:5000` でリスニングします。
外部からの接続を許可するため、Windowsファイアウォールで以下のポートを開放してください：

```powershell
# ファイアウォール規則を追加
New-NetFirewallRule -DisplayName "RemoteCapture Service" `
					 -Direction Inbound `
					 -LocalPort 5000 `
					 -Protocol TCP `
					 -Action Allow
```

## トラブルシューティング

### サービスとして実行時にキャプチャが表示されない

**症状**: サービスは起動しているが、画面が真っ黒または何も表示されない

**原因**:
- セッション0（システムセッション）で実行されている
- GDIはセッション0からユーザーデスクトップをキャプチャできない

**解決策**:
1. イベントビューアーでログを確認（Windows ログ → アプリケーション）
2. ログに以下の警告が出ている場合は、上記の「方法1」または「方法2」を実施：
   ```
   WARNING: Running as Windows Service in Session 0. GDI screen capture may not work.
   ```

### イベントログの確認

コンソールから：
```powershell
# アプリケーションログを確認
Get-EventLog -LogName Application -Source ".NET Runtime" -Newest 10 | Where-Object { $_.Message -like "*RemoteCapture*" }
```

イベントビューアー：
1. `Win + R` → `eventvwr.msc`
2. Windows ログ → アプリケーション
3. ソース「.NET Runtime」でフィルタ

### セッション情報の確認

サービス起動時にログに以下の情報が出力されます：
- `Starting RemoteCapture Service in Session: [セッションID]`
- `Is Windows Service: [True/False]`

**セッションIDの意味**:
- `0`: システムセッション（ユーザーデスクトップをキャプチャ不可）
- `1以上`: ユーザーセッション（キャプチャ可能）

現在のセッションIDを確認：
```powershell
query user
```

## 接続方法

RemoteCapture.Viewerから接続する場合：

1. IPアドレス: サービスが動作しているPCのIPアドレス（または localhost）
2. ポート番号: 5000
3. Connectボタンをクリック

WebSocket接続URL: `ws://[IPアドレス]:5000/screen`

## 参考情報

この実装は、PowerRemoteDesktopプロジェクトのLogonUIキャプチャ方法を参考にしています：
https://github.com/PhrozenIO/PowerRemoteDesktop#how-to-capture-logonui

