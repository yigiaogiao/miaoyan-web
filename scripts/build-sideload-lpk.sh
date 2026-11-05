#!/usr/bin/env bash
# Build a sideloadable LPK without Docker.
#
# The app is published framework-dependently into lazycat/publish and shipped
# as the LPK content directory. The manifest uses the official ASP.NET runtime
# image, so no registry copy or custom image build is needed for personal use.
#
#   ./scripts/build-sideload-lpk.sh
#   ./scripts/build-sideload-lpk.sh --copy-to-netdisk
#
# PlantUML is disabled in this variant (the runtime image has no JRE); use the
# Dockerfile image for the full feature set and App Store submission.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
LZC_VERSION="${LZC_CLI_VERSION:-2.0.9}"
COPY_TO_NETDISK=0

for arg in "$@"; do
  case "${arg}" in
    --copy-to-netdisk) COPY_TO_NETDISK=1 ;;
    *)
      echo "usage: $0 [--copy-to-netdisk]" >&2
      exit 2
      ;;
  esac
done

echo "Publishing linux-x64 framework-dependent build..."
rm -rf "${ROOT}/lazycat/publish"
"${DOTNET}" publish "${ROOT}/server/MiaoYan.Web/MiaoYan.Web.csproj" \
  -c Release -r linux-x64 --self-contained false \
  -o "${ROOT}/lazycat/publish" -p:UseAppHost=false

if command -v lzc-cli >/dev/null 2>&1; then
  LZC=(lzc-cli)
else
  LZC=(npx --yes "@lazycatcloud/lzc-cli@${LZC_VERSION}")
fi

echo "Building LPK..."
cd "${ROOT}/lazycat"
"${LZC[@]}" project build . -f lzc-build.sideload.yml

LPK="$(ls -1t "${ROOT}"/lazycat/*.lpk | head -1)"
echo "LPK: ${LPK}"

if [[ "${COPY_TO_NETDISK}" -eq 1 ]]; then
  if [[ -d /lzcapp/document ]]; then
    cp "${LPK}" /lzcapp/document/
    sync
    echo "Copied to /lzcapp/document/$(basename "${LPK}")"
  else
    echo "/lzcapp/document not found; skipping netdisk copy" >&2
    exit 1
  fi
fi
