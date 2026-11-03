# Plan für konfigurierbare Anmeldegrenzen und verständliche Sperrhinweise

## Ziel und Umfang

Die Anzahl geschützter Anfragen pro Minute und die Anzahl erlaubter Fehlversuche werden in den Settings einstellbar. Die bisherigen Werte bleiben die Standards. Alle Formulare zur Prüfung des Kontopassworts zeigen nach falscher Eingabe die verbleibenden Versuche an, sobald höchstens zwei übrig sind. Bei einer Kontosperre oder einer überschrittenen Anfragerate zeigen sie Ursache und verbleibende Wartezeit an.

Dieser Plan beschreibt die Umsetzung; er ändert noch kein Anwendungsverhalten.

## Bestehender Stand

- `Program.cs` begrenzt die mit der Policy `auth` geschützten Endpunkte gemeinsam auf zehn Anfragen pro IP in einem festen Zeitfenster von einer Minute. Die Grenze zählt Anfragen, auch erfolgreiche, nicht ausschließlich falsche Passwörter.
- `AccountService` verwendet fünf Fehlversuche und eine Kontosperre von fünf Minuten. Fehlversuche und Sperrende liegen in `auth.db` und gelten unabhängig von der IP.
- Passwort- und 2FA-Prüfungen verwenden bereits denselben Kontozähler. Unbekannte Benutzernamen erhöhen diesen Zähler nicht.
- `AuthProblems.LockedOut` liefert `account_locked` und einen `Retry-After`-Header. Der IP-Limiter liefert `too_many_requests`, aktuell mit pauschal 60 Sekunden.
- Das Frontend übersetzt diese Codes, übernimmt aber keine Zeitinformationen. Es zeigt nur „in einigen Minuten“ beziehungsweise „warte einen Moment“.
- Beim Backup ruft `VerifyIdentityAsync` nach einem fehlgeschlagenen `VerifyCurrentPasswordAsync` zusätzlich `RecordFailedAccessAsync` auf. Die Passwortprüfung zählt den Fehler bereits selbst: Ein falsches Passwort wird dadurch derzeit doppelt gezählt.
- Beim Erreichen der Fehlversuchsgrenze setzt der Store den Zähler wieder auf null und setzt das Sperrende. Eine nachträgliche Berechnung aus einem zuvor geladenen Account kann deshalb falsche Restversuche melden. Außerdem liefern manche Abläufe erst beim nächsten Request den Sperrfehler.

## Einstellungen

Im Tab **Allgemein** wird eine Karte **Anmeldeschutz** mit zwei Ganzzahlfeldern ergänzt:

| Einstellung | Technischer Vorschlag | Standard | Zulässiger Bereich |
| --- | --- | --- | --- |
| Geschützte Anfragen pro Minute und IP | `authProtection.requestsPerMinute` | 10 | 1 bis 1000 |
| Fehlversuche bis zur Kontosperre | `authProtection.maxFailedAttempts` | 5 | 1 bis 100 |

Die Grenzen sind Vorschläge für die Implementierung. Null, negative Werte, Bruchzahlen und Werte außerhalb dieser Bereiche werden mit feldbezogenen Fehlern abgelehnt. Der Schutz lässt sich über diese Felder nicht abschalten.

Die Karte erklärt die unterschiedlichen Zählweisen: Das Anfragelimit gilt gemeinsam für alle geschützten Auth-, Account- und Backup-Endpunkte pro IP. Das Fehlversuchslimit gilt gemeinsam für das Konto über alle entsprechenden Prüfungen hinweg. Die Dauer der Kontosperre bleibt fünf Minuten und wird in diesem Umfang nicht konfigurierbar.

