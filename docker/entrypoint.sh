#!/usr/bin/env bash
set -euo pipefail

CONFIG_PATH="${STACK_PROXY_CONFIG_PATH:-/etc/haproxy/generated.cfg}"
HAPROXY_CFG="${HAPROXY_BASE_CONFIG:-/etc/haproxy/base.cfg}"
ADAPTER_BIN="${STACK_PROXY_ADAPTER_BIN:-/app/StackProxy.Adapter.dll}"

mkdir -p "$(dirname "$CONFIG_PATH")"
touch "$CONFIG_PATH"

HAPROXY_PID_FILE=${STACK_PROXY_HAPROXY_PID:-/var/run/haproxy.pid}
export STACK_PROXY_HAPROXY_PID="$HAPROXY_PID_FILE"

haproxy -f "$HAPROXY_CFG" -f "$CONFIG_PATH" -p "$HAPROXY_PID_FILE" -db &
HAPROXY_PID=$!
echo "$HAPROXY_PID" > "$HAPROXY_PID_FILE"

cleanup() {
  kill "$ADAPTER_PID" >/dev/null 2>&1 || true
  kill "$HAPROXY_PID" >/dev/null 2>&1 || true
}

trap cleanup TERM INT

DOTNET_ROOT=${DOTNET_ROOT:-/usr/share/dotnet}
export DOTNET_ROOT

dotnet "$ADAPTER_BIN" &
ADAPTER_PID=$!

wait "$ADAPTER_PID"
cleanup
wait "$HAPROXY_PID" || true
