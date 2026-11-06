# Plan: Fachliche E2E-Abdeckung ausbauen

Stand: 06.11.2026. Status: E01–E14, Szenariokatalog und erweiterte Seeds implementiert. PR, Main und Release prüfen verbindlich alle 18 Fälle in derselben Pipeline; Linux-Abnahme läuft.

## 1. Ziel und Ausgangslage

Die vorhandene, in der [E2E-README](../tests/e2e/README.md) dokumentierte Infrastruktur weiterverwenden und um aussagekräftige Benutzerabläufe ergänzen. Dieser Plan beschreibt die nächste Ausbaustufe der fachlichen Abdeckung, nicht einen erneuten Aufbau des Teststacks.

Vorhanden sind vier Chromium-Szenarien:

- Erstinstallation, Anmeldung, Abmeldung, grundlegender Zugriffsschutz und Offline-Icon-Prüfung.
- Sonarr/Radarr hinzufügen, Verbindung prüfen, synchronisieren, Titel suchen und Details öffnen.
- Seerr konfigurieren und einen offenen Request ablehnen, einschließlich Rückprüfung in Seerr.
- Sonarr-Ausfall mit sichtbarem Verbindungsfehler und erfolgreicher Wiederholung nach Neustart.

Hinzu kommen 14 Infrastrukturtests, ESLint einschließlich Playwright-/Promise-Regeln, Prettier, Typprüfung, Offline-Seeds, Diagnose und Cleanup. Laut Rückmeldung zum PR wurden ein erfolgreicher Pipeline-Lauf und der Abbruch bei einem absichtlichen Browser-Testfehler nachgewiesen.

Die bisherigen Tests zeigen, dass die Hauptwege funktionieren. Sie prüfen noch nicht ausreichend, ob Änderungen korrekt gespeichert werden, Fehler bestehende Daten erhalten, Sitzungen widerrufen werden und Wiederherstellungen wirklich funktionieren.

## 2. Leitlinien

1. Jeder Test schützt einen konkreten Benutzerablauf mit nachvollziehbarem Ausfallrisiko.
2. Die behauptete Nutzeraktion erfolgt im LibManagarr-Browser. Bestehende APIs dürfen Ausgangsdaten vorbereiten und Ergebnisse zusätzlich prüfen.
3. Sichtbare Ergebnisse und tatsächliche Zustandsänderungen prüfen. Ein HTTP-200, eine Erfolgsmeldung oder ein Screenshot allein reicht für schreibende Abläufe nicht.
4. Keine Produktionsendpunkte, Auth-Bypässe oder Ersatzserver für Sonarr, Radarr und Seerr ergänzen. Cookies, Autorisierung und CSRF bleiben aktiv.
5. Die verbindlichen Suites bleiben nach dem Laden von Images und Dependencies ohne Internet ausführbar. Externe Metadaten nur an der vorhandenen eng begrenzten TMDB-Grenze kontrollieren.
6. Fachlich gleiche Detailvarianten weiterhin in Backend-/Angular-Tests prüfen. E2E konzentriert sich auf das Zusammenspiel von Browser, Backend, Persistenz und echten Diensten.

## 3. Testdaten und Isolation zuerst erweitern

### 3.1 Unabhängiger Ausgangszustand

Aktuell setzt `scripts/run.mjs` den vollständigen Stack pro Spec-Datei zurück. Ein neuer Browserkontext setzt die Datenbanken nicht zurück.

Für den ersten Ausbau gilt deshalb: **Eine Spec-Datei enthält genau einen unabhängigen, schreibenden Benutzerablauf.** Mehrere zusammengehörige Schritte dürfen mit `test.step` gegliedert werden. Passwortwechsel, Recovery-Code-Verbrauch, Request-Löschung und Restore erhalten jeweils ihren eigenen frischen Stack.

Die bisher fest im Orchestrator eingetragene Dateiliste durch einen expliziten Szenariokatalog ersetzen. Jedes Szenario bekommt ID, Spec-Datei, Suite und benötigte Ausgangsdaten. Ein gezielter Einzelaufruf soll denselben Aufbau und Cleanup verwenden. Ein unbekannter Szenarioname muss scheitern. Fehlende Pflicht-Specs dürfen nicht zu einem erfolgreichen leeren Lauf führen.

Ein Worker pro Stack bleibt bestehen. Keine geteilten veränderten Datenbanken zwischen Specs oder CI-Läufen. Eine spätere Beschleunigung über kleinere Stacks erst nach Laufzeitmessung und mit denselben Isolationsgarantien umsetzen.

