#!/usr/bin/env bash
# Builds dist-linux/ssmconnect-VERSION-1.noarch.rpm (Fedora, RHEL, openSUSE ...). Needs rpmbuild.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
ver=$(cat "$here/VERSION"); out=$here/dist-linux; top=$(mktemp -d); trap 'rm -rf "$top"' EXIT
tarball=$("$here/packaging/build-tarball.sh")
mkdir -p "$top"/{SOURCES,SPECS,BUILD,RPMS,SRPMS}
cp "$tarball" "$top/SOURCES/"; cp "$here/packaging/ssmconnect.spec" "$top/SPECS/"
rpmbuild --define "_topdir $top" --define "ver $ver" -bb "$top/SPECS/ssmconnect.spec" >/dev/null
cp "$top"/RPMS/noarch/*.rpm "$out/"
ls "$out"/ssmconnect-"$ver"-*.rpm
