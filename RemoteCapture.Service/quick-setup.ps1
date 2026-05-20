# RemoteCapture Service - クイックセットアップ（推奨方法）
# 管理者権限で実行してください

$ErrorActionPreference = "Stop"

Write-Host "=== RemoteCapture Service - クイックセットアップ ===" -ForegroundColor Cyan
Write-Host ""

# 既存のサービスを停止・削除
$serviceName = "RemoteCaptureService"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
	Write-Host "既存のWindowsサービスを停止・削除しています..." -ForegroundColor Yellow
	Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
	sc.exe delete $serviceName | Out-Null
	Start-Sleep -Seconds 2
	Write-Host "削除完了" -ForegroundColor Green
}

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
Write-Host "タスクスケジューラにタスクを登録しています..." -ForegroundColor Cyan

$taskName = "RemoteCaptureService"

# 既存のタスクを削除
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

# XMLでタスクを作成（Console Sessionで実行するため）
$taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
	<Description>RemoteCapture screen sharing service (supports LogonUI)</Description>
  </RegistrationInfo>
  <Triggers>
	<BootTrigger>
	  <Enabled>true</Enabled>
	  <Delay>PT5S</Delay>
	</BootTrigger>
  </Triggers>
  <Principals>
	<Principal id="Author">
	  <UserId>S-1-5-18</UserId>
	  <RunLevel>HighestAvailable</RunLevel>
	</Principal>
  </Principals>
  <Settings>
	<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
	<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
	<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
	<AllowHardTerminate>true</AllowHardTerminate>
	<StartWhenAvailable>true</StartWhenAvailable>
	<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
	<IdleSettings>
	  <StopOnIdleEnd>false</StopOnIdleEnd>
	  <RestartOnIdle>false</RestartOnIdle>
	</IdleSettings>
	<AllowStartOnDemand>true</AllowStartOnDemand>
	<Enabled>true</Enabled>
	<Hidden>false</Hidden>
	<RunOnlyIfIdle>false</RunOnlyIfIdle>
	<WakeToRun>false</WakeToRun>
	<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
	<Priority>7</Priority>
	<RestartOnFailure>
	  <Interval>PT1M</Interval>
	  <Count>3</Count>
	</RestartOnFailure>
  </Settings>
  <Actions Context="Author">
	<Exec>
	  <Command>$servicePath</Command>
	  <WorkingDirectory>$(Split-Path $servicePath)</WorkingDirectory>
	</Exec>
  </Actions>
</Task>
"@

# XMLを一時ファイルに保存
$tempXmlPath = Join-Path $env:TEMP "RemoteCaptureTask.xml"
$taskXml | Out-File -FilePath $tempXmlPath -Encoding Unicode

# XMLからタスクを登録
schtasks.exe /Create /TN $taskName /XML $tempXmlPath /F | Out-Null

# 一時ファイルを削除
Remove-Item $tempXmlPath -ErrorAction SilentlyContinue

Write-Host "タスク登録完了" -ForegroundColor Green
Write-Host ""

# タスクを今すぐ起動
Write-Host "タスクを起動しています..." -ForegroundColor Cyan
Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 3

# プロセスが起動しているか確認
$process = Get-Process -Name "RemoteCapture.Service" -ErrorAction SilentlyContinue
if ($process) {
	Write-Host ""
	Write-Host "✓ RemoteCapture.Service が起動しました！" -ForegroundColor Green
	Write-Host "  PID: $($process.Id)" -ForegroundColor White
	Write-Host "  Session: $($process.SessionId)" -ForegroundColor White
	Write-Host ""
	Write-Host "接続情報:" -ForegroundColor Cyan
	Write-Host "  WebSocket: ws://localhost:5000/screen" -ForegroundColor White
	Write-Host ""
	Write-Host "次回のシステム起動時から自動的に起動します" -ForegroundColor Green
} else {
	Write-Host ""
	Write-Host "✗ プロセスが起動していません" -ForegroundColor Red
	Write-Host ""
	Write-Host "トラブルシューティング:" -ForegroundColor Yellow
	Write-Host "1. タスクスケジューラを開いて 'RemoteCaptureService' の履歴を確認" -ForegroundColor White
	Write-Host "2. 手動でコンソールモードで実行してエラーを確認:" -ForegroundColor White
	Write-Host "   $servicePath" -ForegroundColor Gray
}

Write-Host ""
Write-Host "管理コマンド:" -ForegroundColor Cyan
Write-Host "  起動: Start-ScheduledTask -TaskName '$taskName'" -ForegroundColor White
Write-Host "  停止: Get-Process -Name 'RemoteCapture.Service' | Stop-Process -Force" -ForegroundColor White
Write-Host "  削除: Unregister-ScheduledTask -TaskName '$taskName'" -ForegroundColor White
