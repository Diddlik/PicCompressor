# PicCompressor – Anforderungen und Architektur

| Feld | Wert |
|---|---|
| Status | Implementierung begonnen |
| Letzte fachliche Prüfung | 2026-07-19 |
| Zielplattform | Desktop und CLI auf Windows, macOS und Linux |
| Technologiebasis | .NET 10 LTS, Avalonia 12 |
| Verbindlichkeit | Normativ; Abweichungen müssen in diesem Dokument entschieden werden |

## 1. Zweck und Leser

Dieses Dokument ist die zentrale Quelle für Produktanforderungen, Architekturentscheidungen und Implementierungsstatus von PicCompressor.

Es richtet sich an Entwickler, Reviewer und zukünftige Maintainer. Nach dem Lesen muss eine Person:

- eine Funktion der richtigen Architekturschicht zuordnen können,
- erkennen, welche Anforderungen verbindlich und welche Punkte noch offen sind,
- eine Änderung auf Datenintegrität und Cross-Platform-Kompatibilität prüfen können,
- den tatsächlichen Implementierungsstand korrekt nachführen können.

Allgemeine Framework- oder Sprachkonzepte werden nicht erklärt.

### 1.1 Bedeutung der Schlüsselwörter

- **MUSS**: zwingende Produkt- oder Architekturvorgabe.
- **SOLL**: Vorgabe, von der nur mit dokumentierter Begründung abgewichen werden darf.
- **KANN**: optionale Erweiterung.

## 2. Produktziel

PicCompressor ist eine lokale Desktop- und CLI-Anwendung zur hochwertigen, verlustbehafteten JPEG-Erzeugung aus JPEG- und PNG-Quelldateien. GUI und CLI verwenden dieselbe Anwendungslogik und erzeugen bei identischen Einstellungen dasselbe Ergebnis.

Das Produkt optimiert für:

1. sichere Verarbeitung ohne unbeabsichtigten Verlust von Originaldateien,
2. reproduzierbare Ergebnisse,
3. gute wahrgenommene Bildqualität,
4. transparente Ressourcen- und Fehlerzustände,
5. gleichwertiges Verhalten auf den unterstützten Desktop-Plattformen.

## 3. Umfang

### 3.1 Enthalten

- einzelne JPEG- und PNG-Dateien,
- rekursive Ordnerverarbeitung,
- Ausgabe als JPEG,
- Jpegli als primäre Engine,
- Guetzli als optionale Legacy-Engine,
- Batch-Queue, Abbruch und Wiederholung fehlgeschlagener Jobs,
- GUI und skriptfähige CLI,
- Verlauf und strukturierte Logs,
- visueller Vorher-Nachher-Vergleich in der GUI,
- portable, signierte beziehungsweise prüfbare Installationspakete.

### 3.2 Nicht enthalten

- verlustfreie PNG-Optimierung,
- Ausgabe als AVIF, WebP oder JPEG XL,
- Bildbearbeitung jenseits notwendiger Farb-, Orientierungs- und Alpha-Konvertierung,
- Cloud-Upload oder serverseitige Verarbeitung,
- mobile Anwendungen und Browseranwendung,
- verteilte Verarbeitung über mehrere Rechner.

Eine Erweiterung dieses Umfangs erfordert eine dokumentierte Architekturentscheidung.

## 4. Plattform- und Capability-Modell

### 4.1 Unterstützte Plattformen

Die Anwendung MUSS auf folgenden Zielkombinationen gebaut und getestet werden:

| Betriebssystem | Architektur | GUI | CLI | Jpegli | Guetzli |
|---|---:|---:|---:|---:|---:|
| Windows | x64 | Ziel | Ziel | Ziel | Kandidat |
| Windows | ARM64 | Ziel | Ziel | Ziel | Offen |
| macOS | x64 | Ziel | Ziel | Ziel | Kandidat |
| macOS | ARM64 | Ziel | Ziel | Ziel | Offen |
| Linux | x64 | Ziel | Ziel | Ziel | Kandidat |
| Linux | ARM64 | Ziel | Ziel | Ziel | Offen |

Zulässige Werte dieser Tabelle:

- **Ziel**: verbindlich angestrebt; gilt erst dann als „unterstützt“, wenn Build, Installation, Start und Referenzkompression in CI oder auf dokumentierter Testhardware erfolgreich sind.
- **Kandidat**: wird ausgeliefert, sobald die Engine reproduzierbar in den Wrapper eingebunden und nach Abschnitt 5.2 getestet ist.
- **Offen**: noch nicht entschieden; siehe O-002.

### 4.2 Engine-Verfügbarkeit

- Die Anwendung MUSS beim Start die verfügbaren Engines und deren Versionen ermitteln.
- Eine nicht verfügbare Engine darf den Start der Anwendung nicht verhindern.
- GUI und CLI MÜSSEN nicht verfügbare Engines mit einer konkreten Ursache melden.
- Ein gespeichertes Profil mit nicht verfügbarer Engine MUSS als nicht ausführbar markiert werden; ein stiller Engine-Wechsel ist verboten.
- Plattformabhängige Fähigkeiten MÜSSEN aus einem Capability-Modell stammen und dürfen nicht über verstreute Betriebssystemabfragen implementiert werden.
- Native Buildversion und Source-Revision stammen aus dem gepinnten Paketmanifest; ABI- und Capability-Proben bestätigen zusätzlich die Ladbarkeit des konkreten Wrappers.

### 4.3 Cross-Platform-Invarianten

Auf allen unterstützten Plattformen MÜSSEN identisch sein:

- Qualitäts- und Metadatenregeln,
- Kollisions- und Überschreibschutz,
- Exit-Code-Semantik,
- History-Schema,
- maschinenlesbares CLI-Ausgabeformat,
- Fehlerkategorien,
- Jobstatus und Abbruchverhalten.

Plattformspezifisch dürfen nur Packaging, native Binärdateien, bekannte Dateisystemeigenschaften und UI-Integration sein.

## 5. Engine-Strategie

### 5.1 Jpegli

Jpegli ist die Standard-Engine.

- Die verwendete Revision MUSS fest gepinnt sein.
- Die Anwendung MUSS Qualitätswert, Chroma-Subsampling und Progressive-Level explizit modellieren.
- Der `cjpegli`-Adapter akzeptiert Qualität `1..100`, Chroma-Subsampling `444`, `440`, `422` oder `420` sowie Progressive-Level `0..2`.
- Progressive-Level `0` bedeutet sequenzielles JPEG; höhere unterstützte Werte erzeugen progressive JPEGs.
- Eine vermeintliche „10-Bit-Option“ ist kein Produktparameter. Interne Präzision ist eine Eigenschaft der Engine und kein vom Benutzer steuerbares Versprechen.
- Native Versions- und Buildinformationen MÜSSEN in Diagnoseausgabe und Logs erscheinen.

