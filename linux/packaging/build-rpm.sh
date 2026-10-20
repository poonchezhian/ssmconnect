#!/usr/bin/env bash
# Builds dist-linux/ssmconnect-VERSION-1.ARCH.rpm (Fedora; needs glibc 2.39+) from source. Needs rpmbuild, cargo, gtk4-devel, libadwaita-devel.
# The build needs about 2 GB of temporary space: if /tmp is a small RAM disk, set TMPDIR to a folder on a real disk first.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
cd "$here"
ver=$(cat VERSION); out=$here/dist-linux; top=$(mktemp -d); work=$(mktemp -d); trap 'rm -rf "$top" "$work"' EXIT
mkdir -p "$out" "$top"/{SOURCES,SPECS,BUILD,RPMS,SRPMS} "$work/ssmconnect-$ver-src"
cp -r Cargo.toml Cargo.lock src data packaging README.md CHANGELOG.md VERSION LICENSE NOTICE SIGNING-KEY.asc "$work/ssmconnect-$ver-src/"
tar -C "$work" -czf "$top/SOURCES/ssmconnect-$ver-src.tar.gz" "ssmconnect-$ver-src"
cp packaging/ssmconnect.spec "$top/SPECS/"
rpmbuild --define "_topdir $top" --define "ver $ver" -bb "$top/SPECS/ssmconnect.spec" >&2
cp "$top"/RPMS/*/*.rpm "$out/"
ls "$out"/ssmconnect-"$ver"-*.rpm
