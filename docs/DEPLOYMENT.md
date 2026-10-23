# Deployment with Docker

LibManagarr runs as a **single combined image**: the ASP.NET backend (`.NET 10`) serves the API under
`/api/*` and, in parallel, the built Angular frontend from `wwwroot`. One instance therefore needs
exactly **one container and one port**. Persistent state lives in two folders: the **config
folder** (`/config` in the container, driven by the `LIBMANAGARR_CONFIG_DIR` environment
variable) holds `settings.json` with the Arr API keys, the SQLite database `libmanagarr.db`
(sync histories, instance snapshots, library filter values, scheduled task configuration and run state) and the
log files; the **data folder** (`/app/data`, below the content root) holds the poster cache.
Mount volumes on both – the config folder is the one worth backing up.

Logs always live in the `logs/` subfolder of the config directory (`/config/logs` in Docker).
Their path cannot be changed in the UI or through settings requests. Legacy `logging.path`
values are normalized to `./logs`; old files at custom locations are left untouched.

## Getting the image

There are two ways images get pushed to GHCR:

- **Release** (`.github/workflows/release.yml`): a published GitHub release with tag `v1.2.3` builds
  the image with the tags `1.2.3`, `1.2`, `1` and `latest` after all tests pass. Pre-releases get
  `-rc` variants (e.g. `latest-rc`) instead of `latest`.
- **main** (`.github/workflows/ci.yml`): every merge to `main` pushes `edge` and the fixed commit tag
  `sha-<short>`.

| Tag                   | Meaning                                      |
| --------------------- | -------------------------------------------- |
| `1.2.3` / `1.2` / `1` | released version (from the GitHub release)   |
| `latest`              | last released version (recommended)          |
| `edge`                | current state of `main`                      |
| `sha-<short>`         | fixed commit state (e.g. `sha-a1b2c3d`)      |

You create a release on GitHub under **Releases → Draft a new release**: create tag `v1.2.3` (with a
leading `v`), set the title/description and publish the release – the workflow then runs automatically.

Image: `ghcr.io/noir1992/libmanagarr`. **Note:** GHCR packages are **private** by default on the first
push. For `docker compose pull`, either set the package visibility to public in the GitHub settings
(Packages → libmanagarr → Package settings → Visibility: public) – or log in once before pulling:

```bash
echo "<GHCR token with read:packages scope>" | docker login ghcr.io -u <GitHub-user> --password-stdin
```

## Starting with Docker Compose

`compose.yaml` from this repo:

```yaml
services:
  libmanagarr:
    image: ghcr.io/noir1992/libmanagarr:latest # stable; use :edge for the current main state
    ports:
      - '${LIBMANAGARR_PORT:-8080}:${LIBMANAGARR_PORT:-8080}'
    environment:
      - HTTP_PORTS=${LIBMANAGARR_PORT:-8080}
    volumes:
      - ./libmanagarr-config:/config
      - ./libmanagarr-data:/app/data
    restart: unless-stopped
```

```bash
docker compose pull
docker compose up -d
```

The UI is then reachable at `http://localhost:8080`.

**Port:** the default is `8080`. To run everything on a different port, set `LIBMANAGARR_PORT` – it
drives both the host mapping and the port the backend listens on inside the container (`HTTP_PORTS`):

```bash
LIBMANAGARR_PORT=9000 docker compose up -d
```

or persist it in a `.env` file next to `compose.yaml`:

```
LIBMANAGARR_PORT=9000
```

**Optional timezone for logs/planner:** add `- TZ=Europe/Berlin` to the service's `environment` list.

## Data

- **First start:** the volumes are empty; the app starts with an empty configuration. Create the
  Sonarr/Radarr instances in the settings afterwards and trigger the sync.
- **Migrating existing data:** put `settings.json`, `libmanagarr.db` and the log files from the
  previous installation into `./libmanagarr-config` and the `poster-cache/` folder into
  `./libmanagarr-data`, then restart the container.
- **Local runs (no Docker):** when `LIBMANAGARR_CONFIG_DIR` is unset both folders fall back to
  the `data` folder below the content root, so config and cache share one directory.
- The image itself contains **no** data/API keys (`.dockerignore` excludes `backend/LibManagarr.Api/data`).

## Authentication & two-factor

LibManagarr is a single-user tool and protects every API except the auth endpoints with a
cookie session. On the **first start** the UI redirects to a setup screen that creates the one
admin account (username 3–64 characters, password at least 12). Until that account exists the
statistics endpoints stay locked.