### 5.2 Guetzli

Guetzli ist eine optionale Legacy-Engine, weil das Upstream-Projekt archiviert ist.

- Guetzli darf nicht die einzige verfügbare Engine sein.
- Die Qualitätsgrenze MUSS der tatsächlich eingebundenen Guetzli-Revision entsprechen; für die offizielle Version ist mindestens `84` anzunehmen und automatisiert zu verifizieren.
- Guetzli erzeugt nur sequenzielle JPEGs.
- Die Queue MUSS den hohen RAM- und CPU-Bedarf berücksichtigen.
- Guetzli darf nur für Plattformen angeboten werden, für die eine reproduzierbar gebaute und getestete Wrapper-Einbindung vorliegt.
- Ein späteres Entfernen von Guetzli darf die Core- oder Präsentationsschichten nicht strukturell verändern.

### 5.3 Integrationsgrenze

Native Encoder werden über isolierte Engine-Adapter hinter einer gemeinsamen Anwendungs-Schnittstelle angesprochen.

Jpegli und Guetzli werden als Bibliotheken in einen gemeinsamen C++-Wrapper pro Runtime-ID eingebunden. Die Anwendung startet keine Encoder-Binaries.

- Der Wrapper exportiert eine versionierte, stabile C-ABI ohne C++-Typen oder Ausnahmen an der Grenze.
- .NET ruft die C-ABI ausschließlich über den Managed-Interop-Adapter auf.
- Pfade und Fehlertexte werden als UTF-8 übertragen.
- Optionsstrukturen tragen ihre Größe; die ABI-Version wird vor dem Aufruf geprüft.
- Statuscodes und begrenzte Fehlerpuffer ersetzen native Exceptions an der Grenze.
- Abbruch erfolgt kooperativ über ein thread-sicheres natives Abbruch-Handle.
- Die Aufrufe laufen außerhalb des UI-Threads; Ressourcen- und Zeitlimits werden vor dem Start und kooperativ während der Ausführung geprüft.
- Die erzeugte temporäre Datei wird unabhängig vom nativen Erfolgsstatus validiert.
- Das Paket hängt weder von `PATH` noch von global installierten Encoder-Bibliotheken ab.

Der In-Process-Aufruf reduziert Packaging- und Prozesskomplexität, besitzt aber keine Absturzisolation: ein schwerer nativer Fehler kann die Anwendung beenden. Deshalb bleiben Eingabegrenzen, reproduzierbar gebaute Revisionen, Sanitizer-Läufe und die sichere temporäre Ausgabepipeline verpflichtend.

## 6. Fachliches Modell

### 6.1 Compression Job

Ein Job MUSS mindestens enthalten:

- unveränderliche Job-ID,
- kanonischen Eingabepfad,
- geplanten Ausgabepfad,
- Engine und enginespezifische Einstellungen,
- Qualitätsparameter,
- Metadatenrichtlinie,
- Alpha-Hintergrund für transparente Eingaben,
- Kollisions- und Überschreibrichtlinie,
- Erstellungszeitpunkt,
- optionalen Profilnamen.

Ein Job ist nach dem Einreihen unveränderlich. Eine Einstellungsänderung erzeugt einen neuen Job.

Die Wiederholung eines fehlgeschlagenen Jobs erzeugt ebenfalls einen neuen Job mit neuer Job-ID. Der neue Job MUSS die Job-ID des Vorgängers als optionales Feld führen; Ergebnis und Verlauf geben diese Verkettung wieder. Ein terminaler Job wird durch eine Wiederholung nicht verändert.

### 6.2 Jobstatus

Zulässige Status:

1. `Queued`
2. `Validating`
3. `WaitingForResources`
4. `Encoding`
5. `Finalizing`
6. `Succeeded`
7. `Failed`
8. `Canceled`

Terminale Zustände dürfen nicht wieder verlassen werden. Jeder Fehler MUSS einer stabilen Fehlerkategorie und einer menschenlesbaren Ursache zugeordnet sein.

### 6.3 Compression Result

Ein Ergebnis MUSS mindestens enthalten:

- Job-ID und finalen Status,
- Eingabe- und Ausgabepfad,
- Engine, Engine-Version und effektive Einstellungen,
- Eingabe- und Ausgabegröße in Bytes,
- absolute und prozentuale Einsparung,
- Start, Ende und Dauer,
- Metadaten- und Farbprofilbehandlung,
- Warnungen,
- Fehlerkategorie und Fehlertext,
- Prüfergebnis der Ausgabedatei,
- ob eine Ausgabedatei veröffentlicht wurde.

Erfolgs- und Fehlerergebnisse dürfen keine widersprüchlichen Felder zulassen.

### 6.4 Fehlerkategorien

Fehlerkategorien sind eine stabile, plattformidentische Menge. Zulässig sind:

| Kategorie | Bedeutung |
|---|---|
| `InvalidArguments` | ungültige Argumente, Optionen oder Konfiguration |
| `InputNotFound` | Eingabe existiert nicht oder ist nicht lesbar |
| `UnsupportedInput` | Format nicht unterstützt, beschädigt oder unvollständig |
| `LimitExceeded` | Pixelzahl-, Dateigrößen- oder Laufzeitgrenze überschritten |
| `EngineUnavailable` | angeforderte Engine oder Capability fehlt |
| `EngineFailed` | Engine bricht mit Fehler, Timeout oder Signal ab |
| `OutputValidationFailed` | Ausgabe fehlt, ist leer oder nicht als JPEG validierbar |
| `OutputConflict` | Zielpfad belegt oder kollidiert mit einem anderen geplanten Job |
| `FileSystemError` | Schreib-, Verschiebe-, Rechte- oder Speicherplatzfehler |
| `Canceled` | Abbruch durch Benutzer oder Anwendungsschluss |
| `Unexpected` | nicht klassifizierte Ausnahme |

Neue Kategorien erfordern eine Architekturentscheidung. Kategorien werden nicht in Präsentationscode neu definiert; GUI, CLI, Verlauf und Logs verwenden dieselben Bezeichner.

## 7. Dateiverarbeitung und Datenintegrität

### 7.1 Eingabevalidierung

Vor dem Encoding MUSS die Anwendung:

