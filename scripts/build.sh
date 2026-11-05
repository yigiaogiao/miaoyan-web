#!/usr/bin/env bash
# Build the SPA and publish the API into artifacts/publish.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"

if [[ -f "${ROOT}/web/package-lock.json" ]]; then
  npm --prefix "${ROOT}/web" ci --no-audit --no-fund
else
  npm --prefix "${ROOT}/web" install --no-audit --no-fund
fi

npm --prefix "${ROOT}/web" run build

dotnet publish "${ROOT}/server/MiaoYan.Web/MiaoYan.Web.csproj" \
  -c Release \
  -o "${ROOT}/artifacts/publish" \
  /p:UseAppHost=false

echo "Published to ${ROOT}/artifacts/publish"
echo "Run: ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet ${ROOT}/artifacts/publish/MiaoYan.Web.dll"
