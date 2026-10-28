#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InstallerPath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Windows is required to package the Windows application.' }
$root = Split-Path $PSScriptRoot -Parent
if (-not $InstallerPath) { $InstallerPath = Join-Path $root 'windows/dist/VibeCodingLight-Setup.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'windows/dist/releases' }
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'windows/Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
$changes = & git -C $root status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit source changes before preparing a release.' }
$installer = Get-Item -LiteralPath $InstallerPath
if ($installer.VersionInfo.ProductVersion -ne "$version+$commit") {
    throw 'Installer version/source commit does not match this checkout. Rebuild from the current commit.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
if ($signature.Status -notin @('NotSigned', 'Valid')) {
    throw 'Installer contains an invalid or unverifiable signature.'
}
$name = "VibeCodingLight-windows-v$version-win-x64"
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $output $name
$archive = Join-Path $output "$name.zip"
foreach ($path in @($stage, $archive, "$archive.sha256")) {
    if (Test-Path -LiteralPath $path) { throw "Output already exists; choose a new output directory: $path" }
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$packagedInstaller = Join-Path $stage 'VibeCodingLight-Setup.exe'
Copy-Item -LiteralPath $installer.FullName -Destination $packagedInstaller
foreach ($file in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination (Join-Path $stage 'licenses') -Recurse
$installerHash = (Get-FileHash -LiteralPath $packagedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
if ($installerHash -ne (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Installer changed while being packaged.'
}
$metadata = [ordered]@{
    version = $version
    platform = 'win-x64'
    sourceRepository = 'https://github.com/barancavusoglu/vibecodinglight-desktop'
    sourceCommit = $commit
    installerSha256 = $installerHash
    signatureStatus = [string]$signature.Status
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding utf8NoBOM
Compress-Archive -LiteralPath $stage -DestinationPath $archive
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding utf8NoBOM
[pscustomobject]@{ Archive = $archive; Sha256 = $archiveHash; Signature = $signature.Status; SourceCommit = $commit }
