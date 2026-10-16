#!/usr/bin/env bash
# Lays the program out like an installed system: stage.sh DESTDIR PREFIX PYDIR
#   e.g. stage.sh /tmp/root /usr /usr/lib/python3/dist-packages
# Used by the tarball, deb and rpm builders so they all ship exactly the same files.
set -euo pipefail
[ $# -eq 3 ] || { echo "usage: stage.sh DESTDIR PREFIX PYDIR (DESTDIR may be empty)" >&2; exit 2; }
dest=$1; prefix=$2; pydir=$3
here=$(cd "$(dirname "$0")/.." && pwd)
app=io.github.poonchezhian.SSMConnect

install -d "$dest$pydir" "$dest$prefix/bin" "$dest$prefix/share/applications" "$dest$prefix/share/metainfo" \
  "$dest$prefix/share/icons/hicolor/scalable/apps" "$dest$prefix/share/doc/ssmconnect"
cp -r "$here/src/ssmconnect" "$dest$pydir/"
find "$dest$pydir/ssmconnect" -name __pycache__ -prune -exec rm -rf {} +
install -m 644 "$here/data/$app.desktop" "$dest$prefix/share/applications/"
install -m 644 "$here/data/$app.metainfo.xml" "$dest$prefix/share/metainfo/"
install -m 644 "$here/data/icons/hicolor/scalable/apps/$app.svg" "$dest$prefix/share/icons/hicolor/scalable/apps/"
install -m 644 "$here/README.md" "$here/CHANGELOG.md" "$dest$prefix/share/doc/ssmconnect/"
install -d "$dest$prefix/share/licenses/ssmconnect"
legal=$here; [ -f "$legal/LICENSE" ] || legal=$here/..   # a source checkout keeps them one folder up, the tarball beside the code
install -m 644 "$legal/LICENSE" "$legal/NOTICE" "$dest$prefix/share/licenses/ssmconnect/"
# the launcher: on a distribution package the module is on python's own path; in a tarball install it is added explicitly
case "$pydir" in
  /usr/lib/python3*|/usr/lib64/python3*)
    printf '#!/bin/sh\nexec python3 -m ssmconnect "$@"\n' > "$dest$prefix/bin/ssmconnect" ;;
  *)
    printf '#!/bin/sh\nPYTHONPATH="%s${PYTHONPATH:+:$PYTHONPATH}" exec python3 -m ssmconnect "$@"\n' "$pydir" > "$dest$prefix/bin/ssmconnect" ;;
esac
chmod 755 "$dest$prefix/bin/ssmconnect"
