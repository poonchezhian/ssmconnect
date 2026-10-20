#!/usr/bin/env bash
# Signs the files in dist-linux/ with the SSMConnect Linux release key (OpenPGP, detached, armored) and writes a signed SHA256SUMS.txt.
#   scripts/sign-release.sh                 uses the only secret key in your keyring, or the one named in SSMCONNECT_SIGNING_KEY (fingerprint)
# Produces:  <file>.asc for every .bin / .deb / .rpm,  SHA256SUMS.txt  and  SHA256SUMS.txt.asc
# Then verifies everything against SIGNING-KEY.asc in a throw-away keyring, exactly as a user would.
set -euo pipefail
cd "$(dirname "$0")/.."
key=${SSMCONNECT_SIGNING_KEY:-}
args=(--batch --yes --armor --detach-sign)
[ -n "$key" ] && args+=(--local-user "$key")
shopt -s nullglob
files=(dist-linux/*.bin dist-linux/*.deb dist-linux/*.rpm)
[ ${#files[@]} -gt 0 ] || { echo "nothing to sign in dist-linux/" >&2; exit 1; }
for f in "${files[@]}"; do gpg "${args[@]}" --output "$f.asc" "$f"; done
(cd dist-linux && sha256sum "${files[@]##*/}" > SHA256SUMS.txt)
gpg "${args[@]}" --output dist-linux/SHA256SUMS.txt.asc dist-linux/SHA256SUMS.txt

# verify like a stranger would: a fresh keyring that only knows the published public key
v=$(mktemp -d); chmod 700 "$v"; trap 'rm -rf "$v"' EXIT
GNUPGHOME=$v gpg --batch --quiet --import SIGNING-KEY.asc
fp=$(GNUPGHOME=$v gpg --with-colons --list-keys | awk -F: '/^fpr/{print $10; exit}')
for f in "${files[@]}" dist-linux/SHA256SUMS.txt; do GNUPGHOME=$v gpg --batch --verify "$f.asc" "$f" 2>&1 | grep -E "Good signature|BAD|No public" | sed "s|^|$(basename "$f"): |"; done
(cd dist-linux && sha256sum -c SHA256SUMS.txt)
echo "Signed with key $fp"