- Existenz, Lesbarkeit und unterstütztes Dateiformat prüfen,
- das Format anhand des Inhalts und nicht allein anhand der Endung erkennen,
- Abmessungen und erwarteten Ressourcenbedarf ermitteln,
- beschädigte oder unvollständige Dateien ablehnen,
- symbolische Links und kanonische Pfade kontrolliert behandeln,
- konfigurierbare Grenzen für Pixelzahl, Dateigröße und Laufzeit anwenden.

### 7.2 Sichere Ausgabe

- Ohne explizite Überschreibfreigabe darf der Ausgabepfad nie dem Eingabepfad entsprechen.
- Standard ist ein Suffix `_compressed` bei erhaltener Verzeichnisstruktur und `.jpg` als Endung.
- Die Kollisionsrichtlinie bei belegtem Zielpfad ist genau einer der Werte `skip`, `rename` oder `overwrite`. Standard ist `skip`. `rename` hängt einen eindeutigen numerischen Zusatz an; `overwrite` ist nur nach expliziter Freigabe zulässig.
- Dateiendungen MÜSSEN unabhängig von Groß-/Kleinschreibung korrekt behandelt werden.
- Die Engine schreibt immer in eine eindeutige temporäre Datei im Zielverzeichnis.
- Die temporäre Datei MUSS vollständig dekodierbar und als JPEG validiert werden.
- Erst danach darf sie atomar beziehungsweise mit der sichersten verfügbaren Dateisystemoperation an den Zielpfad verschoben werden.
- Ist die validierte Ausgabe nicht kleiner als die Eingabe, entscheidet eine explizite Richtlinie: Standard ist `discard`, also Verwerfen der temporären Datei und ein erfolgreicher Job ohne Einsparung mit Warnung; `keep` veröffentlicht die größere Datei. Die Richtlinie MUSS in Ergebnis und Verlauf festgehalten werden. Ein stillschweigendes Veröffentlichen einer größeren Datei ist verboten.
- Bei Fehler oder Abbruch bleiben Original und bestehende Zieldatei unverändert.
- Teildateien werden bereinigt; ein Bereinigungsfehler wird protokolliert.
- Kollisionen zwischen gleichzeitig geplanten Jobs werden vor Queue-Start erkannt.

### 7.3 Ordnerverarbeitung

- Rekursion ist explizit ein- oder ausschaltbar.
- Standardmäßig werden symbolische Verzeichnislinks nicht verfolgt.
- Ausgabeordner innerhalb des Eingabebaums werden von einer rekursiven Suche ausgeschlossen.
- Relative Verzeichnisstrukturen bleiben bei Ausgabe in einen dedizierten Ordner erhalten.
- Ein Batch ist eine Sammlung unabhängiger Jobs; ein Fehler stoppt andere Jobs nicht, sofern kein `fail-fast` gewählt wurde.

## 8. Bild-, Farb- und Metadatenregeln

### 8.1 Ausgabeformat

- Jede erfolgreiche Ausgabe ist ein standardkonformes JPEG mit `.jpg`-Endung.
- PNG-Eingaben werden verlustbehaftet nach JPEG konvertiert.
- Transparenz kann JPEG nicht erhalten. Der Benutzer MUSS vor Start über die Hintergrundfarbe entscheiden oder einen sichtbaren Standard bestätigen.
- Standardhintergrund ist Weiß; Schwarz darf nicht stillschweigend verwendet werden.

### 8.2 Orientierung

- EXIF-Orientierung MUSS beim Dekodieren berücksichtigt werden.
- Das Ergebnis MUSS visuell korrekt orientiert sein.
- Nach physischer Rotation wird die Orientierung im Ergebnis auf Normalstellung gesetzt oder entfernt.

Die Rotation erfolgt unabhängig von der Metadatenrichtlinie, weil die Ausgabe auch dann aufrecht stehen muss, wenn der tragende EXIF-Block anschließend entfällt. Die eingebundene Jpegli-Revision wendet die Orientierung nicht selbst an; sie bleibt Aufgabe des Wrappers.

### 8.3 Metadatenrichtlinie

EXIF und Farbprofil werden getrennt konfiguriert:

| Richtlinie | Verhalten |
|---|---|
| EXIF behalten | erlaubte EXIF-Felder kopieren; Dimensionen und Orientierung korrigieren |
| EXIF privat | GPS, Seriennummern und identifizierende Felder entfernen |
| EXIF entfernen | keine EXIF-Daten übernehmen |
| Farbprofil erhalten | Farbdarstellung erhalten; Profil übernehmen oder kontrolliert nach sRGB transformieren |
| Farbprofil sRGB | Pixel nach sRGB transformieren und gültiges sRGB-Profil kennzeichnen |
| Farbprofil entfernen | nur zulässig, wenn Pixel bereits sRGB repräsentieren |

Guetzlis Ignorieren eingebetteter Profile MUSS durch Vorverarbeitung kompensiert oder als nicht unterstützte Kombination abgelehnt werden. „Metadaten erhalten” darf nicht lediglich Byte-Blöcke ungeprüft kopieren.

`EXIF privat` wird über eine Positivliste zulässiger Tags umgesetzt, nicht über eine Sperrliste: ein unbekanntes Tag gilt als identifizierend und entfällt. Damit entfallen ohne Einzelprüfung auch MakerNote, GPS-IFD, Serien- und Besitzerangaben sowie Herstellererweiterungen. Der Thumbnail-IFD wird ebenfalls verworfen, weil er eine unbereinigte Kopie des Originals tragen kann. Der Blob wird dabei neu serialisiert; ein bloßes Entfernen der Verweise würde die Nutzdaten im Puffer belassen.

`Farbprofil entfernen` MUSS abgelehnt werden, wenn die Eingabe ein Profil trägt, das nicht sRGB entspricht; andernfalls würde das Entfernen die Farben verändern.

## 9. Qualitätseinstellungen

- GUI und CLI MÜSSEN enginespezifische Werte anzeigen und validieren.
- Ein gemeinsamer Slider darf nicht behaupten, identische Werte verschiedener Engines seien visuell identisch.
- Vordefinierte Qualitätsprofile KÖNNEN enginespezifische Werte bündeln, müssen diese aber offenlegen.
- Ungültige Werte werden vor Queue-Aufnahme abgelehnt.
- Effektive Werte werden im Ergebnis und Verlauf gespeichert.
- Standardwerte werden durch versionierte Produktprofile definiert und nicht in Präsentationscode dupliziert.

## 10. Queue, Ressourcen und Fortschritt

### 10.1 Scheduling

Die Queue MUSS CPU und geschätzten Speicherbedarf berücksichtigen.