- Einstellungen in `AppSettings` und `settings.json` persistieren; keine Speicherung dieser Konfiguration in `auth.db`.
- Die General-Settings-DTOs, Update-Verarbeitung, Validierung, Kopier-/Normalisierungslogik und Frontend-Modelle vollständig ergänzen. Auch andere Settings-Schreibwege müssen die neuen Werte erhalten.
- Bestehende Dateien und unterstützte Backups ohne `authProtection` erhalten 10 und 5 als Standardwerte. Ein fehlender Wert ist von einem ausdrücklich ungültigen Wert zu unterscheiden.
- Vorhandene Save-/Cancel-Aktionen, Schutz vor ungespeicherten Änderungen, Gestaltung und Übersetzungen für Deutsch, Englisch und Französisch verwenden.
- Änderungen gelten nach erfolgreichem Speichern ohne Neustart. Für laufende Requests gilt der zu deren Beginn erfasste konsistente Konfigurationsstand.
- Eine bereits aktive Kontosperre bleibt bis zu ihrem gespeicherten Ende bestehen, auch wenn der Grenzwert geändert wird. Das Speichern der Einstellungen setzt keine Zähler zurück.
- Bereits gezählte Fehlversuche bleiben erhalten. Bei einer Absenkung, die den bisherigen Zähler bereits erreicht oder unterschreitet, setzt die nächste geschützte Kontoprüfung vor der Passwortprüfung die Sperre. Reines Öffnen der Settings löst keine Sperre aus.

## Welche Eingaben zählen?

Der gemeinsame Fehlversuchszähler schützt die Identität des lokalen Kontos:

- Falsches Passwort beim Login für den vorhandenen Benutzernamen.
- Falsches aktuelles Kontopasswort beim Passwortwechsel.
- Falsches aktuelles Kontopasswort bei Backup-Export und verbindlichem Restore.
- Falsches aktuelles Kontopasswort beim Deaktivieren von 2FA oder Erneuern von Recovery-Codes.
- Falsche 2FA-/Recovery-Codes bei den bereits geschützten Identitätsprüfungen, einschließlich Login und Bestätigung einer 2FA-Einrichtung.

Eine fehlgeschlagene serverseitige Prüfung zählt genau einmal. Während einer aktiven Sperre oder einer Ablehnung durch das IP-Limit wird keine weitere Passwort-/Codeprüfung ausgeführt; diese Ablehnungen erhöhen den Fehlversuchszähler nicht und verlängern keine bestehende Sperre. Unvollständige oder fachlich ungültige Formulare zählen nicht als falsche Passwortprüfung.

Neue Passwörter, Passwortbestätigungen und ein neu gewähltes Backup-Passwort sind keine Prüfung einer bestehenden Identität. Abweichende Bestätigungen oder verletzte Passwortregeln bleiben Validierungsfehler.

**Separates Backup-Passwort:** Ein falsches Passwort zum Entschlüsseln einer importierten Backup-Datei bleibt ein Fehler `backup_invalid_password` und sperrt nicht das lokale Konto. Es prüft ein unabhängiges Archivpasswort. Der Import-Endpunkt unterliegt weiterhin dem konfigurierten IP-Anfragelimit und zeigt bei dessen Überschreitung dieselbe konkrete Wartezeit an. Diese Abgrenzung in UI-Texten und Tests festhalten.

Nach einer vollständig erfolgreichen Anmeldung beziehungsweise Identitätsprüfung wird der Kontozähler zurückgesetzt. Bei aktivierter 2FA reicht ein richtiges Passwort allein dafür nicht. Ein späterer fachlicher Fehler einer bereits autorisierten Aktion, etwa ein fehlgeschlagener Backup-Export, macht die erfolgreiche Identitätsprüfung nicht rückgängig. Verbrauchte Recovery-Codes behalten ihre bestehende Semantik.

## Backend: konsistente Prüfung und Fehlerantworten

### Gemeinsamer Prüfzustand

Die Konfiguration und Berechnung der Sperrzustände aus den Endpunkten in gemeinsam verwendete Services beziehungsweise Ergebnisse verlagern. Endpunkte übersetzen das Ergebnis in eine einheitliche Fehlerantwort.

