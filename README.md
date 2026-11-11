# LibManagarr

LibManagarr is a companion web app for [Sonarr](https://sonarr.tv) and [Radarr](https://radarr.video).
It sits beside your existing *arr installation, reads their libraries through the public APIs and adds
the cross-instance features they don't offer themselves — all in one small container, no changes to
Sonarr or Radarr required.

Not affiliated with the Sonarr or Radarr projects.

## Features

- **Unified library** — one view across all configured Sonarr and Radarr instances, with filters,
  change history and a snapshot archive.
- **Statistics** — library trend charts and per-file codec statistics for movies and episodes.
- **Calendar** — upcoming episode and movie releases (week / month / agenda views) with release-type
  filters and missing/partial status against your libraries.
- **Requests** — view, approve, decline and edit requests through a Seerr integration.
- **Managed media symlinks** — offer Radarr movies as specials of Sonarr series using relative
  symbolic links. Automatic matching, health checks, upgrades when Radarr replaces a file, and
  crash-safe create/edit/delete with full audit trail.
- **Backup & restore** — encrypted (AES-256-GCM) export/import of settings and database as a single
  `.lmbak` archive, with preview, rollback journal and automatic host restart.
- **English, German and French UI** — dark and light themes.

## Installation

LibManagarr ships as a single Docker image: the ASP.NET backend serves the API under `/api` and the
Angular frontend from the same port.

```yaml
services:
  libmanagarr:
    image: ghcr.io/noir1992/libmanagarr:latest
    ports:
      - "8080:8080"
    environment:
      - HTTP_PORTS=8080
    volumes:
      - ./libmanagarr-config:/config   # settings.json, database, logs
      - ./media:/media                 # Media library - for symlink functionality (optional)
      - ./libmanagarr-data:/app/data   # poster cache (optional)
    restart: unless-stopped
```

The UI is then available at `http://localhost:8080`. During the first start an admin account has to be created.

## License

LibManagarr is released under the [Apache License 2.0](LICENSE).