- Jpegli- und Guetzli-Limits sind getrennt konfigurierbar.
- Guetzli-Jobs erhalten ein gewichtetes Speicherbudget anhand ihrer Pixelzahl.
- Ein Job startet nur, wenn CPU- und Speicherbudget verfügbar sind.
- Der Standard darf den Rechner nicht durch unbeschränkte Parallelität unbenutzbar machen.
- Priorisierung bleibt zunächst FIFO; spätere Prioritäten erfordern einen belegten Anwendungsfall.

### 10.2 Fortschritt

Fortschritt besteht aus Statusphase und optionalem Prozentwert.

- Wenn die Engine keinen belastbaren Prozentwert liefert, zeigt die Anwendung einen unbestimmten Fortschritt.
- Parser für native Diagnoseausgaben dürfen keine geschätzten Prozentwerte als exakt ausgeben.
- Batch-Fortschritt basiert auf abgeschlossenen Jobs, nicht auf erfundenem Engine-Fortschritt.

### 10.3 Abbruch

- Wartende und laufende Jobs sind abbrechbar.
- Ein Abbruch setzt das native Abbruch-Handle; Wrapper und eingebundene Engine prüfen es an definierten sicheren Punkten.
- Eine Engine gilt erst als abbrechbar, wenn dieser Pfad in ihrer eingebundenen Revision verifiziert ist.
- Nach dem Abbruch existiert keine freigegebene Teilausgabe.
- Anwendungsschluss mit laufenden Jobs erfordert eine explizite Entscheidung.

## 11. GUI-Anforderungen

- Drag-and-drop unterstützt Dateien und Ordner.
- Vor Queue-Start zeigt die GUI Zielpfad, Engine, Qualitätsprofil, Metadatenregel und erwartete Konflikte.
- Pro Job werden Status, Dauer, Größenänderung, Warnungen und konkrete Fehler angezeigt.
- Fehler eines Jobs blockieren die übrige Oberfläche nicht.
- Der Vorher-Nachher-Vergleich:
  - wird erst nach erfolgreicher Validierung angeboten,
  - berücksichtigt Orientierung und Farbprofil,
  - verwendet für große Bilder speicherschonende Vorschauen,
  - erlaubt Zoom, Schwenken und synchronisierte Ansicht,
  - kennzeichnet Original und Ergebnis eindeutig.
- Alle Kernfunktionen sind per Tastatur erreichbar.
- Status wird nicht ausschließlich über Farbe vermittelt.
- Steuerelemente besitzen zugängliche Namen und sinnvolle Fokusreihenfolge.

### 11.1 UI-Referenzvorlage

Die interaktive [PicCompressor-UI-Vorlage](UI/PicCompressor.html) ist die visuelle Referenz für die Avalonia-GUI. Sie zeigt Dashboard, Einstellungen, Verlauf und Vergleichsansicht sowie Light/Dark Theme und kompakte/maximierte Fensterzustände.

- Navigation, Informationshierarchie, Zustände, Abstände, Typografie und Farbrollen SOLLEN der Vorlage folgen.
- Die Vorlage wird nicht als Web-Inhalt eingebettet; die GUI wird nativ mit Avalonia umgesetzt.
- Produktverhalten und Accessibility-Anforderungen dieses Dokuments haben bei Widersprüchen Vorrang.
- Plattformkonventionen und responsive Fenstergrößen dürfen begründete Abweichungen erfordern.
- Änderungen am grundlegenden UI-Konzept MÜSSEN Vorlage und diesen Abschnitt gemeinsam aktualisieren.

Bekannte Abweichungen der Vorlage von diesem Dokument; hier gilt jeweils das Dokument (D-016):

| Vorlage | Verbindlich |
|---|---|
| Umschalter „10-Bit interne Farbpräzision“ | entfällt; interne Präzision ist kein Produktparameter (5.1) |
| Progressive-Encoding als Ein/Aus | Progressive-Level `0..2` (5.1) |
| Chroma `4:4:4`, `4:2:2`, `4:2:0` | zusätzlich `4:4:0` (5.1) |
| Eingaben „JPG · PNG · WebP · TIFF“ | nur JPEG und PNG (3.1) |
| Ausgabeoption „Originale überschreiben“ | Ausgabeziel und Kollisionsrichtlinie sind getrennt (7.2) |
| Metadaten als eine dreiwertige Auswahl | EXIF und Farbprofil getrennt konfiguriert (8.3) |
| Butteraugli- und SSIM-Kennzahlen im Vergleich | nicht gefordert; Vergleich zeigt Größe und Einsparung |

### 11.2 Sprache und Darstellung

- Die Oberfläche MUSS in Deutsch und Englisch vorliegen. Jede sichtbare Zeichenkette stammt aus versionierten Ressourcendateien; feste Texte in Quelltext oder XAML sind verboten.
- Die Startsprache ist die Betriebssystemsprache. Liegt für sie keine Übersetzung vor, gilt Englisch.
- Ein Sprachwechsel MUSS ohne Neustart wirken.
- Eigennamen, Fehlerkategorien, Exit-Codes, JSON-Felder, Engine-IDs und Logtexte bleiben unübersetzt und plattformidentisch (Abschnitt 4.3).
- Zahlen, Dateigrößen und Zeitpunkte werden über die aktive Kultur formatiert.
- Das Design ist auf `System`, `Hell` oder `Dunkel` einstellbar; Standard ist `System`. Auch der Designwechsel wirkt ohne Neustart.
- Sprache und Design werden nicht dupliziert konfiguriert; die CLI bleibt von der Oberflächensprache unberührt.

Die konkreten Regeln für Schlüsselvergabe und Pflege stehen in [agent_instructions.md](../agent_instructions.md).

## 12. CLI-Anforderungen

Die CLI MUSS die nicht-visuellen Kompressionsfunktionen der GUI abdecken:

- einzelne oder mehrere Dateien,
- Ordner und optionale Rekursion,
- Engine und enginespezifische Optionen,
- Qualitätsprofil,
- Zielordner, Suffix, Kollisionsrichtlinie und Richtlinie für nicht kleinere Ausgaben,
- Metadaten- und Alpha-Regeln,
- Parallelitäts- und Ressourcenlimits,
- Abbruch,
- menschenlesbare oder JSON-basierte Ausgabe,
- `dry-run` ohne Dateischreibzugriff,
- optionales Abschalten der History.

Prompts sind nur in interaktiven Terminals erlaubt. In nicht interaktiven Umgebungen führt eine fehlende Entscheidung zu einem Nutzungsfehler.

Abbruch erfolgt über das plattformübliche Unterbrechungssignal des Terminals. Das erste Signal löst einen geordneten Abbruch nach Abschnitt 10.3 aus; ein zweites Signal erzwingt das Beenden. Auch der erzwungene Abbruch darf keine unvalidierte Zieldatei zurücklassen.

