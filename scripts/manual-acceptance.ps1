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

Wait-ForEnter '1/9 通知領域に Clicker HoldScroll が現れ、「無効」で起動したことを確認してください。'
Wait-ForEnter '2/9 アイコンをダブルクリックして有効化し、短押しの ← / → が各1回だけ動くことを確認してください。'
Wait-ForEnter '3/9 スクロール可能な画面で、←長押しが上、→長押しが下へ動き、離すと直ちに止まることを確認してください。'
Wait-ForEnter '4/9 PowerPointのスライドショーと発表者ツールを起動し、通知領域から「PowerPointノートモード」を有効にしてください。'
Wait-ForEnter '5/9 マウスをPowerPoint以外へ置き、別アプリにフォーカスしたまま、←／→長押しで発表者ツールのノートだけが上下することを確認してください。'
Wait-ForEnter '6/9 PowerPoint発表者ツールを終了し、長押ししても現在の画面がスクロールしないことを確認してください。確認後、ノートモードを無効にしてください。'
Wait-ForEnter '7/9 Ctrl / Shift / Alt / Windows キー併用時に、意図しない変換や別ショートカットが発生しないことを確認してください。'
Wait-ForEnter '8/9 長押し中に Ctrl+Shift+F12 を押し、スクロールが止まって「無効」になることを確認してください。再び有効化し、長押し判定・速度・安全上限を変更できることも確認してください。'
Wait-ForEnter "9/9 ログに startup / enabled / powerpoint-notes-mode / powerpoint-notes-target / emergency-stop が記録されたことを確認してください。`nログ: $logPath"

Write-Host ''
Write-Host '手動受入テストは終了です。Clicker HoldScroll は起動したままです。'
Write-Host '終了する場合は通知領域メニューの「終了」を使用してください。'
