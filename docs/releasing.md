# PicCompressor veröffentlichen

## 1. Ziel

Diese Anleitung richtet sich an Maintainer, die einen prüfbaren PicCompressor-Release für Endanwender erzeugen. Nach dem Lesen kann ein Maintainer den Windows-x64-Build erstellen, die Artefakte prüfen und erkennen, welche Sicherheitsgates vor einer öffentlichen Veröffentlichung noch fehlen.

PicCompressor verwendet für direkte Downloads VeloPack. Der Microsoft Store bleibt ein separater MSIX-Kanal. VeloPack ersetzt weder den nativen Build noch Lizenz-, Prüfsummen- oder Signaturprüfungen, sondern verpackt deren bereits geprüftes Ergebnis.

## 2. Derzeit erzeugte Artefakte

Der Windows-Workflow erzeugt:

- eine selbstenthaltende .NET-Anwendung für `win-x64`,
- den Desktop Host mit Avalonia-GUI als Installations- und Update-Einstiegspunkt,
- die CLI im selben Paket,
- den statisch gebauten nativen Jpegli-Wrapper,
- Lizenztexte für Jpegli, Highway, skcms, libpng und zlib,
- ein Komponentenmanifest mit SHA256-Prüfsummen,
- einen VeloPack-One-Click-Installer,
- ein portables ZIP,
- vollständige und gegebenenfalls Delta-Updatepakete,
- einen Release-Index für spätere Updateprüfungen,
- ein separates SHA256-Manifest der äußeren Releaseartefakte.

Das lokale Tool `vpk` und das VeloPack-Laufzeit-SDK sind auf Version `1.2.0` festgelegt. `PicCompressor.Desktop.exe` ruft den erforderlichen VeloPack-Bootstrap vor Avalonia und der Adapterkomposition auf. Eine Benutzeroberfläche für Updateprüfung und Installation ist noch nicht implementiert.

## 3. Voraussetzungen

Der Windows-x64-Workflow benötigt:

- Windows x64,
- PowerShell 7,
- .NET SDK `10.0.302` oder einen kompatiblen Patchstand gemäß `global.json`,
- CMake ab Version 3.25,
- MSYS2 UCRT64 mit `gcc`, `g++` und `mingw32-make`,
- Netzwerkzugriff auf NuGet und die gepinnten Git-Repositories.

Standardmäßig erwartet das Skript MSYS2 unter `C:\msys64\ucrt64`. Ein anderer Ort wird mit `-ToolchainRoot` angegeben.

## 4. Lokalen Release bauen

Versionen müssen SemVer 2 entsprechen. Vierstellige Windows-Dateiversionen sind keine Releaseversionen.

```powershell
pwsh ./scripts/publish-win-x64.ps1 -Version 0.2.0-alpha.1
```

Mit abweichendem MSYS2-Verzeichnis:

```powershell
pwsh ./scripts/publish-win-x64.ps1 `
  -Version 0.2.0-alpha.1 `
  -ToolchainRoot D:\Tools\msys64\ucrt64
```

Der Workflow:

1. baut die exakt gepinnte Jpegli-Revision und ihre Submodule,
2. prüft ABI und native Capability-Metadaten,
3. veröffentlicht Desktop Host und CLI selbstenthaltend für `win-x64`,
4. legt die native DLL neben die Managed Hosts,
5. kopiert die relevanten Lizenztexte,
6. komprimiert ein Referenz-PNG durch die veröffentlichte CLI,
7. erzeugt Komponenten- und Release-Prüfsummen,
8. erstellt mit VeloPack Installer, Portable ZIP und Updateartefakte.

`vpk` prüft dabei den VeloPack-Bootstrap in `PicCompressor.Desktop.exe`; der Workflow verwendet keinen Prüf-Bypass. Ein fehlgeschlagener Schritt beendet den Release. Vorhandene Releaseausgaben werden nur innerhalb des lokalen Artefaktverzeichnisses ersetzt.

## 5. Artefakte prüfen

Die VeloPack-Ausgabe liegt unter `artifacts/releases/win-x64`. Vor einer Weitergabe müssen mindestens geprüft werden:

