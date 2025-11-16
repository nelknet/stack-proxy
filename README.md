# stack-proxy

A lightweight dev proxy that lets multiple worktrees share the same host by routing HTTP and TCP traffic (e.g., Postgres) through a single HAProxy instance. Each container advertises a friendly hostname via concise `stack-proxy.*` labels, the adapter discovers services through the Docker API, renders HAProxy frontends/backends, and writes the config atomically before triggering a hot reload.

## Running the proxy locally

1. Ensure the shared `stack-proxy` network exists: `docker network create stack-proxy` (no-op if it already exists).
2. Start stack-proxy: `docker compose -f docker-compose.proxy.yml up --build -d`. This builds the image and exposes ports 80 (HTTP) and 5432 (TCP) on your host.
3. Any service that should be reachable from the host must attach to the shared network. By default we look for a network named `stack-proxy`; if you reuse an existing network (e.g., `dev-network`), set `STACK_PROXY_NETWORK=dev-network` on the proxy container so the adapter watches the right network. Labels are optional:
   - **Hostnames:** If you skip `stack-proxy.host`, the adapter builds `"${service}.${COMPOSE_PROJECT_NAME}.localhost"` for you using the Compose labels. This works for most HTTP services out of the box.
   - **Ports:** If the container exposes only one internal port, we automatically use it. Add `stack-proxy.localport=<internalPort>` only when the image exposes multiple ports and you want to pick a specific one.
   - **TCP services:** Set `stack-proxy.mode=tcp` (and optionally `stack-proxy.localport`) for Postgres, Electrum, or any non-HTTP protocol. Everything else defaults to HTTP.

Example service snippet:

```yaml
services:
  moneydevkit.com:
    build: ./moneydevkit.com
    networks: [dev-network, stack-proxy]
    # labels optional unless you want to override defaults
```

For Postgres:

```yaml
  postgres:
    image: postgres:16
    networks: [dev-network, stack-proxy]
    labels:
      stack-proxy.mode: tcp
```

With stack-proxy running, you can hit `http://moneydevkit.mdk-123.localhost` or `psql -h postgres.mdk-123.localhost -p 5432` without port conflicts. The `.localhost` TLD automatically resolves to `127.0.0.1`, so no `/etc/hosts` edits are required.

## Validation & Troubleshooting

1. Start stack-proxy via `docker compose -f docker-compose.proxy.yml up -d --build`.
2. Launch a sample stack (e.g., `lightning-node`) with services attached to the `stack-proxy` network and labeled hosts.
3. (Optional) If you still prefer custom domains, run `sudo scripts/stack-proxy-hosts.sh mdk-123` to map `*.mdk-123.local` to `127.0.0.1`.
4. Validate HTTP: `curl -H 'Host: moneydevkit.mdk-123.localhost' http://127.0.0.1` should return the site.
5. Validate TCP: `psql -h postgres.mdk-123.localhost -p 5432 -U postgres` should connect to the stack Postgres.

If something fails:
- Check adapter logs: `docker logs stack-proxy`.
- Inspect generated config: `docker exec stack-proxy cat /etc/haproxy/generated.cfg`.
- Verify labels: `docker inspect <container> --format '{{ json .Config.Labels }}'`.
- Ensure the service is on the `stack-proxy` network: `docker inspect <container> --format '{{ json .NetworkSettings.Networks }}'`.

### Stress-testing container churn

To verify that stack-proxy keeps up with frequent restarts:
1. Start the proxy as described above.
2. In your feature stack, run `watch -n 1 'docker compose restart moneydevkit.com'` to bounce a service repeatedly, or use `docker compose up --scale moneydevkit.com=3` followed by random `docker kill` commands.
3. Tail the proxy logs (`docker logs -f stack-proxy`) and ensure each start/stop results in a regenerated HAProxy config without errors.
4. Hit the service hostname between restarts to confirm zero-downtime reloads.

If the adapter falls behind, increase `watch` intervals or check `/etc/haproxy/generated.cfg` for stale entries.