### 3.2 Aussagekräftige Bibliotheksdaten

Die bisherige Baseline enthält eine Serie, einen Film und je eine Datei. Für belastbare Filter-/Sortierprüfungen ergänzen:

- Mindestens zwei Filme mit eindeutig unterschiedlicher Titelsortierung und Dateiverfügbarkeit.
- Mindestens zwei Serien sowie eine vorhandene und eine fehlende reguläre Episode; das bestehende Special bleibt erhalten.
- Kontrollierte Unterschiede bei Auflösung, Monitoring und mindestens einem weiteren tatsächlich angebotenen Filtermerkmal, etwa Profil oder Genre.
- Ein kleines, bekanntes Inventar für Codec-/Sprachstatistiken. Video- und Audioeigenschaften mit FFmpeg gezielt erzeugen; tatsächliche Arr-Ergebnisse nach dem Import prüfen.
- Einen offenen Seerr-Request für jeden frischen Request-Test. Unterschiedliche Medien nur ergänzen, wenn ein Szenario sie wirklich benötigt.

Keine Indexer oder Downloader einrichten. Interne IDs aus den echten Diensten ermitteln. Für neue Testmedien passende Profile und Importergebnisse prüfen, statt IDs oder ausgedachte Dateifelder fest einzutragen.

Die fachlichen Sollwerte in einem versionierten Erwartungsmanifest festhalten: Titel, externe Identität, Dateistatus, Episode/Special, Filterzugehörigkeit, relative Reihenfolge, relevante technische Werte und Kalenderdaten. Erwartete Teilmengen ausdrücklich benennen. Nicht einfach die aktuelle App-Antwort als erwartetes Browserergebnis wiederverwenden.

Seed-Archiv und Begleitdateien weiterhin per Prüfsumme schützen. Bei Änderungen an archivierten Daten den Bootstrap ausführen, Dienste vor dem Sichern stoppen und die neue Baseline zweimal aus frischen Volumes laden. Keine SQL-Änderungen an laufenden Fremddatenbanken.

## 4. Szenarien und konkrete Abnahme

P0 und P1 kennzeichnen die fachliche Priorität. In CI sind sämtliche Szenarien verpflichtend. Die vier vorhandenen Szenarien bleiben erhalten; vorhandene Assertions gezielt verstärken, statt dieselben Wege mehrfach neu zu testen. Die lokale Auswahl `core` enthält P0 und die bestehenden vier Fälle; der Standard `full` enthält alle 18 Fälle.

### E01 — Falsche Anmeldung und erfolgreiche Wiederholung · P0

**Vorbereitung:** Ein eingerichtetes Konto ohne 2FA und ein neuer, nicht angemeldeter Browserkontext.

**Ablauf:** Einmal ein falsches Passwort eingeben, Fehlermeldung prüfen, geschützte Route aufrufen und anschließend mit richtigen Daten anmelden.

**Abnahme:** Vor erfolgreicher Anmeldung bleibt die geschützte Ansicht gesperrt; ein unabhängiger API-Aufruf ist nicht autorisiert. Danach ist die Bibliothek erreichbar. Der Fehlversuch darf keine Sitzung erzeugen oder die gültigen Kontodaten verändern.

Nur ein gezielter Fehlversuch: Grenzwertkombinationen, Sperrzeiten und Rate-Limits bleiben zunächst in den bestehenden Auth-Tests. Keine minutenlangen Entsperr-Wartezeiten in der Kern-Suite.

### E02 — Passwort ändern und Sitzungen widerrufen · P0

**Vorbereitung:** Zwei unabhängig angemeldete Browserkontexte A und B für dasselbe Konto.

**Ablauf:** In A zunächst mit falschem aktuellem Passwort eine Änderung versuchen; anschließend mit richtigem aktuellem Passwort ein neues Passwort setzen.

**Abnahme:** Der erste Versuch zeigt einen Fehler und lässt A und B angemeldet. Die erfolgreiche Änderung führt zum erneuten Login; auch B verliert beim nächsten geschützten Zugriff seine Berechtigung. Das alte Passwort funktioniert nicht mehr, das neue schon. Nach Reload und erneutem Login bleibt die Änderung erhalten.

Referenz für die erwartete Sitzungsbehandlung: `AccountEndpoints.cs`, `AccountService` und `AccountSecurityTransactionTests.cs`.

### E03 — 2FA einrichten und damit anmelden · P0

