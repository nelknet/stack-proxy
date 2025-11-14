#!/usr/bin/env bash
set -euo pipefail

CONFIG_PATH="${STACK_PROXY_CONFIG_PATH:-/etc/haproxy/generated.cfg}"
HAPROXY_CFG="${HAPROXY_BASE_CONFIG:-/etc/haproxy/base.cfg}"
ADAPTER_BIN="${STACK_PROXY_ADAPTER_BIN:-/app/StackProxy.Adapter.dll}"

mkdir -p "$(dirname "$CONFIG_PATH")"
touch "$CONFIG_PATH"

haproxy -f "$HAPROXY_CFG" -p /var/run/haproxy.pid &
HAPROXY_PID=$!

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
