#!/bin/sh
# Points the install commands in the docs and scripts at your GitHub repo, and turns on the
# commit-message template that reminds you how version bumps work:
#   scripts/set-repo.sh your-user/usage-widget
set -eu
[ $# -eq 1 ] || { echo "usage: $0 owner/repo" >&2; exit 2; }
cd "$(dirname "$0")/.."
grep -rl "OWNER/REPO" README.md docs macos/scripts windows/install.ps1 | while read -r file; do
  sed -i.bak "s|OWNER/REPO|$1|g" "$file" && rm "$file.bak"
  echo "updated $file"
done
if git rev-parse --git-dir >/dev/null 2>&1; then
  git config commit.template .gitmessage
  echo "commit template on (.gitmessage)"
fi
