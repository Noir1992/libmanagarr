# Plan für Backup Export und Import

LibManagarr erhält eine Backup-Funktion im UI, mit der sich Einstellungen und Anwendungsdaten herunterladen und auf derselben oder einer anderen Installation wiederherstellen lassen. Die erste Version umfasst manuelle Exporte und einen geprüften Import mit Sicherheitskopie und Neustart. Das Konto der Zielinstallation bleibt erhalten.

Die folgenden Abläufe und technischen Entscheidungen bilden den vorgeschlagenen Implementierungsumfang. Automatische, regelmäßig geplante Backups sind eine spätere Erweiterung.

## Umfang der Sicherung

Ein Backup enthält die Einstellungen und Anwendungsdaten aus dem konfigurierten Config-Verzeichnis. Kontodaten sind ausgeschlossen.

| Bestandteil | Inhalt |
| --- | --- |
| `settings.json` | Allgemeine Einstellungen, Instanzen, API-Keys und weitere gespeicherte Konfiguration |
| `libmanagarr.db` | Library-Snapshots, Filterwerte, Statistik- und Änderungshistorie sowie Task-Konfiguration und Ausführungszustand |
| Manifest | Backup-Formatversion, App-Version, Erstellungszeitpunkt, Datenbankschema-Versionen und Dateiprüfsummen |

`auth.db` und `data-protection-keys/` sind ausdrücklich ausgeschlossen. Benutzerkonto, Passwort-Hash, 2FA-Secrets, Recovery-Codes, Sitzungen und offene Anmeldevorgänge werden weder exportiert noch beim Import ersetzt. Das Konto und der Schlüsselsatz der Zielinstallation bleiben erhalten.

Nicht enthalten sind Filme und Serien, Logs, Poster-Cache, Kalender-Cache, bereits vorhandene Backup-Archive, temporäre Dateien und SQLite-WAL-/SHM-Dateien. Die Datenbankkopien enthalten die bereits bestätigten Änderungen aus dem WAL. Docker-Compose-Dateien, Umgebungsvariablen und Reverse-Proxy-Einstellungen liegen außerhalb dieses Anwendungsbackups und werden nicht verändert.

Die Archiv-Pfadprüfung erlaubt ausschließlich `settings.json`, `libmanagarr.db` und `manifest.json`. Archive mit Kontodaten oder Data-Protection-Schlüsseln werden abgewiesen. Eine Bereinigung von Auth-Sitzungen als Teil der Wiederherstellung entfällt.

### Abgrenzung zur Kalenderansicht

Die in [CALENDAR_PLAN.md](CALENDAR_PLAN.md) geplante Kalenderansicht lädt Releases und Dateistatus direkt aus Sonarr und Radarr. Diese Antworten, der kurzlebige Kalender-Cache und daraus berechnete zeitabhängige Status gehören nicht zum Backup. Die benötigten Instanzkonfigurationen und API-Keys sind bereits über `settings.json` enthalten. Für die Sicherung werden keine Kalenderabfragen benötigt.

Falls Kalender-Einstellungen wie Ansicht, Filter oder Film-Release-Auswahl später dauerhaft gespeichert werden, sollen sie in `settings.json` liegen und damit Teil der Sicherung sein. Die Kalenderplanung verlangt bisher keine solche Speicherung. Nur im Browser gespeicherte Anzeigepräferenzen werden nicht gesichert.

Erweiterungen dauerhaft gespeicherter Library-Snapshots um Release-Daten bleiben Bestandteil von `libmanagarr.db` und unterliegen der unten beschriebenen Versionskompatibilität. Änderungen ausschließlich an Kalender-API-Modellen erfordern keine Erweiterung des Backup-Formats.

## UI in den Einstellungen

Ein neuer Tab **Backup** in den Settings erhält zwei Bereiche: **Exportieren** und **Importieren**. Ein direkter Aufruf ist über `/settings?tab=backup` möglich. Gestaltung, Feedback-Anzeigen und Übersetzungen folgen den bestehenden Settings-Tabs.

### Exportieren

