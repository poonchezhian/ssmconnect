#!/usr/bin/env bash
# Builds the signed update repository (dnf + apt) from the .rpm and .deb files and publishes it on GitHub Pages (the gh-pages branch), so installed
# copies get new releases with the system's normal updates. The ssmconnect packages ship the matching repo file and public key.
#
#   scripts/publish-repo.sh [--build-only] [DIR]
#
#   DIR           where the .rpm and .deb are (default: dist-linux). Only the newest version of each is published.
#   --build-only  assemble and sign the site in a temporary folder, print where it is, and publish nothing
#   SSMCONNECT_FLATPAK_REPO  folder with the OSTree repository made by packaging/build-flatpak.sh (dist-linux/flatpak-repo); when it exists it is signed and published too
#   SSMCONNECT_SIGNING_KEY   fingerprint of the release key (default: the SSMConnect Linux Releases key; its secret key must be in your keyring)
#
# Layout of the site:  rpm/ (packages + repodata, repomd.xml signed)   deb/ (packages, Packages(.gz), Release, InRelease, Release.gpg)   SIGNING-KEY.asc  index.html
# Needs: gpg, rpmsign, createrepo_c, dpkg-scanpackages (Fedora: dnf install rpm-sign createrepo_c dpkg-dev), and git to publish.
set -euo pipefail
cd "$(dirname "$0")/.."
build_only=0; [ "${1:-}" = "--build-only" ] && { build_only=1; shift; }
src=${1:-dist-linux}
key=${SSMCONNECT_SIGNING_KEY:-FF9F45E64711710389B3F1FC7483B1F8DBE4A803}
repo=${GITHUB_REPOSITORY:-poonchezhian/ssmconnect}
base="https://${repo%%/*}.github.io/${repo##*/}"
for t in gpg rpmsign createrepo_c dpkg-scanpackages gzip sha256sum; do command -v "$t" >/dev/null || { echo "missing tool: $t" >&2; exit 1; }; done
gpg --list-secret-keys "$key" >/dev/null 2>&1 || { echo "the release key $key is not in your keyring" >&2; exit 1; }
newest() { ls "$src"/$1 2>/dev/null | sort -V | tail -1; }
rpm=$(newest 'ssmconnect-*.rpm'); deb=$(newest 'ssmconnect_*.deb')
[ -n "$rpm" ] && [ -n "$deb" ] || { echo "need one .rpm and one .deb in $src" >&2; exit 1; }
site=$(mktemp -d); mkdir "$site/rpm" "$site/deb"; touch "$site/.nojekyll"
cp SIGNING-KEY.asc "$site/"

echo "== rpm repository ($(basename "$rpm"))"
cp "$rpm" "$site/rpm/"
# sign the rpm itself unless it already is (so the file on the release page and the one in the repository are the same file)
if rpm -qpi "$site/rpm/$(basename "$rpm")" 2>/dev/null | grep -q '^Signature *: *(none)'; then
  rpmsign --addsign --define "_gpg_name $key" "$site/rpm/$(basename "$rpm")" >/dev/null
fi
createrepo_c --quiet "$site/rpm"
gpg --batch --yes --armor --detach-sign --local-user "$key" --output "$site/rpm/repodata/repomd.xml.asc" "$site/rpm/repodata/repomd.xml"

echo "== apt repository ($(basename "$deb"))"
cp "$deb" "$site/deb/"
(cd "$site/deb" && dpkg-scanpackages --multiversion . /dev/null > Packages 2>/dev/null && gzip -9nk Packages)
{
  echo "Origin: SSMConnect"; echo "Label: SSMConnect for Linux"; echo "Suite: stable"; echo "Codename: ssmconnect"
  echo "Date: $(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S UTC')"; echo "Architectures: amd64"
  echo "Description: SSMConnect for Linux update repository"
  echo "SHA256:"
  for f in Packages Packages.gz; do printf ' %s %s %s\n' "$(sha256sum "$site/deb/$f" | cut -d' ' -f1)" "$(stat -c %s "$site/deb/$f")" "$f"; done
} > "$site/deb/Release"
gpg --batch --yes --clearsign --local-user "$key" --output "$site/deb/InRelease" "$site/deb/Release"
gpg --batch --yes --armor --detach-sign --local-user "$key" --output "$site/deb/Release.gpg" "$site/deb/Release"

