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

## Managed media symlinks

LibManagarr adds Radarr movies as specials to Sonarr series using relative symbolic links.
Configure **Settings → Symlinks** (`/settings?tab=symlinks`) with one **media directory**,
which defaults to `/media` in Docker. No manual storage areas, path mappings or reader views are needed.
The link register, suggestions and decline archive live in `libmanagarr.db`; the feature page is `/symlinks`.

The **Enable symlinks** switch pauses matching, health checks and symlink actions without removing
existing links or stored records. Re-enabling resumes pending setup work. Settings save automatically.
The directory browser beside the media directory field lists directories visible to the LibManagarr
server/container; files and linked directories are excluded. The directory can also be entered manually.

### Required shared media tree

Mount the **same complete media tree at the same container path** in Sonarr, Radarr, LibManagarr
and your media player (for example Jellyfin). For example, every service gets the same bind mount:

```yaml
volumes:
  - /mnt/pool/media:/media
```

The resulting layout could be:

```text
/media/
  movies/Film/film.mkv
  series/Show/Specials/Show - S00E01.mkv
```

- Movie and series folders are discovered automatically using each instance's `/api/v3/rootfolder` API.
  Folder names are unrestricted; multiple roots and multiple instances are supported, provided their
  roots stay inside the configured media directory. A different mount prefix is reported as an error.
- Only the series folders need write permission. LibManagarr reads movie files and creates, renames
  and removes links inside the series folders. It does not rename or change the source movie.
- The example special points to `../../../movies/Film/film.mkv`. Link targets are relative and the
  complete tree must retain the same structure in every reader. Mounting only the series folder in
  Sonarr or Jellyfin leaves the movie target inaccessible.
- The backend validates both the common media-directory boundary and the roots reported by the
  corresponding instance. Paths outside those boundaries and paths through symlinked parent
  directories are refused.
- Existing manual configurations remain readable for compatibility. Saving the media directory
  switches them to the shared-mount setup. Existing link paths are retained; changing the root is
  refused if it would leave managed links outside the media tree.

### Automatic capability checks

The media directory saves automatically when the field loses focus or you press Enter. It must already
exist, be accessible and have no linked parent directories; invalid entries leave the saved path unchanged.
The feedback icon beside the field shows saving, success or an error.

LibManagarr automatically checks symlink support when the symlink settings open, after a media-directory
change, before enabling, after a backend restart and before planning new links in the shared media tree.
The check discovers the current root folders, verifies their boundaries and readability, and tests creating,
reading, renaming and replacing relative links in the series roots. Temporary files are removed afterwards;
no writes to the movie folders or top level of the media directory are needed.

Fresh installations start disabled. Enabling requires a successful capability check; otherwise the switch remains
disabled with the reason shown beside it. Automatic checks preserve an existing activation across container restarts.
If verification fails temporarily, processing pauses and verification retries automatically. Processing resumes when
support is confirmed, without requiring the user to enable the feature again. Existing links and records remain intact.
A positive check proves this backend's access; matching mounts in Sonarr and the media player remain required.

| Answer | Meaning |
| --- | --- |
| `Available` | Movie roots are readable and symlinks work in the discovered series roots. |
| `StorageUnavailable` | Root discovery failed, a root is outside the shared tree, or a folder is unavailable. |
| `PermissionDenied` | The process lacks permission to read the folders or create symlinks. |
| `Unsupported` | The runtime does not support file symlinks. |
| `Failed` | A step of the link test failed; see the reported detail. |
| `NotTested` | Waiting for automatic verification of this configuration. |

Windows development runs also need the symlink privilege (elevated or Developer Mode). On Windows,
the initial media-directory default is the system drive's `media` folder; Docker defaults to `/media`.

### Existing episode files in offers

When Sonarr already assigns a file to an offered special, loading the offers checks the current
Sonarr episode/file assignment and the proposed Radarr movie file. The existing entry is inspected
without changing it: relative and absolute symlinks, including link chains, are resolved and compared
with the expected movie. A readable matching link is shown as verified. A different target, a dangling
or cyclic link, a regular file, a missing entry or an unavailable check is reported separately.
Details include the affected paths. Targets outside the configured media directories are never opened.