1. Der Benutzer wählt „Backup erstellen“.
2. Ein Dialog fragt ein eigenes Backup-Passwort mit Bestätigung ab und beschreibt kurz den enthaltenen Umfang. Das Passwort wird nicht gespeichert und kann von LibManagarr nicht wiederhergestellt werden.
3. Der Benutzer bestätigt seine aktuelle Identität mit dem Kontopasswort und, bei aktivierter 2FA, dem vorhandenen zweiten Faktor. Vorhandene Prüfungen aus dem Account-Bereich werden wiederverwendet.
4. Während der Erstellung zeigt das UI den Fortschrittszustand. Mehrfachklicks starten keine parallelen Exporte.
5. Das verschlüsselte Archiv wird als Datei heruntergeladen, beispielsweise `libmanagarr-backup-2026-10-31-093000.lmbak`.
6. Das UI bestätigt die Erstellung und die Übergabe an den Browser. Es behauptet nicht, dass der Browser den Download bereits dauerhaft gespeichert hat.

Das Exportformular benötigt keine Auswahl einzelner Datenbereiche. Ein vollständiges Backup ist das Standardformat der ersten Version.

### Importieren und prüfen

1. Der Benutzer wählt eine Backup-Datei aus und gibt deren Backup-Passwort ein.
2. LibManagarr lädt sie in einen begrenzten, geschützten temporären Bereich und prüft sie vollständig.
3. Das UI zeigt eine Vorschau mit App-Version, Backup-Datum, enthaltenen Datenbereichen, Instanzanzahl und Kompatibilität. Zugangsdaten und Schlüssel erscheinen nicht in der Vorschau.
4. Der Dialog erklärt konkret: Einstellungen und Anwendungsdaten werden ersetzt; Konto, Passwort und 2FA der Zielinstallation bleiben erhalten. Die aktuell laufende App-Version wird durch den Import nicht geändert.
5. Erst „Wiederherstellen und neu starten“ übernimmt den Import. Dafür ist eine erneute Identitätsprüfung gegen das aktuell aktive Konto erforderlich. Die Vorprüfung allein verändert keine produktiven Daten.

Das Backup-Passwort entschlüsselt die Datei; das aktuelle Kontopasswort autorisiert die Änderung an der laufenden Installation. Beide Zwecke bleiben getrennt.

Bei falschem Passwort, beschädigter Datei oder inkompatibler Version bleibt der aktuelle Zustand erhalten. Das UI zeigt einen verständlichen Fehler und erlaubt eine neue Auswahl.

### Nach der Bestätigung

Das UI zeigt die Vorbereitung und anschließend den automatischen Neustart. Es fragt den authentifizierten Operationsstatus einmal pro Sekunde ab und wertet nur den Abschluss des zugehörigen Restore-Auftrags aus. Verbindungsfehler während des Neustarts sind erwartbar. Nach erfolgreicher Wiederherstellung oder Rücknahme lädt es das Frontend neu, damit alte Settings- und Daten-Caches verworfen werden, und zeigt das Ergebnis. Die bestehende Anmeldung bleibt erhalten.

Der Neustart wird von der Anwendung selbst ausgeführt: Den vollständigen Backend-Host stoppen und entsorgen, gepoolte SQLite-Verbindungen schließen und anschließend einen neuen Host im selben Prozess aufbauen. Dadurch funktioniert der Ablauf unter Rider, bei direktem .NET-Start und in Docker Compose ohne einen externen Neustartmechanismus. Alle Services und Caches werden neu erstellt. Ein gewöhnliches Beenden der Anwendung löst keinen Neustart aus. `restart: unless-stopped` bleibt als Docker-Prozessaufsicht bestehen.

Wenn nach zwei Minuten noch kein Abschluss vorliegt, zeigt das UI einen Hinweis auf die längere Dauer und die Backend-Protokolle sowie eine Aktion zum erneuten Prüfen der Verbindung. Diese Aktion wiederholt niemals den Import. Der Status enthält die Operation-ID, damit ein früherer Restore-Erfolg nicht als Abschluss des aktuellen Auftrags erscheint.

### Umzug auf eine neue Installation

