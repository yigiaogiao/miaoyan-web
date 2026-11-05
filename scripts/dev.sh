#!/usr/bin/env bash
# Two-process development: Kestrel on :5080 and Vite on :5173.
# Open http://127.0.0.1:5173; Vite proxies /api, /healthz and /media.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"
export MIAOYAN_LIBRARY="${MIAOYAN_LIBRARY:-${ROOT}/dev-library}"
export MIAOYAN_STATE_DIR="${MIAOYAN_STATE_DIR:-${ROOT}/dev-state}"
mkdir -p "${MIAOYAN_LIBRARY}" "${MIAOYAN_STATE_DIR}"

cleanup() {
  kill "${API_PID:-}" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

dotnet run --project "${ROOT}/server/MiaoYan.Web/MiaoYan.Web.csproj" \
  --urls http://127.0.0.1:5080 &
API_PID=$!

npm --prefix "${ROOT}/web" run dev