- Die doppelte Zählung beim Backup entfernen.
- Den Store nach dem atomaren Registrieren eines Fehlversuchs den tatsächlich entstandenen Zustand zurückgeben lassen, beispielsweise Restversuche und Sperrende. Nicht aus einem veralteten `AccountRecord` oder durch Abziehen im Frontend ableiten.
- Bei konkurrierenden Prüfungen Zähler, Sperrbeginn und Rücksetzen konsistent behandeln. Sobald die Sperre aktiv ist, darf ein gleichzeitig bearbeiteter Request sie weder löschen noch verlängern.
- Der Request, der die Grenze erreicht, liefert sofort `account_locked`, nicht zunächst nochmals „Passwort falsch“.
- Alle relevanten Prüfwege müssen die gleichen Regeln verwenden. Bereits angemeldete Nutzer werden wegen eines falschen aktuellen Passworts oder einer temporären Sperre nicht durch einen allgemeinen 401-Handler ausgeloggt.
- `TimeProvider` auch für die Berechnung der Fehlerantworten verwenden. Die derzeitige direkte Verwendung von `DateTimeOffset.UtcNow` in `AuthProblems.LockedOut` ersetzen, damit Sperrprüfung und Restzeit dieselbe Uhr verwenden.

### Fehlervertrag

Bestehende ProblemDetails-Codes und die zur jeweiligen Aktion passenden Statuscodes erhalten. Zusätzliche typisierte Metadaten transportieren den Zustand:

| Feld | Bedeutung |
| --- | --- |
| `remainingAttempts` | Restversuche nach der fehlgeschlagenen Prüfung; bei aktiver Kontosperre 0 |
| `failureKind` | Fachliche Ursache, beispielsweise `password`, `second_factor` oder `backup_password` |
| `lockedUntil` | UTC-Endzeit einer aktiven Kontosperre |
| `retryAt` | UTC-Zeitpunkt, ab dem die gerade blockierte Aktion erneut versucht werden darf |
| `retryAfterSeconds` | Aufgerundete verbleibende Wartezeit |
| `serverTime` | Serverzeit bei Erzeugung der Antwort zur Einordnung der Endzeiten |

Metadaten nur ausgeben, wenn sie zur Antwort gehören. Bei falschen Login-Benutzernamen weiterhin den allgemeinen Anmeldefehler ohne Details zum vorhandenen Konto liefern. Auf dem bekannten Konto Restversuche erst bei zwei oder einem verbleibenden Versuch ausgeben. Bei weniger als drei konfigurierten Gesamtversuchen gelten dieselben Regeln unmittelbar.

- `account_locked`: HTTP 429, Sperrende, Restzeit, Restversuche 0 und `Retry-After` liefern.
- `too_many_requests`: HTTP 429 und die tatsächliche verbleibende Zeit des IP-Fensters liefern; keinen weiteren Passwortfehler registrieren. `Retry-After` und Antwortdaten müssen zusammenpassen.
- `invalid_credentials`, `current_password_incorrect` und die Backup-Identitätsfehler um die passenden Restversuche ergänzen. Fehler durch einen falschen zweiten Faktor dürfen nicht als falsches Passwort bezeichnet werden.
- Wenn beide Grenzen greifen, hat die zuerst durchgesetzte IP-Begrenzung Vorrang. Das Frontend darf aus einem IP-Fehler keine Aussage ableiten, dass keine Kontosperre besteht; beim nächsten zulässigen Request wird der dann maßgebliche Zustand neu geprüft.
- Fehlerantworten nicht cachen; weder Passwörter noch Codes in Metadaten, URLs oder Logs aufnehmen.

### Zur Laufzeit anpassbares IP-Limit

Die aktuelle Fixed-Window-Semantik und der gemeinsame IP-Schlüssel bleiben erhalten. Keine Einführung getrennter Budgets je Endpunkt oder eines gleitenden Zeitfensters.