**Vorbereitung:** Frisches Konto ohne 2FA. Ein zweiter Browserkontext ist bereits angemeldet, um die Sitzungsinvalidierung zu prüfen.

**Ablauf:** 2FA im Konto-Tab starten, den dort angezeigten manuellen Schlüssel lesen, einen gültigen TOTP berechnen und die Einrichtung im UI bestätigen. Recovery-Codes sichtbar sichern/bestätigen. Abmelden und in einem frischen Kontext mit Passwort und TOTP anmelden.

**Abnahme:** Aktivierter 2FA-Status und Recovery-Code-Anzahl stimmen mit der Konto-API überein. Die andere bestehende Sitzung ist ungültig. Nach dem Passwort allein bleibt die Bibliothek gesperrt. Ein falscher Code führt zu einem sichtbaren Fehler; ein frischer gültiger Code erlaubt die Anmeldung.

Die App verwendet sechsstellige SHA-1-TOTPs mit 30-Sekunden-Schritten und verbietet die Wiederverwendung eines bereits verbrauchten Schritts. Der Testhelfer muss das berücksichtigen: reale Zeit verwenden, frische Schritte mit begrenztem Polling abwarten und ausreichend Szenario-Timeout einplanen. Keine Änderung der Serveruhr und kein Browser-Zeitmock als Ersatz für die echte Authentifizierung.

### E04 — Recovery-Code ist nur einmal nutzbar · P0

**Vorbereitung:** Eigenes frisches Konto mit aktivierter 2FA und einem über den regulären Einrichtungsweg ausgegebenen Recovery-Code. Die Einrichtung darf hier per bestehender API erfolgen; E03 deckt den UI-Einrichtungsweg ab.

**Ablauf:** Mit Passwort und Recovery-Code über den Browser anmelden, abmelden und denselben Code in einer neuen Anmeldung erneut versuchen. Anschließend einen anderen gültigen Code verwenden.

**Abnahme:** Der erste Code erlaubt genau eine Anmeldung. Sein zweiter Einsatz bleibt gesperrt und zeigt eine verständliche Meldung. Der nächste Code funktioniert; die verbleibende Anzahl sinkt jeweils genau um eins.

### E05 — Filter, Sortierung und Ansichtswechsel liefern dieselben Medien · P0

**Vorbereitung:** Synchronisierte erweiterte Bibliotheks-Baseline mit bekannten Treffern und Gegenbeispielen.

**Ablauf:** Im UI einen verfügbaren Filter einschließen, ein Merkmal ausschließen, die Suche kombinieren und anschließend die Sortierrichtung wechseln. Zwischen Karten- und Tabellenansicht wechseln. Filter wieder zurücksetzen und einen Suchbegriff ohne Treffer eingeben.

**Abnahme:** Exakte erwartete Titelmenge, sichtbare Anzahl und Reihenfolge stimmen. Ausgeschlossene Titel fehlen tatsächlich. Beide Ansichten enthalten dieselbe Medienmenge; das Sortieren wird in einer Ansicht mit eindeutig definiertem Sortierumfang geprüft. Nach Reset ist die vollständige Ausgangsmenge wieder sichtbar. Eine Suche ohne Treffer zeigt einen passenden Leerzustand und keine alten Daten.

Nur eine gezielte Kombination der Filter testen. Alle Kombinationen und Grenzwerte verbleiben in Komponenten-/Backend-Tests. Persistenz nach Reload nur für Einstellungen verlangen, deren Speicherung das aktuelle Produkt tatsächlich vorsieht.

### E06 — Instanzänderungen, ungültige Verbindung und Löschen · P0

**Vorbereitung:** Eine funktionierende konfigurierte Arr-Instanz mit synchronisierter Bibliothek.

**Ablauf:** Eine URL oder einen API-Key im Formular ungültig ändern, den Verbindungstest starten und die Fehlermeldung prüfen. Ungespeicherte Änderung beim Verlassen zunächst behalten, anschließend bewusst verwerfen. Danach einen gültigen Namen speichern und die Instanz über „Remove“ löschen. Das bestehende Produkt besitzt dafür keinen Bestätigungsdialog.

**Abnahme:** Die fehlgeschlagene Verbindung überschreibt nicht still die zuletzt gültige gespeicherte Konfiguration. Der Verlassen-Dialog respektiert Abbrechen und Verwerfen. Der gültige neue Name bleibt nach Reload erhalten. Nach bestätigtem Löschen fehlen Instanz und zugehörige Medien in der App; die andere Instanz und die echten Quelldaten im Fremddienst bleiben erhalten.

