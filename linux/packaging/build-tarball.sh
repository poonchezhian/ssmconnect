#!/usr/bin/env bash
# Builds dist-linux/ssmconnect-VERSION-linux.tar.gz: a self-contained folder with install.sh (no root needed by default).
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
ver=$(cat "$here/VERSION")
name=ssmconnect-$ver-linux
out=$here/dist-linux; work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
mkdir -p "$out" "$work/$name"
cp -r "$here/src" "$here/data" "$here/packaging" "$here/README.md" "$here/CHANGELOG.md" "$here/VERSION" "$work/$name/"
cp "$here/../LICENSE" "$here/../NOTICE" "$work/$name/"
find "$work/$name" -name __pycache__ -prune -exec rm -rf {} +
cat > "$work/$name/install.sh" <<'INSTALL'
#!/usr/bin/env bash
# Installs SSMConnect for you (default: ~/.local) or system-wide:  sudo ./install.sh --prefix /usr/local
# Remove again with:  ./install.sh --uninstall [--prefix ...]
set -euo pipefail
cd "$(dirname "$0")"
prefix="$HOME/.local"; mode=install
while [ $# -gt 0 ]; do case "$1" in
  --prefix) prefix=$2; shift 2 ;;
  --uninstall) mode=uninstall; shift ;;
  -h|--help) sed -n '2,3p' "$0"; exit 0 ;;
  *) echo "unknown option $1" >&2; exit 2 ;; esac; done
app=io.github.poonchezhian.SSMConnect
lib=$prefix/lib/ssmconnect
if [ "$mode" = uninstall ]; then
  rm -rf "$lib" "$prefix/bin/ssmconnect" "$prefix/share/applications/$app.desktop" "$prefix/share/metainfo/$app.metainfo.xml" \
    "$prefix/share/icons/hicolor/scalable/apps/$app.svg" "$prefix/share/doc/ssmconnect" "$prefix/share/licenses/ssmconnect"
  echo "SSMConnect removed from $prefix. Your settings stay; remove them with: ssmconnect cleanup (run before uninstalling)."
  exit 0
fi
python3 - <<'PY' || { echo "SSMConnect needs Python 3.10+ with GTK 4 and libadwaita (python3-gobject gtk4 libadwaita, or python3-gi gir1.2-gtk-4.0 gir1.2-adw-1)." >&2; exit 1; }
import sys, gi
assert sys.version_info >= (3, 10)
gi.require_version("Gtk", "4.0"); gi.require_version("Adw", "1")
PY
rm -rf "$lib"
packaging/stage.sh "" "$prefix" "$lib"
(command -v update-desktop-database >/dev/null && update-desktop-database "$prefix/share/applications" 2>/dev/null) || true
(command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$prefix/share/icons/hicolor" 2>/dev/null) || true
echo "Installed SSMConnect to $prefix. Run: ssmconnect   (make sure $prefix/bin is on your PATH)"
echo "First time: ssmconnect doctor"
INSTALL
chmod 755 "$work/$name/install.sh" "$work/$name/packaging/"*.sh
tar -C "$work" --owner=0 --group=0 -czf "$out/$name.tar.gz" "$name"
echo "$out/$name.tar.gz"