Die jetzige einmalige Konfiguration eines Limits in `Program.cs` reicht für dynamische Änderungen nicht aus. Die Umsetzung muss den aktuellen Grenzwert pro Request berücksichtigen und laufende Zeitfenster mit bereits verbrauchten Anfragen erhalten. Gegebenenfalls einen gezielten Auth-Limiter mit begrenztem Speicher und Bereinigung abgelaufener IP-Einträge verwenden, statt bei jeder Settings-Änderung sämtliche Limiter neu anzulegen.

- Erhöhen des Limits gibt das zusätzliche Budget im laufenden Fenster frei; Senken berücksichtigt die bereits verbrauchten Anfragen.
- Abgelehnte Anfragen verlängern das Fenster nicht. Nach Ablauf beginnt das nächste Fenster mit dem dann aktuellen Limit.
- Die Restzeit aus dem tatsächlichen Fensterende bestimmen; keine pauschalen 60 Sekunden bei jeder Ablehnung zurückgeben.
- Die bestehende Behandlung vertrauenswürdiger Proxys erhalten; keine ungeprüften Forwarded-Header als IP-Schlüssel verwenden.
- Neustarts leeren weiterhin die IP-Zähler im Arbeitsspeicher. Persistente Kontosperren bleiben in `auth.db` erhalten.

## Frontend: Hinweise und Countdown

### Betroffene Oberflächen

1. Login einschließlich 2FA- und Recovery-Code-Schritt.
2. Konto: Passwort ändern sowie weitere Formulare mit aktuellem Kontopasswort, insbesondere 2FA deaktivieren und Recovery-Codes erneuern; 2FA-Bestätigung berücksichtigt denselben Sperrzustand.
3. Backup: Exportformular, Bestätigung des Restores und Importprüfung bei einem IP-Limit. Beim Import bleibt ein falsches Archivpasswort als eigener Fehler erkennbar.
4. Setup erhält bei einem überschrittenen IP-Limit ebenfalls die konkrete Wartezeit; es benötigt keine Kontofehlversuchswarnung vor Erstellung eines Kontos.

### Anzeigeverhalten

- Nach falscher Eingabe mit zwei Restversuchen: „Das Passwort ist falsch. Noch 2 Versuche bis zur Kontosperre.“
- Bei einem Restversuch entsprechend Singular verwenden. Bei falschem zweiten Faktor den Code als Ursache nennen.
- Bei Kontosperre: „Dein Konto ist wegen zu vieler fehlgeschlagener Anmelde- oder Bestätigungsversuche gesperrt. Auch mit dem richtigen Passwort ist diese Aktion bis dahin nicht möglich. Erneut versuchen in 04:32.“ Zusätzlich den lokalen Endzeitpunkt anzeigen.
- Bei IP-Limit: „Zu viele Anfragen von deiner IP-Adresse. Erneut versuchen in 00:27.“ Erklären, dass diese Grenze unabhängig von der Richtigkeit des Passworts gilt.
- Meldungen direkt am betroffenen Formular anzeigen. Sperrhinweise nicht nach wenigen Sekunden durch allgemeine Feedback-Timer ausblenden.
- Betroffene Absendeaktionen während der bekannten Sperrzeit deaktivieren. Eingaben bleiben bearbeitbar; die Seite bleibt navigierbar. Keine automatischen Anmeldeversuche starten.
- Restzeit aus einem Endzeitpunkt ableiten und mindestens sekündlich aktualisieren. Nach inaktiven Browser-Tabs und Zeitsprüngen neu berechnen, statt Sekunden lokal herunterzuzählen. Serverseitige relative Wartezeit mit Empfangszeit verankern, damit abweichende Browseruhren die Sperrdauer nicht verfälschen.
- Nach Ablauf den Hinweis auf abgelaufene Wartezeit aktualisieren und die Aktion wieder freigeben. Der Server entscheidet beim nächsten Request, ob die Aktion tatsächlich zulässig ist.
- Bekannte Kontosperren gelten innerhalb der laufenden App auch in anderen betroffenen Formularen. Bekannte IP-Wartezeiten gelten für die gemeinsam begrenzten Aktionen. Diese beiden Zustände getrennt verwalten.
- Ein Neuladen darf keinen zusätzlichen Anmeldeversuch auslösen. Ohne bekannte Fehlerdaten wird der Zustand spätestens mit der nächsten Nutzeraktion erneut vom Server geliefert; dafür keinen anonymen Endpoint zur Abfrage beliebiger Kontosperren einführen.
- Statische Meldung mit zugänglicher Status-/Fehlerausgabe versehen; Screenreader nicht jede Sekunde mit dem Countdown unterbrechen.
- Deutsche, englische und französische Texte einschließlich Singular/Plural ergänzen.

