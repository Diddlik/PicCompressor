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
