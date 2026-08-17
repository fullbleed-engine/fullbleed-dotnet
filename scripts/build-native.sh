#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST_PATH="$ROOT_DIR/native/fullbleed-dotnet-native/Cargo.toml"
RID="${1:-}"

if [[ -z "$RID" ]]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID="linux-x64" ;;
    Linux-aarch64|Linux-arm64) RID="linux-arm64" ;;
    Darwin-x86_64) RID="osx-x64" ;;
    Darwin-arm64) RID="osx-arm64" ;;
    MINGW*|MSYS*|CYGWIN*) RID="win-x64" ;;
    *) echo "unsupported host; pass an explicit RID" >&2; exit 2 ;;
  esac
fi

echo "[build-native] cargo build --release ($RID)"
cargo build --locked --manifest-path "$MANIFEST_PATH" --release

TARGET_DIR="$ROOT_DIR/native/fullbleed-dotnet-native/target/release"
case "$RID" in
  win-*) FILE_NAME="fullbleed_dotnet_native.dll" ;;
  osx-*) FILE_NAME="libfullbleed_dotnet_native.dylib" ;;
  linux-*) FILE_NAME="libfullbleed_dotnet_native.so" ;;
  *) echo "unsupported RID: $RID" >&2; exit 2 ;;
esac

SOURCE="$TARGET_DIR/$FILE_NAME"
DESTINATION="$ROOT_DIR/runtimes/$RID/native"
test -f "$SOURCE"
mkdir -p "$DESTINATION"
cp "$SOURCE" "$DESTINATION/$FILE_NAME"
echo "[build-native] staged $DESTINATION/$FILE_NAME"