These checks do not adopt existing links, change the suggestion decision, overwrite files or perform
any Arr writes. Filesystem results are checked again on the next load even if the library snapshots
are unchanged. Arr reads are shared within the request, with at most four inspections in parallel and
a ten-second limit per inspection for the API reads.

A matching existing symlink can be **explicitly adopted** from the confirmation dialog. The preview
shows the actual existing path, its unchanged link text and the current source metadata. Confirmation
is bound to the source version, Sonarr file id, path and link text shown in that preview, and verifies
them again before registration. A changed, broken or mismatched link and a regular file remain blocked.
Adoption records the current Sonarr assignment, metadata, naming revision and monitoring state, and
starts with setup complete. It does not recreate, move, rename or rescan the link, alter monitoring or
write to Sonarr. Subsequent management follows the consent shown in the dialog. Future filesystem
changes still require the normal capability test; registering a readable existing link does not need
permission to create a new one. Existing absolute link targets and valid link chains are retained.

### The health pass waits until the tree is ready

The task `symlinkHealthCheck` (**Settings → Tasks**, `/settings?tab=tasks`) walks every managed link with
limited parallelism. It is seeded **disabled** with a daily 03:00 suggestion, and schedule, activation and
a manual run all use the existing task editor. It reads current Arr data for the referenced movies and
series, batching series reads within the pass; a failed API read never reuses an old healthy snapshot.
It reports per row: a link that is gone, a regular file
where a link belongs, a link that points somewhere else, a missing source, a read the rights refused, a
share that is not mounted, a movie whose Radarr file moved, a missing Sonarr mapping, file metadata that
was never confirmed or deviates, a name the instance rewrote, an episode that is monitored again. Where a
look cannot answer, it says that with the cause instead of guessing. Switching the task off stops the
periodic pass only: **following Radarr upgrades of an existing link belongs to the feature** and keeps
running, and a single row can be looked at by hand from **`/symlinks`** through the same logic.

### Creating, editing and recovering links

**Create manually** selects a movie file and an existing Sonarr special from the synchronized libraries.
Search by title or instance, then request a live preview before saving. The preview shows source,
provisional link path, link text and expected file metadata. Confirmation authorizes subsequent Radarr
file upgrades for this pair; following upgrades can be disabled per link. Editing uses the same preview
and preserves an interrupted change in the register. **Retry setup** resumes the recorded operation;
it never authorizes overwriting a foreign entry. Filters cover title, status and instance, and the
navigation shows the number of pending offers.

The source signature includes its path, file identity, size, date and available metadata. A replacement
uses a distinct provisional filename so Sonarr imports and analyzes it even when the source file ID,
path or size stayed the same. Quality and languages are mapped by name to the target Sonarr instance;
unsupported mappings and unconfirmed MediaInfo keep setup incomplete. Radarr custom-format IDs and
scores are not copied into Sonarr.

The final path comes from Sonarr's rename preview (`episodeFileId`, `existingPath`, `newPath`).
LibManagarr creates the relative link at that path, verifies it and removes its previous own link,
then rescans and verifies the association and metadata again. It does not use Sonarr's file mover for
new renames, which avoids source timestamp/permission changes and stale relative link text on folder
changes. Naming moves and source swaps have persistent checkpoints and can be resumed after a crash.

After Sonarr has finished setting up a new link and confirmed its final name, LibManagarr refreshes
only the affected series through the targeted snapshot sync. Library details, change history, archive
and filter counts are updated before setup is marked complete. A failed refresh keeps setup open for
automatic retry. Explicit adoption of an existing symlink also refreshes that series before registering
the completed link. Import and subsequent rename events account for the file size only once.