Beim Aufbau prüfen, welche Felder automatisch und welche erst nach erfolgreichem Verbindungstest gespeichert werden. Die Tests müssen diese vorhandenen Regeln abbilden.

### E07 — Frische und konfigurierte leere Bibliothek unterscheiden · P0

**Vorbereitung:** Frische App; danach eine gültige Verbindung zu einem echten, aber leeren Arr-Zustand. Für diesen Fall eine separat vorbereitete leere Konfiguration oder das gezielte Entfernen ausschließlich der eigenen Testeinträge per API verwenden.

**Ablauf:** Ohne Instanzen die Bibliothek öffnen, über den Einrichtungshinweis zu den Einstellungen wechseln, die leere Instanz konfigurieren und im UI synchronisieren.

**Abnahme:** Ohne Instanz ist der Konfigurationshinweis sichtbar. Nach erfolgreicher Synchronisierung der leeren Instanz erscheint ein normaler Leerzustand mit null Treffern, kein Verbindungsfehler. Keine Daten aus vorherigen Szenarien tauchen auf.

### E08 — Seerr-Request löschen und Abbrechen respektieren · P1

**Vorbereitung:** Ein echter offener Request mit kontrollierten Metadaten; frischer Zustand unabhängig vom bestehenden Ablehnungs-Test.

**Ablauf:** Löschdialog öffnen und abbrechen, Request nach Reload prüfen, danach erneut öffnen und bestätigen.

**Abnahme:** Abbrechen erhält den Request im UI und in Seerr. Bestätigen entfernt genau diesen Request, auch nach Reload. Die Seerr-API bestätigt die Entfernung. Keine Freigabe oder Download-Anforderung auslösen.

Request-Freigabe und Änderungen von Server/Profil sind nicht Bestandteil dieser Ausbaustufe: Dafür zuerst einen gesonderten, downloadfreien Seerr-/Arr-Vertrag nachweisen.

### E09 — Seerr-Ausfall und erneutes Laden · P1

**Vorbereitung:** Funktionierendes Seerr mit sichtbarem Request. Der Host-Orchestrator steuert ausschließlich den eigenen Seerr-Container.

**Ablauf:** Seerr stoppen, Requests im Browser laden, Fehler anzeigen lassen, Dienst starten und die Seite erneut laden. Das bestehende Produkt besitzt an dieser Stelle keine eigene Wiederholen-Schaltfläche.

**Abnahme:** Fehler wird verständlich angezeigt; zwischengespeicherte Requests werden nicht als erfolgreich frisch geladen ausgegeben. Nach Wiederholung erscheint der erwartete Request. Instanzkonfiguration und Request-Zustand bleiben unverändert.

Den bestehenden Sonarr-Ausfalltest weiterverwenden; nicht denselben Verbindungstest für alle Container duplizieren.

### E10 — Geplante Aufgabe konfigurieren und manuell ausführen · P1

**Vorbereitung:** Konfigurierte Instanzen, leere App-Snapshots und deaktivierter `arrDataSync`-Zeitplan.

**Ablauf:** Im Aufgaben-Tab einen gültigen Zeitplan speichern und nach Reload prüfen. Anschließend den Zeitplan wieder deaktivieren und die Aufgabe über „Jetzt ausführen“ starten.

**Abnahme:** Gespeicherte Konfiguration wird korrekt angezeigt. Die manuelle Ausführung wird abgeschlossen, der Zeitstempel des letzten Laufs aktualisiert und die erwartete Bibliothek tatsächlich synchronisiert. Die Aufgabe bleibt danach deaktiviert. Ein ungültiges Eingabebeispiel wird im Editor abgefangen.

Hier wird der manuelle Aufgabenweg getestet. Automatische Fälligkeit, Sommerzeit, überlappende Läufe und Berechnung des nächsten Termins bleiben in den vorhandenen Scheduler-Tests; keine beschleunigte Browseruhr zur Steuerung des Backends einsetzen.

### E11 — Verschlüsseltes Backup exportieren und Import prüfen · P0

**Vorbereitung:** Bekannte App-Einstellung A und synchronisierte Medien. Testkonto ohne 2FA; die Kombination Backup + 2FA ist bereits auf API-Ebene zu prüfen und kann später als eigener Browserfall folgen.

**Ablauf:** Backup im UI exportieren, den echten Download sichern und über den Datei-Upload wieder auswählen. Einmal mit falschem Archivpasswort prüfen, danach mit richtigem Passwort. Importvorschau anzeigen und abbrechen.

