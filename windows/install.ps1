param(
    [string]$PackagePath,
    [string]$PackageUrl,
    [string]$ExpectedSha256,
    [string]$PublisherThumbprint
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Bu beta 64-bit Windows içindir.' }
if ($PackageUrl) {
    if (-not $PackageUrl.StartsWith('https://')) { throw 'HTTPS paket adresi gerekli.' }
    if (-not $ExpectedSha256 -or -not $PublisherThumbprint) { throw 'Uzak paket için yayıncı sertifika izi ve SHA256 gerekli.' }
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('ai-light-install-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    $PackagePath = Join-Path $stage 'VibeCodingLight-Setup.exe'
    Invoke-WebRequest -Uri $PackageUrl -OutFile $PackagePath
    $signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $PublisherThumbprint) { throw 'Yayıncı imzası doğrulanamadı.' }
} elseif (-not $PackagePath) {
    $PackagePath = Join-Path $PSScriptRoot 'VibeCodingLight-Setup.exe'
}
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
if ($ExpectedSha256 -and (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Paket SHA256 eşleşmedi.' }
$installer = Start-Process -FilePath $resolvedPackage -ArgumentList '--install' -WindowStyle Hidden -PassThru
# Wait only for the installer, not its long-lived tray child process.
$installer.WaitForExit()
if ($installer.ExitCode -ne 0) { throw "Kurulum başarısız: $($installer.ExitCode)" }
Write-Output 'VibeCodingLight kuruldu. Bağlantılar sekmesinden entegrasyonlarını etkinleştirebilirsin.'