Das JSON-Ausgabeformat trägt eine eigene Schemaversion. Änderungen sind additiv; eine nicht abwärtskompatible Änderung erhöht die Version und erfordert eine Architekturentscheidung.

### 12.1 Exit Codes

| Code | Bedeutung |
|---:|---|
| `0` | alle Jobs erfolgreich |
| `1` | unerwarteter, nicht klassifizierter Fehler |
| `2` | ungültige Argumente oder Konfiguration |
| `3` | keine passende Eingabe gefunden |
| `4` | teilweise erfolgreich |
| `5` | alle gestarteten Jobs fehlgeschlagen |
| `6` | Verarbeitung abgebrochen |
| `7` | angeforderte Engine nicht verfügbar |
| `8` | Datenintegritäts- oder Schreibfehler |

Fehlerdiagnosen gehen nach Standardfehler. Maschinenlesbare Ausgabe bleibt auch bei Fehlern syntaktisch gültig.

## 13. Verlauf, Einstellungen und Logging

### 13.1 Verlauf

- Der Verlauf wird lokal in einer versionierten SQLite-Datenbank im plattformgerechten Anwendungsdatenverzeichnis gespeichert.
- GUI und CLI nutzen dasselbe Schema und dieselbe Zugriffsschicht.
- Gespeichert werden Ergebnisse, keine Bildinhalte.
- Schreibzugriffe sind transaktional und nebenläufig sicher.
- Schemaänderungen benötigen vorwärtsgerichtete, getestete Migrationen.
- Benutzer können Verlauf deaktivieren, Einträge löschen und eine Aufbewahrungsdauer festlegen.
- Absolute Pfade gelten als potenziell sensible Daten und werden bei Export auf Wunsch anonymisiert.

### 13.2 Einstellungen

- Einstellungen liegen im plattformgerechten Benutzer-Konfigurationsverzeichnis.
- Unbekannte oder beschädigte Konfiguration führt zu einer verständlichen Diagnose und sicheren Defaults.
- Profile sind versioniert.
- Enginespezifische Einstellungen werden beim Engine-Wechsel nicht stillschweigend umgedeutet.

### 13.3 Logging

- Interne Logs sind strukturiert und enthalten Zeit, Schweregrad, Job-ID, Komponente und Fehlerkategorie.
- Standardpfad ist das Anwendungsdatenverzeichnis, nicht der Bild-Zielordner.
- Ein expliziter JSONL-Export in den Zielordner ist möglich.
- Logs enthalten keine Bilddaten, Metadateninhalte oder unnötigen vollständigen Pfade.
- Logrotation und maximale Aufbewahrung sind konfigurierbar.

## 14. Architektur

### 14.1 Schichten

| Schicht | Verantwortung | Darf abhängen von |
|---|---|---|
| Domain | Jobs, Ergebnisse, Richtlinien, Invarianten | nichts Infrastrukturbezogenem |
| Application | Anwendungsfälle, Validierung, Queue-Orchestrierung | Domain und Ports |
| Engine Adapters | Übersetzung zu Jpegli/Guetzli, Capability-Erkennung | Application-Ports |
| Infrastructure | Dateisystem, History, Einstellungen, Logging | Domain/Application-Ports |
| CLI | Argumente, Ausgabe, Exit Codes und Composition Root | Application; ausschließlich der Composition Root zusätzlich Engine-Adapter, Infrastructure und Native Interop |
| GUI | Darstellung und Interaktion | Domain und Application |
| Desktop Host | GUI-Composition-Root und VeloPack-Bootstrap | GUI, Application sowie konkrete Engine-, Infrastructure- und Interop-Adapter |
| Managed Interop | ABI-Prüfung, P/Invoke, Status- und Abbruchübersetzung | Application-Ports, Native Wrapper |
| Native Wrapper | stabile C-ABI und direkte Bibliotheksaufrufe zu Jpegli/Guetzli | gebündelte Encoder-Bibliotheken |

Abhängigkeiten zeigen nach innen. GUI und CLI dürfen weder den Native Wrapper direkt aufrufen noch History-Datenbanken direkt öffnen. Der Desktop Host bleibt frei von Präsentationslogik und verdrahtet ausschließlich die konkreten Adapter.

### 14.2 Zentrale Ports

Die Architektur benötigt fachliche Schnittstellen für:

- Kompressionsausführung,
- Engine-Katalog und Capabilities,
- Queue und Jobbeobachtung,
- Dateisystemoperationen,
- Metadaten- und Farbverarbeitung,
- Verlauf,
- Einstellungen,
- Uhr und ID-Erzeugung,
- strukturierte Diagnose.

Schnittstellen werden nach fachlichem Bedarf eingeführt. Ein Interface ohne alternative Implementierung oder Testgrenze ist nicht automatisch erforderlich.

### 14.3 Fehlerbehandlung

- Erwartbare Fehler werden als typisierte Fehlerkategorien transportiert.
- Unerwartete Ausnahmen werden an der jeweiligen Shell-Grenze protokolliert und verständlich dargestellt.
- Native Statuscodes, ABI-Fehler und kooperative Abbrüche werden in stabile Produktfehler übersetzt.
- Fehlermeldungen enthalten eine konkrete nächste Handlung, wenn diese bekannt ist.

### 14.4 Nebenläufigkeit

- Der Queue-Dienst besitzt die alleinige Scheduling-Verantwortung.
- UI- und CLI-Schichten dürfen keine eigene Parallelverarbeitung aufbauen.
- Statusänderungen sind geordnet und thread-sicher.
- Persistenzfehler dürfen ein korrekt erzeugtes Bild nicht nachträglich als Kompressionsfehler deklarieren; sie werden als separate Warnung ausgewiesen.

## 15. Distribution und Supply Chain

- Der verbindliche Releaseablauf und seine Sicherheitsgates sind in der [Release-Anleitung](releasing.md) beschrieben.
- Der lokale Windows-Debugablauf ist in der [Entwicklungsanleitung](development.md) beschrieben.
- Direkte Downloads verwenden VeloPack für Installer, portable Archive und Updates.
- Der Microsoft Store verwendet einen getrennten MSIX-Kanal und dessen Updateinfrastruktur.
- VeloPack erhält ausschließlich bereits gebaute, getestete und manifestierte RID-Ausgaben.
- GUI und CLI werden gemeinsam ausgeliefert; die GUI ist der Installations- und Update-Einstiegspunkt.
- Die CLI darf niemals selbstständig Updates installieren.
- Native Binärdateien werden pro Runtime-ID gebaut und paketiert.
- Version, Quellrevision, Buildverfahren, Lizenz und Prüfsumme jeder nativen Komponente sind dokumentiert.
- Builds SOLLEN reproduzierbar sein.
- Installationspakete enthalten Lizenzhinweise und eine Software-Stückliste.
- Die Anwendung lädt standardmäßig keine ausführbaren Dateien zur Laufzeit nach.
- Updates MÜSSEN signiert und vor Installation verifiziert werden.
- Das Paket darf nicht von einer global installierten Jpegli-/Guetzli-Version abhängen.
- Fehlt eine optionale Engine, bleibt das Paket mit den übrigen Engines funktionsfähig.

