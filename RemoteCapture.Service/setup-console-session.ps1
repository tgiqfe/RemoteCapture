# RemoteCapture Service - Console Session起動スクリプト
# 管理者権限で実行してください
# LogonUIをキャプチャするため、Console Session (通常Session 1)で起動します

$ErrorActionPreference = "Stop"

Write-Host "=== RemoteCapture Service - Console Session Setup ===" -ForegroundColor Cyan
Write-Host ""

# 実行ファイルのパスを取得
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishPath = Join-Path $scriptPath "bin\Release\net10.0\publish\RemoteCapture.Service.exe"
$debugPath = Join-Path $scriptPath "bin\Debug\net10.0\RemoteCapture.Service.exe"

if (Test-Path $publishPath) {
	$servicePath = $publishPath
	Write-Host "Published版を使用: $servicePath" -ForegroundColor Green
} elseif (Test-Path $debugPath) {
	$servicePath = $debugPath
	Write-Host "Debug版を使用: $servicePath" -ForegroundColor Yellow
} else {
	Write-Host "エラー: 実行ファイルが見つかりません" -ForegroundColor Red
	exit 1
}

Write-Host ""
Write-Host "LogonUIをキャプチャするには、Console Sessionで起動する必要があります。" -ForegroundColor Cyan
Write-Host ""
Write-Host "方法1: PsExecを使用（推奨）" -ForegroundColor Green
Write-Host "  - Console Session (Session 1)で確実に起動"
Write-Host "  - LogonUI/ユーザーデスクトップ両方をキャプチャ可能"
Write-Host "  - ダウンロード: https://live.sysinternals.com/PsExec64.exe" -ForegroundColor Gray
Write-Host ""
Write-Host "方法2: NSSMを使用（サービスマネージャー）" -ForegroundColor Yellow
Write-Host "  - サービスとしてインストールし、Session指定"
Write-Host "  - ダウンロード: https://nssm.cc/download" -ForegroundColor Gray
Write-Host ""

$choice = Read-Host "選択してください (1 または 2)"

