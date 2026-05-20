# RemoteCapture Serviceのインストールスクリプト
# 管理者権限で実行してください

# サービスが既に存在する場合は停止・削除
$serviceName = "RemoteCaptureService"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
	Write-Host "既存のサービスを停止・削除しています..." -ForegroundColor Yellow
	Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
	sc.exe delete $serviceName
	Start-Sleep -Seconds 2
}

# 実行ファイルのパスを取得（スクリプトと同じディレクトリのbin\Release\net10.0\publish内）
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishPath = Join-Path $scriptPath "bin\Release\net10.0\publish\RemoteCapture.Service.exe"
$debugPath = Join-Path $scriptPath "bin\Debug\net10.0\RemoteCapture.Service.exe"

# publishが存在すればそれを、なければdebugを使用
if (Test-Path $publishPath) {
	$servicePath = $publishPath
	Write-Host "Published版を使用します: $servicePath" -ForegroundColor Green
} elseif (Test-Path $debugPath) {
	$servicePath = $debugPath
	Write-Host "Debug版を使用します: $servicePath" -ForegroundColor Yellow
} else {
	Write-Host "エラー: 実行ファイルが見つかりません。先にビルドしてください。" -ForegroundColor Red
	Write-Host "探したパス:" -ForegroundColor Red
	Write-Host "  - $publishPath" -ForegroundColor Red
	Write-Host "  - $debugPath" -ForegroundColor Red
	exit 1
}

# サービスを作成
Write-Host "サービスを作成しています..." -ForegroundColor Cyan
New-Service -Name $serviceName `
	-BinaryPathName $servicePath `
	-DisplayName "RemoteCapture Service" `
	-Description "Screen capture service via WebSocket (supports LogonUI capture)" `
	-StartupType Automatic

Write-Host "サービスが作成されました。" -ForegroundColor Green

# サービスを開始
Write-Host "サービスを開始しています..." -ForegroundColor Cyan
Start-Service -Name $serviceName

# サービスの状態を確認
$service = Get-Service -Name $serviceName
Write-Host ""
Write-Host "サービスの状態:" -ForegroundColor Green
Write-Host "  名前: $($service.Name)" -ForegroundColor White
Write-Host "  表示名: $($service.DisplayName)" -ForegroundColor White
Write-Host "  状態: $($service.Status)" -ForegroundColor $(if ($service.Status -eq 'Running') { 'Green' } else { 'Red' })
Write-Host "  起動の種類: Automatic" -ForegroundColor White
Write-Host ""
Write-Host "WebSocket接続先: ws://localhost:5000/screen" -ForegroundColor Cyan
