#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InstallerPath,
    [string]$OutputDirectory,
    [switch]$AllowUnsigned
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Windows is required to verify Authenticode signatures.' }
$root = Split-Path $PSScriptRoot -Parent
if (-not $InstallerPath) { $InstallerPath = Join-Path $root 'windows/dist/VibeCodingLight-Setup.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'windows/dist/releases' }
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'windows/Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$notes = Join-Path $root "docs/releases/windows-v$version.md"
if (-not (Test-Path -LiteralPath $notes -PathType Leaf)) { throw "Missing release notes: $notes" }
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
$changes = & git -C $root status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit source changes before preparing a release.' }
$installer = Get-Item -LiteralPath $InstallerPath
if ($installer.VersionInfo.ProductVersion -ne "$version+$commit") {
    throw 'Installer version/source commit does not match this checkout. Rebuild from the current commit.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
$unsigned = $signature.Status -eq 'NotSigned'
if ($unsigned) {
    if (-not $AllowUnsigned) { throw 'Installer is unsigned. Sign it first, or explicitly use -AllowUnsigned for a review-only prerelease.' }
} elseif ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
    throw 'Installer must have a valid trusted Authenticode signature and timestamp.'
}
$suffix = if ($unsigned) { '-unsigned-review' } else { '' }
$name = "VibeCodingLight-windows-v$version-win-x64$suffix"
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
Copy-Item -LiteralPath $notes -Destination (Join-Path $stage 'RELEASE_NOTES.md')
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
    signer = $(if ($unsigned) { $null } else { $signature.SignerCertificate.Subject })
    reviewOnly = [bool]$unsigned
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding utf8NoBOM
if ($unsigned) {
    'UNSIGNED REVIEW BUILD. No publisher signature. Not approved for the stable download channel. A checksum does not establish publisher identity.' |
        Set-Content -LiteralPath (Join-Path $stage 'UNSIGNED_REVIEW.txt') -Encoding utf8NoBOM
}
Compress-Archive -LiteralPath $stage -DestinationPath $archive
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding utf8NoBOM
[pscustomobject]@{ Archive = $archive; Sha256 = $archiveHash; Signature = $signature.Status; SourceCommit = $commit }
