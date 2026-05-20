# RemoteCapture Service - ログ確認スクリプト

$logPath = Join-Path $env:TEMP "RemoteCapture.Service.log"

if (Test-Path $logPath) {
	Write-Host "=== RemoteCapture.Service Log ===" -ForegroundColor Cyan
	Write-Host "Log file: $logPath" -ForegroundColor White
	Write-Host ""

	# ログの最後の50行を表示
	Get-Content $logPath -Tail 50

	Write-Host ""
	Write-Host "=================================" -ForegroundColor Cyan
	Write-Host "リアルタイムでログを監視するには:" -ForegroundColor Yellow
	Write-Host "  Get-Content '$logPath' -Wait" -ForegroundColor White
} else {
	Write-Host "ログファイルが見つかりません: $logPath" -ForegroundColor Red
	Write-Host ""
	Write-Host "サービスが起動していない可能性があります。" -ForegroundColor Yellow
	Write-Host "サービスを起動してください:" -ForegroundColor Yellow
	Write-Host "  Start-ScheduledTask -TaskName 'RemoteCaptureService'" -ForegroundColor White
}
