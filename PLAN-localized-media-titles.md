# Plan: Medientitel in der eingestellten LibManagarr-Sprache

Stand: 20.11.2026. Status: geplant, Umsetzung später.

## Ziel und Umfang

Die Library Page und die Symlink-Vorschläge zeigen Medientitel in der aktuell
in LibManagarr eingestellten Sprache an. Das betrifft Filmtitel sowie – soweit
entsprechende Übersetzungen verfügbar sind – Serien- und Episoden-/Special-Titel.
Die derzeit unterstützten App-Sprachen sind Englisch, Deutsch und Französisch.

Die Spracheinstellung anderer Anwendungen soll dafür nicht geändert werden.
Dateinamen, Pfade und Symlink-Ziele werden durch die Titelanzeige nicht verändert.
Die Umsetzung ist von den bereits begonnenen Matching- und Radarr-Importkorrekturen
zu unterscheiden; diese Datei dokumentiert ausschließlich die spätere Erweiterung.

## Gewünschtes Verhalten

- Titel werden mit Sprachzuordnung gespeichert, nicht nur in der beim Sync aktiven Sprache.
- Die Anzeige wählt: eingestellte Sprache → Englisch → Originaltitel → vorhandener Quelltitel.
- Leere Titel zählen als nicht vorhanden. Identische Fallback-Stufen werden übersprungen.
- Ein Sprachwechsel verwendet bereits gespeicherte Übersetzungen sofort und benötigt
  keinen erneuten vollständigen Sync.
- Fehlt die gewünschte Übersetzung, erscheint sofort ein Fallback. Fehlende Daten
  werden im Hintergrund nachgeladen, soweit die Quelle dies unterstützt. Anschließend
  wird die Anzeige aktualisiert.
- Ist eine Übersetzung dauerhaft nicht verfügbar oder die Quelle nicht erreichbar,
  bleibt der Fallback nutzbar. Vorhandene Titel werden nicht durch leere Antworten ersetzt.
- Alphabetische Sortierung folgt den angezeigten Titeln und den bestehenden
  Titelsortierregeln. Suche berücksichtigt Anzeige- und Alternativtitel.
- Matching berücksichtigt weiterhin alle bekannten Titelvarianten. Ein Wechsel der
  Anzeigesprache allein darf keine neuen Vorschläge oder Entscheidungen erzeugen.

Beispiel: Nach einem Sync bei englischer App-Sprache führt der Wechsel auf Deutsch
sofort zu deutschen Titeln, wenn diese gespeichert sind. Andernfalls bleibt zunächst
der Fallback sichtbar, bis eine deutsche Übersetzung erfolgreich geladen wurde.
Eine sofortige deutsche Anzeige aller Titel kann ohne vorhandene Übersetzungen
nicht garantiert werden.

## Aktueller Stand und Grenzen

- Library und Symlink-DTOs verwenden derzeit die einzelnen Titel aus den Snapshots.
- `AppSettings.Language` ist die zentrale Spracheinstellung; im Frontend wird sie
  über `AppSettingsService` bereitgestellt.
- Radarr unterstützt beim Abruf der Filmliste `languageId`. Ohne diesen Parameter
  richtet sich die Antwort nach Radarrs eigener Einstellung für die Filminfosprache.
- Radarr liefert `originalTitle` und `alternateTitles`. Alternativtitel besitzen
  keine verlässliche Sprachkennzeichnung und reichen deshalb für eine gezielte
  Auswahl nach App-Sprache allein nicht aus.
- Radarr kann bei fehlender Übersetzung selbst einen Fallback liefern. Dieser darf
  nicht ungeprüft als nachgewiesene Übersetzung der angefragten Sprache gespeichert werden.
- Für Sonarr-Serien und insbesondere Episoden/Specials ist eine geeignete Quelle
  für Übersetzungen noch nicht bestätigt. Die konkrete API-Version muss geprüft werden.
- TVDB-Episoden-IDs und TMDB-Film-IDs sind keine gemeinsame Kennung. Eine
  sprachübergreifende Titelanzeige löst die separate Zuordnung von Specials zu Filmen nicht.

## Umsetzungsschritte

### 1. Übersetzungsquellen prüfen

- Unterstützte Radarr-API-Versionen auf Sprachabfragen, Sprachkennungen und
  erkennbare Fallbacks prüfen. Verfügbare Sprachkennungen über die API ermitteln.
- Prüfen, ob Übersetzungen gebündelt oder pro Film abgerufen werden können.
- Für Sonarr Serien- und Episodentitel getrennt untersuchen: Verfügbarkeit über
  die unterstützte Sonarr-API, enthaltene externe IDs und mögliche Metadatenquellen.
- Falls eine zusätzliche Quelle nötig ist, Aufwand, Zugangsdaten und Abdeckung
  dokumentieren und vor Einführung abstimmen. Keine externe Abhängigkeit stillschweigend hinzufügen.
- Ergebnis: nachgewiesene Abdeckung je Medientyp und klare Fallback-Grenzen.

### 2. Sprachbezogenen Titelbestand modellieren

- Bestehende Quelltitel für Kompatibilität erhalten.
- Übersetzungen separat nach stabiler Medienidentität und Sprachcode speichern;
  Originaltitel, Alternativtitel und Herkunft ebenfalls berücksichtigen.
