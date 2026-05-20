# RemoteCapture Serviceのアンインストールスクリプト
# 管理者権限で実行してください

$serviceName = "RemoteCaptureService"

# サービスが存在するか確認
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if (-not $service) {
	Write-Host "サービス '$serviceName' は存在しません。" -ForegroundColor Yellow
	exit 0
}

# サービスを停止
Write-Host "サービスを停止しています..." -ForegroundColor Cyan
Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# サービスを削除
Write-Host "サービスを削除しています..." -ForegroundColor Cyan
sc.exe delete $serviceName

Write-Host "サービスが削除されました。" -ForegroundColor Green