## 16. Sicherheit

- Bilddateien sind nicht vertrauenswürdige Eingaben.
- Native Bibliotheken laufen im Anwendungsprozess mit Benutzerrechten; es wird keine Shell gestartet.
- Pfade werden kanonisiert und als UTF-8 über die C-ABI übertragen.
- Ressourcen-, Pixel- und Zeitlimits schützen vor absichtlicher oder versehentlicher Überlastung.
- Temporäre Dateien verwenden nicht vorhersagbare Namen und restriktive Zugriffsrechte.
- Sensible Metadaten werden nur entsprechend der gewählten Richtlinie übernommen.
- Abstürze oder manipulierte Engine-Ausgaben dürfen keine unvalidierte Zieldatei veröffentlichen.
- Abhängigkeiten und native Komponenten werden automatisiert auf bekannte Schwachstellen geprüft.

## 17. Qualitätsanforderungen

### 17.1 Tests

Erforderlich sind:

- Domain- und Application-Unit-Tests,
- Adapter-Vertragstests je Engine,
- Golden-File-Tests mit kleinen lizenzfreien Referenzbildern,
- Tests für EXIF-Orientierung, ICC und Alpha,
- Datenintegritätstests bei Abbruch, vollem Datenträger und existierendem Ziel,
- CLI-End-to-End-Tests einschließlich Exit Codes und JSON,
- History-Migrations- und Nebenläufigkeitstests,
- Paket-Smoke-Tests für jede unterstützte Runtime,
- GUI-Tests für kritische Tastatur- und Fehlerpfade.

### 17.2 Ergebnisvalidierung

Ein Job gilt nur als erfolgreich, wenn:

- die Engine erfolgreich beendet wurde,
- die erzeugte Datei vollständig als JPEG dekodiert werden kann,
- Abmessungen und Orientierung den Erwartungen entsprechen,
- die Datei nicht leer ist.

Wird die Ausgabe veröffentlicht, MUSS zusätzlich genau die erwartete Ausgabedatei am Zielpfad existieren und die atomare Veröffentlichung erfolgreich gewesen sein.

Die Richtlinie `discard` aus Abschnitt 7.2 ist der einzige Fall, in dem ein erfolgreicher Job keine Ausgabedatei veröffentlicht. Das Ergebnis MUSS dies ausweisen; die temporäre Datei wird bereinigt.

### 17.3 Performance

Performanceziele werden erst nach einem reproduzierbaren Benchmark auf Referenzhardware festgelegt. Bis dahin gelten:

- keine unbeschränkte Parallelität,
- keine vollständige Bilddekodierung nur für Listenansichten,
- kein Laden vollständiger Bilddateien in die History,
- UI bleibt während Encoding und Vorschauerzeugung bedienbar,
- Speicher- und Laufzeitmessungen werden pro Engine erfasst.

## 18. Definition of Done

Eine Anforderung ist erst umgesetzt, wenn:

1. Verhalten in Core sowie allen betroffenen Shells implementiert ist,
2. relevante Fehler- und Abbruchpfade behandelt sind,
3. automatisierte Tests bestanden sind,
4. mindestens die betroffenen Plattformen verifiziert sind,
5. dieses Dokument den neuen Ist-Stand wiedergibt,
6. Capability-Matrix und offene Punkte aktualisiert sind,
7. keine unvalidierten temporären oder Ausgabedateien verbleiben.

## 19. Aktueller Implementierungsstand

