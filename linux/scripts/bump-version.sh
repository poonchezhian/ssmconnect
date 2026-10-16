#!/usr/bin/env bash
# Usage: scripts/bump-version.sh patch|minor|major|X.Y.Z
# Updates src/ssmconnect/_version.py and linux/VERSION. Commit, then tag with: git tag -a linux-vX.Y.Z -m "Linux X.Y.Z"
set -euo pipefail
cd "$(dirname "$0")/.."
cur=$(sed -n 's/^__version__ = "\(.*\)"/\1/p' src/ssmconnect/_version.py)
IFS=. read -r ma mi pa <<<"$cur"
case "${1:-patch}" in
  patch) new="$ma.$mi.$((pa+1))" ;;
  minor) new="$ma.$((mi+1)).0" ;;
  major) new="$((ma+1)).0.0" ;;
  [0-9]*.[0-9]*.[0-9]*) new="$1" ;;
  *) echo "usage: $0 patch|minor|major|X.Y.Z" >&2; exit 2 ;;
esac
sed -i "s/^__version__ = .*/__version__ = \"$new\"/" src/ssmconnect/_version.py
echo "$new" > VERSION
echo "$cur -> $new"
