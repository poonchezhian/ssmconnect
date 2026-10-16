#!/usr/bin/env bash
# Lays the program out like an installed system: stage.sh DESTDIR PREFIX   (DESTDIR may be empty)
#   e.g. stage.sh /tmp/root /usr
# Used by the tarball, deb and rpm builders so they all ship exactly the same files.
set -euo pipefail
[ $# -eq 2 ] || { echo "usage: stage.sh DESTDIR PREFIX (DESTDIR may be empty)" >&2; exit 2; }
dest=$1; prefix=$2
here=$(cd "$(dirname "$0")/.." && pwd)
app=io.github.poonchezhian.SSMConnect
bin=$here/target/release/ssmconnect; [ -x "$bin" ] || bin=$here/bin/ssmconnect   # source checkout, or unpacked tarball
[ -x "$bin" ] || { echo "no built program found: run  cargo build --release --locked  first" >&2; exit 1; }

install -d "$dest$prefix/bin" "$dest$prefix/share/applications" "$dest$prefix/share/metainfo" \
  "$dest$prefix/share/icons/hicolor/scalable/apps" "$dest$prefix/share/doc/ssmconnect" "$dest$prefix/share/licenses/ssmconnect"
install -m 755 "$bin" "$dest$prefix/bin/ssmconnect"
install -m 644 "$here/data/$app.desktop" "$dest$prefix/share/applications/"
install -m 644 "$here/data/$app.metainfo.xml" "$dest$prefix/share/metainfo/"
install -m 644 "$here/data/icons/hicolor/scalable/apps/$app.svg" "$dest$prefix/share/icons/hicolor/scalable/apps/"
install -m 644 "$here/README.md" "$here/CHANGELOG.md" "$dest$prefix/share/doc/ssmconnect/"
legal=$here; [ -f "$legal/LICENSE" ] || legal=$here/..   # a source checkout keeps them one folder up, the tarball beside the code
install -m 644 "$legal/LICENSE" "$legal/NOTICE" "$dest$prefix/share/licenses/ssmconnect/"