- **Session:** cookie `LibManagarr.Auth` (HttpOnly, SameSite=Lax) with an **absolute lifetime of
  30 days** – there is no sliding renewal, so expect a sign-in about once a month.
- **Two-factor (optional):** TOTP authenticator apps plus **10 one-time recovery codes** shown
  once when you enable it. Enable it in **Settings → Account**.
- **Brute-force guards:** the auth endpoints allow 10 requests per minute per IP (HTTP 429), and
  5 failed password or second-factor proofs lock sensitive account operations for 5 minutes.
  Password changes use the same per-IP rate limit and require fresh proof when 2FA is enabled.
- **Clock sync:** TOTP codes are time-based (±90 seconds tolerance). Keep the host clock on NTP,
  otherwise valid codes get rejected.

### HTTP vs. HTTPS and reverse proxies

Out of the box the auth cookies are `Secure` whenever the request itself arrives over HTTPS. If
TLS terminates **in front of** the container, the backend only sees plain HTTP, so force the flag:

```yaml
    environment:
      - Auth__RequireSecureCookies=true
      - ForwardedHeaders__KnownProxies__0=<proxy-ip>
```

`ForwardedHeaders:KnownProxies` is a list of proxy IP addresses; only from those the backend
trusts `X-Forwarded-For`/`X-Forwarded-Proto`. IP rate limits use the forwarded client address
only for those trusted proxies. Otherwise clients behind a proxy share its rate-limit budget;
forwarded headers from untrusted sources are ignored.

### Backups and recovery

The config folder (`/config`) is security-critical now: besides `settings.json` and
`libmanagarr.db` it contains **`auth.db`** (account, sessions, encrypted TOTP secrets and hashed recovery codes) and the
**`data-protection-keys/`** folder. Back all of it up **together** – the keys encrypt the stored
TOTP secret, so `auth.db` without the keys (or vice versa) cannot be restored as a working pair.

- **Authenticator app lost:** sign in with one of the recovery codes, then re-enable 2FA (rotate
  the codes in the same dialog).
- **Recovery codes lost too:** stop the container, delete `auth.db` inside the config folder and
  restart. The setup screen comes back and you can create a fresh account; all old sessions and
  the 2FA configuration are gone, but `settings.json` and the statistics history in
  `libmanagarr.db` are untouched.

## Updating

```bash
docker compose pull
docker compose up -d   # replaces the container, the volume is kept
```

## Building locally (optional)

```bash
docker build -t libmanagarr:local .
```

Requires Docker in the project root; the build needs no Node/.NET installed on the host.

## Scheduled task database migration

Deploy backend and frontend together: the task API contract changed. The application migrates
before starting its scheduler or accepting HTTP requests. `scheduled_tasks` in `libmanagarr.db`
is the authoritative store for schedules, activation, revisions and UTC run timestamps.
`settings.json` no longer contains `scheduledTasks`. Calendar schedules use the host timezone;
invalid local times advance to the first valid minute, and repeated times run once per planned day.
Overdue tasks run once after restart; missed occurrences are not replayed individually.

Before the first import, the application creates a paired backup in
`/config/backups/scheduled-tasks-<id>/`: the original `settings.json` and a consistent SQLite
backup of `libmanagarr.db` (including committed WAL data). These files may contain API keys.
Keep the config directory private; Unix backup directories/files have owner-only permissions,
and Windows backups inherit the config directory ACL. A new install without settings only
backs up the initial database. A damaged settings file stops startup without importing defaults.

The migration marker and imported tasks commit in one database transaction. File cleanup follows
the commit. If cleanup cannot replace `settings.json`, startup logs a warning and the next start
retries cleanup without reimporting old schedules. Existing task rows are never overwritten by
legacy JSON. The old `task_run_state` table remains as an unused fallback artifact; the application
neither updates nor reads it after migration.

To roll back, **stop the application** and restore `settings.json` and `libmanagarr.db` from the
same pre-migration backup before running the old version. With the process stopped, remove stale
`libmanagarr.db-wal`/`libmanagarr.db-shm` sidecars before restoring that database. Restoring only
`task_run_state` cannot restore the old configuration. Authentication remains in `auth.db` and
`data-protection-keys/`; preserve those together as described above.

Task execution uses per-task locks within one application process. Multiple application instances
sharing the same database are not supported; exactly-once execution across crashes is not promised.

See [the task API contract](SCHEDULED-TASKS.md) for client changes and extension points.
