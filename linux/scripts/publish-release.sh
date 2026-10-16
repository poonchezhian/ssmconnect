#!/usr/bin/env bash
# Publishes a Linux release to GitHub: pushes the branch and the signed tag, creates the release with the signed files, and updates the wiki.
#
#   export GITHUB_TOKEN=...      a personal access token with "repo" (and wiki) write access; never printed, never stored
#   scripts/publish-release.sh [--dry-run]
#
# What it does, in order (each step is safe to repeat):
#   1. git push origin linux and the tag linux-vX.Y.Z (the tag is signed with the release key)
#   2. creates the GitHub release for that tag (as a pre-release while the version is 0.x) and uploads
#      the files in dist-linux/ (.bin, .deb, .rpm, their .asc signatures, SHA256SUMS.txt(.asc)) plus SIGNING-KEY.asc
#   3. copies the pages in ../wiki/ into the project wiki and pushes them (existing pages are kept; only these files are written)
set -euo pipefail
cd "$(dirname "$0")/.."
dry=0; [ "${1:-}" = "--dry-run" ] && dry=1
ver=$(cat VERSION); tag="linux-v$ver"
repo=${GITHUB_REPOSITORY:-poonchezhian/ssmconnect}
run() { if [ $dry = 1 ]; then echo "[dry run] $*"; else "$@"; fi; }
[ $dry = 1 ] || [ -n "${GITHUB_TOKEN:-}" ] || { echo "Set GITHUB_TOKEN (a personal access token with write access to $repo) and run again." >&2; exit 2; }
auth=(-c "http.extraheader=Authorization: Basic $(printf 'x-access-token:%s' "${GITHUB_TOKEN:-dry}" | base64 -w0)")

echo "== 1. push the code and the tag $tag"
git remote get-url origin >/dev/null 2>&1 || git remote add origin "https://github.com/$repo.git"
git rev-parse -q --verify "refs/tags/$tag" >/dev/null || { echo "tag $tag does not exist; create it first" >&2; exit 1; }
git verify-tag "$tag" 2>&1 | grep -E "Good signature" || echo "note: the tag is not signed (or the key is not in this keyring)"
run git "${auth[@]}" push origin linux "$tag"

echo "== 2. the GitHub release with the signed files"
files=(dist-linux/*.bin dist-linux/*.bin.asc dist-linux/*.deb dist-linux/*.deb.asc dist-linux/*.rpm dist-linux/*.rpm.asc dist-linux/SHA256SUMS.txt dist-linux/SHA256SUMS.txt.asc SIGNING-KEY.asc)
existing=(); for f in "${files[@]}"; do [ -e "$f" ] && existing+=("$f"); done
notes=$(awk -v v="$ver" '$0 ~ "^## \\["v"\\]" {on=1; next} /^## \[/ {on=0} on' CHANGELOG.md)
notes+=$'\n\nLinux edition (native, Rust + GTK 4). Check a download: `gpg --import SIGNING-KEY.asc`, confirm the fingerprint (FF9F 45E6 4711 7103 89B3 F1FC 7483 B1F8 DBE4 A803), then `gpg --verify <file>.asc <file>`. Checksums: `sha256sum -c SHA256SUMS.txt`.'
if [ $dry = 1 ]; then echo "[dry run] would create release $tag and upload: ${existing[*]}"; else
  api() { curl -fsS -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" "$@"; }
  rel=$(api "https://api.github.com/repos/$repo/releases/tags/$tag" 2>/dev/null || true)
  if [ -z "$rel" ]; then
    body=$(python3 -c 'import json,sys; print(json.dumps({"tag_name":sys.argv[1],"name":"SSMConnect for Linux "+sys.argv[2],"body":sys.argv[3],"prerelease":sys.argv[2].startswith("0."),"draft":False}))' "$tag" "$ver" "$notes")
    rel=$(api -X POST -d "$body" "https://api.github.com/repos/$repo/releases")
  fi
  id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$rel")
  have=$(api "https://api.github.com/repos/$repo/releases/$id/assets?per_page=100" | python3 -c 'import json,sys; print("\n".join(a["name"] for a in json.load(sys.stdin)))')
  for f in "${existing[@]}"; do
    n=$(basename "$f")
    if grep -qx "$n" <<<"$have"; then echo "  already uploaded: $n"; continue; fi
    echo "  uploading $n"
    api -X POST -H "Content-Type: application/octet-stream" --data-binary "@$f" "https://uploads.github.com/repos/$repo/releases/$id/assets?name=$n" >/dev/null
  done
fi

echo "== 3. the wiki"
w=$(mktemp -d); trap 'rm -rf "$w"' EXIT
if [ $dry = 1 ]; then echo "[dry run] would copy $(ls ../wiki/*.md | wc -l) page(s) from ../wiki into the wiki repo and push"; else
  git "${auth[@]}" clone -q "https://github.com/$repo.wiki.git" "$w"
  cp ../wiki/*.md "$w"/
  git -C "$w" add -A
  if git -C "$w" diff --cached --quiet; then echo "  wiki already up to date"; else
    git -C "$w" -c user.name="$(git config user.name)" -c user.email="$(git config user.email)" commit -q -m "Linux edition: announcement, install and update guide, release notes"
    git "${auth[@]}" -C "$w" push -q origin HEAD
    echo "  wiki updated"
  fi
fi
echo "Done: https://github.com/$repo/releases/tag/$tag"
