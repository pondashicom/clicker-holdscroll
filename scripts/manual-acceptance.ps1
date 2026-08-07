[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executablePath = Join-Path $projectRoot 'dist\Clicker-HoldScroll.exe'
$logPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Clicker-HoldScroll\logs\clicker-holdscroll.log'

function Wait-ForEnter([string]$message) {
    Write-Host ''
    Read-Host "$message`n確認できたら Enter"
}

Write-Host 'Clicker HoldScroll 業務受入テスト'
Write-Host 'このスクリプトは判定を自動化しません。画面と操作感を人が確認してください。'

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "実行ファイルがありません: $executablePath"
}

if (Get-Process -Name 'Clicker-HoldScroll' -ErrorAction SilentlyContinue) {
    Write-Warning 'Clicker HoldScroll がすでに起動しています。通知領域から終了して、もう一度実行してください。'
    exit 2
}

Write-Host "起動します: $executablePath"
Start-Process -FilePath $executablePath

Wait-ForEnter '1/7 通知領域に Clicker HoldScroll が現れ、「無効」で起動したことを確認してください。'
Wait-ForEnter '2/7 アイコンをダブルクリックして有効化し、短押しの ← / → が各1回だけ動くことを確認してください。'
Wait-ForEnter '3/7 スクロール可能な画面で、←長押しが上、→長押しが下へ動き、離すと直ちに止まることを確認してください。'
Wait-ForEnter '4/7 Ctrl / Shift / Alt / Windows キー併用時に、意図しない変換や別ショートカットが発生しないことを確認してください。'
Wait-ForEnter '5/7 長押し中に Ctrl+Shift+F12 を押し、スクロールが止まって「無効」になることを確認してください。'
Wait-ForEnter '6/7 再び有効化し、通知領域メニューから長押し判定・速度・安全上限を変更できることを確認してください。'
Wait-ForEnter "7/7 ログに startup / enabled / emergency-stop が記録されたことを確認してください。`nログ: $logPath"

Write-Host ''
Write-Host '手動受入テストは終了です。Clicker HoldScroll は起動したままです。'
Write-Host '終了する場合は通知領域メニューの「終了」を使用してください。'
