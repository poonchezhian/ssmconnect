#!/usr/bin/env bash
# Replaces the Linux installers kept in the repository's dist/ folder with the files of a release, commits that on main and fast-forwards the linux branch.
#   scripts/update-dist.sh DIR [--no-push]    DIR holds the release files (ssmconnect*, SHA256SUMS.txt, SHA256SUMS.txt.asc). Run from a checkout of main.
set -euo pipefail
src=${1:?directory with the release files}; push=1; [ "${2:-}" = "--no-push" ] && push=0
root=$(git -C "$(dirname "$0")" rev-parse --show-toplevel); cd "$root"
ver=$(cat linux/VERSION)
git rm -q --ignore-unmatch dist/ssmconnect* dist/SHA256SUMS-linux.txt dist/SHA256SUMS-linux.txt.asc
cp "$src"/ssmconnect* dist/
cp "$src/SHA256SUMS.txt" dist/SHA256SUMS-linux.txt; cp "$src/SHA256SUMS.txt.asc" dist/SHA256SUMS-linux.txt.asc
git add -A dist
if git diff --cached --quiet; then echo "dist/ already holds $ver"; exit 0; fi
git -c user.name="$(git config user.name || echo 'github-actions[bot]')" -c user.email="$(git config user.email || echo '41898282+github-actions[bot]@users.noreply.github.com')" commit -q -m "dist: Linux $ver installers"
[ $push = 1 ] || exit 0
git push -q origin HEAD:main
git push -q origin HEAD:linux
echo "dist/ updated to $ver on main and linux"
