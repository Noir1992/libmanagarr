# Browser-E2E

Die Suite testet das kombinierte LibManagarr-Image mit Chromium gegen echte
Sonarr-, Radarr-, Seerr- und Jellyfin-Container. Produktive Konfigurationen,
Host-Medienverzeichnisse, Indexer und Downloader werden nicht verwendet.

## Lokal starten (Windows und Linux)

Voraussetzungen: Node.js ab 22, npm, Docker mit Linux-Containern und Compose v2
oder neuer. Docker Desktop muss laufen. Befehle im Verzeichnis `tests/e2e`:

```text
npm ci
npm run lint
npm run format:check
npm test
npm run typecheck
npm run e2e
```

Der letzte Befehl baut das aktuelle App-Image und den Runner. Browser und
Systembibliotheken sind im Runner enthalten; eine lokale Browserinstallation
ist nicht erforderlich. Mit `E2E_APP_IMAGE` lässt sich ein bereits gebautes
lokales Prüfimage übergeben (PowerShell: `$env:E2E_APP_IMAGE='libmanagarr:e2e'`,
Linux: `E2E_APP_IMAGE=libmanagarr:e2e npm run e2e`). Dieses Image wird weder
neu gebaut noch beim Cleanup gelöscht.

ESLint prüft die TypeScript-Specs und Fixtures sowie die JavaScript-Helfer und
Konfigurationen. Die Specs verwenden zusätzlich die empfohlenen Playwright-
Regeln; typisierte Promise-Regeln erkennen unter anderem vergessene `await`s.
Warnungen lassen den Lint-Lauf scheitern. `npm run lint:fix` behebt automatisch
korrigierbare Lint-Probleme, `npm run format` formatiert die Testdateien.
Generierte Artefakte, Abhängigkeiten und die per Prüfsumme geschützten Seeds
sind ausgeschlossen. Dieselben Lint-/Formatprüfungen laufen in CI und Release.
Git hält die Testquellen auf LF-Zeilenenden; generierte Seeds werden ohne
Zeilenenden-Konvertierung gespeichert, damit ihre Prüfsummen auch nach einem
Windows-Checkout gültig bleiben. Der `minimatch`-Override hält die transitive
Glob-Abhängigkeit der Lint-Werkzeuge auf einer korrigierten Version.

Jede Spec erhält einen vollständig frischen Stack mit eigener App-Konfiguration
und wiederhergestellter Dienst-Baseline. Ein Worker und ein neuer Browserkontext
pro Test verhindern konkurrierende Schreibzugriffe. Die Reihenfolge der Specs
ist keine Voraussetzung. Der LibManagarr-Sync-Zeitplan ist standardmäßig
deaktiviert; Bibliothekstests synchronisieren über die sichtbare UI-Aktion.

Die Kern-Suite verwendet ein internes Docker-Netzwerk ohne externe Route.
`bootstrap/verify.mjs` prüft Dienstzustände, Dateizuordnungen, einen offenen
Request und den fehlgeschlagenen Zugriff auf eine externe IP. Unbekannte
TMDB-Endpunkte liefern Fehler. Nur die TMDB-Grenze für den bekannten
Request-Film wird mit synthetischen Metadaten bedient, über einen eigenen
TLS-Server samt ausschließlich im Test erzeugtem Zertifikat. Die Seerr-
Authentifizierung, Request-Verwaltung und Statusänderung bleiben real.

## Diagnose und Reset

Unter `artifacts/lme2e-…/<szenario>/` liegen Containerlogs, Bootstrap-Status,
HTML-/JUnit-Report sowie bei Browserfehlern Screenshot und Playwright-Trace.
Ein früher Aufbaufehler erzeugt zusätzlich `failure.txt`. Beispiel:

```text
npx playwright show-report artifacts/<lauf>/<szenario>/report
npx playwright show-trace artifacts/<lauf>/<szenario>/test-results/<test>/trace.zip
```

`npm run e2e:debug` erhält einen fehlgeschlagenen Stack und gibt den exakten
projektbezogenen Cleanup-Befehl aus. Der Browser läuft weiterhin im Container.
Es werden keine festen Hostports veröffentlicht. Container lassen sich über
`docker ps --filter label=com.docker.compose.project=<projekt>` und
`docker logs <container>` inspizieren. Der ausgegebene Cleanup-Befehl liest
Projekt und Image-Variablen aus `invocation.json` im Artefaktverzeichnis.
In CI ist Retention des Stacks verboten. Jeder normale Lauf räumt
seine eigenen Container, Volumes und Netzwerke auch nach Fehlern auf.
Keine globalen Prune-Befehle verwenden.

Ein neuer Lauf ist gleichzeitig ein vollständiger Reset. `seeds/` enthält
ausschließlich synthetische Testkonten und deren öffentliche Testpasswörter;
sie dürfen niemals als produktive Zugangsdaten verwendet werden.

Der Fehlerpfad lässt sich mit `node scripts/run.mjs --fail-after-start`
gezielt prüfen. Der Befehl muss mit Exitcode 1 enden und trotzdem alle eigenen
Ressourcen entfernen; er ist kein erfolgreicher Browser-Testlauf.

## Baseline bewusst erneuern (Online-Integration)

```text
npm run bootstrap
npm run e2e
npm run e2e
```

Nur `bootstrap` benötigt Live-Metadaten für die Serie und den Bibliotheksfilm.
Er erzeugt leere Konfigurationen, richtet Jellyfin und Seerr über deren APIs
ein, erzeugt mit FFmpeg zwei kurze blaue Videos und lässt sie durch Arr scannen.
Es wird kein urheberrechtlich geschütztes Videomaterial heruntergeladen.
Die externe Serien-/Filmidentität steht in `versions.json`; interne IDs werden
aus den Antworten ermittelt. Ein Special der Serie ist Teil der Baseline.
Der offene Seerr-Request stammt von einem eigenen Benutzer mit ausschließlich
REQUEST-Berechtigung; die Suite lehnt ihn ab und prüft den Status direkt in Seerr.

