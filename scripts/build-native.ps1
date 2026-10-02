[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Join-Path $repository 'native/fullbleed-dotnet-native/Cargo.toml'

$rustInfo = & rustc -vV
if ($LASTEXITCODE -ne 0) { throw 'rustc is required to build the native library.' }
$rustHost = ($rustInfo | Where-Object { $_ -like 'host: *' }) -replace '^host: ', ''
$hostRids = @{
    'x86_64-pc-windows-msvc' = 'win-x64'
    'aarch64-pc-windows-msvc' = 'win-arm64'
    'x86_64-unknown-linux-gnu' = 'linux-x64'
    'aarch64-unknown-linux-gnu' = 'linux-arm64'
    'x86_64-apple-darwin' = 'osx-x64'
    'aarch64-apple-darwin' = 'osx-arm64'
}
$hostRid = $hostRids[$rustHost]
if (-not $hostRid) { throw "Unsupported Rust host: $rustHost" }
if ($Rid -and $Rid -ne $hostRid) {
    throw "RID $Rid does not match Rust host $rustHost ($hostRid). Build on the matching host."
}
$Rid = $hostRid
$targetDirectory = if ($env:CARGO_TARGET_DIR) {
    [System.IO.Path]::GetFullPath($env:CARGO_TARGET_DIR)
} else { Join-Path $repository 'native/fullbleed-dotnet-native/target' }

& cargo build --locked --manifest-path $manifest --release --target $rustHost --target-dir $targetDirectory
if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE"
}

$fileName = switch -Wildcard ($Rid) {
    'win-*' { 'fullbleed_dotnet_native.dll'; break }
    'osx-*' { 'libfullbleed_dotnet_native.dylib'; break }
    'linux-*' { 'libfullbleed_dotnet_native.so'; break }
    default { throw "Unsupported RID: $Rid" }
}
$source = Join-Path $targetDirectory "$rustHost/release/$fileName"
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Native build output was not found: $source"
}

$destination = Join-Path $repository "runtimes/$Rid/native"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $destination $fileName) -Force
Write-Host "Staged runtimes/$Rid/native/$fileName"
