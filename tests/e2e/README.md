# Browser E2E

The suite tests the combined LibManagarr image with Chromium against real Sonarr,
Radarr, Seerr and Jellyfin containers. Productive configurations, host media
directories, indexers and downloaders are never used.

## Running locally (Windows and Linux)

Requirements: Node.js 22 or newer, npm, Docker with Linux containers and Compose
v2 or newer. Docker Desktop has to be running. Run these commands inside
`tests/e2e`:

```text
npm ci
npm run lint
npm run format:check
npm test
npm run typecheck
npm run e2e
npm run e2e -- --suite full
npm run e2e -- --scenario E12
```

The E2E commands build the current app image and the runner. The browser and the
system libraries are part of the runner; a local browser installation is not
required. `E2E_APP_IMAGE` passes an already built local verification image
(PowerShell: `$env:E2E_APP_IMAGE='libmanagarr:e2e'`, Linux:
`E2E_APP_IMAGE=libmanagarr:e2e npm run e2e`). That image is neither rebuilt nor
deleted during cleanup. `E2E_RUNNER_IMAGE` similarly reuses an explicitly supplied
runner image; the caller is responsible for building it from the current test sources.

ESLint checks the TypeScript specs and fixtures as well as the JavaScript helpers
and configurations. The specs additionally use the recommended Playwright rules;
the typed Promise rules detect forgotten `await`s among other things. Warnings
fail the lint run. `npm run lint:fix` fixes auto-fixable lint problems,
`npm run format` formats the test files. Generated artifacts, dependencies and the
checksum-protected seeds are excluded. The same lint and format checks run in CI
and on release. Git keeps the test sources on LF line endings; generated seeds are
stored without line-ending conversion so their checksums stay valid after a
Windows checkout. The `minimatch` override keeps the transitive Glob dependency of
the lint tooling on a patched version.

Each invocation starts one isolated Docker project and restores its service baseline
once. The runner, network and volumes stay alive until the invocation finishes.
External services start lazily: authentication cases need only LibManagarr; Arr
library cases need Sonarr/Radarr; request cases need Seerr and its dependencies.
Read-only cases reuse those external processes.

Before every spec, LibManagarr is stopped, its configuration, database, identity
and caches are cleared, and it is started again. This preserves a fresh account,
fresh settings, session isolation and clean rate limits without restarting the
external infrastructure. There is one browser worker and a new context per test.
The service baseline is verified through real APIs before every applicable case.
Only the first case checks external-network isolation, since the network itself
is reused throughout the invocation.

The catalog explicitly declares `services` and `mutates`. After a writing case,
only the changed external state is restored: Sonarr after deleting its series,
Seerr after changing requests, and Sonarr plus media after confirming a symlink.
The owning process is stopped before restoring SQLite and its WAL sidecars.
Both Arr services stop before restoring the shared media tree. Other services
keep running. The next test does not depend on the previous one's changes or on
the catalog order. Adding a writing case requires declaring its mutated resources.
The LibManagarr sync schedule is disabled by default. Preparation may use regular
APIs; the user flow under test always happens in the browser.

`npm run e2e` runs all 20 scenarios by default, exactly like the CI.
`--scenario E12` or `--scenario backup-restore` selects exactly one case locally,
with the same fresh application state and verified service baseline. Unknown options, an empty selection, duplicate catalog
entries and missing specs fail before anything is built. The binding catalog lives
in `scripts/scenarios.mjs`.

CI runs the complete catalog in one job, using a separately cached browser-runner
image. The tested candidate image is exported only after every scenario succeeds.
Publishing on CI or release remains dependent on the entire E2E job succeeding.

| IDs     | Coverage                                                                                          | Suite |
| ------- | ------------------------------------------------------------------------------------------------- | ----- |
| S01–S04 | Setup/auth/icons, Arr sync/details, Seerr decline, Sonarr outage                                  | core  |
| E01–E04 | Wrong password, password change and session revocation, TOTP setup/login, one-time recovery codes | core  |
| E05–E07 | Filters/search/sorting/views, instance edit/delete, empty library                                 | core  |
| E08–E10 | Seerr delete/cancel, Seerr outage, task editor/manual sync                                        | full  |
| E11–E12 | Encrypted backup download/verify/discard, restore with a host restart                             | core  |
| E13–E14 | Month/agenda calendar with a fixed date, codec counts/percentages/filters                         | full  |
| E15–E16 | Confirm a symlink offer with a real Linux symlink in the volume, a declined pair stays declined   | full  |

`full` contains every `core` case and is binding on PR, main and release. The 13
cases of `--suite core` are purely an optional local selection; the CI offers no
shortened suite. Every writing case has its own spec and declares the upstream state it modifies. Two
account sessions are independent browser contexts. TOTP uses the exactly pinned
OTPAuth library and real 30-second time steps; only the calendar fixes the
browser's display time. Deleting an Arr instance has no confirmation dialog in the
current product. When Seerr fails to load, the retry reloads the page.