| Bereich | Status | Verifiziert |
|---|---|---|
| Anforderungen | Baseline erstellt | 2026-07-19 |
| Solution und Projekte | Teilweise verifiziert | Windows x64: .NET SDK 10.0.302; normaler Solution-Build erzeugt den MSVC-`RelWithDebInfo`-Wrapper inkrementell, führt dessen ABI- und EXIF-Tests aus und kopiert DLL/PDB transitiv zu Desktop, CLI und Testhost; vollständige Solution mit 157 Tests am 2026-07-20 verifiziert |
| Domain-Modell | In Arbeit | Windows x64: Job-Invarianten und Statusübergänge |
| Native Wrapper/Interop | In Arbeit | Windows x64: C-ABI v3, Capability-Metadaten, Status-/Fehlerübersetzung und kooperatives Abbruch-Handle; ABI-Smoke-Test und eigener EXIF-Modultest (`piccompressor_exif_tests`); gepinntes Jpegli 0.12.0 mit skcms, libpng und zlib statisch eingebunden; UCRT64- und MSVC-`RelWithDebInfo`-Build am 2026-07-20 verifiziert. Reines MSVC `Debug` blockiert beim Encoding und ist ausgeschlossen; Guetzli ist noch nicht eingebunden |
| Jpegli-Adapter | In Arbeit | Windows x64: reale PNG-/JPEG-Dekodierung und JPEG-Erzeugung über P/Invoke sowie vollständiger CLI-Einzeldateipfad verifiziert; Qualität, Chroma, Progressive-Level und Alpha-Hintergrund werden übertragen. Alle Metadatenrichtlinien aus 8.3 sind umgesetzt: EXIF-Orientierung wird auf die Pixel angewendet (Orientierungen 1–8 gegen den echten Wrapper getestet), `Keep`/`Private`/`Remove` sowie `Preserve`/`Srgb`/`Remove` werden über die ABI übertragen. Die Ablehnung von `Farbprofil entfernen` bei Nicht-sRGB-Eingaben ist implementiert, aber mangels Testbild mit fremdem ICC-Profil noch nicht automatisiert verifiziert |
| Guetzli-Adapter | Nicht begonnen | – |
| Sichere Dateipipeline | In Arbeit | Windows x64: Eingabeprüfung, Datei-/Pixelgrenzen, Kollisionsplanung, reservierte temporäre Dateien, bedarfsgerechtes Anlegen verschachtelter Zielverzeichnisse, strukturelle JPEG-Ausgabeprüfung, `discard`/`keep`-Veröffentlichung sowie gemeinsamer Einzeljob-Anwendungsfall mit Fehler-/Abbruchbereinigung |
| Queue und Ressourcensteuerung | In Arbeit | Windows x64: gemeinsame Application-Batchausführung mit konfigurierbarer harter Parallelitätsgrenze, stabiler Ergebnisreihenfolge, geordneten Statusmeldungen (`WaitingForResources`, `Encoding`, `Finalizing` und terminale Zustände) und `Canceled`-Ergebnissen für noch wartende Jobs; GUI-Wiederholung erzeugt einen neuen Job mit Vorgängerreferenz. CPU-/speichergewichtete Budgets bleiben bis O-005 offen |
| CLI | In Arbeit | Windows x64: mehrere Datei- und Ordnereingaben, optionale Rekursion ohne Verfolgung von Verzeichnislinks, Erhalt relativer Unterordner, Ausschluss des Ausgabeordners, `dry-run`, begrenzte Parallelität, Jpegli-Optionen, `--exif` und `--color-profile`, menschenlesbare/JSON-Ausgabe, `Ctrl+C` und Batch-Exit-Codes; History fehlt |
| GUI | In Arbeit | Windows x64: Avalonia-12-Shell (Titelzeile, Navigationsschiene mit kompaktem Zustand, Statuszeile), Light-/Dark-Farbrollen der Vorlage, vier Ansichten (Arbeitsbereich, Einstellungen, Verlauf, Vergleich) sowie Drag-and-drop und Dateiauswahl gebaut und gestartet. Oberflächentexte vollständig aus `Strings.resx`/`Strings.de.resx`; Sprachwechsel Deutsch/Englisch und Designwechsel System/Hell/Dunkel zur Laufzeit verifiziert. Der Desktop Host bindet reale Jpegli-Kompression, Engine-Erkennung, Application-Queue, Fortschritt, Abbruch, Wiederholung und lokalen Verlauf an die ViewModels an. Vorschauerzeugung sowie Persistenz von Sprach-, Design- und Kompressionseinstellungen fehlen |
| History und Logging | In Arbeit | Windows x64: lokaler GUI-Verlauf über eine versionierte SQLite-Datenbank (`user_version = 1`) im Anwendungsdatenverzeichnis; transaktionales Schreiben, serialisierter Zugriff und erneutes Öffnen getestet. Gespeichert wird nur der Dateiname statt des vollständigen Eingabepfads. CLI-Anbindung, Löschung/Aufbewahrung, Migration aus älteren Schemata und strukturiertes Logging fehlen |
| Packaging und Plattformmatrix | In Arbeit | Windows x64: Release `0.2.0-alpha.1` mit separatem Desktop-Composition-Root, verifiziertem `VelopackApp.Build().Run()`, selbstenthaltendem Desktop-/CLI-Publish, gepinntem VeloPack-1.2.0-Workflow ohne Bootstrap-Prüf-Bypass, UCRT64-Native-Build, ABI-/Portable-CLI-Smoke, One-Click-Installer, Portable ZIP, Updatepaket, Lizenzen und validierten SHA256-Manifesten erzeugt. GUI-Updateoberfläche, Signierung, Rollbacktests und weitere RIDs fehlen |

Statuswerte: `Nicht begonnen`, `In Arbeit`, `Teilweise verifiziert`, `Verifiziert`, `Blockiert`, `Verworfen`.

## 20. Offene Entscheidungen

| ID | Entscheidung | Benötigte Evidenz |
|---|---|---|
| O-001 | Jpegli-Pin `031a0077f5799a6041004267fc12b956c1f52a20` freigeben | reproduzierbarer Build und Referenztests je Runtime |
| O-002 | Guetzli-Unterstützung je Plattform | verfügbare, lizenzkonforme, getestete Library-Einbindungen |
| O-007 | Positivliste der bei `EXIF privat` erhaltenen Tags | Abgleich mit realen Kameramodellen; die aktuelle Liste ist bewusst eng und kann fachlich nützliche Felder verwerfen |
| O-004 | Standardwerte der Qualitätsprofile | Vergleichstest mit repräsentativem Bildkorpus |
| O-005 | Ressourcenbudgets | Benchmarks nach Pixelzahl, Engine und Runtime |
| O-006 | Signing-Dienst und Hosting für direkte Releases | Signaturtest mit Azure Artifact Signing, Hostingkosten, Rollback und Wartungsaufwand |

Offene Entscheidungen dürfen nicht durch stillschweigende Implementierungsdetails vorweggenommen werden.

## 21. Architekturentscheidungen

