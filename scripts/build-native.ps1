[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Join-Path $repository 'native/fullbleed-dotnet-native/Cargo.toml'

if (-not $Rid) {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $Rid = "win-$architecture"
    } elseif ($IsMacOS) {
        $Rid = "osx-$architecture"
    } elseif ($IsLinux) {
        $Rid = "linux-$architecture"
    } else {
        throw 'Unsupported operating system. Pass -Rid explicitly.'
    }
}

& cargo build --locked --manifest-path $manifest --release
if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE"
}

$fileName = switch -Wildcard ($Rid) {
    'win-*' { 'fullbleed_dotnet_native.dll'; break }
    'osx-*' { 'libfullbleed_dotnet_native.dylib'; break }
    'linux-*' { 'libfullbleed_dotnet_native.so'; break }
    default { throw "Unsupported RID: $Rid" }
}
$source = Join-Path $repository "native/fullbleed-dotnet-native/target/release/$fileName"
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Native build output was not found: $source"
}

$destination = Join-Path $repository "runtimes/$Rid/native"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $destination $fileName) -Force
Write-Host "Staged runtimes/$Rid/native/$fileName"
