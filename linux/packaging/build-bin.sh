#!/usr/bin/env bash
# Builds dist-linux/ssmconnect-VERSION-linux-ARCH.bin: one self-extracting file. Run it and the installer wizard opens.
# It contains SSMConnect only; no third-party tool is bundled (see packaging/bin-stub.sh).
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
cd "$here"
ver=$(cat VERSION); arch=$(uname -m); out=$here/dist-linux
bin=${SSMCONNECT_BIN:-}
if [ -z "$bin" ]; then cargo build --release --locked >&2; bin=target/release/ssmconnect; fi
mkdir -p "$out"
sha=$(sha256sum "$bin" | cut -d' ' -f1)
file=$out/ssmconnect-$ver-linux-$arch.bin
sed -e "s/@VERSION@/$ver/g" -e "s/@ARCH@/$arch/g" -e "s/@SHA256@/$sha/" packaging/bin-stub.sh > "$file"
cat "$bin" >> "$file"
chmod 755 "$file"
echo "$file"
