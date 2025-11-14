# stack-proxy

A lightweight dev proxy that lets multiple moneydevkit worktrees share the same host by routing HTTP and TCP traffic (e.g., Postgres) through a single HAProxy instance. Each container advertises a friendly hostname via concise `mdk.*` labels, the adapter discovers services through the Docker API, renders HAProxy frontends/backends, and writes the config atomically before triggering a hot reload.

## Project Layout

- `src/StackProxy.Adapter` — F# control plane that:
  - reads configuration from environment variables (`STACK_PROXY_*`),
  - watches Docker for containers attached to the shared `proxy` network,
  - infers hostnames/ports from labels or exposed ports,
  - renders HAProxy config and writes it atomically.
- `tests/StackProxy.Adapter.Tests` — unit tests for metadata inference, rendering, config writing, and reconciliation.
- `PLAN.md` — implementation checklist and future work.

## Next Steps

- Implement the Docker event loop (initial list + streaming updates).
- Package HAProxy + adapter into a container with an entrypoint script.
- Provide a sample `docker-compose.proxy.yml` and docs for service-side labels.
