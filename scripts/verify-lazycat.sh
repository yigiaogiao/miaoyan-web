#!/usr/bin/env bash
# Cheap pre-flight checks before handing a package to the LazyCat store.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG="${ROOT}/lazycat/package.yml"
MANIFEST="${ROOT}/lazycat/lzc-manifest.yml"
ICON="${ROOT}/lazycat/icon.png"
ALLOW_PLACEHOLDER=0
fail=0

for arg in "$@"; do
  case "${arg}" in
    --allow-placeholder) ALLOW_PLACEHOLDER=1 ;;
    *)
      echo "usage: $0 [--allow-placeholder]" >&2
      exit 2
      ;;
  esac
done

check() {
  local description="$1"
  shift
  if "$@"; then
    echo "ok   ${description}"
  else
    echo "FAIL ${description}" >&2
    fail=1
  fi
}

check "package id is cloud.lazycat.app.miaoyanweb" grep -q '^package: cloud\.lazycat\.app\.miaoyanweb$' "${PKG}"
check "version is semver" grep -Eq '^version: [0-9]+\.[0-9]+\.[0-9]+$' "${PKG}"
# The earlier `check ... grep -A4 ... | grep -q ...` form piped check's own
# output: the description contains "document.private", so it matched itself.
check "document.private permission declared" awk '
  /^permissions:/ { in_permissions = 1; next }
  in_permissions && /^[^[:space:]]/ { in_permissions = 0 }
  in_permissions && /document\.private/ { found = 1 }
  END { exit !found }
' "${PKG}"
check "zh-CN locale present" grep -q '^  zh-CN:' "${PKG}"
check "en-US locale present" grep -q '^  en-US:' "${PKG}"
check "icon is a PNG" bash -c "test -f '${ICON}' && file '${ICON}' | grep -q PNG"
check "icon under 200 KB" bash -c "test \"\$(stat -c %s '${ICON}')\" -lt 204800"
check "image is on registry.lazycat.cloud" grep -Eq '^\s+image: registry\.lazycat\.cloud/' "${MANIFEST}"
if [[ "${ALLOW_PLACEHOLDER}" -eq 1 ]]; then
  echo "skip image is not the repository placeholder (allowed by --allow-placeholder)"
else
  check "image is not the repository placeholder" bash -c "! grep -q 'registry.lazycat.cloud/miaoyan/miaoyan-web:0.1.0' '${MANIFEST}'"
fi
check "run_as 1000:1000 declared" grep -q 'run_as: "1000:1000"' "${MANIFEST}"
check "health check present" grep -q 'http://127.0.0.1:5080/healthz' "${MANIFEST}"
check "state dir under /lzcapp/var" grep -q 'MIAOYAN_STATE_DIR=/lzcapp/var/miaoyan' "${MANIFEST}"
check "no legacy ext_config permissions" bash -c "! grep -q 'ext_config' '${MANIFEST}'"

if [[ "${fail}" -ne 0 ]]; then
  echo "" >&2
  echo "LazyCat pre-flight failed. See lazycat/README.md." >&2
  exit 1
fi
echo "LazyCat pre-flight passed."
