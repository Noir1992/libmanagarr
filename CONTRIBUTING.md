# Contributing to LibManagarr

## Local development

Prerequisites: .NET 10 SDK and Node.js 22 (npm 11).

### Backend

```bash
dotnet run --project backend/LibManagarr.Api
```

The API listens on `http://localhost:5072` (profile `http` in
`Properties/launchSettings.json`, `ASPNETCORE_ENVIRONMENT=Development`).

### Frontend

```bash
cd frontend
npm ci
npm start
```

The dev server runs on `http://localhost:4200` and proxies `/api` to the backend via
`proxy.conf.json`. On the first start the UI redirects to the setup screen that creates the
single admin account.

Without `LIBMANAGARR_CONFIG_DIR` set, config and cache fall back to the `data` folder below the
content root, so a local run needs no extra setup.

### Windows note

Developing the symlink feature on Windows also needs the symlink privilege (elevated terminal or
Developer Mode); without it, file symlinks cannot be created and real-filesystem tests report that
they did not execute their link assertions.

## Tests

```bash
dotnet test backend/LibManagarr.Backend.sln   # backend unit tests
cd frontend && npm run test:ci                # frontend unit tests (Karma, headless Chrome)
```

End-to-end tests live in `tests/e2e`.

## Lint and formatting

```bash
cd frontend
npm run lint
npx prettier --check <changed files>
```

CI runs `npm run lint` and `npx prettier --check .` inside `frontend/`; a formatting finding fails
the build. Format changed files with `npx prettier --write <changed files>` before committing.

## Continuous integration

On every pull request and every merge to `main`, CI runs the backend tests, the frontend tests
(including lint and the Prettier check) and the end-to-end suite. A green merge to `main` additionally
publishes the `edge` image and a `sha-<short>` tag to GHCR; published GitHub releases (tag `v1.2.3`)
build the version tags and `latest`.

## Repository layout

| Path       | Contents                                        |
| ---------- | ----------------------------------------------- |
| `backend/` | ASP.NET Core (.NET 10) API, services and tests  |
| `frontend/`| Angular 22 + Angular Material single-page app   |
| `tests/`   | End-to-end tests                                |


## Localized media titles

`MediaTitles` lives with each snapshot in the existing SQLite/backup storage. The resolver selects
app language, English, original title, then source title. Source titles remain matching input. Library
GET/POST responses sort after projection and include all variants for search; symlink suggestions,
managed links, rejections and create choices share the resolver. Calendar and history retain source
names. Display changes preserve library filters, sorting and expanded table rows.

Radarr v3 supplies language IDs through `/language` and batched movie titles through
`/movie?languageId=…`. English is fetched first as a reference. Equal German/French responses are
ambiguous because Radarr silently falls back to English; they are cached as missing rather than
labelled as verified translations. The visible text remains the same. English relies on the API's
language-parameter contract. Successful translations are reused for seven days, missing/ambiguous
results for one day and errors for five minutes. Existing titles survive empty/error responses. A
single refresh gate limits concurrency; each instance has a 45-second timeout. The worker checks
every 30 seconds, and the frontend independently requests enrichment and observes the saved revision.

Sonarr's stable API has no series/episode translation collection. Explicit series `translations`
from newer versions are read when supplied; episodes/specials otherwise retain source titles. No
additional metadata provider, credentials or upstream language-setting changes are introduced.
See the upstream [Radarr controller](https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/MovieController.cs),
[stable Sonarr series resource](https://github.com/Sonarr/Sonarr/blob/main/src/Sonarr.Api.V3/Series/SeriesResource.cs)
and [newer series translations](https://github.com/Sonarr/Sonarr/blob/develop/src/Sonarr.Api.V3/Series/SeriesTranslationResource.cs).

`MediaTitleTests`, localized endpoint tests and the frontend title tests cover fallbacks, failures,
cache reuse and response ordering. E2E scenario E17 uses the pinned Arr baseline to switch from
English to German after sync and checks library titles, alternative-title search and unchanged
suggestion identities without another sync.