**Abnahme:** Download ist vorhanden und nicht leer; er wird vom echten Importvalidator mit dem richtigen Passwort akzeptiert. Falsches Passwort zeigt einen Fehler und ändert keine Einstellungen. Die Vorschau enthält die erwarteten Komponenten. Abbrechen lässt Einstellung A und Bibliothek unverändert und räumt den vorgemerkten Import auf.

Keine Prüfung, die lediglich Dateiname oder ZIP-Signatur als Beweis für ein gültiges verschlüsseltes Backup verwendet.

### E12 — Backup anwenden und nach Host-Neustart weiterarbeiten · P0

**Vorbereitung:** Eigener frischer Stack. Backup mit Einstellung A und bekanntem Bibliotheksstand über den regulären Export erzeugen. Danach im UI auf Einstellung B ändern.

**Ablauf:** Archiv im UI hochladen, Vorschau prüfen, Identität mit dem aktuellen Kontopasswort bestätigen und Wiederherstellung anwenden. Die Wiederanlauf-Anzeige beobachten und die App nach dem tatsächlichen Host-Neustart erneut öffnen.

**Abnahme:** Während der Wiederherstellung verständlicher Zustand statt einer fälschlichen Erfolgsmeldung. Nach Bereitschaft sind Einstellung A und die gesicherten Bibliothekseinträge wieder vorhanden; B ist verschwunden. Das aktuelle Konto funktioniert weiter: Kontodaten und Data-Protection-Schlüssel gehören laut vorhandenem Backup-Vertrag nicht zum zu ersetzenden Anwendungsbackup. Die eigentlichen Videodateien bleiben auf dem separaten Medienvolume; ihre Sicherung ist keine Funktion dieses App-Backups.

Der Neustart erfolgt bereits intern über `ApplicationHostRunner`; nicht pauschal einen Container-Neustart erwarten oder den App-Prozess durch einen Fake ersetzen. Mit begrenzten Bereitschaftsprüfungen auf die echte neue Host-Instanz warten. Bei Fehlern Backup-Status, App-Logs und Browser-Trace sichern.

### E13 — Kalender mit stabilen Datumswerten · P1

**Vorbereitung:** Ein bekannter Termin aus den gespeicherten Serien-/Episodendaten. Datum, UTC-Zeit und erwartete sichtbare Bezeichnung im Erwartungsmanifest festhalten. Browser-Zeitzone explizit setzen.

**Ablauf:** Den Browser-Anzeigetag mit Playwright `setFixedTime` auf einen passenden Tag setzen, Kalender öffnen, zwischen Monats- und Agendaansicht wechseln und den Termin öffnen.

**Abnahme:** Titel, Datum, Uhrzeit und gegebenenfalls Staffel-/Episodennummer entsprechen der Fixture. Beide Ansichten zeigen denselben Termin. Die abgefragte Zeitspanne passt zum sichtbaren Zeitraum. Keine Abhängigkeit davon, welcher reale Monat gerade läuft.

Die Browserzeit beeinflusst nur die Datumsanzeige und Bereichsauswahl. Backend, Arr-Dienste und Authentifizierung behalten ihre reale Zeit. Ein zusätzlicher Zeitzonen-Grenzfall ist erst nach dem stabilen Grundfall sinnvoll.

### E14 — Statistiken aus bekanntem Medieninventar · P1

**Vorbereitung:** Synchronisierte Dateien mit bekannten Video-/Audio-Codecs und sinnvoller Filterunterscheidung.

**Ablauf:** Codec-Statistik öffnen, einen Instanzfilter anwenden und anschließend wieder entfernen.

**Abnahme:** Sichtbare Beschriftungen, Anzahlen und Prozentwerte entsprechen den vorher festgelegten Sollwerten. Der Filter verändert die erwartete Teilmenge; Reset stellt die Ausgangswerte wieder her. Leere Teilmengen zeigen einen normalen Leerzustand ohne ungültige Prozentwerte.

Keine Pixelmessung des Diagramms. Falls Anzahlen nur grafisch vorliegen, zuerst sinnvolle zugängliche Beschriftungen ergänzen. Bibliotheks-Trends mit mehreren historischen Snapshots werden separat geplant, nicht durch erfundene Historie in einer laufenden Datenbank erzeugt.

## 5. Geplante Struktur und Hilfsfunktionen

Die vorhandenen vier Specs behalten. Neue unabhängige Abläufe beispielsweise so ablegen:

