[CmdletBinding()]
param(
    [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'build-native.ps1')
}

& cargo fmt --manifest-path (Join-Path $repository 'native/fullbleed-dotnet-native/Cargo.toml') -- --check
if ($LASTEXITCODE -ne 0) { throw 'cargo fmt failed' }

& cargo test --locked --manifest-path (Join-Path $repository 'native/fullbleed-dotnet-native/Cargo.toml')
if ($LASTEXITCODE -ne 0) { throw 'cargo test failed' }

& cargo clippy --locked --manifest-path (Join-Path $repository 'native/fullbleed-dotnet-native/Cargo.toml') --all-targets -- -D warnings
if ($LASTEXITCODE -ne 0) { throw 'cargo clippy failed' }

& dotnet restore (Join-Path $repository 'FullBleed.DotNet.sln')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

& dotnet format (Join-Path $repository 'FullBleed.DotNet.sln') --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet format failed' }

& dotnet build (Join-Path $repository 'FullBleed.DotNet.sln') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

& dotnet test (Join-Path $repository 'tests/FullBleed.DotNet.Tests/FullBleed.DotNet.Tests.csproj') -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed' }

Write-Host 'All Fullbleed .NET checks passed.'