Deleting removes the owned symlink and retains its managed entry until the operation is fully complete.
The row controls stay disabled and the delete button shows loading feedback throughout. A durable
`CleanupPending` journal remains while Sonarr's matching episode-file record is removed
through its targeted API; no series or library rescan is needed for deletion. The file ID and mapped
path are verified before this API call. Before the cleanup journal is removed, LibManagarr refreshes the affected series from Sonarr through
its snapshot sync pipeline: the persisted library, snapshot archive, change history and filter counts
are updated together with the normal sync processing. Suggestions are then recalculated automatically.
The targeted sync reads that series' episodes, files and history, plus the small profile/tag lists; it
neither scans Sonarr's disk nor fetches unrelated series. Without an existing library baseline, the first
sync initializes the instance normally. A partial sync keeps the full-sync history watermark and tracks
its own series history window, so later full syncs neither skip other series nor duplicate these events.
A failed targeted sync leaves the cleanup journal for automatic retry. A broken own link
can be removed, but a regular file or changed link at the registered path is a conflict. Storage areas
and mappings used by registered links cannot be removed first. Live health checks queue unambiguous
monitoring, naming and metadata repairs; inaccessible infrastructure makes the job fail visibly.

All existing linked parent directories, including junctions, are refused. Use actual mount points for
media roots, rather than symlinked directory aliases. LibManagarr serializes its own filesystem and
Sonarr operations and rechecks entries before changes. This is not an OS transaction with other tools:
do not let unrelated processes replace directory entries concurrently during an operation. Mount film
data read-only in LibManagarr and Sonarr wherever the deployment permits it. TrueNAS dataset ACLs,
container path views and playback in the intended media server still require on-site acceptance.

### After a restore

An archive carries `libmanagarr.db`, so the register, the suggested pairs and the decline archive come
with it; the links themselves live on the shares and are never part of an archive. Mount the datasets the
same way on the target host, then run a check pass. Rows whose tree or source is missing report it and
stay as they are: a missing source never deletes a film or a stored pair, and removing a managed link
deletes the link entry only, never the film. Deletion does not reject the pair; it can be suggested again
automatically after the targeted Sonarr cleanup. The page retains the locked managed row until the
refreshed lists are available, then removes it and displays the suggestion in the same update. Failed
reads retain the pending row and are retried automatically. See **Backup export and import** below.

## Authentication & two-factor

LibManagarr is a single-user tool and protects every API except the auth endpoints with a
cookie session. On the **first start** the UI redirects to a setup screen that creates the one
admin account (username 3–64 characters, password at least 12). Until that account exists the
statistics endpoints stay locked.

- **Session:** cookie `LibManagarr.Auth` (HttpOnly, SameSite=Lax) with an **absolute lifetime of
  30 days** – there is no sliding renewal, so expect a sign-in about once a month.
- **Two-factor (optional):** TOTP authenticator apps plus **10 one-time recovery codes** shown
  once when you enable it. Enable it in **Settings → Account**.
- **Brute-force guards:** two limits, both editable in **Settings → General → Sign-in protection** and
  stored in `settings.json` (`authProtection.requestsPerMinute`, `authProtection.maxFailedAttempts`).
  The request budget allows 10 protected requests per minute per IP by default (1–1000) and answers
  HTTP 429; it is shared by every protected route – sign-in, initial setup, second-factor verification,
  password change, the two-factor setup/confirm/disable and recovery-code routes, and the backup export,
  import validation and apply routes. The failure budget allows 5 wrong passwords or second-factor
  proofs per account by default (1–100), counted across all of those identity checks, and then locks the
  account for 5 minutes. A stored `settings.json` without that section keeps the defaults 10 and 5.
- **Limits apply immediately:** saving the card takes effect without a restart, because the budget is
  read per request. Saving never resets the failed-attempt counter and never lifts a running lockout.
  The lockout duration itself is not configurable and stays 5 minutes.
- **The archive password is a separate credential:** a wrong password for an imported archive is
  reported as an archive error, is not counted as an account failure and cannot lock the account. The
  account password a backup form asks for is the identity proof and is counted like every other proof.
- **Counters and lockouts live in `auth.db`:** they are excluded from backup archives, so an import never
  unlocks an account, resets a counter, or lifts a lockout on the target installation.
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

## Backup export and import

