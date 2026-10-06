#!/bin/zsh
# Builds "AI Usage.app" from source, installs it to ~/Applications and (re)launches it.
set -euo pipefail

ROOT="${0:A:h:h}"
DEST="$HOME/Applications/AI Usage.app"

APP="$("$ROOT/scripts/build-app.sh" | tail -1)"

pkill -x AIUsage 2>/dev/null && sleep 1 || true
mkdir -p "$HOME/Applications"
rm -rf "$DEST"
cp -R "$APP" "$DEST"
open "$DEST"
echo "Installed and launched: $DEST"
