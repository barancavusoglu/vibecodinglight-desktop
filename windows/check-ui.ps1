param(
    [string]$Executable = (Join-Path $PSScriptRoot 'dist/portable/AI Light.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$env:AI_LIGHT_DATA_DIR = Join-Path $OutputDirectory 'ci-preview-data'
$env:AI_LIGHT_TEST_PIPE = [guid]::NewGuid().ToString('N')
function Check-Preview([string]$Name, [string]$Arguments) {
    $image = Join-Path $OutputDirectory "ci-$Name.png"
    $process = Start-Process -FilePath $Executable -ArgumentList "--preview `"$image`" --layout-check $Arguments" -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw "Preview timed out: $Name" }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $image)) {
        if (Test-Path -LiteralPath "$image.error.txt") { Get-Content -LiteralPath "$image.error.txt" }
        throw "Preview failed: $Name ($($process.ExitCode))"
    }
    Write-Output "PASS Shared UI: $Name"
}
foreach ($width in @(420, 755, 1440)) {
    foreach ($tab in @('sessions', 'connections', 'settings')) {
        $arguments = "--preview-width=$width --showcase --theme=night"
        if ($tab -ne 'sessions') { $arguments += " --$tab" }
        Check-Preview "$width-$tab" $arguments
    }
}
foreach ($theme in @('light', 'dark', 'mist', 'sand', 'forest', 'night')) {
    Check-Preview "theme-$theme" "--settings --theme=$theme --theme-preview"
}
foreach ($scenario in @('session-check', 'runtime-check', 'review-ui-test', 'verify-check', 'update-available-test', 'diagnostics-test', 'notice-test')) {
    Check-Preview $scenario "--$scenario"
}
Check-Preview 'brightness' '--settings --empty --brightness-check'
Check-Preview 'wheel' '--settings --color-wheel'
Check-Preview 'empty' '--empty'
foreach ($transport in @('usb', 'bluetooth', 'none')) {
    Check-Preview "device-$transport" "--device-preview=$transport"
}
