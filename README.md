# LibManagarr

---

[![Release](https://img.shields.io/github/v/release/Noir1992/libmanagarr?label=release&color=blue)](https://github.com/Noir1992/libmanagarr/releases)
[![Apache License 2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
![UI languages](https://img.shields.io/badge/i18n-en%20%C2%B7%20de%20%C2%B7%20fr-informational)
![Angular](https://img.shields.io/badge/Angular-22-DD0031?logo=angular&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)

LibManagarr is a companion web app for [Sonarr](https://sonarr.tv) and [Radarr](https://radarr.video).
It sits beside your existing *arr installation, reads their libraries through the public APIs and adds
the cross-instance features they don't offer themselves — all in one small container, no changes to
Sonarr or Radarr required.

Not affiliated with the Sonarr or Radarr projects.

>## AI disclosure
>
>This project is AI-assisted but not vibe-coded. AI is used as a tool for discussing specific problems,
>analysis, suggestions, and spell checking, never as a substitute for the logic, regression testing, code
>coverage, and static checks that gate every change. AI output is never trusted blindly.

---

## Features

- ***Unified library*** — one view across all configured Sonarr and Radarr instances, with filters,
  change history and a snapshot archive.
- ***Statistics*** — library trend charts and per-file codec statistics for movies and episodes.
- ***Calendar*** — upcoming episode and movie releases (week / month / agenda views) with release-type
  filters and missing/partial status against your libraries.
- ***Requests*** — view, approve, decline and edit requests through a Seerr integration.
- ***Managed media symlinks*** — offer Radarr movies as specials of Sonarr series using relative
  symbolic links. Automatic matching, health checks, upgrades when Radarr replaces a file, and
  crash-safe create/edit/delete with full audit trail.
- ***Backup & restore*** — encrypted (AES-256-GCM) export/import of settings and database as a single
  `.lmbak` archive, with preview, rollback journal and automatic host restart.
- ***Multiple UI languages*** — English, German and French. Library and symlink titles follow the
  selected language when translations are available, with cached fallback titles and background
  loading. Radarr movie titles are supported; Sonarr titles depend on explicitly supplied translations
  (the stable episode API provides none). Matching decisions and media paths stay independent of the
  display language.

---

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

---

## Documentation

| Resource                           | Description                                              |
| ---------------------------------- | -------------------------------------------------------- |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Local development, tests, lint, CI and repository layout |

---

## License

LibManagarr is released under the [Apache License 2.0](LICENSE).

### Symlink live updates and reverse proxies

Authenticated browser sessions open one Server-Sent Events connection at
`/api/settings/symlinks/events`. Allow streaming responses through your proxy and
keep its idle timeout above the 20-second keepalive interval. The endpoint sends
`Cache-Control: no-cache, no-transform` and `X-Accel-Buffering: no`; disable any
additional response buffering or caching on this route. For nginx, for example:

```nginx
location = /api/settings/symlinks/events {
    proxy_pass http://libmanagarr:8080;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_buffering off;
    proxy_cache off;
    proxy_read_timeout 60s;
}
```

Preserve your deployment's existing forwarded headers and authentication cookies.
HTTP/2 on the browser-facing connection is recommended when many tabs are open.
Reconnects always reconcile current state; events are not archived or replayed.
Debug logs under `SymlinkEvents` record connections; browser debug logs record
reconnects and refreshed areas, without logging keepalives.

The menu reads `/api/settings/symlinks/count`, which counts stored pending rows
without contacting Arr or probing files. Existing-file checks used for display are
cached in memory, refreshed asynchronously after relevant writes and every five
minutes to detect external filesystem changes. Preview and confirmation still
validate the current filesystem and source versions immediately before acting.
