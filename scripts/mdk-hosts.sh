#!/usr/bin/env bash
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: mdk-hosts.sh <slug>" >&2
  exit 1
fi

SLUG="$1"
shift || true
HOSTS=("moneydevkit.$SLUG.local" "admin.$SLUG.local" "postgres.$SLUG.local")

if [ "$EUID" -ne 0 ]; then
  echo "This script modifies /etc/hosts and must be run as root." >&2
  exit 1
fi

for host in "${HOSTS[@]}"; do
  if ! grep -q "\s$host$" /etc/hosts 2>/dev/null; then
    echo "127.0.0.1 $host" >> /etc/hosts
    echo "Added $host"
  else
    echo "$host already present"
  fi
fi
