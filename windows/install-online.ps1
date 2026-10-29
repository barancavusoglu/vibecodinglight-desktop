#Requires -Version 5.1
[CmdletBinding()]
param([switch]$DownloadOnly)

& {
    param([bool]$OnlyDownload)
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitOperatingSystem) {
        throw 'VibeCodingLight requires 64-bit Windows.'
    }

    $release = Invoke-RestMethod -Uri 'https://www.vibecodinglight.com/releases/windows/installer.json' -TimeoutSec 30
    $version = [string]$release.version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid Windows release version.' }
    foreach ($hash in @($release.archiveSha256, $release.installerSha256)) {
        if ([string]$hash -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid release checksum.' }
    }
    $name = "VibeCodingLight-windows-v$version-win-x64"
    $url = "https://github.com/barancavusoglu/vibecodinglight-desktop/releases/download/windows-v$version/$name.zip"
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $stage = Join-Path $tempRoot ('VibeCodingLight-install-' + [guid]::NewGuid().ToString('N'))
    $retain = $false
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        Write-Host "Downloading VibeCodingLight $version..."
        $archive = Join-Path $stage "$name.zip"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive -TimeoutSec 300
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $release.archiveSha256) {
            throw 'Archive checksum mismatch. Installation stopped.'
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, (Join-Path $stage 'package'))
        $package = Join-Path (Join-Path $stage 'package') $name
        $exe = Join-Path $package 'VibeCodingLight-Setup.exe'
        if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $release.installerSha256) {
            throw 'Installer checksum mismatch. Installation stopped.'
        }
        foreach ($notice in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'licenses')) {
            if (-not (Test-Path -LiteralPath (Join-Path $package $notice))) { throw "Missing license material: $notice" }
        }
        if ($OnlyDownload) {
            $retain = $true
            return [pscustomobject]@{ Version = $version; Installer = $exe; Directory = $stage }
        }

        Write-Host 'Installing for the current Windows user...'
        $installer = Start-Process -FilePath $exe -ArgumentList '--install' -PassThru
        # Wait for the installer only; its tray process stays open.
        $installer.WaitForExit()
        if ($installer.ExitCode -ne 0) { throw "Installer failed with exit code $($installer.ExitCode)." }
        $installDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/AI Light'
        $installedExe = Join-Path $installDir 'AI Light.exe'
        if ((Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash -ne $release.installerSha256) {
            throw 'Installed application does not match the verified release.'
        }
        foreach ($notice in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md')) {
            Copy-Item -LiteralPath (Join-Path $package $notice) -Destination $installDir -Force
        }
        Copy-Item -LiteralPath (Join-Path $package 'licenses') -Destination $installDir -Recurse -Force
        Write-Host 'VibeCodingLight is installed. Choose your assistants under Connections.'
    } finally {
        if (-not $retain) {
            $resolvedStage = [IO.Path]::GetFullPath($stage)
            if ([IO.Path]::GetDirectoryName($resolvedStage) -ne $tempRoot -or
                [IO.Path]::GetFileName($resolvedStage) -notmatch '^VibeCodingLight-install-[0-9a-f]{32}$') {
                throw 'Unexpected temporary path; cleanup stopped.'
            }
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
} -OnlyDownload $DownloadOnly.IsPresent