The entire suite uses an internal Docker network without an external route.
`bootstrap/verify.mjs` checks the service states, the file assignments, one open
request and the failing access to an external IP. Unknown TMDB endpoints return
errors. Only the TMDB boundary for the known request film is served with synthetic
metadata, through its own TLS server plus a certificate created solely for the
test. Seerr authentication, request management and status changes stay real.

The two symlink cases rest on a pair the baseline itself carries.
`bootstrap/seed.mjs` looks for a seeded special whose title a film in Radarr carries
unchanged, adds exactly that film with a real video file of its own and writes both
ids into `seeds/expected-library.json`; `bootstrap/verify.mjs` then demands the pair
back from every restored seed. Nothing is invented while a scenario runs, and a
baseline without such a pair is refused instead of shipping two cases that could
never find an offer.

## Diagnostics and reset

`artifacts/lme2e-…/<ID>-<scenario>/` holds the container logs, the bootstrap
status, the HTML and JUnit report and, on browser errors, the screenshot and the
Playwright trace. An early build error additionally writes `failure.txt`.
The run-level `summary.json` includes `phases` for baseline restore, stack startup,
baseline verification, application/service stops, state reset, browser execution
and final teardown, including failed phases.
Each phase prints its start and duration to the console. CI also shows phase totals
in the job summary; these separate environment overhead from the actual
test time. Image build times are shown by the corresponding CI build steps.
Use `node scripts/report-timings.mjs` to print saved phase totals locally. Example:

```text
npx playwright show-report artifacts/<run>/<scenario>/report
npx playwright show-trace artifacts/<run>/<scenario>/test-results/<test>/trace.zip
```

`npm run e2e:debug` keeps a failed stack and prints the exact project-scoped
cleanup command. The browser still runs inside a container. No fixed host ports
are published. Containers can be inspected with
`docker ps --filter label=com.docker.compose.project=<project>` and
`docker logs <container>`. The printed cleanup command reads the project and image
variables from `invocation.json` in the artifact directory. Retaining the stack is
forbidden in CI. Every normal run removes its own containers, volumes and networks
even after failures. Never use global prune commands.

A new invocation still gets a completely isolated project with fresh volumes. `seeds/` contains only synthetic test
accounts and their public test passwords; they must never be used as productive
credentials.

The failure path can be checked deliberately with
`node scripts/run.mjs --fail-after-start`. That command has to exit with code 1
and still remove all of its own resources; it is not a successful browser test
run.

## Regenerating the baseline deliberately (online integration)

```text
npm run bootstrap
npm run e2e -- --suite full
npm run e2e -- --suite full
```

Only `bootstrap` needs live metadata for the series and the library films. It
creates empty configurations, sets up Jellyfin and Seerr through their APIs,
creates four short colour videos with FFmpeg and lets Arr scan them. No
copyrighted video material is downloaded. The external series and film identity
lives in `versions.json`; internal ids are derived from the responses. One special
of the series is part of the baseline, together with the film whose title Radarr
reports for it unchanged — the lookup is the only source for that film, and a
bootstrap whose specials offer no such film stops instead of seeding a pairless
baseline. The open Seerr request belongs to its own user with REQUEST permission
only; the suite declines it and checks the status directly in Seerr.

After a successful bootstrap the services are stopped and their files are saved
together with the shared media volume. `seeds/manifest.json` binds the archive to
the full image and seed versioning with SHA-256. Both are validated before the
test stack is built. The manifest additionally contains the test account contract
from `fixtures/accounts.json` and SHA-256 checksums for `baseline.json`,
`seerr-key.json` and `expected-library.json`. The last document holds versioned
expectations for titles, external ids, monitoring, file status, filter counts,
imported technical properties and calendar dates. Bootstrap and the browser tests
read the same synthetic credentials. Different accounts or changed seed companion
files fail the run before the services start. The app account is created only in
the respective test; the Jellyfin and Seerr accounts come from the baseline. Never
copy a running database or use productive backups as a seed. Image changes require
a new baseline and two independent offline verification runs. Version the archive,
the manifest, the media description and the test key together. If the archives keep
growing, add a versioned artifact store and keep the same checksum contract.