if ($choice -eq "1") {
	# PsExecを使用
	Write-Host ""

	# PsExecをダウンロード
	$psexecPath = Join-Path $scriptPath "PsExec64.exe"

	if (-not (Test-Path $psexecPath)) {
		Write-Host "PsExec64.exeをダウンロードしています..." -ForegroundColor Cyan
		try {
			Invoke-WebRequest -Uri "https://live.sysinternals.com/PsExec64.exe" -OutFile $psexecPath
			Write-Host "ダウンロード完了" -ForegroundColor Green
		} catch {
			Write-Host "ダウンロード失敗: $($_.Exception.Message)" -ForegroundColor Red
			Write-Host ""
			Write-Host "手動でダウンロードしてください:" -ForegroundColor Yellow
			Write-Host "  https://live.sysinternals.com/PsExec64.exe" -ForegroundColor White
			Write-Host "  保存先: $psexecPath" -ForegroundColor White
			exit 1
		}
	} else {
		Write-Host "PsExec64.exeが見つかりました: $psexecPath" -ForegroundColor Green
	}

	Write-Host ""
	Write-Host "タスクスケジューラにタスクを登録しています..." -ForegroundColor Cyan

	$taskName = "RemoteCaptureService"

	# 既存のタスクを削除
	Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

	# PsExecコマンドライン（Session 1で起動）
	$psexecCommand = "`"$psexecPath`" -accepteula -s -i 1 -d `"$servicePath`""

	# タスクアクション（cmdでPsExecを実行）
	$action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c $psexecCommand"

	# トリガー（システム起動時 + 遅延10秒）
	$trigger = New-ScheduledTaskTrigger -AtStartup
	$trigger.Delay = "PT10S"

	# SYSTEM権限で実行
	$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

	# タスク設定
	$settings = New-ScheduledTaskSettingsSet `
		-AllowStartIfOnBatteries `
		-DontStopIfGoingOnBatteries `
		-StartWhenAvailable `
		-ExecutionTimeLimit (New-TimeSpan -Days 0)

	# タスク登録
	Register-ScheduledTask `
		-TaskName $taskName `
		-Action $action `
		-Trigger $trigger `
		-Principal $principal `
		-Settings $settings `
		-Description "RemoteCapture service (Console Session via PsExec)" | Out-Null

	Write-Host "タスク登録完了" -ForegroundColor Green
	Write-Host ""

	# タスクを今すぐ起動
	Write-Host "タスクを起動しています..." -ForegroundColor Cyan
	Start-ScheduledTask -TaskName $taskName
	Start-Sleep -Seconds 5

	# プロセスが起動しているか確認
	$process = Get-Process -Name "RemoteCapture.Service" -ErrorAction SilentlyContinue
	if ($process) {
		Write-Host ""
		Write-Host "✓ RemoteCapture.Service が起動しました！" -ForegroundColor Green
		Write-Host "  PID: $($process.Id)" -ForegroundColor White
		Write-Host "  Session: $($process.SessionId)" -ForegroundColor White

		if ($process.SessionId -eq 1) {
			Write-Host "  → Console Sessionで起動しています！LogonUIキャプチャ可能です" -ForegroundColor Green
		} elseif ($process.SessionId -eq 0) {
			Write-Host "  → Session 0で起動しています。LogonUIキャプチャできない可能性があります" -ForegroundColor Yellow
		}

		Write-Host ""
		Write-Host "接続情報:" -ForegroundColor Cyan
		Write-Host "  WebSocket: ws://localhost:5000/screen" -ForegroundColor White
		Write-Host ""
		Write-Host "ログ確認:" -ForegroundColor Cyan
		Write-Host "  .\view-log.ps1" -ForegroundColor White
	} else {
		Write-Host ""
		Write-Host "✗ プロセスが起動していません" -ForegroundColor Red
		Write-Host ""
		Write-Host "ログを確認してください: .\view-log.ps1" -ForegroundColor Yellow
	}

} elseif ($choice -eq "2") {
	# NSSMを使用
	Write-Host ""
	Write-Host "NSSM (Non-Sucking Service Manager) を使用します" -ForegroundColor Cyan
	Write-Host ""

	$nssmPath = Join-Path $scriptPath "nssm.exe"

	if (-not (Test-Path $nssmPath)) {
		Write-Host "NSSMが見つかりません" -ForegroundColor Red
		Write-Host ""
		Write-Host "以下のURLからダウンロードしてください:" -ForegroundColor Yellow
		Write-Host "  https://nssm.cc/release/nssm-2.24.zip" -ForegroundColor White
		Write-Host ""
		Write-Host "ダウンロード後、win64\nssm.exe を以下に配置:" -ForegroundColor Yellow
		Write-Host "  $nssmPath" -ForegroundColor White
		exit 1
	}

	$serviceName = "RemoteCaptureService"

	# 既存のサービスを停止・削除
	& $nssmPath stop $serviceName 2>$null
	& $nssmPath remove $serviceName confirm 2>$null

	Write-Host "NSSMでサービスをインストールしています..." -ForegroundColor Cyan

	# サービスをインストール
	& $nssmPath install $serviceName $servicePath
	& $nssmPath set $serviceName AppDirectory (Split-Path $servicePath)
	& $nssmPath set $serviceName DisplayName "RemoteCapture Service"
	& $nssmPath set $serviceName Description "Screen capture service via WebSocket (supports LogonUI)"
	& $nssmPath set $serviceName Start SERVICE_AUTO_START
	& $nssmPath set $serviceName AppStdout "C:\WINDOWS\Temp\RemoteCapture.Service.stdout.log"
	& $nssmPath set $serviceName AppStderr "C:\WINDOWS\Temp\RemoteCapture.Service.stderr.log"

	Write-Host "サービスを開始しています..." -ForegroundColor Cyan
	& $nssmPath start $serviceName

	Start-Sleep -Seconds 3

	# 状態確認
	$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
	if ($service -and $service.Status -eq 'Running') {
		Write-Host ""
		Write-Host "✓ サービスが起動しました" -ForegroundColor Green
		Write-Host ""
		Write-Host "接続情報:" -ForegroundColor Cyan
		Write-Host "  WebSocket: ws://localhost:5000/screen" -ForegroundColor White
	} else {
		Write-Host ""
		Write-Host "✗ サービスの起動に失敗しました" -ForegroundColor Red
	}

} else {
	Write-Host "無効な選択です" -ForegroundColor Red
}

Write-Host ""
Write-Host "次回のシステム起動時から自動的に起動します" -ForegroundColor Cyan
