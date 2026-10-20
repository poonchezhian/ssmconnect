#!/usr/bin/env bash
# Keeps the stable-name downloads of the newest Linux release on the rolling `linux-latest` release, the way the Windows app has SSMConnect-Setup-x64.exe:
#   https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.bin   (also ...-x86_64.rpm, ...-amd64.deb, each with .asc, and SHA256SUMS.txt)
# (releases/latest/download/ belongs to the Windows release, so Linux uses its own fixed tag.)
#
#   scripts/publish-stable.sh [DIR]     DIR holds the versioned, signed release files (default: dist-linux). Needs gh, gpg, the release key in the keyring.
#   SSMCONNECT_SIGNING_KEY              fingerprint of the release key (default: the SSMConnect Linux Releases key)
set -euo pipefail
cd "$(dirname "$0")/.."
src=${1:-dist-linux}
key=${SSMCONNECT_SIGNING_KEY:-FF9F45E64711710389B3F1FC7483B1F8DBE4A803}
repo=${GITHUB_REPOSITORY:-poonchezhian/ssmconnect}
newest() { ls "$src"/$1 2>/dev/null | grep -v '\.asc$' | sort -V | tail -1; }
bin=$(newest 'ssmconnect-*-linux-*.bin'); deb=$(newest 'ssmconnect_*.deb'); rpm=$(newest 'ssmconnect-*.rpm')
[ -n "$bin" ] && [ -n "$deb" ] && [ -n "$rpm" ] || { echo "need a .bin, .deb and .rpm in $src" >&2; exit 1; }
ver=$(basename "$bin" | sed 's/^ssmconnect-\(.*\)-linux-.*/\1/')
out=$(mktemp -d); trap 'rm -rf "$out"' EXIT
cp "$bin" "$out/ssmconnect-linux-x86_64.bin"; cp "$deb" "$out/ssmconnect-linux-amd64.deb"; cp "$rpm" "$out/ssmconnect-linux-x86_64.rpm"
# the signature covers the file's bytes, so the versioned .asc is valid for the renamed copy too
cp "$bin.asc" "$out/ssmconnect-linux-x86_64.bin.asc"; cp "$deb.asc" "$out/ssmconnect-linux-amd64.deb.asc"; cp "$rpm.asc" "$out/ssmconnect-linux-x86_64.rpm.asc"
cp SIGNING-KEY.asc "$out/"
(cd "$out" && sha256sum ssmconnect-linux-* | grep -v '\.asc' > SHA256SUMS.txt)
gpg --batch --yes --armor --detach-sign --local-user "$key" --output "$out/SHA256SUMS.txt.asc" "$out/SHA256SUMS.txt"
notes="Always the newest SSMConnect for Linux (currently $ver), under fixed file names so a link never goes stale. Release notes and older versions: https://github.com/$repo/releases/tag/linux-v$ver
Check a download: gpg --import SIGNING-KEY.asc, confirm the fingerprint FF9F 45E6 4711 7103 89B3 F1FC 7483 B1F8 DBE4 A803, then gpg --verify <file>.asc <file>."
target=$(git rev-parse "linux-v$ver^{commit}" 2>/dev/null || git rev-parse HEAD)
gh release delete linux-latest -R "$repo" --cleanup-tag -y >/dev/null 2>&1 || true
gh release create linux-latest -R "$repo" --target "$target" --prerelease --latest=false --title "SSMConnect for Linux (latest, stable file names)" --notes "$notes" "$out"/*
echo "Stable downloads now serve $ver: https://github.com/$repo/releases/download/linux-latest/ssmconnect-linux-x86_64.bin"
