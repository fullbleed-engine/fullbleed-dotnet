[CmdletBinding()]
param(
    [switch]$SkipPack
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packages = Join-Path $repository 'artifacts/packages'

if (-not $SkipPack) {
    & (Join-Path $PSScriptRoot 'pack.ps1')
}

$project = Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/FullBleed.DotNet.PackageSmoke.csproj'
$output = Join-Path $repository 'artifacts/package-smoke/package-smoke.pdf'
& dotnet restore $project --source $packages --force-evaluate
if ($LASTEXITCODE -ne 0) { throw 'package smoke restore failed' }

& dotnet run --project $project -c Release --no-restore -- $output
if ($LASTEXITCODE -ne 0) { throw 'package smoke execution failed' }
