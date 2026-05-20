# RemoteCapture Service - 再インストールスクリプト
# 管理者権限で実行してください

$ErrorActionPreference = "Stop"

$serviceName = "RemoteCaptureService"

Write-Host "=== RemoteCapture Service 再インストール ===" -ForegroundColor Cyan
Write-Host ""

# 既存のサービスを停止・削除
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
	Write-Host "既存のサービスを停止しています..." -ForegroundColor Yellow
	Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
	Start-Sleep -Seconds 2

	Write-Host "既存のサービスを削除しています..." -ForegroundColor Yellow
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
	Write-Host "以下のパスを確認しました:" -ForegroundColor Red
	Write-Host "  $publishPath" -ForegroundColor Red
	Write-Host "  $debugPath" -ForegroundColor Red
	exit 1
}

Write-Host ""
Write-Host "サービスを作成しています..." -ForegroundColor Cyan

# サービスを作成
New-Service -Name $serviceName `
	-BinaryPathName $servicePath `
	-DisplayName "RemoteCapture Service" `
	-Description "Screen capture service via WebSocket (supports LogonUI capture)" `
	-StartupType Automatic | Out-Null

Write-Host "サービス作成完了" -ForegroundColor Green

# サービスをインタラクティブモードに設定（デスクトップとの対話を許可）
Write-Host "サービスをインタラクティブモードに設定しています..." -ForegroundColor Cyan
sc.exe config $serviceName type= interact type= own | Out-Null

Write-Host "設定完了" -ForegroundColor Green
Write-Host ""

# サービスを開始
Write-Host "サービスを開始しています..." -ForegroundColor Cyan
try {
	Start-Service -Name $serviceName -ErrorAction Stop
	Start-Sleep -Seconds 3

	# サービスの状態を確認
	$service = Get-Service -Name $serviceName

	Write-Host ""
	Write-Host "=== サービス情報 ===" -ForegroundColor Green
	Write-Host "  名前       : $($service.Name)" -ForegroundColor White
	Write-Host "  表示名     : $($service.DisplayName)" -ForegroundColor White
	Write-Host "  状態       : $($service.Status)" -ForegroundColor $(if ($service.Status -eq 'Running') { 'Green' } else { 'Red' })
	Write-Host "  起動の種類 : Automatic" -ForegroundColor White
	Write-Host ""

	if ($service.Status -eq 'Running') {
		Write-Host "✓ サービスは正常に起動しました！" -ForegroundColor Green
		Write-Host ""
		Write-Host "接続情報:" -ForegroundColor Cyan
		Write-Host "  WebSocket: ws://localhost:5000/screen" -ForegroundColor White
	} else {
		Write-Host "✗ サービスが起動していません" -ForegroundColor Red
		Write-Host ""
		Write-Host "イベントログを確認してください:" -ForegroundColor Yellow
		Write-Host "  Get-EventLog -LogName Application -Newest 10" -ForegroundColor White
	}

} catch {
	Write-Host ""
	Write-Host "✗ サービスの起動に失敗しました" -ForegroundColor Red
	Write-Host "エラー: $($_.Exception.Message)" -ForegroundColor Red
	Write-Host ""
	Write-Host "トラブルシューティング:" -ForegroundColor Yellow
	Write-Host "1. イベントログを確認:" -ForegroundColor White
	Write-Host "   Get-EventLog -LogName Application -Newest 10" -ForegroundColor Gray
	Write-Host ""
	Write-Host "2. コンソールモードで実行してエラーを確認:" -ForegroundColor White
	Write-Host "   cd `"$scriptPath`"" -ForegroundColor Gray
	Write-Host "   .\bin\Debug\net10.0\RemoteCapture.Service.exe" -ForegroundColor Gray
}
