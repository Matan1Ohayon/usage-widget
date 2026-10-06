#!/bin/zsh
# Builds "AI Usage.app" into macos/build/.
#   --universal      Apple Silicon + Intel in one binary (used by the release workflow)
#   --version X.Y.Z  stamps the version into Info.plist
set -euo pipefail

ROOT="${0:A:h:h}"
BUILD="$ROOT/build"
APP="$BUILD/AI Usage.app"
VERSION=""
UNIVERSAL=0

while (( $# )); do
  case "$1" in
    --universal) UNIVERSAL=1 ;;
    --version) VERSION="$2"; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

cd "$ROOT"
if (( UNIVERSAL )); then
  # One build per architecture, merged with lipo (works with or without Xcode).
  SLICES=()
  for arch in arm64 x86_64; do
    swift build -c release --product AIUsage --arch $arch
    SLICES+=("$(swift build -c release --show-bin-path --arch $arch)/AIUsage")
  done
  BIN="$BUILD/AIUsage-universal"
  mkdir -p "$BUILD"
  lipo -create "${SLICES[@]}" -output "$BIN"
else
  swift build -c release --product AIUsage
  BIN="$(swift build -c release --show-bin-path)/AIUsage"
fi

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/AIUsage"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
if [[ -n "$VERSION" ]]; then
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP/Contents/Info.plist"
fi

# App icon (rendered once, then reused).
if [[ ! -f "$BUILD/AppIcon.icns" ]]; then
  ICONSET="$BUILD/AppIcon.iconset"
  rm -rf "$ICONSET" && mkdir -p "$ICONSET"
  swift "$ROOT/scripts/make-icon.swift" "$BUILD/icon-1024.png"
  for size in 16 32 128 256 512; do
    sips -z $size $size "$BUILD/icon-1024.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$BUILD/icon-1024.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$BUILD/AppIcon.icns"
fi
cp "$BUILD/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc signature: required to run on Apple Silicon. Not notarized (that needs a paid Apple Developer account).
codesign --force --sign - --identifier dev.aiusage.mac "$APP"
echo "$APP"
