#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$installerScript = if ($PSScriptRoot) { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../install-online.ps1')) } else { Join-Path $PWD 'windows/install-online.ps1' }
$script:installerBlock = [scriptblock]::Create([IO.File]::ReadAllText($installerScript))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$fixture = Join-Path $tempRoot ('VibeCodingLight-online-tests-' + [guid]::NewGuid().ToString('N'))
$retained = @()
function Remove-TestDirectory([string]$Path, [string]$Prefix) {
    $full = [IO.Path]::GetFullPath($Path)
    if ([IO.Path]::GetDirectoryName($full) -ne $tempRoot -or [IO.Path]::GetFileName($full) -notmatch ('^' + $Prefix + '[0-9a-f]{32}$')) {
        throw 'Unexpected test directory; cleanup stopped.'
    }
    Remove-Item -LiteralPath $full -Recurse -Force
}
try {
    $name = 'VibeCodingLight-windows-v0.1.51-win-x64'
    $package = Join-Path $fixture $name
    New-Item -ItemType Directory -Path (Join-Path $package 'licenses') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $package 'VibeCodingLight-Setup.exe'), 'test fixture; never executable')
    foreach ($notice in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'licenses/dependency.txt')) {
        [IO.File]::WriteAllText((Join-Path $package $notice), 'test license')
    }
    $script:archive = Join-Path $fixture 'fixture.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $script:archive, [IO.Compression.CompressionLevel]::Optimal, $true)
    $script:release = [pscustomobject]@{
        version = '0.1.51'
        archiveSha256 = (Get-FileHash -LiteralPath $script:archive).Hash
        installerSha256 = (Get-FileHash -LiteralPath (Join-Path $package 'VibeCodingLight-Setup.exe')).Hash
    }
    $originalArchiveHash = $script:release.archiveSha256
    $originalExeHash = $script:release.installerSha256
    $script:launches = 0
    $script:downloads = 0
    function Invoke-RestMethod($Uri, $TimeoutSec) {
        if ($Uri -ne 'https://www.vibecodinglight.com/releases/windows/installer.json') { throw 'Unexpected manifest origin.' }
        return $script:release
    }
    function Invoke-WebRequest($Uri, $OutFile, $TimeoutSec, [switch]$UseBasicParsing) {
        if ($Uri -ne 'https://github.com/barancavusoglu/vibecodinglight-desktop/releases/download/windows-v0.1.51/VibeCodingLight-windows-v0.1.51-win-x64.zip') {
            throw 'Unexpected download origin.'
        }
        $script:downloads++
        Copy-Item -LiteralPath $script:archive -Destination $OutFile
    }
    function Start-Process($FilePath, $ArgumentList, [switch]$PassThru) {
        if ($ArgumentList -ne '--install' -or (Get-FileHash -LiteralPath $FilePath).Hash -ne $originalExeHash) { throw 'Unverified installer launch.' }
        $script:launches++
        throw 'VERIFIED_INSTALL_REQUESTED'
    }
    function Expect-Failure([scriptblock]$Action, [string]$Message) {
        $observed = $null
        try { & $Action | Out-Null } catch { $observed = $_.Exception.Message }
        if (-not $observed -or $observed -notlike "$Message*") { throw "Expected '$Message', observed '$observed'." }
    }

    $result = & $script:installerBlock -DownloadOnly
    $retained += $result.Directory
    if ($result.Version -ne '0.1.51' -or -not (Test-Path -LiteralPath $result.Installer) -or $script:launches -ne 0) { throw 'Download-only validation failed.' }
    Write-Output 'PASS: valid release downloads and verifies without installing.'

    $script:release.archiveSha256 = '0' * 64
    Expect-Failure { & $script:installerBlock -DownloadOnly } 'Archive checksum mismatch.'
    $script:release.archiveSha256 = $originalArchiveHash
    $script:release.installerSha256 = '0' * 64
    Expect-Failure { & $script:installerBlock -DownloadOnly } 'Installer checksum mismatch.'
    $script:release.installerSha256 = $originalExeHash
    $beforeDownloads = $script:downloads
    $script:release.version = '../../unexpected'
    Expect-Failure { & $script:installerBlock -DownloadOnly } 'Invalid Windows release version.'
    if ($script:downloads -ne $beforeDownloads -or $script:launches -ne 0) { throw 'Invalid metadata caused download or execution.' }
    $script:release.version = '0.1.51'
    Write-Output 'PASS: archive hash, executable hash and invalid version fail before execution.'

    $noticePath = Join-Path $package 'NOTICE'
    Remove-Item -LiteralPath $noticePath
    $script:archive = Join-Path $fixture 'missing-notice.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $script:archive, [IO.Compression.CompressionLevel]::Optimal, $true)
    $script:release.archiveSha256 = (Get-FileHash -LiteralPath $script:archive).Hash
    Expect-Failure { & $script:installerBlock -DownloadOnly } 'Missing license material:'
    if ($script:launches -ne 0) { throw 'Incomplete package was executed.' }
    $script:archive = Join-Path $fixture 'fixture.zip'
    $script:release.archiveSha256 = $originalArchiveHash
    Write-Output 'PASS: missing license material prevents execution.'

    Expect-Failure { Invoke-Expression ([IO.File]::ReadAllText($installerScript)) } 'VERIFIED_INSTALL_REQUESTED'
    if ($script:launches -ne 1) { throw 'One-step expression did not request the verified installer exactly once.' }
    Write-Output 'PASS: Invoke-Expression reaches the verified installer; no real process was started.'
} finally {
    foreach ($directory in $retained) { Remove-TestDirectory $directory 'VibeCodingLight-install-' }
    if (Test-Path -LiteralPath $fixture) { Remove-TestDirectory $fixture 'VibeCodingLight-online-tests-' }
}
