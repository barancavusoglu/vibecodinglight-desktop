param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
[string[]]$restoreArgs = @()
if ($NoRestore) { $restoreArgs = @('--no-restore') }
Push-Location $PSScriptRoot
try {
    dotnet run --project Tests/AiLight.Tests.csproj -c Release @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Durum ve hook testleri başarısız.' }
    dotnet publish Desktop/AiLight.Desktop.Windows.csproj -c Release -r win-x64 --self-contained true -o dist/portable @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Windows yayını derlenemedi.' }
    $smokeReport = Join-Path $PSScriptRoot 'smoke-result.txt'
    $smoke = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist/portable/AI Light.exe') -ArgumentList "--integration-smoke `"$smokeReport`"" -WindowStyle Hidden -PassThru
    if (!$smoke.WaitForExit(60000)) { throw 'Hook kabuk testi zaman aşımına uğradı.' }
    Get-Content -LiteralPath $smokeReport
    if ($smoke.ExitCode -ne 0) { throw "Hook kabuk testi başarısız: $smokeReport" }
    $platform = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist/portable/AI Light.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'dist/platform-result.txt') -RedirectStandardError (Join-Path $PSScriptRoot 'dist/platform-error.txt')
    if (!$platform.WaitForExit(30000)) { throw 'Windows platform checks timed out.' }
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dist/platform-result.txt')
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dist/platform-error.txt')
    if ($platform.ExitCode -ne 0) { throw 'Windows platform checks failed.' }
    node Tests/opencode.test.mjs (Join-Path $PSScriptRoot 'dist/portable/AI Light.exe') ([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    if ($LASTEXITCODE -ne 0) { throw 'OpenCode eklenti aktarım testi başarısız.' }
    Copy-Item -LiteralPath 'dist/portable/AI Light.exe' -Destination 'dist/VibeCodingLight-Setup.exe' -Force
    Copy-Item -LiteralPath 'install.ps1' -Destination 'dist/install.ps1' -Force
    Get-FileHash -Algorithm SHA256 -LiteralPath 'dist/VibeCodingLight-Setup.exe'
} finally { Pop-Location }
