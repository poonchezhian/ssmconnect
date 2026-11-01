#!/usr/bin/env bash
# Copies wiki/*.md from this repository to the GitHub wiki (a separate git repository) and pushes when something changed.
#   scripts/sync-wiki.sh [MESSAGE]       run from anywhere inside the repository; needs push access to the wiki
# In GitHub Actions set GITHUB_REPOSITORY and GH_TOKEN; by hand it uses your own git credentials.
set -euo pipefail
root=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
repo=${GITHUB_REPOSITORY:-poonchezhian/ssmconnect}
url="https://github.com/$repo.wiki.git"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
source "$(dirname "$0")/git-auth.sh"
setup_git_auth "$work"
work_repo="$work/repo"
git clone -q "$url" "$work_repo"
cp "$root"/wiki/*.md "$work_repo"/
git -C "$work_repo" add -A
if git -C "$work_repo" diff --cached --quiet; then echo "wiki already up to date"; exit 0; fi
name=$(git -C "$root" config user.name || echo "github-actions[bot]"); mail=$(git -C "$root" config user.email || echo "41898282+github-actions[bot]@users.noreply.github.com")
git -C "$work_repo" -c user.name="$name" -c user.email="$mail" commit -q -m "${1:-Sync from the repository}"
git -C "$work_repo" push -q origin HEAD
echo "wiki updated"
