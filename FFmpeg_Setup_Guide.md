# FFmpeg ネイティブライブラリのセットアップガイド

## 問題
SIPSorceryMedia.FFmpeg を使用するには、FFmpeg のネイティブ DLL ファイルが必要です。

## エラーメッセージ
```
NotSupportedException: Specified method is not supported.
```

このエラーは FFmpeg のネイティブライブラリが見つからない場合に発生します。

## 解決方法

### オプション 1: 自動ダウンロード（推奨）

以下の PowerShell コマンドを実行して、FFmpeg バイナリを自動的にダウンロードして配置します：

```powershell
# 作業ディレクトリに移動
cd C:\Users\User\Documents\Work\RemoteCapture\RemoteCapture

# FFmpeg バイナリをダウンロード（BtbN ビルド - Windows 用）
$ffmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"
$zipPath = "$env:TEMP\ffmpeg.zip"
$extractPath = "$env:TEMP\ffmpeg"

Write-Host "Downloading FFmpeg..."
Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zipPath

Write-Host "Extracting..."
Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

# DLL を出力ディレクトリにコピー
$binDir = ".\bin\Debug\net10.0-windows10.0.26100.0"
if (!(Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force }

$ffmpegBinPath = Get-ChildItem -Path $extractPath -Recurse -Directory -Filter "bin" | Select-Object -First 1
Copy-Item -Path "$($ffmpegBinPath.FullName)\*.dll" -Destination $binDir -Force

Write-Host "FFmpeg DLLs copied to $binDir"

# クリーンアップ
Remove-Item $zipPath -Force
Remove-Item $extractPath -Recurse -Force

Write-Host "Setup complete!"
```

### オプション 2: 手動ダウンロード

1. FFmpeg Windows Shared Builds をダウンロード:
   - URL: https://github.com/BtbN/FFmpeg-Builds/releases
   - ファイル: `ffmpeg-master-latest-win64-gpl-shared.zip`

2. ZIP ファイルを解凍

3. `bin` フォルダ内の以下の DLL ファイルをコピー:
   - `avcodec-61.dll`
   - `avdevice-61.dll`
   - `avfilter-10.dll`
   - `avformat-61.dll`
   - `avutil-59.dll`
   - `swresample-5.dll`
   - `swscale-8.dll`

4. コピー先:
   ```
   C:\Users\User\Documents\Work\RemoteCapture\RemoteCapture\bin\Debug\net10.0-windows10.0.26100.0\
   ```

### オプション 3: システム全体にインストール

1. FFmpeg をダウンロード（上記と同じ）
2. `C:\ffmpeg\bin` に DLL をコピー
3. システム環境変数 PATH に `C:\ffmpeg\bin` を追加

## 確認方法

以下のファイルが存在することを確認:

```
RemoteCapture\bin\Debug\net10.0-windows10.0.26100.0\avcodec-61.dll
RemoteCapture\bin\Debug\net10.0-windows10.0.26100.0\avutil-59.dll
RemoteCapture\bin\Debug\net10.0-windows10.0.26100.0\swscale-8.dll
```

## トラブルシューティング

### DLL が見つからない場合
デバッグ出力で以下のメッセージを確認:
```
[CaptureVideoSource] FFmpeg libraries found at: <path>
```

このメッセージが表示されない場合、DLL が正しい場所に配置されていません。

### バージョン不一致
FFmpeg のバージョンは 6.1 以上が必要です。古いバージョンの場合は最新版をダウンロードしてください。

### x64 vs x86
必ず **win64** (x64) 版をダウンロードしてください。x86 版は動作しません。