Auf dem Ziel wird zunächst das gewünschte Konto über den vorhandenen Setup-Prozess eingerichtet. Nach Anmeldung kann das Backup importiert werden; dieses Konto bleibt einschließlich Passwort und gegebenenfalls aktivierter 2FA erhalten. So funktioniert der Umzug ohne einen zusätzlichen anonym erreichbaren Import-Endpunkt.

## Verschlüsselung und Archivformat

Alle UI-Exporte sind passwortverschlüsselt und gegen Veränderungen authentifiziert, da das Backup API-Keys enthält. Die Verschlüsselung muss mit dem Backup-Passwort auf einer neuen Installation funktionieren und darf nicht vom Data-Protection-Schlüsselsatz der Quellinstallation abhängen.

Vorgesehen ist ein versioniertes Dateiformat mit komprimiertem Inhalt und einer etablierten Bibliothek für passwortbasierte, authentifizierte Verschlüsselung. Die konkrete Bibliothek und das Format werden vor der Implementierung auf .NET-Kompatibilität, Wartungszustand, Lizenz und Unterstützung großer Dateien geprüft. Es werden keine eigenen kryptografischen Verfahren implementiert.

Die Formatdefinition legt vor dem ersten Release verbindlich fest:

- Dateikennung und Formatversion.
- Verschlüsselungsverfahren sowie Algorithmus und Parameter der Passwortableitung.
- Zufälligen Salt und eindeutige Nonces nach Vorgaben der verwendeten Bibliothek.
- Authentifizierung des Headers und des gesamten Inhalts.
- Interne Pfade, erlaubte Dateien und Prüfsummen im Manifest.
- Lese- und Schreibgrenzen für Header, Dateien und Gesamtarchiv.

Das Manifest liegt im verschlüsselten Inhalt. Unverschlüsselte Header enthalten nur die zum Öffnen und zur Formatidentifikation notwendigen Angaben. Prüfsummen dienen der Konsistenzprüfung; die Manipulationserkennung übernimmt die authentifizierte Verschlüsselung.

Backups werden gestreamt oder über temporäre Dateien verarbeitet. Große Library-Datenbanken dürfen nicht vollständig im Arbeitsspeicher liegen. Weder Passwörter noch entschlüsselte Inhalte gelangen in Browser-Speicher, URLs oder Anwendungslogs.

## Konsistenter Export

Der vorhandene Migrationscode nutzt bereits `SqliteConnection.BackupDatabase` für `libmanagarr.db`. Diese Technik wird für die Library-Datenbank in einem eigenen Backup-Service wiederverwendet. Live-Datenbankdateien werden nicht einfach während laufender Schreibzugriffe kopiert. Microsoft.Data.Sqlite stellt dafür eine unterstützte [Online-Backup-Funktion](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup) bereit.

Ein Backup-Koordinator sorgt für einen zusammengehörigen Stand von Einstellungen, Library-Daten und Tasks:

1. Exklusive Backup-Operation reservieren; Export und Import dürfen nicht überlappen.
2. Neue schreibende Aktionen und neue Task-Ausführungen vorübergehend zurückhalten. Laufende Sync-, Settings- und Account-Operationen kontrolliert abschließen lassen.
3. Eine zeitlich begrenzte, konsistente Aufnahmephase starten. Reihenfolge und Sperren werden so definiert, dass kein Task auf eine Sperre wartet, während der Export auf denselben Task wartet.
4. Die Library-Datenbank über die Backup-API in ein geschütztes temporäres Verzeichnis sichern und die zugehörigen Einstellungen aufnehmen. `auth.db` und Data-Protection-Schlüssel werden nicht gelesen oder kopiert.
5. Schreibzugriffe wieder freigeben, sobald die konsistenten Kopien vorliegen.
6. Manifest erzeugen, Archiv verschlüsseln und zum Download bereitstellen.
7. Temporäre Dateien bei Erfolg, Fehler, Abbruch und Prozessneustart bereinigen.

Eine globale Sperre nur im Backup-Service reicht dafür nicht: Alle relevanten Writer und der Scheduler müssen an der Koordination teilnehmen. Lesende Requests können weiterlaufen, soweit sie den Aufnahmevorgang nicht behindern. Das Loggen des Backup-Fortschritts darf keine Sperrenrekursion verursachen.