Nach erfolgreichem Bootstrap werden die Dienste gestoppt und ihre Dateien mit
dem gemeinsamen Medienvolume gesichert. `seeds/manifest.json` bindet das Archiv
per SHA-256 an die vollständige Image-/Seed-Versionierung. Beide werden vor
dem Testaufbau validiert. Das Manifest enthält zusätzlich den Testkonto-Vertrag
aus `fixtures/accounts.json` sowie SHA-256-Prüfsummen für `baseline.json` und
`seerr-key.json`. Bootstrap und Browser-Tests lesen dieselben synthetischen
Zugangsdaten. Abweichende Konten oder veränderte Seed-Begleitdateien lassen den
Lauf vor dem Start der Dienste scheitern. Das App-Konto wird erst im jeweiligen
Test angelegt; die Jellyfin-/Seerr-Konten stammen aus der Baseline.
Nie eine laufende Datenbank kopieren oder produktive
Backups als Seed verwenden. Änderungen an Images erfordern eine neue Baseline
und zwei unabhängige Offline-Prüfläufe. Archiv, Manifest, Medienbeschreibung
und Test-Key zusammen versionieren. Bei größer werdenden Archiven eine
versionierte Artefaktablage ergänzen und denselben Prüfsummenvertrag erhalten.

Referenzen: [Sonarr OpenAPI](https://github.com/Sonarr/Sonarr/blob/develop/src/Sonarr.Api.V3/openapi.json),
[Radarr OpenAPI](https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/openapi.json),
[Seerr v3.0.1 Auth](https://github.com/seerr-team/seerr/blob/v3.0.1/server/routes/auth.ts),
[Seerr v3.0.1 Permissions](https://github.com/seerr-team/seerr/blob/v3.0.1/server/lib/permissions.ts),
[Playwright Docker](https://playwright.dev/docs/docker).

Die aktuelle Baseline wurde mit Sonarr 4.0.20.3014, Radarr 6.4.4.10685,
Seerr 3.0.1 und Jellyfin 10.11.6 erzeugt. Sie enthält Kabaneri (TVDB 305082,
Special S00E01), Spirited Away (TMDB 129) und den offenen Inception-Request
(TMDB 27205). Die beiden Videodateien zeigen zwei Minuten blaues Bild mit
synthetischer stummer Audiospur. Arr lehnt reine Videos ohne Audiospur ab.

Eine Speicher-Stichprobe im Browser-/Ausfalltest ergab etwa 1 GiB für die
laufenden Container zusammen; dies ist kein gemessener Spitzenwert. Für
Image-Builds zusätzlich Speicher und mehrere GiB Docker-Plattenplatz einplanen.
`summary.json` protokolliert die Gesamtlaufzeit jedes neuen Laufs.

Lokaler Nachweis vom 05.11.2026 (Windows, Docker Desktop mit Linux-Containern):
zwei frische Bootstrap-Läufe und zwei vollständige Offline-Läufe mit jeweils
vier bestandenen Browser-Szenarien. Der zweite Offline-Lauf benötigte 221
Sekunden mit bereits gebautem App-Image und geladenen Abhängigkeiten.
Der absichtliche Aufbaufehler endete mit Exitcode 1 und vollständigem Cleanup;
auch die erfolgreichen Läufe hinterließen keine Container, Volumes oder
Netzwerke. Sechs Infrastrukturtests, TypeScript, Formatprüfung und actionlint
für alle drei Workflows bestanden.

Der Ausbau vom 06.11.2026 umfasst 14 Infrastrukturtests sowie eigene ESLint-
und Prettier-Prüfungen. Laut Rückmeldung zum Pull Request ist der reguläre
GitHub-Actions-Lauf erfolgreich; ein anschließend absichtlich fehlschlagender
Browser-Test hat die Pipeline wie vorgesehen gestoppt. Dieser temporäre Fehler
ist wieder entfernt. Die Repository-Branch-Regeln sind separat zu konfigurieren
beziehungsweise zu prüfen. Eine tatsächliche Veröffentlichung nach GHCR über
Main oder Release wurde mit diesen PR-Prüfläufen nicht ausgeführt.

## CI und Veröffentlichung

`e2e.yml` ist der gemeinsame Prüfpfad für CI und Release. Er baut ein
Linux-amd64-Prüfimage, führt dieselbe Offline-Suite aus und exportiert erst
nach Erfolg dieses Image als Artefakt. Die Publikationsjobs laden genau dieses
Image, taggen und pushen es erst nach erfolgreichen Backend-, Frontend- und
E2E-Jobs. Es gibt keinen zweiten Build im Publikationsjob. Weitere Architekturen
werden derzeit nicht veröffentlicht; sie benötigen einen eigenen Prüfpfad.
Der E2E-Job hat ausschließlich Leserechte und benötigt keine Registry-Secrets.
Diagnosen bleiben sieben Tage, das Prüfimage einen Tag gespeichert.
Der E2E-Job ist auf 35 Minuten begrenzt. Seine einzelnen Schritte haben eigene
Zeitlimits, insbesondere jeweils 15 Minuten für App-Build und Browserlauf.

Den Status `E2E tests` zusätzlich in den GitHub-
Branch-Regeln als erforderlichen Check hinterlegen. Workflow-Dateien können
diesen Repository-Mergeschutz nicht selbst aktivieren.
