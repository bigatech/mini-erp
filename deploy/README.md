# Local development topology

Compose starts the API, Worker, MiniErp.Web, and local infrastructure dependencies defined by ERP-10: PostgreSQL, Redis, and an S3-compatible object store. Database and object storage data are kept in named Docker volumes; published service ports bind to `127.0.0.1`.

The API and Worker use separate multi-stage Dockerfiles and run as non-root users. Both wait for PostgreSQL health before startup. A one-shot helper grants RustFS volume access to its non-root runtime user before the object store starts. The API exposes its liveness and readiness endpoints; the Worker has no HTTP endpoint and its container healthcheck verifies that its process is alive. MiniErp.Web is served by Nginx on port 3000 and checks the API liveness endpoint in the browser.

## Prerequisites

- Docker Engine or Docker Desktop with Docker Compose v2.
- The repository root as the working directory for the commands below.

## First start

Copy the local template to an ignored local environment file:

```powershell
Copy-Item deploy/environments/local.env.example deploy/environments/local.env
```

Fill `POSTGRES_PASSWORD`, `OBJECT_STORAGE_ACCESS_KEY` and `OBJECT_STORAGE_SECRET_KEY` in the ignored file with newly generated unique values before starting. The template contains no credentials. Never commit populated environment files or reuse production values locally.

Validate the Compose configuration and start the services:

```powershell
docker compose --env-file deploy/environments/local.env -f deploy/compose/compose.yaml config
docker compose --env-file deploy/environments/local.env -f deploy/compose/compose.yaml up -d
```

## Check status and logs

```powershell
docker compose --env-file deploy/environments/local.env -f deploy/compose/compose.yaml ps
docker compose --env-file deploy/environments/local.env -f deploy/compose/compose.yaml logs -f
```

Wait for each service health status to become `healthy`. Local endpoints:

| Service | Endpoint | Credentials |
|---|---|---|
| PostgreSQL | `localhost:<POSTGRES_PORT>` (default `5432`) | Values in `local.env` |
| Redis | `localhost:6379` | No password; bound to loopback for local development |
| S3-compatible API | `http://localhost:9000` | `OBJECT_STORAGE_ACCESS_KEY` / `OBJECT_STORAGE_SECRET_KEY` |
| Object storage console | `http://localhost:9001` | Same object storage credentials |
| MiniErp.Api | `http://localhost:8080` | `GET /health/live`, `GET /health/ready` |
| MiniErp.Web | `http://localhost:3000` | React application |
| MiniErp.Worker | No published endpoint | Container process-liveness healthcheck |

Ports can be changed in `local.env` if they conflict with other local services. Set `API_PORT` to change the API host port.

## Stop and reset

Stop containers while keeping local data:

```powershell
docker compose --env-file deploy/environments/local.env -f deploy/compose/compose.yaml down
```

To delete the local database, cache, and object storage data as well, remove the named volumes explicitly:

```powershell
docker compose --env-file deploy/environments/local.env -f deploy/compose/compose.yaml down --volumes
```

The volume removal permanently deletes this local development data. It does not affect production or managed environments.