- `PicCompressor-win-Setup.exe` ist vorhanden,
- das portable ZIP ist vorhanden,
- das vollständige Updatepaket `PicCompressor-<Version>-full.nupkg` ist vorhanden,
- `releases.win.json` ist syntaktisch gültig,
- `release-manifest.json` nennt dieselbe Version und Runtime-ID,
- alle darin aufgeführten SHA256-Prüfsummen stimmen,
- der Installer startet auf einer sauberen Windows-x64-Testmaschine,
- eine PNG- und eine JPEG-Kompression funktionieren nach Installation,
- Deinstallation entfernt Programmdateien, aber keine Benutzerbilder.

Buildverzeichnisse und lokale Artefakte werden nicht eingecheckt.

## 6. Signing und öffentliche Freigabe

Der lokale Workflow erzeugt absichtlich einen **unsignierten** Release. Dieser darf nicht als offizieller Download beworben werden. Windows SmartScreen kann unsignierte Installer blockieren oder deutlich warnen.

Vor einem öffentlichen Release sind erforderlich:

1. Authenticode-Signierung aller ausführbaren Dateien und des Installers,
2. vertrauenswürdiger Zeitstempel,
3. Verifikation der Signaturen nach dem Packaging,
4. Malware- und Schwachstellenscan,
5. Software-Stückliste,
6. Installation, Update, Rollback und Deinstallation auf sauberer Testhardware.

Für direkte Windows-Downloads ist Azure Artifact Signing der bevorzugte Signaturdienst. Geheimnisse und Tokens gehören ausschließlich in den CI-Secret-Store. Sie dürfen weder in Skriptparameter, Logs, Manifeste noch Repository-Dateien geschrieben werden.

## 7. Updatekanäle

Geplant sind:

- `stable` für freigegebene Versionen,
- `beta` für öffentliche Vorabversionen,
- optional `internal` für nicht öffentliche Tests.

Release-Indizes und Pakete können auf statischem HTTPS-Hosting oder GitHub Releases liegen. Die Anwendung darf Updates erst anbieten, wenn:

- Releaseindex und Pakete über HTTPS erreichbar sind,
- Pakete signiert sind,
- Kanalwechsel explizit erfolgen,
- Download, Signaturprüfung, Abbruch, Rollback und Neustart getestet sind.

CLI-Aufrufe führen niemals selbstständig ein Update aus. Updateprüfung und Benutzerentscheidung gehören in die GUI.

## 8. Weitere Plattformen

### Microsoft Store

Der Store erhält später ein separates MSIX-Paket. Store-Signierung und Store-Updates ersetzen dort VeloPack-Updates. Die Anwendung darf nicht beide Updatequellen gleichzeitig verwenden.

### macOS

Direkte Downloads können mit VeloPack als `.pkg` und portables Archiv erzeugt werden. Signierung und Notarisierung müssen auf macOS mit Apple-Werkzeugen erfolgen. Der Mac App Store benötigt einen getrennten, sandbox-kompatiblen Paket- und Updatepfad.

### Linux

VeloPack erzeugt ein AppImage. Flatpak kann später als zusätzlicher Distributionskanal hinzukommen. Distro-Pakete und AppImage dürfen dieselbe Installation nicht gegenseitig aktualisieren.

## 9. Bekannte Grenzen

- Der aktuelle Workflow ist nur auf Windows x64 verifiziert.
- Releaseartefakte sind noch nicht signiert.
- Die VeloPack-GUI für Updateprüfung, Benutzerentscheidung und Installation ist noch nicht integriert.
- Es existiert noch kein Rollback- oder Update-End-to-End-Test.
- Eine vollständige SBOM und automatisierte Schwachstellenprüfung fehlen.
- Installer-Grafiken und finales Anwendungssymbol fehlen.

## 10. Primärquellen

- [VeloPack Packaging](https://docs.velopack.io/packaging/overview)
- [VeloPack Signing](https://docs.velopack.io/packaging/signing)
- [VeloPack Runtime-IDs](https://docs.velopack.io/packaging/runtime)
- [Microsoft Code-Signing-Optionen](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
