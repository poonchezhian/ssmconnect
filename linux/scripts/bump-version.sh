#!/usr/bin/env bash
# Usage: scripts/bump-version.sh patch|minor|major|X.Y.Z
# Updates Cargo.toml (and Cargo.lock), linux/VERSION, the AppStream release entry, and the download links in the READMEs and wiki pages.
# Then: edit CHANGELOG.md, commit, and tag with: git tag -a linux-vX.Y.Z -m "SSMConnect for Linux X.Y.Z"
set -euo pipefail
cd "$(dirname "$0")/.."
cur=$(cat VERSION)
IFS=. read -r ma mi pa <<<"$cur"
case "${1:-patch}" in
  patch) new="$ma.$mi.$((pa+1))" ;;
  minor) new="$ma.$((mi+1)).0" ;;
  major) new="$((ma+1)).0.0" ;;
  [0-9]*.[0-9]*.[0-9]*) new="$1" ;;
  *) echo "usage: $0 patch|minor|major|X.Y.Z" >&2; exit 2 ;;
esac
sed -i "0,/^version = \".*\"/s//version = \"$new\"/" Cargo.toml
echo "$new" > VERSION
sed -i "s|<release version=\"[^\"]*\" date=\"[^\"]*\"/>|<release version=\"$new\" date=\"$(date +%F)\"/>|" data/io.github.poonchezhian.SSMConnect.metainfo.xml
# download links and install steps in the READMEs and the wiki pages follow the new version too
for f in ../README.md README.md ../wiki/Linux-Edition.md ../wiki/Linux-Install-and-Update.md; do
  sed -i "s/linux-v$cur/linux-v$new/g; s/ssmconnect-$cur-linux/ssmconnect-$new-linux/g; s/Linux $cur release/Linux $new release/g" "$f"
done
echo "Also add a $new entry to ../wiki/Linux-Release-Notes.md, and check the file size shown in ../README.md."
cargo update --workspace --offline -q 2>/dev/null || cargo check -q
echo "$cur -> $new"