References: [Sonarr OpenAPI](https://github.com/Sonarr/Sonarr/blob/develop/src/Sonarr.Api.V3/openapi.json),
[Radarr OpenAPI](https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/openapi.json),
[Seerr v3.0.1 Auth](https://github.com/seerr-team/seerr/blob/v3.0.1/server/routes/auth.ts),
[Seerr v3.0.1 Permissions](https://github.com/seerr-team/seerr/blob/v3.0.1/server/lib/permissions.ts),
[Playwright Docker](https://playwright.dev/docs/docker).

The current baseline was created with Sonarr 4.0.20.3014, Radarr 6.4.4.10685,
Seerr 3.0.1 and Jellyfin 10.11.6. It contains Kabaneri (TVDB 305082, special
S00E01 in 1080p and regular episode S01E01 in 720p), Death Note (TVDB 79481,
without files), Spirited Away (TMDB 129, 1080p), My Neighbor Totoro (TMDB 8392,
without a file), Death Note Relight 1: Visions of a God (TMDB 51482, 1080p) and
the open Inception request (TMDB 27205). The Relight film is the pair the symlink
cases work on: the Death Note special S00E01 carries exactly its title and stays
without a file of its own, so a link is how that special gets its media. Its file
is named after the folder Radarr created and not after the title the API reports,
because a title may hold a separator such as ':' that no path segment may contain.
The four video files show two minutes of a blue or a green frame with a synthetic silent
Japanese AAC audio track and x264 video. Arr rejects videos without an audio
track. The missing regular episode S01E02 stays without a file. The calendar case
uses 2016-04-07 at 15:00 UTC.

A memory sample in the browser and outage test showed about 1 GiB for the running
containers together; that is not a measured peak. Plan additional memory and
several GiB of Docker disk space for the image builds. `summary.json` records the
suite, the selected and completed ids, the total runtime and the duration of
successfully completed scenarios including build and cleanup.

Local evidence from 2026-11-05 (Windows, Docker Desktop with Linux containers):
two fresh bootstrap runs and two complete offline runs with four passed browser
scenarios each. The second offline run needed 221 seconds with an already built
app image and loaded dependencies. The deliberate build error ended with exit code
1 and a complete cleanup; the successful runs also left no containers, volumes or
networks behind. Six infrastructure tests, TypeScript, the format check and
actionlint for all three workflows passed.

The expansion from 2026-11-06 adds 14 infrastructure tests plus dedicated ESLint
and Prettier checks. According to the feedback on the pull request, the regular
GitHub Actions run is successful; a browser test that was then made to fail on
purpose stopped the pipeline as intended. That temporary failure is removed again.
The repository branch rules have to be configured or checked separately. An actual
publication to GHCR through main or a release was not executed with these PR
verification runs.

## CI and publishing

`e2e.yml` is the shared verification path for CI and release. It builds a
Linux-amd64 verification image, runs the selected offline suite and only exports
that image as an artifact after it succeeded. The publication jobs load exactly
this image, tag it and push it only after the backend, frontend and E2E jobs
succeeded. There is no second build in the publication job. Additional
architectures are not published at the moment; they need their own verification
path. The E2E job has read-only permissions and needs no registry secrets.
Diagnostics are kept for seven days, the verification image for one day.
PR, main and release always check all 20 cases. `e2e.yml` is exclusively a
reusable job, not a separately startable workflow. A manually started `CI` run
executes the same full pipeline too. Every E2E error therefore fails the normal
PR run and prevents publication. The E2E job is limited to 50 minutes: at most 15
minutes for the app build and 30 minutes for the browser run. The previous
15-minute limit of the browser run was widened for fully isolated stacks; it is
not a measured Linux runtime. The actual duration is in the run artifact.

Local evidence for the functional expansion from 2026-11-06: two complete offline
runs with 18/18 passed scenarios each, 1,445 and 1,430 seconds. Both ran in
parallel on Windows/Docker Desktop and were fully cleaned up. There are
additionally 19 infrastructure tests, plus three repetitions of the filter case
that was corrected after the Linux diagnosis and the statistics case. The final
Linux acceptance happens in the normal CI run with all 20 scenarios.

Local evidence for the symlink pair from 2026-11-06: one complete offline run with
20/20 passed scenarios in 803 seconds, after a run of every case that had not
passed yet. The four infrastructure cases took 34 to 40 seconds and each browser
case 31 to 61 seconds, so the browser step in CI keeps its 30-minute limit with
room on this machine; the Linux runtime stays measured by CI itself. E15 creates a
real symbolic link in the shared volume and reads it back through Sonarr, E16
declines the same pair and leaves the film file as it was. Three defects came out
of these cases: the settings endpoint refused the enum names the settings page
sends, both Arr instances answer 0 for a file that is not there and the offer
calculation read that as an existing file, and the note under a managed link named
the series instead of the linked episode, which neither the plan nor the code
agrees with. The first two are fixed where they came from and each has a unit test
for the exact wire form; the third is a wording fix in all three languages. Two
browser cases counted the library by hand, so the library headings now read their
count from the seed instead.

Also add the `E2E tests` status as a required check in the GitHub branch rules.
Workflow files cannot enable this repository merge protection themselves.
