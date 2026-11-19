[CmdletBinding()]
param(
    [string] $InstallDirectory = 'C:\Program Files\PowerShell\7.7'
)

$ErrorActionPreference = 'Stop'

function Assert-PowerShell77 {
    param([string] $Directory)

    $executable = Join-Path $Directory 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "PowerShell executable is missing at '$executable'."
    }

    $version = & $executable -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()'
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^7\.7\.\d+$') {
        throw "Expected a stable PowerShell 7.7 executable at '$executable'. Reported version: '$version'."
    }
}

if (Test-Path -LiteralPath $InstallDirectory) {
    Assert-PowerShell77 -Directory $InstallDirectory
    return
}

$archive = Join-Path $PSScriptRoot 'PowerShell-7.7.0-win-x64.zip'
$expectedHash = '7C74E03B20A75B59715847439D046A6214B1863EE79B187872980F139BB4A05F'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    throw "SHA-256 mismatch for '$archive'."
}

$stagingDirectory = "$InstallDirectory.install-$([guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $stagingDirectory
    Assert-PowerShell77 -Directory $stagingDirectory

    try {
        [IO.Directory]::Move($stagingDirectory, $InstallDirectory)
    } catch [IO.IOException] {
        if (-not (Test-Path -LiteralPath $InstallDirectory -PathType Container)) {
            throw
        }
        Assert-PowerShell77 -Directory $InstallDirectory
    }
} finally {
    if (Test-Path -LiteralPath $stagingDirectory -PathType Container) {
        [IO.Directory]::Delete($stagingDirectory, $true)
    }
}
