#!/usr/bin/env bash
# Build the LazyCat package. Requires lzc-cli (or npx network access) and the
# image referenced from lazycat/lzc-manifest.yml to be reachable.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LZC_VERSION="${LZC_CLI_VERSION:-2.0.9}"

if command -v lzc-cli >/dev/null 2>&1; then
  LZC=(lzc-cli)
else
  LZC=(npx --yes "@lazycatcloud/lzc-cli@${LZC_VERSION}")
fi

"${ROOT}/scripts/verify-lazycat.sh"
cd "${ROOT}/lazycat"
"${LZC[@]}" project build . -f lzc-build.yml

ls -lh "${ROOT}/lazycat"/*.lpk
