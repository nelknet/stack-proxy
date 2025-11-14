# stack-proxy Implementation Plan

## 1. Requirements & Architecture Validation
- [x] List functional goals (HTTP/TCP routing, hostname conventions, zero host-port conflicts, support for multiple worktrees).
- [x] Capture DX constraints (minimal labels, automatic host inference, shared `proxy` Docker network).
- [x] Confirm tech stack: HAProxy for data plane, F# adapter for control plane, Docker Compose deployment.

## 2. Adapter Specification
- [x] Define metadata schema (required labels/env vars, defaults for host/mode/ports).
- [x] Describe host/slug inference rules and conflict handling.
- [x] Outline interaction with Docker (socket access, event stream, initial sync) and HAProxy reload strategy.

## 3. Repository Scaffolding
- [x] Initialize solution structure (e.g., `src/StackProxy.Adapter`, `docker/`, `templates/`).
- [x] Add basic `README.md` summarizing purpose and usage.
- [x] Configure `.editorconfig`, `.gitignore`, and formatting/linting prefs.

## 4. Adapter Implementation (F#)
- [x] Bootstrap .NET project, add dependencies (`Docker.DotNet`, templating library, logging).
- [x] Implement Docker client wrapper: initial container scan + event subscription with debounce.
- [x] Implement metadata extraction + default resolution.
- [x] Render HAProxy config snippets via template; write atomically to disk.
- [ ] Trigger HAProxy reloads (runtime API or `haproxy -sf`) and log outcomes.
- [x] Add unit/component tests for key logic (slug → host, label parsing, template rendering).

## 5. HAProxy & Container Packaging
- [x] Author base HAProxy config (globals, defaults, include for generated routes, runtime socket).
- [x] Create entrypoint script to launch HAProxy + adapter and manage graceful shutdown.
- [x] Write multi-stage Dockerfile (build adapter, assemble runtime image with HAProxy + config + entrypoint).

## 6. Local Development & DX
- [x] Provide `docker-compose.proxy.yml` that runs stack-proxy with socket + external `proxy` network.
- [x] Document service-side changes (network attachment, optional `stack-proxy.*` labels) with examples.
- [x] Add helper script (optional) to ensure `/etc/hosts` contains `*.${COMPOSE_PROJECT_NAME}.local` entries.

## 7. Validation & Testing
- [x] Create sample compose stack to verify HTTP routing across multiple worktrees.
- [x] Add TCP validation scenario (Postgres or custom TCP echo) to confirm SNI-based routing.
- [x] Stress test rapid container churn and ensure adapter debounce works.
- [x] Document troubleshooting steps (logs, config output path, reload commands).

## 8. Future Enhancements (Backlog)
- [ ] TLS termination & Let’s Encrypt integration.
- [ ] Observability (Prometheus metrics, structured logs).
- [ ] Support for custom middlewares (auth, rate limiting) if needed later.
