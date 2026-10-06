#!/bin/sh
# Prints the version the next release should get, from the latest vX.Y.Z tag:
#   - commit messages since that tag contain "#major" → X+1.0.0, "#minor" → X.Y+1.0, otherwise → X.Y.Z+1
#   - BUMP=patch|minor|major overrides the commit messages (manual "Run workflow")
#   - no tags yet → 1.0.0; HEAD already tagged (re-run) → that same version
set -eu

current=$(git tag --points-at HEAD --list 'v[0-9]*.[0-9]*.[0-9]*' | sort -V | tail -n1)
if [ -n "$current" ]; then echo "${current#v}"; exit 0; fi

last=$(git tag --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname | head -n1)
if [ -z "$last" ]; then echo 1.0.0; exit 0; fi

version=${last#v}
major=${version%%.*}; rest=${version#*.}; minor=${rest%%.*}; patch=${rest#*.}

bump=${BUMP:-}
if [ -z "$bump" ]; then
  log=$(git log --format=%B "$last..HEAD")
  case "$log" in
    *"#major"*) bump=major ;;
    *"#minor"*) bump=minor ;;
    *) bump=patch ;;
  esac
fi

case "$bump" in
  major) echo "$((major + 1)).0.0" ;;
  minor) echo "$major.$((minor + 1)).0" ;;
  patch) echo "$major.$minor.$((patch + 1))" ;;
  *) echo "unknown BUMP '$bump' (use patch, minor or major)" >&2; exit 2 ;;
esac
