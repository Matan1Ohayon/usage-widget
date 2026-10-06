#!/bin/zsh
# Installs (or updates) the latest AI Usage release for macOS into ~/Applications and launches it:
#   curl -fsSL https://raw.githubusercontent.com/OWNER/REPO/main/macos/scripts/install-release.sh | zsh
# Downloads made with curl aren't quarantined, so Gatekeeper doesn't block the unsigned app.
set -euo pipefail

REPO="${AIUSAGE_REPO:-OWNER/REPO}"
# AIUSAGE_URL overrides the download (e.g. a file:// zip when testing the script).
URL="${AIUSAGE_URL:-https://github.com/$REPO/releases/latest/download/AI-Usage-macOS.zip}"
DEST="$HOME/Applications/AI Usage.app"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "Downloading $URL"
curl -fL --progress-bar "$URL" -o "$TMP/AI-Usage-macOS.zip"
ditto -x -k "$TMP/AI-Usage-macOS.zip" "$TMP"

pkill -x AIUsage 2>/dev/null && sleep 1 || true
mkdir -p "$HOME/Applications"
rm -rf "$DEST"
ditto "$TMP/AI Usage.app" "$DEST"
xattr -dr com.apple.quarantine "$DEST" 2>/dev/null || true
open "$DEST"
echo "Installed: $DEST — look for two small bars in the menu bar."