**Settings → Backup** (`/settings?tab=backup`) downloads settings and application data as one archive
and restores one later – the supported way to move an installation to another host. Everything else
about the config folder above still holds: logs, the poster cache and temporary files are never part
of an archive.

**Inside the archive** (`libmanagarr-backup-<timestamp>.lmbak`): `settings.json`, `libmanagarr.db`,
and a manifest with the application version, library schema version and a checksum per component.
`auth.db` and `data-protection-keys/` are excluded: accounts, passwords, 2FA, recovery codes and sessions
stay on the target installation. Set up the desired account before importing on a new installation.
The archive is AES-256-GCM encrypted with a key derived from the backup password
(PBKDF2-SHA512, 600 000 iterations) and capped at 512 MB.

The two **sign-in protection limits travel with `settings.json`**, so an archive carries them and the
imported values apply once the restore finished. A `settings.json` written by an older build has no
`authProtection` section at all; that is treated as "nothing configured" and keeps the defaults 10 and 5
rather than failing the import. Failed-attempt counters and running lockouts are in `auth.db` and therefore
never part of an archive: restoring neither unlocks an account nor resets its counter.

**Export** requires a fresh identity proof – account password, plus a 2FA or recovery code when 2FA
is on – and a backup password of at least 12 characters, typed twice. That password is not stored
anywhere, not even hashed: an archive whose password is lost cannot be opened again.

**Import** takes the archive and its password and shows a preview first: application version,
creation date, library schema version, the number of Sonarr/Radarr instances, and the packed versus
unpacked size. Credentials and API keys are
never displayed. A checked archive stays available for 15 minutes and only for the session that
uploaded it; after that – or on **Discard** – it is dropped and has to be uploaded again.

**Restore and restart** asks for the identity proof a second time, then writes
`restore-journal.json` into the config folder and stops the backend host. The automatic host restart does the actual
switch, before migrations, the settings file, the data-protection provider and the scheduler are
touched: settings and the library database move out of the way into `<config>/backups/rollback-<operation-id>/`,
the imported ones move in, and a final integrity check decides. On failure every file moves back and
the previous state starts again; a crash between two moves is completed or undone by the journal, so
a mix of the two states never comes up. The rollback copy stays on disk afterwards.

**Automatic restart:** the application stops and disposes its complete backend host, closes pooled
SQLite connections, and builds a fresh host in the same process before applying the restore.
This works under Rider, with plain `dotnet LibManagarr.Api.dll`, and in Docker Compose. No child
process or Docker API access is needed. An ordinary shutdown still exits normally; only a confirmed
restore requests this host restart. Compose's `restart: unless-stopped` remains useful for process
failures but is not required for this restore flow.

The UI displays restart progress and polls the authenticated status route once per second. It matches
the restore operation ID before treating a restore or rollback as complete, then reloads the frontend
to discard cached configuration and show the result. Temporary connection failures during restart
are expected. After two minutes without completion, it offers a connection retry and a backend log
hint; retrying never submits the restore again.

After a successful restore, **the target installation keeps its account, password, 2FA configuration,
recovery-code usage and data-protection keys**. The archive does not change the application version.
The account-free format is version 2; older version 1 archives containing account data are refused.
Create a fresh backup before moving an installation. A pending restore journal containing auth or key
components is also refused at startup and must be resolved before starting the application.
An archive whose schema is unsupported by the installed build is refused, and an application version
difference in the preview is shown as a warning.

The calendar cache is not part of an archive either: after a restore the calendar loads releases and
file state from the restored instance configuration on the next opening, so it reflects the sources
rather than the backup point in time.

**Rolling back by hand** is only needed when the automatic rollback could not finish and the
application refuses to start (it logs the rollback directory in that case): stop the application,
move the files back from `<config>/backups/rollback-<operation-id>/` into `<config>/`, remove
`restore-journal.json` and the stale `libmanagarr.db-wal`/`libmanagarr.db-shm` sidecars, then start
again. UI backup rollback only replaces settings and the library database; leave `auth.db` and
`data-protection-keys/` on the target installation untouched.

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
