[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'ArrowWheel.sln'
$testProject = Join-Path $projectRoot 'tests\ArrowWheel.StateTests\ArrowWheel.StateTests.csproj'
$appProject = Join-Path $projectRoot 'src\ArrowWheel\ArrowWheel.csproj'
$builtExecutable = Join-Path $projectRoot 'src\ArrowWheel\bin\Release\net8.0-windows\Clicker-HoldScroll.exe'
$outputDirectory = Join-Path $projectRoot 'dist'
$executablePath = Join-Path $outputDirectory 'Clicker-HoldScroll.exe'

Push-Location $projectRoot
try {
    & dotnet build $solutionPath -c Release
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }

    & dotnet run --project $testProject -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $LASTEXITCODE" }

    & $builtExecutable --hook-smoke-test
    if ($LASTEXITCODE -ne 0) { throw "Keyboard hook smoke test failed: $LASTEXITCODE" }

    & $builtExecutable --ui-smoke-test
    if ($LASTEXITCODE -ne 0) { throw "Tray and safety integration smoke test failed: $LASTEXITCODE" }

    & dotnet publish $appProject -p:PublishProfile=win-x64 -o $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }

    if (-not (Test-Path -LiteralPath $executablePath)) {
        throw "Published executable was not found: $executablePath"
    }

    Get-Item -LiteralPath $executablePath | Select-Object FullName, Length, LastWriteTime
    $hash = Get-FileHash -LiteralPath $executablePath -Algorithm SHA256
    Write-Output "SHA256=$($hash.Hash)"
}
finally {
    Pop-Location
}