Kalenderabfragen und die Aktualisierung ihres kurzlebigen Caches benötigen keine gemeinsame Aufnahme mit den gesicherten Daten. Falls die Kalenderfunktion später Einstellungen oder Library-Daten dauerhaft schreibt, nehmen diese Schreibvorgänge an derselben Writer-Koordination teil.

## Importprüfung

Der Upload wird erst nach erfolgreicher Authentifizierung und CSRF-Prüfung angenommen. Die Importprüfung arbeitet ausschließlich im Staging-Verzeichnis.

- Dateikennung, Formatversion und Verschlüsselung prüfen; nicht allein Dateiendung oder MIME-Type vertrauen.
- Upload-Größe, entschlüsselte Gesamtgröße, Dateianzahl, Kompressionsverhältnis und KDF-Aufwand begrenzen.
- Nur erlaubte Archivpfade übernehmen. Absolute Pfade, `..`, Symlinks, doppelte Einträge und unerwartete Dateien ablehnen.
- Manifest, erwartete Bestandteile und Dateiprüfsummen überprüfen.
- `settings.json` deserialisieren und fachlich prüfen, ohne die produktive Settings-API aufzurufen oder Live-Dateien zu normalisieren.
- SQLite-Integrität, erwartete Tabellen und die tatsächlichen Schema-Versionen mit dem Manifest abgleichen. Unbekannte Schemas ablehnen.
- Verfügbaren Speicherplatz für Staging, Sicherheitskopie und Übernahme kontrollieren.

Ergebnis ist eine zeitlich begrenzte Import-ID, die an die aktuelle Sitzung gebunden ist. Die bestätigte Übernahme verwendet genau die zuvor geprüften Dateien; ein Austausch nach der Vorschau wird verhindert. Ein abgelaufener oder bereits verwendeter Import muss erneut geprüft werden.

### Versionskompatibilität

Backup-Format und Anwendungsversion werden getrennt versioniert. Entscheidend sind unterstütztes Format, Settings-Struktur und Library-Datenbankschema. Formatversion 2 enthält keine Kontodaten. Die bisherige Formatversion 1 mit Kontodaten wird ausdrücklich abgelehnt; vor einem Umzug ist ein neues Backup zu erstellen. Auch noch vorbereitete Restore-Aufträge mit Auth-Dateien oder Schlüsselverzeichnissen dürfen nicht übernommen werden und müssen vor dem Start geklärt werden.

Backups aus derselben App-Version müssen unterstützt werden. Ältere Stände sind nur dann importierbar, wenn ein ausdrücklich unterstützter Migrationspfad existiert. Dessen Migration und Prüfung erfolgen zunächst auf den Staging-Kopien. Neuere, nicht unterstützte Schemas werden vor jeder Übernahme abgelehnt. Es gibt kein stilles Downgrade und keinen Start mit automatisch geleerten Datenbanken bei einem Fehler.

Das gilt auch für spätere Kalender-Einstellungen und zusätzliche Release-Felder in gespeicherten Library-Snapshots. Unterstützte Migrationen müssen fehlende neue Einstellungen mit definierten Standardwerten ergänzen und vorhandene Release-Daten erhalten. Reine Datumsangaben und UTC-Zeitstempel bleiben dabei unterscheidbar; unbekannte Uhrzeiten werden nicht zu Mitternacht umgewandelt. Änderungen an persistierten Snapshot-Strukturen müssen auch dann in der Kompatibilitätsprüfung berücksichtigt werden, wenn sie keine neue SQLite-Tabellenstruktur benötigen.

## Wiederherstellung mit Neustart und Rückfall

Produktive Einstellungen und die Library-Datenbank werden nicht während des normalen HTTP-Betriebs ersetzt. Bestehende Settings-Caches und SQLite-Verbindungen würden sonst mit unterschiedlichen Datenständen weiterarbeiten. Auth-Datenbank und Data-Protection-Schlüssel bleiben unverändert.

### Vorbereitung in der laufenden Anwendung

