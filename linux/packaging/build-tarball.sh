#!/usr/bin/env bash
# Builds dist-linux/ssmconnect-VERSION-linux-ARCH.tar.gz: the compiled program plus install.sh (no root needed by default).
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
cd "$here"
ver=$(cat VERSION); arch=$(uname -m)
name=ssmconnect-$ver-linux-$arch
out=$here/dist-linux; work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cargo build --release --locked >&2
mkdir -p "$out" "$work/$name/bin" "$work/$name/packaging"
cp target/release/ssmconnect "$work/$name/bin/"
cp -r data "$work/$name/"
cp packaging/stage.sh "$work/$name/packaging/"
cp README.md CHANGELOG.md VERSION "$work/$name/"
cp ../LICENSE ../NOTICE "$work/$name/"
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
if [ "$mode" = uninstall ]; then
  rm -rf "$prefix/bin/ssmconnect" "$prefix/share/applications/$app.desktop" "$prefix/share/metainfo/$app.metainfo.xml" \
    "$prefix/share/icons/hicolor/scalable/apps/$app.svg" "$prefix/share/doc/ssmconnect" "$prefix/share/licenses/ssmconnect"
  echo "SSMConnect removed from $prefix. Your settings stay; remove them with: ssmconnect cleanup (run before uninstalling)."
  exit 0
fi
if ! ./bin/ssmconnect --version >/dev/null 2>&1; then
  echo "This build does not start on this system (missing GTK 4 / libadwaita, or a too-old C library)." >&2
  echo "Install gtk4 (>= 4.12) and libadwaita (>= 1.5), or build from source: cargo build --release --locked" >&2
  ldd ./bin/ssmconnect 2>&1 | grep "not found" >&2 || true
  exit 1
fi
packaging/stage.sh "" "$prefix"
(command -v update-desktop-database >/dev/null && update-desktop-database "$prefix/share/applications" 2>/dev/null) || true
(command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$prefix/share/icons/hicolor" 2>/dev/null) || true
echo "Installed SSMConnect to $prefix. Run: ssmconnect   (make sure $prefix/bin is on your PATH)"
echo "First time: ssmconnect doctor"
INSTALL
chmod 755 "$work/$name/install.sh" "$work/$name/packaging/stage.sh"
tar -C "$work" --owner=0 --group=0 -czf "$out/$name.tar.gz" "$name"
echo "$out/$name.tar.gz"
