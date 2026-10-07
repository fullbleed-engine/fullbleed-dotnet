[CmdletBinding()]
param(
    [switch]$SkipPack,
    [ValidateSet('net8.0', 'net9.0', 'net10.0')]
    [string]$TargetFramework = 'net8.0',
    [string]$PackageVersion,
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
# macOS exposes /var through /private/var. MSBuild item updates can miss files
# when the project and its current directory use different spellings of that path.
# See https://github.com/dotnet/sdk/issues/34442.
if ([System.IO.Path]::DirectorySeparatorChar -eq '/') {
    Push-Location $consumer
    try {
        $consumer = & /bin/pwd -P
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the physical consumer directory.' }
    } finally { Pop-Location }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
[xml]$versions = Get-Content -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Raw
$version = ($versions.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'FullBleed.DotNet' }).Version
if ($PackageVersion) { $version = $PackageVersion }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a stable numeric package version.' }
$packagePath = Join-Path $packages "FullBleed.DotNet.$version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath)) { throw "Missing package: $packagePath" }
$project = Join-Path $consumer 'Consumer.csproj'
$frameworkVersion = $TargetFramework.Substring(3)
# Select the matching stable SDK even when newer SDKs are installed. The consumer
# lives outside the repository, so its global.json does not inherit the build SDK.
@{
    sdk = @{ version = "$frameworkVersion.100"; rollForward = 'latestFeature'; allowPrerelease = $false }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $consumer 'global.json') -Encoding utf8
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>$TargetFramework</TargetFramework>
    <RollForward>LatestPatch</RollForward>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FullBleed.DotNet" Version="$version" />
    <None Update="Assets/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/Program.cs') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/FontFamilyFixtures.cs') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/InlineLayoutFixtures.cs') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/StandardFontFixtures.cs') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repository 'tests/FullBleed.DotNet.PackageSmoke/TaggedStructureFixtures.cs') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repository 'samples/FullBleed.DotNet.Showcase/Assets') -Destination $consumer -Recurse
foreach ($name in @('tagged-structure.html', 'tagged-structure.css')) {
    Copy-Item -LiteralPath (Join-Path $repository "tests/FullBleed.DotNet.PackageSmoke/Assets/$name") -Destination (Join-Path $consumer 'Assets')
}
$verificationFonts = Join-Path $consumer 'Assets/verification-fonts'
New-Item -ItemType Directory -Path $verificationFonts | Out-Null
foreach ($name in @('NotoSans-Regular.ttf', 'NotoSans-OFL.txt')) {
    Copy-Item -LiteralPath (Join-Path $repository "tests/FullBleed.DotNet.Tests/Assets/$name") -Destination $verificationFonts
}
$priorNative = $env:FULLBLEED_NATIVE_LIBRARY
try {
    $env:FULLBLEED_NATIVE_LIBRARY = $null
    Push-Location $consumer
    try {
        $sdkVersion = & dotnet --version
        if ($LASTEXITCODE -ne 0) { throw 'The matching stable .NET SDK is required.' }
        & dotnet restore $project --source $packages --packages (Join-Path $consumer 'packages') --force-evaluate
        if ($LASTEXITCODE -ne 0) { throw 'package smoke restore failed' }
        & dotnet run --project $project -c Release --no-restore -- (Join-Path $output 'invoice.pdf') $version $frameworkVersion
        if ($LASTEXITCODE -ne 0) { throw 'package smoke execution failed' }
    } finally { Pop-Location }
} finally { $env:FULLBLEED_NATIVE_LIBRARY = $priorNative }
@{
    package = (Split-Path $packagePath -Leaf)
    sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    target_framework = $TargetFramework
    sdk_version = $sdkVersion
    consumer = $consumer
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'package.json') -Encoding utf8
Write-Host "Isolated package evidence: $output"