1. Identitätsprüfung, Import-ID, unveränderte Staging-Dateien und freien Speicher erneut prüfen.
2. Wartungsmodus aktivieren. Neue Tasks und schreibende Requests sperren; laufende Operationen kontrolliert beenden lassen. Bei einem Timeout keine Übernahme beginnen.
3. Eine konsistente Sicherheitskopie der zu ersetzenden Einstellungen und Library-Daten erstellen. Diese bleibt lokal mit restriktiven Dateirechten für einen Rollback verfügbar. Eine unverschlüsselte Kopie verlässt das Config-Verzeichnis nicht; ihre Aufbewahrung ist ausdrücklich begrenzt.
4. Geprüfte Importdateien im Config-Verzeichnis dauerhaft für den Neustart bereitstellen. Der Speicherort liegt auf demselben Dateisystem wie die Zieldateien.
5. Ein dauerhaftes Restore-Journal mit Operation-ID und Zustand schreiben. Der Staging- und Rollback-Bereich wird gegen Manipulation geschützt und beim Start erneut geprüft.
6. Den Auftrag als angenommen bestätigen und erst nach Ausgabe der HTTP-Antwort den automatischen Host-Neustart anfordern. Laufende Requests und Hintergrunddienste beenden, den Host vollständig entsorgen und die SQLite-Pools schließen. Erst danach darf der neue Host die Dateien übernehmen.

### Übernahme vor dem normalen Start

Ein früher Bootstrap-Schritt läuft vor Datenbankmigrationen, Settings-Ladevorgängen, dem produktiven Data-Protection-Provider und dem Scheduler. Er verarbeitet einen vorbereiteten Restore-Auftrag ausschließlich lokal.

1. Restore-Journal und Staging-Bestand prüfen.
2. Die alte Library-Datenbank samt WAL-/SHM-Zustand in den Rollback-Bereich übernehmen. Keine alten Sidecars mit der importierten Datenbank kombinieren. Auth-Dateien bleiben unberührt.
3. Einstellungen und Library-Datenbank als zusammengehörigen Zustand übernehmen. Konto, Sitzungen, Recovery-Codes und Schlüsselsatz der Zielinstallation werden nicht verändert.
4. Library-Datenbank erneut prüfen. Erst danach den regulären Start erlauben.
5. Nach erfolgreicher Initialisierung den Restore als abgeschlossen markieren und den Staging-Bereich entfernen. Die Sicherheitskopie bleibt für den dokumentierten Zeitraum erhalten.

Das Ersetzen mehrerer Dateien ist als Gesamtvorgang nicht atomar. Das Restore-Journal macht den Ablauf daher nach einem Absturz fortsetzbar oder vollständig rücknehmbar. Fehler vor dem erfolgreichen Abschluss stellen den gesamten vorherigen Zustand wieder her. Schlägt auch die Rücknahme fehl, startet die Anwendung keine leere Installation, sondern beendet sich mit einer konkreten Wiederherstellungsanleitung.

Gespeicherte Task-Termine werden nach dem Restore gemäß ihrer bestehenden Regeln behandelt: Cleanup überspringt verpasste Termine, der Sync kann fällige Arbeit nachholen. Der Import selbst startet keinen zusätzlichen Sync.

Nach dem Neustart beginnt der Kalender mit leerem Cache. Beim nächsten Öffnen lädt er die Releases und den Dateistatus aus den wiederhergestellten Instanzkonfigurationen neu und berechnet zeitabhängige Status anhand der aktuellen Zeit. Die Kalenderanzeige entspricht damit dem aktuellen Stand der Quellen und nicht dem Sicherungszeitpunkt; gesicherte Library-Snapshots können bis zum nächsten regulären Sync einen älteren Stand zeigen. Nicht erreichbare Quellen erscheinen gemäß Kalender-Plan als Quellenfehler. Ihre Erreichbarkeit ist keine Voraussetzung für einen erfolgreichen Restore.

Recovery-Codes der Zielinstallation behalten ihren aktuellen Verbrauchszustand. Durch einen Import werden keine alten Codes reaktiviert; ein Hinweis zur Erneuerung wegen des Restores entfällt.

