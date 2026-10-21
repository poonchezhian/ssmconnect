#!/usr/bin/env bash
# Builds dist-linux/ssmconnect-VERSION-x86_64.flatpak (a single-file bundle) and an OSTree repository in dist-linux/flatpak-repo.
# Needs flatpak, flatpak-builder and, from Flathub, org.gnome.Platform//50, org.gnome.Sdk//50 and org.freedesktop.Sdk.Extension.rust-stable//25.08.
#   packaging/build-flatpak.sh [--install]     --install also installs it for this user, to try it
set -euo pipefail
cd "$(dirname "$0")/.."
ver=$(cat VERSION); id=io.github.poonchezhian.SSMConnect
state=${SSMCONNECT_FLATPAK_STATE:-$HOME/.cache/ssmconnect-flatpak}
mkdir -p "$state" dist-linux
extra=(); [ "${1:-}" = "--install" ] && extra+=(--user --install)
flatpak-builder --force-clean --disable-rofiles-fuse --default-branch=stable --state-dir="$state" --repo=dist-linux/flatpak-repo "${extra[@]}" "$state/build" packaging/flatpak/$id.yml
flatpak build-update-repo dist-linux/flatpak-repo >/dev/null
flatpak build-bundle dist-linux/flatpak-repo "dist-linux/ssmconnect-$ver-x86_64.flatpak" $id stable
echo "Built dist-linux/ssmconnect-$ver-x86_64.flatpak"
