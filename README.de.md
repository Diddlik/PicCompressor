[English](README.md) · **Deutsch** · [Русский](README.ru.md)

# PicCompressor

Lokale, datenschutzfreundliche JPEG-Komprimierung für Desktop und Kommandozeile.

## Über PicCompressor

PicCompressor konvertiert JPEG- und PNG-Bilder mit Jpegli in kompakte JPEG-Dateien. Die Verarbeitung bleibt auf deinem Computer; Bilder werden nicht hochgeladen.

Desktop-Anwendung und CLI verwenden dieselbe Kompressionspipeline und dieselben Sicherheitsregeln.

## Funktionen

- JPEG-Komprimierung mit Jpegli
- Desktop-GUI und skriptfähige CLI
- Einzeldateien, Stapel und rekursive Ordner
- Drag-and-drop, Abbruch, Wiederholung und lokaler Verlauf
- Einstellbare Qualität, Chroma-Subsampling und progressive Kodierung
- Regeln für EXIF-Datenschutz und Farbprofile
- Validierte temporäre Ausgabe vor Veröffentlichung der Zieldatei
- Kollisionsschutz und explizites Überschreibverhalten
- Menschenlesbare und versionierte JSON-Ausgabe der CLI

## Aktueller Stand

PicCompressor befindet sich in der Alpha-Entwicklung. Derzeit ist Windows x64 verifiziert. macOS, Linux und weitere Architekturen sind geplant, aber noch nicht verifiziert.

Die Anwendung unterstützt aktuell Englisch und Deutsch. Die russische README-Übersetzung bedeutet nicht, dass die Benutzeroberfläche bereits Russisch unterstützt.

Öffentliche Installer werden noch nicht angeboten, weil Release-Signierung und weitere Sicherheitsprüfungen ausstehen.

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
