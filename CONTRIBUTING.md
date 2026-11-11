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