- Fehlende Übersetzung, noch nicht abgefragt und fehlgeschlagener Abruf unterscheidbar machen.
- Originaltitel als eigenen Wert behandeln; dessen Sprache nicht aus dem Text erraten.
- Alte Snapshots ohne Sprachvarianten bleiben lesbar und zeigen ihren bisherigen Titel.
- Den Cache bei entfernten Medien bereinigen und im bestehenden Speicher-/Backupkonzept berücksichtigen.

### 3. Laden und Aktualisieren

- Möglichst Varianten für alle unterstützten App-Sprachen beim Sync vorladen,
  sofern die jeweilige Quelle dies mit vertretbarem Aufwand ermöglicht.
- Neue oder geänderte Medien aktualisieren; erfolgreiche Übersetzungen wiederverwenden.
- Nachladen fehlender Sprachen bei Sprachwechsel ermöglichen, ohne einen vollständigen
  Bibliotheks-Sync zu erzwingen. Mehrfachanfragen bündeln und Parallelität begrenzen.
- Fehlende Übersetzungen mit begrenzter Gültigkeit cachen, damit nicht jeder Seitenaufruf
  dieselben erfolglosen Anfragen auslöst und später ergänzte Übersetzungen auffindbar bleiben.
- Veraltete Antworten dürfen nach einem weiteren Sprachwechsel nicht die aktuelle Anzeige überschreiben.

### 4. Zentrale Titelauswahl und API

- Einen gemeinsamen Resolver für Sprache und Fallback-Reihenfolge verwenden.
- Anzeigetitel von Matching-Eingaben und gespeicherten Entscheidungen trennen.
- Library-Antworten und Symlink-Vorschläge konsistent mit Anzeigetiteln versorgen.
- Herkunft/tatsächliche Sprache intern mitführen, damit Fallbacks korrekt behandelt werden.
- Bestehende Snapshots nicht global auf die gerade gewählte Anzeigesprache umschreiben.
- Matching-Signaturen so abgrenzen, dass reine Anzeigeänderungen keine fachliche
  Neubewertung auslösen. Neue Matching-Titel können weiterhin eine Neubewertung erfordern.

### 5. Library und Vorschläge anbinden

- Library in Tabellen- und Kartenansicht einschließlich Serien-, Film- und Episodentiteln aktualisieren.
- Filmtitel, Serienname und vorhandene Special-Titel in Vorschlägen über dieselbe Auswahl beziehen.
- Beim Sprachwechsel beziehungsweise nachgeladenen Übersetzungen die betroffenen
  Daten reaktiv aktualisieren oder gezielt neu abrufen.
- Sortierung, Suche und gegebenenfalls serverseitige Filter/Paginierung auf dieselben
  Anzeigetitel abstimmen. Nur sichtbare Zeilen nachträglich umzusortieren reicht nicht.
- Bestehende Filter, Sortierrichtung und aufgeklappte Bereiche erhalten.
- Weitere Oberflächen wie Managed links, Rejections, Create-Auswahl und Historie
  bei der Umsetzung auf Konsistenz prüfen; Ausweitung über Library und Vorschläge
  ausdrücklich festlegen.

## Abnahme und Tests

- Backend: Sprachwahl, vollständige Fallback-Kette, leere Werte und alte Snapshots.
- API-Integration: echte Feldnamen, angefragte Sprachkennung und Radarr-eigene Fallbacks.
- Cache: Nachladen, Ausfälle, wiederholte Abrufe und zwischenzeitlich entfernte Medien.
- Frontend: Englisch synchronisieren, danach ohne Vollsync auf Deutsch wechseln;
  gespeicherte deutsche Titel erscheinen sofort.
- Frontend: fehlendes Deutsch zeigt Fallback und aktualisiert nach erfolgreichem Nachladen.
- Frontend: schneller mehrfacher Sprachwechsel übernimmt keine veralteten Antworten.
- Library: alphabetische Reihenfolge und Suche passen in beiden Ansichten zu den Titeln,
  auch bei serverseitiger Paginierung/Filterung.
- Symlinks: gleiche Vorschlagsidentitäten und Entscheidungen vor und nach Sprachwechsel;
  bekannte Fehlmatches wie die Girls-und-Panzer-Kurzfassung bleiben ausgeschlossen.
- Sonarr ohne Übersetzungsquelle bleibt über den definierten Fallback verwendbar.
- Ein gezielter E2E-Test deckt den Sprachwechsel über Library und Vorschläge ab.

## Offene Entscheidungen vor der Umsetzung

1. Welche Übersetzungsquelle deckt Sonarr-Serien und Episoden/Specials zuverlässig ab?
2. Ist bei einer externen Quelle zusätzlicher Konfigurationsaufwand akzeptabel?
3. Wie werden verfügbare Übersetzungen mit vertretbarem Aufwand vorab geladen und aktualisiert?
4. Sollen weitere titelanzeigende Symlink-Bereiche gleich mit vereinheitlicht werden?

## Referenzen zur erneuten Prüfung bei Umsetzung

- Radarr-Sprachparameter und Fallback-Verhalten:
  https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/MovieController.cs
- Radarr-Filmtitel, Originaltitel und IDs:
  https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/MovieResource.cs
- Struktur alternativer Radarr-Titel:
  https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/AlternativeTitleResource.cs
- Sonarr-Episodenfelder:
  https://github.com/Sonarr/Sonarr/blob/develop/src/Sonarr.Api.V3/Episodes/EpisodeResource.cs

Die verlinkten Entwicklungsstände können sich ändern; bei Umsetzung gegen die
unterstützten beziehungsweise tatsächlich installierten Versionen verifizieren.
