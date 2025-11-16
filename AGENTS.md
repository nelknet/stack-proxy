# Repository Guidelines

## Project Structure & Module Organization
The adapter lives in `src/StackProxy.Adapter`, an F# console app that tails Docker events, reconciles metadata, and rewrites HAProxy config. Supporting Docker compose examples and entrypoints live under `docker/` plus `docker-compose.proxy.yml`. Reusable scripts (e.g., host overrides) are in `scripts/`, while integration and unit coverage sit in `tests/StackProxy.Adapter.Tests`. Keep generated artifacts inside `bin/` and `obj/` folders that are already ignored.

## Build, Test, and Development Commands
Use the pinned .NET SDK from `global.json` (8.0.413). Typical workflow:

```
dotnet tool restore              # install local tools (if any get added)
dotnet build stack-proxy.sln     # compile adapter + tests
dotnet test tests/StackProxy.Adapter.Tests/StackProxy.Adapter.Tests.fsproj
```

Run the adapter locally with `dotnet run --project src/StackProxy.Adapter/StackProxy.Adapter.fsproj` or build/run the container via `docker compose -f docker-compose.proxy.yml up --build -d`. Tear down containers before switching branches to prevent stale HAProxy state.

## Coding Style & Naming Conventions
Follow idiomatic F#: modules and types in `PascalCase`, values/functions `camelCase`, and 2-space indentation as seen in `Program.fs`. Prefer expression-first code, pure helpers, and pattern matching over mutable state. Keep `.fs` compile order aligned with `StackProxy.Adapter.fsproj`; place new modules near related files (e.g., routing helpers next to `Routing.fs`). If formatting drifts, run `dotnet format` scoped to touched files.

## Testing Guidelines
All tests use xUnit; decorate with `[<Fact>]` and descriptive ``backticked`` names mirroring behavior. Unit tests should focus on metadata parsing, routing rendering, and Docker reconciliation paths. Run `dotnet test` before pushing and add regression tests whenever changing label parsing, config writing, or watcher behavior. Prefer deterministic data builders (see `Tests.fs` helpers) over ad-hoc literals.

## Commit & Pull Request Guidelines
Commit history follows `type: summary` (e.g., `docs: document network env`, `ci: build multi-arch image`). Use imperative mood, keep to <72 characters, and squash fixups locally. PRs should describe the scenario, list verification steps (`dotnet test`, `docker compose ... up --build -d`), and link issue IDs. Include screenshots or log excerpts when touching HAProxy rendering so reviewers can see the new config diff.

## Security & Configuration Tips
Never bake secrets into compose files. Prefer `STACK_PROXY_*` env vars (`STACK_PROXY_DOCKER_URI`, `STACK_PROXY_CONFIG_PATH`, `STACK_PROXY_NETWORK`, etc.) or `.env` entries consumed at runtime. When exposing new services, ensure they attach to the shared `stack-proxy` Docker network and declare the right labels; reference `scripts/stack-proxy-hosts.sh` only for legacy `/etc/hosts` flows. Keep HAProxy config path writable (default `/etc/haproxy/generated.cfg`) and avoid running multiple adapters against the same file.
