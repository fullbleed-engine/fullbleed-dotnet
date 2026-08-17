[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/packages',
    [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository $OutputDirectory
}

if (-not $SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'build-native.ps1')
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& dotnet pack (Join-Path $repository 'src/FullBleed.DotNet/FullBleed.DotNet.csproj') `
    -c Release `
    -o $OutputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE"
}

Write-Host "Packages written to $OutputDirectory"
