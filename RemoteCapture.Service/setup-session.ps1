# RemoteCapture Service - アクティブセッションで起動するスクリプト
# 管理者権限で実行してください
# このスクリプトは、サービスをユーザーセッション内で起動します

$ErrorActionPreference = "Stop"

$serviceName = "RemoteCaptureService"

Write-Host "=== RemoteCapture Service - セッション設定 ===" -ForegroundColor Cyan
Write-Host ""

# 実行ファイルのパスを取得
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishPath = Join-Path $scriptPath "bin\Release\net10.0\publish\RemoteCapture.Service.exe"
$debugPath = Join-Path $scriptPath "bin\Debug\net10.0\RemoteCapture.Service.exe"

if (Test-Path $publishPath) {
	$servicePath = $publishPath
} elseif (Test-Path $debugPath) {
	$servicePath = $debugPath
} else {
	Write-Host "エラー: 実行ファイルが見つかりません" -ForegroundColor Red
	exit 1
}

# アクティブなユーザーセッションを取得
$activeSession = query user | Select-String "Active" | ForEach-Object {
	$parts = $_ -split '\s+'
	$sessionId = $parts | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
	return $sessionId
}

if (-not $activeSession) {
	Write-Host "警告: アクティブなユーザーセッションが見つかりません" -ForegroundColor Yellow
	Write-Host "Session 1 を使用します" -ForegroundColor Yellow
	$activeSession = 1
} else {
	Write-Host "アクティブセッション: $activeSession" -ForegroundColor Green
}

Write-Host ""
Write-Host "このスクリプトは2つのオプションを提供します:" -ForegroundColor Cyan
Write-Host ""
Write-Host "オプション1: タスクスケジューラで自動起動（推奨）" -ForegroundColor Green
Write-Host "  - システム起動時に自動的にユーザーセッションで起動"
Write-Host "  - LogonUI/ユーザーデスクトップ両方をキャプチャ可能"
Write-Host ""
Write-Host "オプション2: 手動でセッション内起動" -ForegroundColor Yellow
Write-Host "  - 今すぐセッション内でプロセスを起動"
Write-Host "  - 再起動後は手動で再実行が必要"
Write-Host ""

$choice = Read-Host "選択してください (1 または 2)"

if ($choice -eq "1") {
	# タスクスケジューラで登録
	Write-Host ""
	Write-Host "タスクスケジューラにタスクを登録しています..." -ForegroundColor Cyan

	$taskName = "RemoteCaptureService"

	# 既存のタスクを削除
	Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

	# タスクアクション
	$action = New-ScheduledTaskAction -Execute $servicePath

	# トリガー（システム起動時）
	$trigger = New-ScheduledTaskTrigger -AtStartup

	# 設定（SYSTEM権限で実行）
	$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

	# タスク設定
	$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

	# タスク登録
	Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "RemoteCapture screen sharing service" | Out-Null

	Write-Host "タスク登録完了" -ForegroundColor Green
	Write-Host ""
	Write-Host "タスクを今すぐ起動しますか? (y/n)" -ForegroundColor Yellow
	$startNow = Read-Host

	if ($startNow -eq "y") {
		Write-Host "タスクを起動しています..." -ForegroundColor Cyan
		Start-ScheduledTask -TaskName $taskName
		Start-Sleep -Seconds 2

		# プロセスが起動しているか確認
		$process = Get-Process -Name "RemoteCapture.Service" -ErrorAction SilentlyContinue
		if ($process) {
			Write-Host "✓ RemoteCapture.Service が起動しました (PID: $($process.Id))" -ForegroundColor Green
		} else {
			Write-Host "✗ プロセスが見つかりません" -ForegroundColor Red
		}
	}

	Write-Host ""
	Write-Host "次回のシステム起動時から自動的に起動します" -ForegroundColor Cyan

} elseif ($choice -eq "2") {
	# PsExecを使用してセッション内で起動
	Write-Host ""
	Write-Host "セッション $activeSession でプロセスを起動します..." -ForegroundColor Cyan
	Write-Host ""
	Write-Host "注意: この方法ではPsExecが必要です" -ForegroundColor Yellow
	Write-Host "PsExecダウンロード: https://docs.microsoft.com/en-us/sysinternals/downloads/psexec" -ForegroundColor Yellow
	Write-Host ""

	$psexecPath = Read-Host "PsExec.exeのフルパスを入力してください（例: C:\Tools\PsExec.exe）"

	if (Test-Path $psexecPath) {
		Write-Host "PsExecを使用して起動しています..." -ForegroundColor Cyan
		& $psexecPath -s -i $activeSession $servicePath
	} else {
		Write-Host "エラー: PsExec.exeが見つかりません: $psexecPath" -ForegroundColor Red
		Write-Host ""
		Write-Host "代わりに、以下のコマンドを手動で実行してください:" -ForegroundColor Yellow
		Write-Host "PsExec.exe -s -i $activeSession `"$servicePath`"" -ForegroundColor White
	}
} else {
	Write-Host "無効な選択です" -ForegroundColor Red
}
