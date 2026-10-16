#!/bin/sh
# SSMConnect @VERSION@ installer for Linux (@ARCH@).   Run it:  sh ./ssmconnect-@VERSION@-linux-@ARCH@.bin   (or make it executable and double-click it)
#
# This single file carries SSMConnect itself and nothing else. It does not bundle any third-party tool: the installer window
# first fetches what SSMConnect needs from each tool's own source (Amazon for the AWS CLI and the Session Manager plugin, with
# Amazon's signature checked; your distribution's repositories for GTK 4, libadwaita, OpenSSH and Remote Desktop), and only then
# installs SSMConnect.
#
# Options: --text (questions in this terminal instead of a window)  --yes (accept the defaults)  --system (install for all users)
#          --prefix DIR  --extract DIR (only unpack the program)  --help
set -u
PAYLOAD_SHA256="@SHA256@"

die() { echo "ssmconnect installer: $*" >&2; exit 1; }
case "${1:-}" in
  -h|--help) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
esac

tmp=$(mktemp -d "${TMPDIR:-/tmp}/ssmconnect-install.XXXXXX") || die "cannot create a temporary folder"
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT INT TERM

# 1. unpack the program that is appended to this file
marker=$(grep -a -n '^__SSMCONNECT_PAYLOAD__$' "$0" | head -n 1 | cut -d: -f1)
[ -n "$marker" ] || die "this file is damaged (no payload found); download it again"
tail -n +$((marker + 1)) "$0" > "$tmp/ssmconnect" || die "cannot unpack"
if command -v sha256sum >/dev/null 2>&1; then
  got=$(sha256sum "$tmp/ssmconnect" | cut -d' ' -f1)
  [ "$got" = "$PAYLOAD_SHA256" ] || die "this file is damaged or incomplete (checksum mismatch); download it again"
else
  echo "warning: sha256sum not found; skipping the integrity check" >&2
fi
chmod 755 "$tmp/ssmconnect"

if [ "${1:-}" = "--extract" ]; then
  [ -n "${2:-}" ] || die "--extract needs a folder"
  mkdir -p "$2" && cp "$tmp/ssmconnect" "$2/ssmconnect" && echo "Unpacked to $2/ssmconnect" && exit 0
  die "cannot write to $2"
fi

# 2. GTK 4 and libadwaita come from the distribution; if the program cannot start without them, offer to install them first
if ! "$tmp/ssmconnect" --version >/dev/null 2>&1; then
  pm=""; pkgs=""
  . /etc/os-release 2>/dev/null || true
  like=" ${ID:-} ${ID_LIKE:-} "
  case "$like" in
    *" fedora "*|*" rhel "*|*" centos "*|*" rocky "*|*" almalinux "*|*" amzn "*) pm="dnf"; pkgs="gtk4 libadwaita"; cmd="dnf install -y" ;;
    *" debian "*|*" ubuntu "*) pm="apt"; pkgs="libgtk-4-1 libadwaita-1-0"; cmd="env DEBIAN_FRONTEND=noninteractive apt-get install -y" ;;
    *" suse "*|*" opensuse "*|*" sles "*) pm="zypper"; pkgs="libgtk-4-1 libadwaita-1-0"; cmd="zypper --non-interactive install" ;;
    *" arch "*|*" manjaro "*) pm="pacman"; pkgs="gtk4 libadwaita"; cmd="pacman -S --noconfirm --needed" ;;
  esac
  echo "SSMConnect needs GTK 4 (4.12 or newer) and libadwaita (1.5 or newer), which this computer does not have yet."
  "$tmp/ssmconnect" --version 2>&1 | head -n 2 | sed 's/^/  /' >&2
  [ -n "$pm" ] || die "install GTK 4 and libadwaita with your package manager, then run this installer again"
  printf "Install %s from your distribution's repositories with %s? [Y/n] " "$pkgs" "$pm"
  read -r answer || answer=y
  case "${answer:-y}" in n|N|no|NO) die "cancelled; nothing was installed" ;; esac
  if [ "$(id -u)" -eq 0 ]; then sh -c "$cmd $pkgs"; else
    command -v sudo >/dev/null 2>&1 || die "sudo was not found; run as root or install $pkgs yourself"
    sudo sh -c "$cmd $pkgs"
  fi || die "could not install $pkgs"
  "$tmp/ssmconnect" --version >/dev/null 2>&1 || die "the program still cannot start; run \"$tmp/ssmconnect --version\" to see why"
fi

# 3. hand over to the installer (a window when there is a display, questions in the terminal otherwise)
"$tmp/ssmconnect" --installer --source "$tmp/ssmconnect" "$@"
exit $?
__SSMCONNECT_PAYLOAD__
