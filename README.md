# stack-proxy

A lightweight dev proxy that lets multiple worktrees share the same host by routing HTTP and TCP traffic (e.g., Postgres) through a single HAProxy instance. Each container advertises a friendly hostname via concise `stack-proxy.*` labels, the adapter discovers services through the Docker API, renders HAProxy frontends/backends, and writes the config atomically before triggering a hot reload.

## Running the proxy locally

1. Ensure the shared `stack-proxy` network exists: `docker network create stack-proxy` (no-op if it already exists).
2. Start stack-proxy: `docker compose -f docker-compose.stack-proxy.yml up -d`. This pulls the published image and exposes ports 80 (HTTP) and 5432 (TCP) on your host.
3. Any service that should be reachable from the host must attach to the shared network. By default we look for a network named `stack-proxy`; if you reuse an existing network (e.g., `dev-network`), set `STACK_PROXY_NETWORK=dev-network` on the proxy container so the adapter watches the right network. Labels are optional:
   - **Hostnames:** If you skip `stack-proxy.host`, the adapter builds `"${service}.${COMPOSE_PROJECT_NAME}.localhost"` for you using the Compose labels. This works for most HTTP services out of the box.
   - **Ports:** If the container exposes only one internal port, we automatically use it. Add `stack-proxy.localport=<internalPort>` only when the image exposes multiple ports and you want to pick a specific one.
   - **TCP services:** Set `stack-proxy.mode=tcp` (and optionally `stack-proxy.localport`) for Postgres, Electrum, or any non-HTTP protocol. Everything else defaults to HTTP.

Example service snippet (mirrors `example/project1/docker-compose.yml`):

```yaml
services:
  web:
    build: ./web
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

With stack-proxy running, you can hit `http://web.test1.localhost` or `psql -h postgres.test1.localhost -p 5432` without port conflicts. The `.localhost` TLD automatically resolves to `127.0.0.1`, so no `/etc/hosts` edits are required.

### Minimal `docker-compose.stack-proxy.yml`

Use the provided file (or copy the snippet below) when running the proxy alongside other stacks:

```yaml
services:
  stack-proxy:
    image: ghcr.io/nelknet/stack-proxy:latest
    restart: unless-stopped
    ports:
      - "80:80"
      - "5432:5432"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    networks:
      - stack-proxy

networks:
  stack-proxy:
    external: true
```

Bring the proxy up with `docker compose -f docker-compose.stack-proxy.yml up -d`. Make sure the shared network exists first (`docker network create stack-proxy`). Every feature stack that wants routing must join this external network in addition to its own project network.

### Local testing with the sample stacks

The repository ships with two identical sample projects (`example/project1` and `example/project2`) that expose a simple Python HTTP server on port 3000. They are useful for tight feedback when developing the adapter:

1. Create the shared network if needed: `docker network create stack-proxy`.
2. Build and start the proxy locally: `docker compose -f docker-compose.stack-proxy.yml up -d --build stack-proxy`.
3. In `example/project1`, run `COMPOSE_PROJECT_NAME=test1 docker compose up -d web`.
4. In `example/project2`, run `COMPOSE_PROJECT_NAME=test2 docker compose up -d web`.
5. Hit `http://web.test1.localhost` and `http://web.test2.localhost` (or `curl -H 'Host: web.test1.localhost' http://127.0.0.1`) to verify routing.

Bring the stacks down with `docker compose down -v` inside each project when you finish testing.

## Validation & Troubleshooting

1. Start stack-proxy via `docker compose -f docker-compose.proxy.yml up -d --build`.
2. Launch a sample stack (e.g., `lightning-node`) with services attached to the `stack-proxy` network and labeled hosts.
3. (Optional) If you still prefer custom domains, run `sudo scripts/stack-proxy-hosts.sh mdk-123` to map `*.mdk-123.local` to `127.0.0.1`.
4. Validate HTTP: `curl -H 'Host: web.test1.localhost' http://127.0.0.1` should return your app (or use whichever hostname your stack emits).
5. Validate TCP: `psql -h postgres.test1.localhost -p 5432 -U postgres` should connect to your stack’s Postgres (swap in the Compose project and service names you actually use).

If something fails:
- Check adapter logs: `docker logs stack-proxy`.
- Inspect generated config: `docker exec stack-proxy cat /etc/haproxy/generated.cfg`.
- Verify labels: `docker inspect <container> --format '{{ json .Config.Labels }}'`.
- Ensure the service is on the `stack-proxy` network: `docker inspect <container> --format '{{ json .NetworkSettings.Networks }}'`.

### Stress-testing container churn

To verify that stack-proxy keeps up with frequent restarts:
1. Start the proxy as described above.
2. In your feature stack, run `watch -n 1 'docker compose restart web'` (or any service name you care about) to bounce a service repeatedly, or use `docker compose up --scale web=3` followed by random `docker kill` commands.
3. Tail the proxy logs (`docker logs -f stack-proxy`) and ensure each start/stop results in a regenerated HAProxy config without errors.
4. Hit the service hostname between restarts to confirm zero-downtime reloads.

If the adapter falls behind, increase `watch` intervals or check `/etc/haproxy/generated.cfg` for stale entries.
