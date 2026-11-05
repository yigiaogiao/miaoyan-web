#!/bin/sh
# Standalone containers use /data; LazyCat mounts per-user documents itself.
# When started as root (plain `docker run`), prepare /data and drop to 1000.
set -eu

if [ ! -d /lzcapp/pkg ]; then
  : "${MIAOYAN_LIBRARY:=/data/Miaoyan}"
  : "${MIAOYAN_STATE_DIR:=/data/state}"
  export MIAOYAN_LIBRARY MIAOYAN_STATE_DIR
fi

if [ "$(id -u)" = "0" ]; then
  mkdir -p /data
  chown 1000:1000 /data
  exec gosu 1000:1000 "$@"
fi

if [ ! -d /lzcapp/pkg ]; then
  mkdir -p "$MIAOYAN_LIBRARY" "$MIAOYAN_STATE_DIR"
fi

exec "$@"
