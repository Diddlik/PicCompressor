# PicCompressor in Visual Studio debuggen

## Voraussetzungen

- Visual Studio mit den Workloads **.NET-Desktopentwicklung** und **Desktopentwicklung mit C++**
- .NET SDK gemäß `global.json`
- CMake ab Version 3.25
- PowerShell 7 (`pwsh`)
- Git für den ersten Abruf der gepinnten Jpegli-Quellen

MSYS2 ist nur für den Release-Build erforderlich. Der lokale Visual-Studio-Debug-Build verwendet MSVC `RelWithDebInfo` und erzeugt passende `.pdb`-Symbole. Jpeglis reine MSVC-`Debug`-Konfiguration wird nicht verwendet, weil ihr Encoding mit der derzeit gepinnten Revision blockiert; der Build- und Testpfad erzwingt stattdessen die verifizierte symbolbehaftete Konfiguration.

## F5-Workflow

1. `PicCompressor.slnx` öffnen.
2. `PicCompressor.Desktop` als Startprojekt wählen.
3. Profil **PicCompressor Desktop** für schnelles Managed Debugging oder **PicCompressor Desktop (Managed + Native)** für gemischtes C#-/C++-Debugging wählen.
4. F5 drücken.

Beim ersten Build werden die gepinnten Jpegli-Quellen abgerufen und der Wrapper einschließlich ABI-Smoke-Test gebaut. Spätere Builds verwenden MSBuild-Eingaben und -Ausgaben: Der native Build läuft nur erneut, wenn die DLL fehlt oder sich Wrapper, Header, Tests, CMake-Konfiguration beziehungsweise Dependency-Pin geändert haben.

Debug-Artefakte:

- `artifacts/native/win-x64/Debug/piccompressor_native.dll`
- `artifacts/native/win-x64/Debug/piccompressor_native.pdb`
- Kopien neben `PicCompressor.Desktop.exe`, `PicCompressor.Cli.exe` und betroffenen Testhosts

Der Release-Build verwendet getrennte Zwischenartefakte unter `artifacts/native/win-x64/Release-Mingw` und löscht den Visual-Studio-Debug-Build nicht.

Native Breakpoints können direkt in `native/src/piccompressor_native.cpp` gesetzt werden. Unter **Debuggen > Fenster > Module** muss für `piccompressor_native.dll` das benachbarte PDB als geladen erscheinen. Microsoft beschreibt die Option als „Enable unmanaged code debugging“ im Launch-Profil.

## Native Komponente separat bauen

```powershell
pwsh ./scripts/build-native-win-x64.ps1 -Configuration RelWithDebInfo -Toolchain Msvc
```

Der öffentliche Windows-Release bleibt reproduzierbar auf dem gepinnten UCRT64-/MinGW-Weg:

```powershell
pwsh ./scripts/publish-win-x64.ps1 -Version 0.2.0-alpha.1
```

Ein explizit vorgebauter Wrapper kann weiterhin angegeben werden; damit wird der automatische MSVC-Build abgeschaltet:

```powershell
dotnet build PicCompressor.slnx `
  -p:PicCompressorNativeLibrary=C:\path\to\piccompressor_native.dll
```

Nur für Managed-Analyse kann der automatische Build deaktiviert werden. Die Hosts sind dann ohne bereitgestellte DLL nicht ausführbar:

```powershell
dotnet build PicCompressor.slnx -p:PicCompressorBuildNative=false
```

## Funktionstest

1. PNG und JPEG in den Arbeitsbereich ziehen.
2. Einen Job erfolgreich komprimieren und die JPEG-Ausgabe öffnen.
3. Einen laufenden Job abbrechen.
4. Einen fehlgeschlagenen oder abgebrochenen Job wiederholen.
5. Anwendung neu starten und den persistenten Verlauf prüfen.

Release-, Plattform- und Sicherheitsgates bleiben in der [Release-Anleitung](releasing.md) verbindlich.
