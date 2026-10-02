#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST_PATH="$ROOT_DIR/native/fullbleed-dotnet-native/Cargo.toml"
RID="${1:-}"

RUST_HOST=""
while read -r key value; do
  if [[ "$key" == "host:" ]]; then RUST_HOST="$value"; fi
done < <(rustc -vV)
case "$RUST_HOST" in
  x86_64-pc-windows-msvc) HOST_RID="win-x64" ;;
  aarch64-pc-windows-msvc) HOST_RID="win-arm64" ;;
  x86_64-unknown-linux-gnu) HOST_RID="linux-x64" ;;
  aarch64-unknown-linux-gnu) HOST_RID="linux-arm64" ;;
  x86_64-apple-darwin) HOST_RID="osx-x64" ;;
  aarch64-apple-darwin) HOST_RID="osx-arm64" ;;
  *) echo "unsupported Rust host: $RUST_HOST" >&2; exit 2 ;;
esac
if [[ -n "$RID" && "$RID" != "$HOST_RID" ]]; then
  echo "RID $RID does not match Rust host $RUST_HOST ($HOST_RID); build on the matching host" >&2
  exit 2
fi
RID="$HOST_RID"
TARGET_DIR="${CARGO_TARGET_DIR:-$ROOT_DIR/native/fullbleed-dotnet-native/target}"

echo "[build-native] cargo build --release ($RID)"
cargo build --locked --manifest-path "$MANIFEST_PATH" --release --target "$RUST_HOST" --target-dir "$TARGET_DIR"

case "$RID" in
  win-*) FILE_NAME="fullbleed_dotnet_native.dll" ;;
  osx-*) FILE_NAME="libfullbleed_dotnet_native.dylib" ;;
  linux-*) FILE_NAME="libfullbleed_dotnet_native.so" ;;
  *) echo "unsupported RID: $RID" >&2; exit 2 ;;
esac

SOURCE="$TARGET_DIR/$RUST_HOST/release/$FILE_NAME"
DESTINATION="$ROOT_DIR/runtimes/$RID/native"
test -f "$SOURCE"
mkdir -p "$DESTINATION"
cp "$SOURCE" "$DESTINATION/$FILE_NAME"
echo "[build-native] staged $DESTINATION/$FILE_NAME"
