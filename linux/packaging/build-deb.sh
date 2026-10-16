#!/usr/bin/env bash
# Builds dist-linux/ssmconnect_VERSION_all.deb (Debian, Ubuntu, Mint ...). Needs dpkg-deb.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
ver=$(cat "$here/VERSION"); out=$here/dist-linux; root=$(mktemp -d); trap 'rm -rf "$root"' EXIT
mkdir -p "$out"
"$here/packaging/stage.sh" "$root" /usr /usr/lib/python3/dist-packages
mv "$root/usr/share/licenses/ssmconnect" "$root/usr/share/doc/ssmconnect/license" && rmdir "$root/usr/share/licenses"
install -d "$root/DEBIAN"
cat > "$root/DEBIAN/control" <<CTL
Package: ssmconnect
Version: $ver
Section: net
Priority: optional
Architecture: all
Depends: python3 (>= 3.10), python3-gi, gir1.2-gtk-4.0, gir1.2-adw-1, openssh-client
Recommends: freerdp3-x11 | freerdp2-x11 | remmina, gnome-terminal | konsole | xfce4-terminal | xterm
Maintainer: Poonchezhian Periasamy <poonchezhian@gmail.com>
Homepage: https://github.com/poonchezhian/ssmconnect
Description: SSH and Remote Desktop to AWS servers over Session Manager
 Finds your AWS servers, opens secure tunnels through AWS Systems Manager (port 22 stays closed)
 and configures OpenSSH so each server is one short command away.
CTL
find "$root" -type d -exec chmod 755 {} +
dpkg-deb --root-owner-group --build "$root" "$out/ssmconnect_${ver}_all.deb" >/dev/null
echo "$out/ssmconnect_${ver}_all.deb"