| ID | Datum | Entscheidung | Grund |
|---|---|---|---|
| D-001 | 2026-07-18 | Jpegli ist Standard; Guetzli ist optionales Legacy-Modul | Guetzli ist archiviert und ressourcenintensiv |
| D-002 | 2026-07-18 | Native Engines werden zunächst als isolierte Prozesse integriert; ersetzt durch D-010 | frühere Annahme zur Fehlerisolation und zum Packaging |
| D-003 | 2026-07-18 | .NET 10 LTS und Avalonia 12 bilden die Basis | aktuelle LTS-Basis und Desktop-Unterstützung |
| D-004 | 2026-07-18 | SQLite speichert den gemeinsamen lokalen Verlauf | Transaktionen, Migrationen und nebenläufiger Zugriff |
| D-005 | 2026-07-18 | Ausgabe erfolgt über validierte temporäre Dateien | Schutz vor beschädigten oder verlorenen Originalen |
| D-006 | 2026-07-19 | Fehlerkategorien sind eine geschlossene, normative Menge (6.4) | Abschnitt 4.3 fordert plattformidentische Kategorien; ohne Liste nicht prüfbar |
| D-007 | 2026-07-19 | Nicht kleinere Ausgaben werden standardmäßig verworfen (`discard`) | ein stillschweigend vergrößertes Bild widerspricht dem Produktziel |
| D-008 | 2026-07-19 | Kollisionsrichtlinie ist `skip`/`rename`/`overwrite`, Standard `skip` | Überschreibschutz muss der Default sein |
| D-009 | 2026-07-19 | Wiederholung erzeugt einen neuen Job mit Vorgängerreferenz | Jobs sind unveränderlich und terminale Zustände endgültig |
| D-010 | 2026-07-19 | Jpegli und Guetzli werden als Bibliotheken über einen gemeinsamen In-Process-C++-Wrapper mit versionierter C-ABI aufgerufen | keine Encoder-Binaries; eine kontrollierte Plattformgrenze für beide Libraries |
| D-011 | 2026-07-19 | Der erste Wrapper-Build pinnt Jpegli auf `031a0077f5799a6041004267fc12b956c1f52a20` und Guetzli auf v1.0.1 (`a0f47a297f802630f937a3091964838eaf3b87d8`) | reproduzierbare Quellbasis; Freigabe bleibt bis zu Referenztests offen |
| D-012 | 2026-07-19 | Die verlinkte interaktive HTML-Vorlage ist die visuelle Referenz für die native Avalonia-GUI, aber keine Laufzeitkomponente | einheitliches UI-Zielbild ohne WebView-Abhängigkeit |
| D-013 | 2026-07-19 | Der CLI-Composition-Root verdrahtet die konkreten Engine-, Infrastructure- und Interop-Adapter; CLI-Parsing und Ausgabe bleiben frei von Infrastrukturverhalten | ausführbare Host-Komposition ohne Abhängigkeiten der Application-Schicht nach außen |
| D-014 | 2026-07-19 | C-ABI v2 ergänzt den expliziten RGB-Hintergrund für Alpha-Flattening; Jpegli wird aus der gepinnten Revision mit gepinnten Submodulen statisch in den Wrapper gelinkt | PNG-Transparenz darf nicht implizit behandelt werden; das Paket darf keine global installierten Encoder-Bibliotheken voraussetzen |
| D-015 | 2026-07-19 | Native Komponenten werden pro RID vor dem Managed Publish gebaut und über einen expliziten MSBuild-Pfad neben den Host kopiert; das Paket enthält Lizenzen und ein SHA256-Manifest | keine Laufzeitabhängigkeit von `PATH` oder zufälligen lokalen Buildverzeichnissen |
| D-018 | 2026-07-19 | Direkte Releases verwenden VeloPack; der Microsoft Store bleibt ein separater MSIX-Kanal; GUI und CLI liegen im selben RID-Paket, die GUI ist Update-Einstiegspunkt | plattformübergreifender direkter Releaseweg mit Delta-Updates ohne Vermischung mit Store-Updateinfrastruktur |
| D-016 | 2026-07-19 | Die GUI folgt der UI-Vorlage in Layout, Farbrollen und Informationshierarchie, weicht aber dort ab, wo die Vorlage diesem Dokument widerspricht (Tabelle in 11.1); die GUI hängt nur von Domain und Application ab und nutzt keine zusätzliche MVVM-Bibliothek | Produktanforderungen haben nach 11.1 Vorrang; die Schichtregel aus 14.1 verbietet der GUI direkte Infrastruktur- und Interop-Abhängigkeiten |
| D-017 | 2026-07-19 | Oberflächentexte liegen ausschließlich in `.resx`-Ressourcen; Sprache (System/Englisch/Deutsch, Startwert Systemsprache mit Rückfall auf Englisch) und Design (System/Hell/Dunkel, Standard System) wirken ohne Neustart | eine feste Sprache im Quelltext lässt sich nicht prüfen und nicht erweitern; ein Neustartzwang widerspricht der Bedienbarkeitsanforderung aus Abschnitt 11 |
| D-019 | 2026-07-19 | Ein eigener Desktop Host ist Composition Root und VeloPack-Einstiegspunkt; das GUI-Projekt bleibt eine von Infrastruktur und Interop unabhängige Präsentationsbibliothek | die Schichtregel bleibt erhalten, während installierte GUI, reale Adapter und Update-Lifecycle in einem ausführbaren Host zusammengeführt werden |
| D-021 | 2026-07-20 | C-ABI v3 überträgt EXIF- und Farbprofilrichtlinie; die Farbtransformation nach sRGB nutzt das bereits statisch eingebundene skcms. Schließt O-003 | eine weitere Farbmanagement-Bibliothek wäre eine zusätzliche native Abhängigkeit ohne fachlichen Mehrwert, da skcms bereits über `jpegli_cms` im Paket liegt |
| D-022 | 2026-07-20 | `EXIF privat` filtert über eine Positivliste und serialisiert den TIFF-Blob neu; der Thumbnail-IFD entfällt | eine Sperrliste lässt unbekannte und herstellerspezifische Felder durch, und das bloße Entfernen von Verweisen ließe die Nutzdaten im Puffer stehen |
| D-023 | 2026-07-20 | Der mitgelieferte libpng-Build verwendet immer die vorgefertigte `pnglibconf.h`; die AWK-basierte Neugenerierung wird abgeschaltet | sie lief nur, wenn zufällig ein AWK im PATH lag, und scheiterte dann unter dem Visual-Studio-Generator an fehlenden zlib-Headern. Der Build war damit vom Entwicklerrechner abhängig und widersprach der Reproduzierbarkeitsvorgabe aus Abschnitt 15 |
| D-020 | 2026-07-19 | Lokale Windows-Debug-Builds verwenden MSVC `RelWithDebInfo` mit PDB und werden inkrementell vom Managed-Interop-Projekt ausgelöst; reines MSVC `Debug` ist wegen eines reproduzierten Encoding-Hängers ausgeschlossen; Windows-Releases bleiben beim gepinnten UCRT64-/MinGW-Build | Visual Studio kann Managed- und Native-Code gemeinsam debuggen, ohne einen blockierenden Upstream-Build oder eine stillschweigend geänderte Release-Toolchain einzuführen |

## 22. Pflege dieses Dokuments

Dieses Dokument beschreibt den aktuellen Stand, nicht nur das ursprüngliche Ziel.

Bei jeder fachlichen oder architektonischen Änderung MÜSSEN im selben Change:

1. betroffene Anforderungen angepasst,
2. der Implementierungsstand aktualisiert,
3. neue oder geänderte Entscheidungen eingetragen,
4. offene Punkte geschlossen oder ergänzt,
5. das Datum der fachlichen Prüfung nur nach tatsächlicher Gesamtprüfung geändert werden.

Planung darf nicht als implementiert und Implementierung nicht als plattformübergreifend verifiziert bezeichnet werden, solange die entsprechende Evidenz fehlt. Veraltete Passagen werden ersetzt; widersprüchliche historische Varianten bleiben nicht im Haupttext.

## 23. Primärquellen

- [Jpegli](https://github.com/google/jpegli)
- [Guetzli](https://github.com/google/guetzli)
- [Avalonia – unterstützte Plattformen](https://docs.avaloniaui.net/docs/supported-platforms)
- [.NET-Support-Richtlinie](https://dotnet.microsoft.com/platform/support/policy)
- [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/)
- [VeloPack – Packaging](https://docs.velopack.io/packaging/overview)
- [VeloPack – C#-Integration](https://docs.velopack.io/getting-started/csharp)
- [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite)