```text
tests/e2e/
  fixtures/
    account.ts                     Kontovorbereitung und Anmeldung mehrerer Kontexte
    library.ts                     Sync, Ansichtswechsel und fachliche Detailprüfungen
    backup.ts                      Download-/Upload-Dateien und Bereitschaft nach Restore
    totp.ts                        standardkonforme TOTP-Erzeugung ohne App-Interna
  scripts/
    scenarios.mjs                  expliziter Katalog: ID, Spec-Datei und Suite
  seeds/
    expected-library.json          bekannte Medien, Filtermengen, Reihenfolgen und Termine
  specs/
    auth-invalid-login.spec.ts
    account-password.spec.ts
    account-two-factor.spec.ts
    account-recovery.spec.ts
    library-filters.spec.ts
    instance-lifecycle.spec.ts
    library-empty.spec.ts
    seerr-delete.spec.ts
    seerr-recovery.spec.ts
    tasks-manual-run.spec.ts
    backup-preview.spec.ts
    backup-restore.spec.ts
    calendar.spec.ts
    codec-statistics.spec.ts
```

Dateinamen sind Vorschläge. Kleine fachliche Helfer bevorzugen; keine allgemeine Page-Object-Hierarchie für jede Schaltfläche bauen. Primär Rollen, Labels und sichtbare Inhalte verwenden. Strukturabhängige Elternnavigation schrittweise durch geeignete vorhandene Regionen ersetzen; zusätzliche ARIA-Beschriftungen nur dort ergänzen, wo sie auch für die Bedienung sinnvoll sind.

Feste Wartezeiten, `force`-Klicks, breite Fehlerunterdrückung, `test.only` und dauerhafte `skip`-/`fixme`-Markierungen vermeiden. Netzwerk-Waits vor der auslösenden Aktion registrieren und zusätzlich das sichtbare Ergebnis prüfen. TOTP-/Restore-Zeitlimits pro Szenario begründen.

## 6. Suites, CI und Diagnose

- **Verbindlicher Standard:** alle vier vorhandenen Szenarien plus E01–E14. PR, Main und Release führen immer `full` mit allen 18 Fällen aus. Jeder Fehler macht den normalen Pipeline-Lauf rot.
- **Optionale lokale Auswahl:** `--suite core` umfasst die vorhandenen vier Fälle plus E01–E07, E11 und E12. Sie wird in CI nicht angeboten. Ohne ausdrückliche Auswahl laufen lokal ebenfalls alle 18 Fälle.
- `e2e.yml` ist ausschließlich per `workflow_call` in CI beziehungsweise Release eingebunden. Es gibt keinen separaten manuellen Browser-E2E-Workflow. Der manuell startbare Workflow `CI` führt dieselbe vollständige Pipeline aus. Alle verwenden denselben Orchestrator, dieselben Seeds und das exakt weitergereichte Prüfimage.
- Ein Lauf dokumentiert gewählte Suite und tatsächlich ausgeführte Szenario-IDs. Der Katalog muss sicherstellen, dass `full` alle Kernfälle enthält. Die vorhandene Publikationssperre bleibt bestehen.
- Lint, Prettier, Typprüfung und Infrastrukturtests bleiben verpflichtend. Neue Katalog-/Reset-Logik mit gezielten Unit-Tests prüfen; keine zusätzlichen Unit-Tests für reine Dateinamenlisten ohne Verhaltensprüfung.
- Trace, Screenshot, HTML-/JUnit-Report, Dienstlogs, Seed-Manifest und fachlicher Vorabstatus weiterhin bei Fehlern sichern. IDs zusätzlich im Artefaktpfad verwenden. Mehrere Kontexte und Datei-Downloads sollen im Trace nachvollziehbar bleiben.

Laufzeiten auf einem Linux-GitHub-Runner messen. Das frühere Zehn-Minuten-Ziel der optionalen Kern-Suite ist kein Limit für die verbindlichen 18 Fälle. Browser-Schritt und Job erlauben 30 beziehungsweise 50 Minuten, einschließlich frischer Dienststarts pro Fall. Bei Überschreitung zunächst Aufbau und unabhängige Wiederholungen untersuchen; Pflichtfälle nicht still überspringen oder durch Retries grün machen.

Der Check-Name `E2E tests` bleibt verständlich und stabil. Erforderliche Branch-Checks separat passend zur Workflow-Konfiguration hinterlegen. Ein fehlgeschlagener vollständiger Release-Lauf darf kein Image publizieren.