## Backend und API

Vorgeschlagene Komponenten:

- `BackupService` für konsistente Aufnahmen und Export.
- `BackupArchiveService` für Format, Manifest, Verschlüsselung und Prüfung.
- `BackupOperationCoordinator` für Writer-Abstimmung, Wartungsmodus und parallele Operationen.
- `RestoreService` für Vorprüfung, Vorschau und Vorbereitung.
- Ein früher `RestoreBootstrapper` für Übernahme und Rollback beim Start.
- `BackupEndpoints` für den authentifizierten UI-Zugriff.

Vorgeschlagene API:

| Route | Zweck |
| --- | --- |
| `POST /api/backups/export` | Export autorisieren, erstellen und als Download ausgeben |
| `POST /api/backups/import/validate` | Datei und Backup-Passwort annehmen; Vorschau und Import-ID liefern |
| `POST /api/backups/import/{id}/apply` | Import nach erneuter Autorisierung verbindlich vorbereiten |
| `DELETE /api/backups/import/{id}` | Vorbereiteten, noch nicht bestätigten Import verwerfen |
| `GET /api/backups/status` | Zustand der aktuellen Operation für das angemeldete UI liefern |

Ändernde Requests nutzen den bestehenden CSRF-Schutz; Authentifizierungsprüfungen erhalten Rate-Limits. Backup-Antworten sind nicht cachebar. Geheimnisse werden ausschließlich im Request-Body übertragen. Fehler erhalten stabile Codes und übersetzbare Meldungen.

Vor der verbindlichen Übernahme bleibt Abbrechen möglich. Sobald das Restore-Journal zur Übernahme freigegeben ist, wird der Vorgang serverseitig abgeschlossen oder zurückgenommen, auch wenn der Browser geschlossen wird.

## Frontend und Dokumentation

- Backup-Tab, Importvorschau und konkrete Bestätigungsdialoge ergänzen.
- Fortschritts-, Fehler- und Neustartzustände anzeigen; konkurrierende Aktionen deaktivieren.
- Bei ungespeicherten Settings vor dem Export den vorhandenen Schutz gegen ungespeicherte Änderungen berücksichtigen. Das Backup enthält nur den gespeicherten Zustand.
- Backup-Passwort und Kontonachweis getrennt beschriften; vorhandene Passwort-Anzeige-Controls wiederverwenden.
- Übersetzungen für Deutsch, Englisch und Französisch ergänzen.
- Deployment-Dokumentation um Archivumfang, Umzug, Neustartvoraussetzungen, Versionskompatibilität und manuelle Rollback-Anleitung erweitern.
- Dokumentieren, dass das Backup-Passwort benötigt wird und dass Konto, Kontopasswort und gegebenenfalls Authenticator der Zielinstallation nach dem Import weiter gelten.
- Den Ausschluss des Kalender-Caches und das erneute Laden aktueller Kalenderdaten nach dem Restore im dokumentierten Backup-Umfang erläutern.

## Umsetzungsschritte

1. Archivformat, Bibliothek, Größenlimits, Versionsmatrix und Aufbewahrungsdauer der Sicherheitskopie festlegen.
2. Gemeinsame Koordination für Writer, Scheduler und Wartungsmodus entwerfen und integrieren.
3. Konsistente Datenbank-/Settings-Aufnahme und Export implementieren.
4. Archivprüfung, Staging und Importvorschau umsetzen.
5. Restore-Journal, Sicherheitskopie, frühen Bootstrap und Rollback implementieren.
6. API mit Authentifizierung, CSRF-Schutz und Operationsstatus ergänzen.
7. Backup-Tab mit Export, Importprüfung und Bestätigung integrieren.
8. Neustartablauf, Dokumentation und gezielte Integrationsprüfungen abschließen.

## Abnahmekriterien und Tests