### Gemeinsame Fehlerverarbeitung

`AuthRequestError` und die ProblemDetails-Auswertung um die Metadaten erweitern. Für Login, Account und Backup dieselbe Berechnung der Meldungen und Wartezeiten verwenden. Auch als Blob gelieferte Fehler beim Backup-Export müssen die Informationen aus Antwort-Body und `Retry-After` erhalten.

Metadaten validieren: fehlende oder ungültige Zeiten dürfen keinen endlosen Countdown erzeugen. Bei älteren Antworten ohne Metadaten bleibt ein verständlicher allgemeiner Sperrhinweis als Fallback. Sperren aus Account- und Backup-Requests dürfen keine unbeabsichtigte Abmeldung auslösen.

## Backup und Wiederherstellung

Die beiden Einstellungen sind Teil von `settings.json` und werden mit dem bestehenden Backup erfasst. Konto, Fehlversuchszähler und aktive Kontosperren bleiben auf der Zielinstallation; `auth.db` und Data-Protection-Schlüssel bleiben ausgeschlossen. Importprüfung und Settings-Migration müssen die neuen Felder prüfen beziehungsweise bei fehlenden Feldern die Standards ergänzen. Nach dem Restore gelten die importierten Grenzwerte; eine bereits aktive Kontosperre der Zielinstallation wird nicht aufgehoben.

## Umsetzungsschritte

1. Settings-Modell, Standards, Validierung und General-Settings-API ergänzen; Erhalt der Werte über alle Settings-Schreibwege sicherstellen.
2. Gemeinsame Konfigurationsversorgung und atomare Ergebnisse für Fehlversuche und Sperren umsetzen; doppelte Backup-Zählung beseitigen und erfolgreiche Identitätsprüfungen konsistent behandeln.
3. Den IP-Limiter für Änderungen zur Laufzeit und die Rückgabe tatsächlicher Wartezeiten anpassen.
4. Einheitliche ProblemDetails-Metadaten in Auth-, Account- und Backup-Abläufe integrieren.
5. Settings-Karte und gemeinsame Frontend-Verarbeitung für Restversuche, Sperrgrund und Countdown implementieren; alle genannten Formulare anbinden.
6. Übersetzungen, Backup-Kompatibilität und Deployment-Dokumentation ergänzen.
7. Gezielte Tests und anschließend die bestehenden Backend- und Frontend-Prüfungen sowie Angular-Build, Linting und visuelle Kontrolle ausführen.

## Abnahmekriterien und Tests

