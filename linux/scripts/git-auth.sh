#!/usr/bin/env bash
# Source inside a private temporary directory; the caller owns cleanup.
setup_git_auth() {
  [ -n "${GH_TOKEN:-}" ] || return 0
  export GH_TOKEN
  export GIT_ASKPASS="$1/git-askpass"
  export GIT_TERMINAL_PROMPT=0
  cat >"$GIT_ASKPASS" <<'ASKPASS'
#!/bin/sh
case "$1" in
  *Username*) printf '%s\n' x-access-token ;;
  *Password*) printf '%s\n' "$GH_TOKEN" ;;
  *) exit 1 ;;
esac
ASKPASS
  chmod 700 "$GIT_ASKPASS"
  export GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=credential.helper GIT_CONFIG_VALUE_0=
}