# the Flatpak repository (RHEL 8/9, Ubuntu 22.04, Debian 12, Mint 21 ...): signed with the same key, installed with ssmconnect.flatpakref
fp=${SSMCONNECT_FLATPAK_REPO:-}; [ -z "$fp" ] && [ -d "$src/flatpak-repo" ] && fp="$src/flatpak-repo"
if [ -n "$fp" ] && [ -d "$fp" ] && command -v flatpak >/dev/null; then
  echo "== flatpak repository"
  mkdir -p "$site/flatpak"; cp -a "$fp" "$site/flatpak/repo"
  gh_home=${GNUPGHOME:-$HOME/.gnupg}
  flatpak build-sign --gpg-sign="$key" --gpg-homedir="$gh_home" "$site/flatpak/repo" io.github.poonchezhian.SSMConnect stable
  flatpak build-update-repo --gpg-sign="$key" --gpg-homedir="$gh_home" --prune "$site/flatpak/repo" >/dev/null
  gpgkey=$(gpg --export "$key" | base64 -w0)
  cat > "$site/ssmconnect.flatpakrepo" <<REPO
[Flatpak Repo]
Title=SSMConnect for Linux
Url=$base/flatpak/repo/
Homepage=https://github.com/$repo
Comment=SSMConnect for Linux, for distributions whose GTK 4 and libadwaita are too old for the native packages
GPGKey=$gpgkey
REPO
  cat > "$site/ssmconnect.flatpakref" <<REF
[Flatpak Ref]
Title=SSMConnect for Linux
Name=io.github.poonchezhian.SSMConnect
Branch=stable
Url=$base/flatpak/repo/
SuggestRemoteName=ssmconnect
IsRuntime=false
RuntimeRepo=https://dl.flathub.org/repo/flathub.flatpakrepo
GPGKey=$gpgkey
REF
fi

cat > "$site/index.html" <<HTML
<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SSMConnect for Linux: update repository</title>
<body style="font:16px/1.5 system-ui,sans-serif;max-width:46rem;margin:2rem auto;padding:0 1rem">
<h1>SSMConnect for Linux: update repository</h1>
<p>The <code>ssmconnect</code> .rpm and .deb install the repository file for you, so new releases arrive with your system's normal updates. Download the first package from the
<a href="https://github.com/$repo/releases">Releases page</a>, or add the repository by hand:</p>
<p><b>Fedora, RHEL, openSUSE (dnf)</b><br><code>sudo curl -o /etc/yum.repos.d/ssmconnect.repo $base/ssmconnect.repo &amp;&amp; sudo dnf install ssmconnect</code></p>
<p><b>Debian, Ubuntu (apt)</b><br><code>sudo curl -o /usr/share/keyrings/ssmconnect-archive-keyring.asc $base/SIGNING-KEY.asc</code><br>
<code>echo -e "Types: deb\nURIs: $base/deb/\nSuites: ./\nSigned-By: /usr/share/keyrings/ssmconnect-archive-keyring.asc" | sudo tee /etc/apt/sources.list.d/ssmconnect.sources</code><br>
<code>sudo apt update &amp;&amp; sudo apt install ssmconnect</code></p>
<p><b>Older distributions (RHEL 8 and 9, Ubuntu 22.04, Debian 12, Linux Mint 21: GTK 4 / libadwaita too old for the packages above), Flatpak</b><br><code>flatpak install --user $base/ssmconnect.flatpakref</code><br>Updates arrive with <code>flatpak update</code>.</p>
<p>Signing key: <a href="SIGNING-KEY.asc">SIGNING-KEY.asc</a>, fingerprint <code>FF9F 45E6 4711 7103 89B3 F1FC 7483 B1F8 DBE4 A803</code>. Check it matches before you trust it.</p>
HTML
# the by-hand dnf instructions fetch the repo file and the key from the site root
sed "s|file:///etc/pki/rpm-gpg/RPM-GPG-KEY-ssmconnect|$base/SIGNING-KEY.asc|" packaging/repo/ssmconnect.repo > "$site/ssmconnect.repo"

if [ $build_only = 1 ]; then echo "Built (not published): $site"; exit 0; fi

echo "== publish to the gh-pages branch"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
url="https://github.com/$repo.git"
if git ls-remote --exit-code --heads "$url" gh-pages >/dev/null 2>&1; then git clone -q --branch gh-pages --depth 1 "$url" "$work"; else git init -q -b gh-pages "$work"; git -C "$work" remote add origin "$url"; fi
find "$work" -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +
cp -a "$site"/. "$work"/
git -C "$work" add -A
if git -C "$work" diff --cached --quiet; then echo "nothing changed"; exit 0; fi
git -C "$work" -c user.name="$(git config user.name)" -c user.email="$(git config user.email)" commit -q -m "Repository for SSMConnect $(cat VERSION)"
git -C "$work" push -q origin HEAD:gh-pages
echo "Published: $base/"