## 7. Umsetzungspakete und Reihenfolge

1. **Daten und Auswahl:** Szenariokatalog, eindeutiger Reset-Vertrag, Einzelaufruf und erweiterte Erwartungsdaten einführen. Seed-Kompatibilität/Prüfsummen aktualisieren und zwei unabhängige Offline-Ladevorgänge nachweisen.
2. **Alltagsabläufe:** E01, E02 sowie E05–E07 umsetzen. Nach jedem Paket negative Gegenproben für konkrete Assertions durchführen und zurücknehmen.
3. **Sicherheits- und Wiederherstellungswege:** E03, E04, E11 und E12 umsetzen. Reale Sitzungswechsel, TOTP-Schritte, Dateiübertragung und Host-Neustart prüfen. Damit ist die neue Kern-Suite vollständig.
4. **Erweiterte Abläufe:** E08–E10, E13 und E14 mit ihren eigenen Ausgangszuständen ergänzen. Kalenderdatum und Statistik-Sollwerte nachvollziehbar dokumentieren.
5. **Pipeline und Abnahme:** Vollständige Suite in PR, Main und Release verpflichtend machen, Laufzeiten messen, Fehlerdiagnose und Veröffentlichungsblockade prüfen und lokale Bedienung in der E2E-README ergänzen.

Die TOTP-Bibliothek erst bei Umsetzung auswählen und exakt versionieren. Für den Testhelfer keine eigene Kryptografie entwickeln. Produktänderungen nur bei nachgewiesenen Fehlern oder notwendigen Verbesserungen der Bedienbarkeit vornehmen und getrennt erklären.

## 8. Definition of Done

- [x] E01–E14 und die bestehenden vier Szenarien sind implementiert; die dokumentierte Zuordnung `core`/`full` stimmt mit dem Katalog überein.
- [ ] Jeder neue schreibende Ablauf besteht einzeln sowie innerhalb seiner Suite aus einem frischen Zustand. Kein Erfolg nur durch eine bestimmte Testreihenfolge.
- [ ] Browseraktionen prüfen sichtbare Ergebnisse; relevante Änderungen sind nach Reload und über gespeicherte Zustände beziehungsweise echte Fremd-APIs bestätigt.
- [ ] Fehler-, Abbruch- und Wiederholungsfälle verändern keine anderen Testdaten und hinterlassen keine eigenen Docker-Ressourcen.
- [x] Neue Testdaten sind synthetisch, versioniert, nachvollziehbar erzeugt und ohne Live-Metadatenanbieter nutzbar.
- [x] Zwei unabhängige vollständige Offline-Läufe bestehen lokal.
- [ ] Die vollständige Suite besteht als verpflichtender Teil der normalen Pipeline auf einem Linux-GitHub-Runner.
- [x] Lint, Formatprüfung, Typprüfung und erforderliche Unit-/Integrationstests bestehen.
- [ ] Eine kontrollierte falsche Browsererwartung lässt den vorgesehenen Job scheitern, erzeugt brauchbare Artefakte und blockiert die zugehörige Publikation. Der Prüfdefekt ist anschließend entfernt.
- [ ] Gemessene Laufzeit, Suite-Auswahl, Einzelaufruf und Diagnose sind dokumentiert. Geänderte Schritt-/Job-Zeitlimits sind begründet.

## 9. Bewusst außerhalb dieses Plans

Sichtbarer lokaler Browsermodus, zusätzliche Browser und mobile Geräte, vollständige Screenshot-Regression, Lasttests, automatische Downloads, Request-Freigabe/erweiterte Bearbeitung, vollständige Rate-Limit-Matrix, native Windows-Dateisystemtests und TrueNAS-Abnahme.

Symlink-Abläufe bleiben separat geplant. Die hier erweiterte Suite soll dafür eine stabile Grundlage liefern, aber noch keine Funktion voraussetzen, die das Produkt nicht implementiert hat.

## Umsetzungsnachweis

Der Katalog umfasst 18 verbindliche Fälle; 13 davon lassen sich bei Bedarf lokal als `core` auswählen. 19 Infrastrukturtests sichern Auswahl, vollständigen Standard, Fehlerweitergabe und Seed-Vertrag ab. Seed-Version 2 wurde zweimal aus frischen echten Diensten erzeugt. Das Erwartungsmanifest enthält zwei Filme, zwei Serien und drei echte FFmpeg-Dateien mit x264/AAC und japanischer Audiospur. Sonarr/Radarr bestätigen die importierten technischen Eigenschaften beim Bootstrap und vor jedem Szenario.

