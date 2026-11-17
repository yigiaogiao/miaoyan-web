#!/usr/bin/env bash
# Copy a publicly reachable image into registry.lazycat.cloud and point the
# LazyCat manifest at the returned reference.
#
#   docker buildx build --platform linux/amd64,linux/arm64 \
#     -t <your-dockerhub>/miaoyan-web:1.0.0 --push .
#   ./scripts/prepare-store-image.sh <your-dockerhub>/miaoyan-web:1.0.0
#
# The image must be public: copy-image runs `docker pull` on LazyCat's servers.
set -euo pipefail

PUBLIC_IMAGE="${1:-}"
if [[ -z "${PUBLIC_IMAGE}" ]]; then
  echo "usage: $0 <public-image-reference>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="${ROOT}/lazycat/lzc-manifest.yml"
LZC_VERSION="${LZC_CLI_VERSION:-2.0.9}"

if command -v lzc-cli >/dev/null 2>&1; then
  LZC=(lzc-cli)
else
  LZC=(npx --yes "@lazycatcloud/lzc-cli@${LZC_VERSION}")
fi

echo "Copying ${PUBLIC_IMAGE} to the LazyCat registry..."
OUTPUT="$("${LZC[@]}" appstore copy-image "${PUBLIC_IMAGE}")"
echo "${OUTPUT}"

REFERENCE="$(printf '%s\n' "${OUTPUT}" | grep -o 'registry\.lazycat\.cloud/[^[:space:]]*' | tail -n 1 || true)"
if [[ -z "${REFERENCE}" ]]; then
  echo "Could not find a registry.lazycat.cloud reference in the output." >&2
  exit 1
fi

python3 - "${MANIFEST}" "${REFERENCE}" <<'PY'
import re
import sys
from pathlib import Path

path = Path(sys.argv[1])
reference = sys.argv[2]
text = path.read_text(encoding="utf-8")
new_text, count = re.subn(
    r"(?m)^(\s+image:\s*)\S+\s*$",
    lambda m: m.group(1) + reference,
    text,
    count=1,
)
if count != 1:
    raise SystemExit("no services.miaoyan.image line found in " + str(path))
path.write_text(new_text, encoding="utf-8")
print(f"Updated {path} -> {reference}")
PY

"${ROOT}/scripts/verify-lazycat.sh"