- Export und Import funktionieren für eine konfigurierte Installation mit Library-Daten, Historie, mehreren Instanzen und Task-Zuständen.
- Ein Backup lässt sich nach Einrichtung eines Kontos auf einer neuen Installation wiederherstellen. Konto, Passwort, aktivierte 2FA, Recovery-Codes und Schlüssel der Zielinstallation bleiben bei erfolgreichem Restore und Rollback unverändert.
- Kalenderantworten, Kalender-Cache und berechnete Kalenderstatus werden nicht exportiert. Nach dem Restore lädt die Kalenderseite beim Öffnen aktuelle Daten aus den wiederhergestellten Instanzen; alte Cache-Einträge werden nicht verwendet. Ein Restore gelingt auch bei nicht erreichbaren Arr-Instanzen, deren Ausfall anschließend als Quellenfehler angezeigt wird.
- Falls dauerhaft gespeicherte Kalender-Einstellungen oder zusätzliche Release-Felder umgesetzt sind, bleiben sie bei Export und Import erhalten. Unterstützte ältere Backups erhalten definierte Standardwerte; reine Datumsangaben und unbekannte Uhrzeiten bleiben bei Migrationen korrekt erhalten.
- API-Keys und Dateiinhalte sind ohne Backup-Passwort nicht lesbar; falsches Passwort oder Manipulation wird vor einer Übernahme erkannt.
- Export unter parallelem Sync, Settings-Speichern und Account-Änderungen liefert einen konsistenten Zustand ohne Deadlocks. SQLite-WAL-Daten gehen nicht verloren.
- `auth.db` und Data-Protection-Schlüssel sind weder im Archiv noch in den Restore-Komponenten enthalten. Export und Importprüfung funktionieren ohne Zugriff auf diese Dateien; die API erfordert weiterhin die bestehende Authentifizierung und Identitätsprüfung. Alte Archive mit Kontodaten und alte Restore-Aufträge mit Auth-Komponenten werden abgewiesen.
- Ungültige Archive, Pfadmanipulationen, übergroße Dateien, beschädigte Datenbanken und unbekannte Versionen verändern keine produktiven Daten.
- Vorprüfung und Abbruch lassen den laufenden Zustand unverändert; Import-IDs sind sitzungsgebunden, einmalig und zeitlich begrenzt.
- Ein Fehler oder Prozessabsturz an jeder Übernahmestufe führt nach dem Neustart zu einem vollständigen Import oder einem vollständigen Rollback; Mischzustände werden nicht freigegeben.
- Fehlender Speicherplatz, eine nicht erstellbare Sicherheitskopie oder nicht beendbare Tasks verhindern den Import vor dem Ersetzen von Daten.
- Der Host startet nach einer Restore-Bestätigung automatisch mit dem importierten Stand, auch ohne Prozessaufsicht. Wiederholte Restores im selben Prozess funktionieren; normales Beenden startet den Host nicht neu.
- Das Frontend zeigt während des Neustarts Fortschritt und anschließend Erfolg oder Rollback an. Statusantworten anderer Restore-Aufträge lösen keinen Abschluss aus. Verbindungsfehler, verzögerter Neustart und erneutes Prüfen der Verbindung führen nicht zu einem zweiten Import.
- Nicht angemeldete Requests und Requests ohne erforderlichen CSRF-Schutz werden abgewiesen. Logs enthalten keine Passwörter, API-Keys oder Schlüssel.
- Das UI bleibt bei Fehlern bedienbar, zeigt verständliche Zustände und funktioniert mit Tastatur, schmalem Bildschirm sowie Light- und Dark-Theme.

## Spätere Erweiterungen

Automatische Backups mit Zeitplan und Aufbewahrung und externe Backup-Ziele können auf demselben Format aufbauen. Diese Funktionen gehören nicht zur ersten Version. Ein lokales Backup ersetzt keine getrennt aufbewahrte Sicherung gegen den Verlust des Servers.

## Technische Referenzen

- [Microsoft.Data.Sqlite Online Backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)
- [SQLite Backup API](https://www.sqlite.org/backup.html)
- Bestehende Projektdateien: `StoragePaths.cs`, `JsonSettingsService.cs`, `SqliteHistoryStore.cs`, `SqliteAccountStore.cs`, `ScheduledTaskMigration.cs`, `TwoFactorService.cs`, `Program.cs`, `compose.yaml` und `docs/DEPLOYMENT.md`.