- Ohne neue Settings gelten weiterhin zehn geschützte Anfragen pro Minute und fünf Fehlversuche; die Kontosperre dauert fünf Minuten.
- Beide Felder lassen sich speichern und nach einem Neustart wieder laden. Andere Settings-Aktionen überschreiben sie nicht. Ungültige Eingaben verändern den gespeicherten Zustand nicht.
- Änderungen werden ohne Neustart wirksam. Erhöhen und Senken beider Grenzwerte bei bereits vorhandenen Zählern und aktiven Sperren folgen den festgelegten Regeln.
- Bei Standardwerten erzeugt der dritte falsche Versuch die Warnung „noch 2“, der vierte „noch 1“ und der fünfte unmittelbar die Sperrmeldung. Dies funktioniert bei Login, Passwortänderung und beiden Backup-Identitätsprüfungen.
- Beim Backup zählt ein falsches Kontopasswort genau einmal. Ein falsches Archivpasswort bleibt ein Archivfehler und verändert den Kontozähler nicht.
- Der Zähler ist über die Prüfwege hinweg gemeinsam: Fehler im Login und anschließend im Backup ergeben dieselbe verbleibende Versuchsanzahl. Erfolgreiche vollständige Prüfungen setzen ihn wie festgelegt zurück.
- Falsche 2FA-/Recovery-Codes verwenden dieselben Sperrregeln und passende Meldungen. Ein falsches Passwort verbraucht keinen Recovery-Code.
- Eine aktive Kontosperre blockiert auch richtige Eingaben; Ursache, Endzeit und ablaufende Restzeit erscheinen. Wiederholtes Absenden verlängert die Sperre nicht.
- Das IP-Limit gilt gemeinsam für geschützte Endpunkte. Ein abgelehnter Request führt keine Identitätsprüfung aus und verändert keinen Kontofehlversuchszähler. Die tatsächlich verbleibende Wartezeit wird angezeigt.
- Konkurrierende Requests verursachen weder doppelte Zählung noch verlorene Fehlversuche, negative Restwerte oder das unbeabsichtigte Aufheben einer Sperre.
- Tests mit `TimeProvider` decken Fensterende, Sperrende, Einstellungsänderungen und das Zusammentreffen beider Grenzen ohne reale Wartezeiten ab.
- Frontend-Tests prüfen Metadaten-Auswertung, Countdown, fehlende Metadaten, Bearbeitbarkeit der Felder, deaktivierte Absendeaktionen, Freigabe nach Ablauf und das Ausbleiben automatischer Requests. Backup-Blob-Fehler behalten sämtliche Sperrinformationen.
- Unbekannte Benutzernamen verändern nicht den Kontozähler und liefern keine kontospezifischen Metadaten. Falsche aktuelle Passwörter und HTTP 429 in angemeldeten Formularen lösen keine automatische Abmeldung aus.
- Backup mit und ohne neue Settings-Felder funktioniert gemäß Versionsregeln. Ein Restore verändert weder Konto noch aktive Kontosperre der Zielinstallation.
- Die Hinweise sind in Deutsch, Englisch und Französisch verständlich, mit Tastatur und Screenreader nutzbar und in beiden Themes sowie auf schmalen Ansichten lesbar.

## Relevante Projektstellen

- Backend: `Program.cs`, `AccountService.cs`, `TwoFactorService.cs`, `IAccountStore.cs`, `SqliteAccountStore.cs`, `SqliteAccountStore.Security.cs`, `AuthProblems.cs`, `AuthEndpoints.cs`, `AccountEndpoints.cs` und `BackupEndpoints.cs`.
- Settings: `AppSettings.cs`, `GeneralSettingsDto.cs`, `GeneralSettingsUpdateRequest.cs`, `AppSettingsValidator`, `JsonSettingsService.cs`, `SettingsEndpoints.cs` sowie die zugehörigen Frontend-Modelle und Services.
- Frontend: `auth-error.util.ts`, `auth.service.ts`, `account.service.ts`, `backup.service.ts`, `login.page`, `settings-account-tab.widget`, `settings-backup-tab.widget`, `settings-general-tab.widget` und die bestehenden Sprachdateien.
- Tests: vorhandene Auth-/2FA-API-Tests, `BackupIdentityApiTests`, Settings-Tests und die bestehenden Specs der betroffenen Frontend-Services und Formulare.
- Dokumentation: `docs/BACKUP_PLAN.md` und `docs/DEPLOYMENT.md`.
