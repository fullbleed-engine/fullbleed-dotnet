[CmdletBinding()]
param(
    [switch]$SkipPack,
    [string]$PackageDirectory = 'artifacts/packages',
    [string]$OutputDirectory = 'artifacts/package-smoke'
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packages = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory
} else { Join-Path $repository $PackageDirectory }
$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else { Join-Path $repository $OutputDirectory }
$packages = [System.IO.Path]::GetFullPath($packages)
$output = [System.IO.Path]::GetFullPath($output)

if (-not $SkipPack) {
    & (Join-Path $PSScriptRoot 'pack.ps1')
}

# A consumer under the checkout could silently use a staged native DLL through the
# development resolver. Keep both the consumer and NuGet cache outside the source tree.
$consumer = Join-Path ([System.IO.Path]::GetTempPath()) ('fullbleed-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $consumer -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null
[xml]$versions = Get-Content -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Raw
$version = ($versions.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'FullBleed.DotNet' }).Version
$packagePath = Join-Path $packages "FullBleed.DotNet.$version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath)) { throw "Missing package: $packagePath" }
$project = Join-Path $consumer 'Consumer.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FullBleed.DotNet" Version="$version" />
    <None Update="Assets/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/Program.cs') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repository 'samples/FullBleed.DotNet.Showcase/Assets') -Destination $consumer -Recurse
$priorNative = $env:FULLBLEED_NATIVE_LIBRARY
try {
    $env:FULLBLEED_NATIVE_LIBRARY = $null
    Push-Location $consumer
    try {
        & dotnet restore $project --source $packages --packages (Join-Path $consumer 'packages') --force-evaluate
        if ($LASTEXITCODE -ne 0) { throw 'package smoke restore failed' }
        & dotnet run --project $project -c Release --no-restore -- (Join-Path $output 'invoice.pdf') $version
        if ($LASTEXITCODE -ne 0) { throw 'package smoke execution failed' }
    } finally { Pop-Location }
} finally { $env:FULLBLEED_NATIVE_LIBRARY = $priorNative }
@{
    package = (Split-Path $packagePath -Leaf)
    sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    consumer = $consumer
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'package.json') -Encoding utf8
Write-Host "Isolated package evidence: $output"
