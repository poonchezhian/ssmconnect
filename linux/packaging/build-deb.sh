#!/usr/bin/env bash
# Builds dist-linux/ssmconnect_VERSION_ARCH.deb (Debian 13+, Ubuntu 24.04+, Mint 22+ ...). Needs dpkg-deb and a Rust toolchain.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
cd "$here"
ver=$(cat VERSION); arch=$(dpkg --print-architecture); out=$here/dist-linux; root=$(mktemp -d); trap 'rm -rf "$root"' EXIT
mkdir -p "$out"
cargo build --release --locked >&2
target/release/ssmconnect --install-files --prefix /usr --destdir "$root"
mv "$root/usr/share/licenses/ssmconnect" "$root/usr/share/doc/ssmconnect/license" && rmdir "$root/usr/share/licenses"
# the update repository: new releases arrive with the system's normal updates (apt upgrade, GNOME Software, Discover)
install -Dm644 packaging/repo/ssmconnect.sources "$root/etc/apt/sources.list.d/ssmconnect.sources"
install -d "$root/usr/share/keyrings"
gpg --dearmor < SIGNING-KEY.asc > "$root/usr/share/keyrings/ssmconnect-archive-keyring.gpg"
chmod 644 "$root/usr/share/keyrings/ssmconnect-archive-keyring.gpg"
install -d "$root/DEBIAN"
echo /etc/apt/sources.list.d/ssmconnect.sources > "$root/DEBIAN/conffiles"
cat > "$root/DEBIAN/control" <<CTL
Package: ssmconnect
Version: $ver
Section: net
Priority: optional
Architecture: $arch
Depends: libgtk-4-1 (>= 4.12), libadwaita-1-0 (>= 1.5), libc6 (>= 2.34), openssh-client
Recommends: freerdp3-x11 | freerdp2-x11 | remmina, gnome-terminal | konsole | xfce4-terminal | xterm
Maintainer: Poonchezhian Periasamy <poonchezhian@gmail.com>
Homepage: https://github.com/poonchezhian/ssmconnect
Description: SSH and Remote Desktop to AWS servers over Session Manager
 Finds your AWS servers, opens secure tunnels through AWS Systems Manager (port 22 stays closed)
 and configures OpenSSH so each server is one short command away. Native GTK 4 application written in Rust.
CTL
find "$root" -type d -exec chmod 755 {} +
dpkg-deb --root-owner-group --build "$root" "$out/ssmconnect_${ver}_${arch}.deb" >/dev/null
echo "$out/ssmconnect_${ver}_${arch}.deb"
