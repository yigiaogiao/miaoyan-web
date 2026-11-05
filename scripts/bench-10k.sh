#!/usr/bin/env bash
# Generates a throwaway library and measures the API targets from PLAN.md.
# Usage: scripts/bench-10k.sh [note-count]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COUNT="${1:-10000}"
PORT="${BENCH_PORT:-5199}"
DOTNET="${DOTNET:-dotnet}"
WORK="${BENCH_DIR:-$(mktemp -d -t miaoyan-bench-XXXXXX)}"
LIBRARY="$WORK/library"
STATE="$WORK/state"
BASE="http://127.0.0.1:$PORT"
SERVER_PID=""

cleanup() {
  if [[ -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  if [[ -z "${BENCH_DIR:-}" ]]; then
    rm -rf "$WORK"
  fi
}
trap cleanup EXIT

mkdir -p "$LIBRARY" "$STATE"
echo "Generating $COUNT notes in $LIBRARY ..."
node - "$LIBRARY" "$COUNT" <<'NODE'
const fs = require('fs');
const path = require('path');
const [root, count] = [process.argv[2], Number(process.argv[3])];
for (let index = 0; index < count; index += 1) {
  const id = String(index).padStart(5, '0');
  const title = `笔记 ${id}`;
  const body = `# ${title}\n\n第 ${index} 条笔记，用于 10k 性能验证。量子纠缠 ${index % 7}\n\n` +
    `- 列表\n- 搜索\n\n[[笔记 ${String((index + 1) % count).padStart(5, '0')}]]\n`;
  const dir = index % 20 === 0 ? path.join(root, '收件箱') : root;
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, `${title}.md`), body);
}
NODE

echo "Starting server on $PORT ..."
MIAOYAN_LIBRARY="$LIBRARY" MIAOYAN_STATE_DIR="$STATE" \
  "$DOTNET" run --project "$ROOT/server/MiaoYan.Web" --no-build --urls "$BASE" \
  >"$WORK/server.log" 2>&1 &
SERVER_PID=$!

for _ in $(seq 1 120); do
  if curl -sf "$BASE/healthz" >/dev/null; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "Server exited; see $WORK/server.log" >&2
    tail -30 "$WORK/server.log" >&2
    exit 1
  fi
  sleep 0.5
done
curl -sf "$BASE/healthz" >/dev/null || { echo "Server did not become healthy" >&2; exit 1; }

time_it() {
  local label="$1"
  shift
  local result
  result="$(curl -s -o /dev/null -w '%{time_total}' "$@")"
  printf '%-34s %8.1f ms\n' "$label" "$(awk -v t="$result" 'BEGIN { printf "%.1f", t * 1000 }')"
}

echo
echo "Measured (warm unless marked):"
time_it "first summary list (cold)" "$BASE/api/notes?summary=1"
ID="$(curl -s "$BASE/api/notes?summary=1" | node -e 'let d="";process.stdin.on("data",c=>d+=c).on("end",()=>console.log(JSON.parse(d)[0].id))')"
time_it "warm summary list" "$BASE/api/notes?summary=1"
time_it "full-body list (summary omitted)" "$BASE/api/notes"
time_it "search 2 chars (cold)" --get --data-urlencode 'q=笔记' "$BASE/api/search"
time_it "search 2 chars (warm)" --get --data-urlencode 'q=笔记' "$BASE/api/search"
time_it "search 4 chars" --get --data-urlencode 'q=量子纠缠' "$BASE/api/search"
time_it "note fetch" "$BASE/api/notes/$ID"
echo
echo "Targets: list < 2000 ms, search < 150 ms, note fetch < 100 ms."
echo "Work directory: $WORK"