Die neuen Abläufe wurden mit realen Browseraktionen geprüft, insbesondere unabhängige Kontositzungen, verbrauchte TOTP-/Recovery-Codes, Download/Upload, echter Host-Neustart und Wiederherstellung zwischenzeitlich entfernter Instanzen/Bibliotheksdaten. Der Aufgabenfall prüft eine vor dem manuellen Start leere Bibliothek und einen noch nicht vorhandenen Laufzeitstempel. Die Kalenderzeit ist ausschließlich im Browser fixiert.

Eine absichtlich falsche Login-Fehlermeldung wurde lokal als Gegenprobe eingesetzt: Exitcode 1, Screenshot, Trace und JUnit wurden erzeugt; Container, Volumes und Netzwerk wurden entfernt. Der Prüfdefekt wurde automatisch zurückgenommen. Dieser lokale Nachweis ersetzt nicht den noch ausstehenden GitHub-Lauf der neuen Suite.

Einzige notwendige Produktänderung: Die beiden Aufgaben-Schalter geben ihre übersetzte zugängliche Beschriftung jetzt an den tatsächlichen Material-Schalter weiter. Die bestehenden sechs Aufgaben-Komponententests, Frontend-Lint und Angular-Build bestehen. `actionlint` prüft alle drei Workflows.

Zwei vollständige lokale Offline-Läufe mit jeweils 18 bestandenen Fällen dauerten 1.445 und 1.430 Sekunden. Sie liefen parallel unter Windows/Docker Desktop mit eigenständigen Volumes und bereits gebautem App-Image; diese belasteten lokalen Laufzeiten sind keine Linux-CI-Messung. Beide Läufe hinterließen keine Container, Volumes oder Netzwerke. Nach einem auf Linux beobachteten Rennen beim Entfernen von Filterchips wartet der Helfer auf das tatsächliche Verschwinden jedes Chips; drei frische E05-Läufe und ein E14-Lauf mit dieser Korrektur bestehen ebenfalls.

Ein früherer Linux-Kernlauf bestand mit 13/13 Fällen in 672 Sekunden für den Browser-Schritt beziehungsweise 795 Sekunden für den E2E-Job. Das Artefakt weist alle 13 IDs aus; etwa 40 Sekunden entfielen auf den Runner-Aufbau, die übrige Zeit auf 13 unabhängige Dienststarts, Browserabläufe und Cleanup. Die verkürzte CI-Auswahl und der separate manuelle E2E-Start sind inzwischen entfernt. Die verbindliche Abnahme erfolgt mit allen 18 Fällen im normalen PR-Lauf.

## 10. Geprüfte Referenzen für die Umsetzung

- Bestehende Specs, Fixture-Helfer, Bootstrap und Orchestrator unter `tests/e2e/`; Bedienung in [README.md](../tests/e2e/README.md).
- Konto/2FA: `AccountEndpoints.cs`, `AuthEndpoints.cs`, `TwoFactorService.cs`, Konto-/Login-Komponenten sowie `TwoFactorApiTests.cs` und `AccountSecurityTransactionTests.cs`.
- Backup: `BackupEndpoints.cs`, `BackupCaptureService.cs`, `BackupImportService.cs`, `ApplicationHostRunner` und die vorhandenen Backup-Integrationstests. Anwendungseinstellungen und Bibliotheksdaten werden wiederhergestellt; Kontoidentität und Schlüssel verbleiben in der aktuellen Installation.
- Aufgaben: `ScheduledTaskCatalog.cs`, `ScheduledTaskEndpoints.cs`, Aufgaben-Tab und vorhandene Scheduler-Tests. `arrDataSync` ist konfigurierbar und manuell startbar.
- [Sonarr OpenAPI](https://raw.githubusercontent.com/Sonarr/Sonarr/develop/src/Sonarr.Api.V3/openapi.json) und [Radarr OpenAPI](https://raw.githubusercontent.com/Radarr/Radarr/develop/src/Radarr.Api.V3/openapi.json): Ressourcen für Serien/Filme, Episoden/Dateien, Profile, Monitoring und Kalender. Bei Seed-Erweiterung die konkreten Payloads und Felder gegen die gepinnten Dienstversionen prüfen; die Develop-Schemas sind keine Versionsgarantie.
- [Playwright Clock](https://playwright.dev/docs/clock): feste Browser-Anzeigezeit für den Kalender; keine Steuerung von Backend-Jobs oder TOTP-Verifikation.
