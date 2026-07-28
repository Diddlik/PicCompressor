[English](README.md) · **Deutsch** · [Русский](README.ru.md)

# PicCompressor

Lokale, datenschutzfreundliche JPEG-Komprimierung für Desktop und Kommandozeile.

## Über PicCompressor

PicCompressor konvertiert JPEG- und PNG-Bilder mit Jpegli in kompakte JPEG-Dateien. Die Verarbeitung bleibt auf deinem Computer; Bilder werden nicht hochgeladen.

Desktop-Anwendung und CLI verwenden dieselbe Kompressionspipeline und dieselben Sicherheitsregeln.

## Funktionen

- JPEG-Komprimierung mit Jpegli
- Jpegli ist der einzige Encoder; die frühere Guetzli-Integration wurde wegen unverhältnismäßiger Laufzeit und Ressourcennutzung entfernt
- Desktop-GUI und skriptfähige CLI
- Einzeldateien, Stapel und rekursive Ordner
- Drag-and-drop, Abbruch, Wiederholung und lokaler Verlauf
- Vorher-Nachher-Vorschau, Queue-Vorschaubilder, Statistiken und Updateprüfung
- Einstellbare Qualität, Chroma-Subsampling und progressive Kodierung
- Regeln für EXIF-Datenschutz und Farbprofile
- Validierte temporäre Ausgabe vor Veröffentlichung der Zieldatei
- Optionales, deutlich gewarntes Ersetzen von JPEG-Originalen nach der Validierung
- Kollisionsschutz und explizites Überschreibverhalten
- Menschenlesbare und versionierte JSON-Ausgabe der CLI

## Aktueller Stand

PicCompressor befindet sich in der Alpha-Entwicklung. GUI, CLI und die native Jpegli-Pipeline sind derzeit unter Windows x64 verifiziert. Ein selbstenthaltendes Linux-x64-CLI-Paket lässt sich bauen und besteht seine Tests; GUI-, macOS- und `linux-arm64`-Pakete sind geplant, aber noch nicht verifiziert.

Die Anwendung unterstützt aktuell Englisch und Deutsch. Die russische README-Übersetzung bedeutet nicht, dass die Benutzeroberfläche bereits Russisch unterstützt.

Vorabpakete für Windows x64 werden über GitHub Releases veröffentlicht. Sie sind derzeit unsigniert und lösen eine SmartScreen-Warnung aus; Signierung und weitere Freigabeprüfungen stehen noch aus.

## Schnellstart

Voraussetzungen für den aktuellen Windows-Entwicklungsbuild:

- .NET SDK `10.0.302`
- Visual Studio mit .NET-Desktopentwicklung und Desktopentwicklung mit C++
- CMake `3.25` oder neuer
- PowerShell 7

```powershell
dotnet build PicCompressor.slnx
dotnet run --project src/PicCompressor.Desktop/PicCompressor.Desktop.csproj
```

Der erste Build lädt die gepinnten Jpegli-Quellen herunter und kompiliert sie.

## Veröffentlichung

Releases liegen auf GitHub Releases. Ein Versionstag baut das win-x64-Paket und veröffentlicht es über den `release`-Workflow (`.github/workflows/release.yml`):

```powershell
git tag v0.2.0-alpha.1
git push origin v0.2.0-alpha.1
```

## CLI

```text
piccompressor <Eingabe> [<Eingabe> ...] [Optionen]
```

Beispiel:

```powershell
piccompressor "C:\Bilder" --recursive --quality 80 --output-dir "C:\Bilder\komprimiert"
```

Alle Optionen anzeigen:

```powershell
piccompressor --help
```

Für wiederkehrende Ordnerscans (etwa als NAS-Aufgabe) überspringt `--state` unveränderte
Eingaben als erfolgreichen No-op, `--lock` verhindert überlappende Läufe, und `--stable-for`
lässt noch hochgeladene Dateien für einen späteren Lauf liegen:

```bash
piccompressor /volume1/photo --recursive --output-dir /volume1/photo-compressed \
  --state /var/piccompressor/scan-state.json --lock /var/piccompressor/scan.lock \
  --stable-for 120 --parallelism 2 --timeout 300 --no-history
```

## Docker

`--config <Pfad>` lässt die CLI dauerhaft über beliebig viele überwachte Ordner laufen, jeder mit
eigenem Ausgabeordner, eigener Zustandsdatei und wahlweise eigenen Kompressionseinstellungen.
`mode` bestimmt den Taktgeber: `Interval` scannt alle `intervalSeconds` erneut, `Watch` reagiert
zusätzlich auf entprellte Dateisystemereignisse und scannt trotzdem periodisch, weil `inotify` auf
eingehängten SMB-/NFS-Freigaben keine Ereignisse liefert. `docker stop` beendet den Container
geordnet: der laufende Job wird fertig, und es bleibt keine ungeprüfte Ausgabe zurück.

Das fertige Image liegt in der GitHub Container Registry und wird bei jedem Push auf `main` und
bei jedem Versions-Tag neu gebaut (`.github/workflows/container.yml`):

```bash
docker pull ghcr.io/diddlik/piccompressor:latest
docker compose -f docker/docker-compose.yml up -d
```

Verfügbare Tags: `latest` (letzte endgültige Version), `X.Y.Z` und `X.Y` je Versions-Tag sowie
`main` als jeweils aktueller Entwicklungsstand. Docker aktualisiert von sich aus nichts; ein neues
Image holt `docker compose pull && docker compose up -d` — per Zeitplan oder über den in
[docker/docker-compose.yml](docker/docker-compose.yml) vorbereiteten Watchtower-Dienst.

Lokal bauen statt ziehen:

```bash
docker build -f docker/Dockerfile -t piccompressor .
```

Das Image erwartet die Konfiguration unter `/config/piccompressor.json`, die überwachten Ordner
unterhalb von `/data` und ein beschreibbares `/state` für Zustand, Lock und Log. Eine vollständige
Datei steht in [docker/piccompressor.example.json](docker/piccompressor.example.json). Image und
Container laufen mit der UID/GID, der die eingehängten Ordner gehören; im Container läuft nie
`root`. `--once` führt genau einen Zyklus über alle Ordner aus und beendet sich — der Weg, dieselbe
Konfiguration von einem externen Zeitplaner starten zu lassen.

Gebaut und geprüft ist nur `linux/amd64`; `linux/arm64` ist über `TARGETARCH` vorbereitet, aber
ungeprüft. Im Container läuft die CLI, nicht die grafische Oberfläche.
