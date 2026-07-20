# Gemeinsame Agent-Instruktionen

Diese Regeln gelten für alle Coding- und Dokumentationsagenten in diesem Repository.

## Verbindliche Quellen

1. `docs/requirements.md` ist die zentrale Quelle für Produktanforderungen, Architekturentscheidungen und Implementierungsstatus.
2. Diese Datei regelt die Arbeitsweise der Agenten.
3. `CLAUDE.md` und `AGENTS.md` sind Einstiegspunkte und dürfen die gemeinsamen Regeln nicht duplizieren.

Bei Widersprüchen zwischen Code und Spezifikation gilt nicht automatisch eine Seite als richtig. Ermittle den tatsächlichen Stand. Korrigiere Code oder Spezifikation im selben Change; verschleiere keine Abweichung.

## Vor jeder Änderung

- Lies die für die Aufgabe relevanten Abschnitte in `docs/requirements.md`.
- Prüfe den realen Repository- und Implementierungsstand.
- Ordne die Änderung einer fachlichen Anforderung und Architekturschicht zu.
- Prüfe Auswirkungen auf Windows, macOS und Linux.
- Bewahre nicht zusammenhängende Änderungen des Benutzers.

## Architekturregeln

- Domain und Application bleiben unabhängig von Avalonia, CLI, SQLite und nativen Prozessen.
- GUI und CLI verwenden dieselben Application-Anwendungsfälle.
- Native Encoder sind ausschließlich über Engine-Adapter und den versionierten C++-Wrapper mit stabiler C-ABI erreichbar.
- Plattformunterschiede werden über Capabilities modelliert, nicht über verteilte Sonderfälle.
- Datenintegrität, Validierung, Abbruch, Sicherheit und Accessibility dürfen nicht zugunsten kleinerer Änderungen entfallen.
- Neue Abstraktionen brauchen einen aktuellen fachlichen Bedarf oder eine echte Test-/Systemgrenze.
- Guetzli bleibt optional; keine neue Kernfunktion darf ausschließlich davon abhängen.

## Lebende Spezifikation

Wenn eine Änderung den beschriebenen Stand beeinflusst, aktualisiere im selben Change:

- betroffene Anforderungen,
- `Aktueller Implementierungsstand`,
- `Offene Entscheidungen`,
- `Architekturentscheidungen`,
- Plattform- oder Capability-Aussagen.

Markiere nur aktuell verifizierte Tatsachen als `Verifiziert`. Nenne Plattformen, Tests und Artefakte, die die Aussage tragen. Ändere das Datum der fachlichen Gesamtprüfung nur nach einer vollständigen Prüfung des Dokuments.

Keine zweite Anforderungsliste in anderen Dateien anlegen. Verlinken statt kopieren.

## Umsetzung

- Bevorzuge die kleinste vollständige Änderung.
- Verwende Standardbibliothek und vorhandene Abhängigkeiten vor neuen Paketen.
- Validiere Eingaben an Datei-, Prozess-, Konfigurations- und CLI-Grenzen.
- Schreibe Ausgaben zuerst temporär und veröffentliche sie erst nach erfolgreicher Validierung.
- Starte native Programme nie über einen Shell-Interpreter.
- Protokolliere keine Bildinhalte oder unnötigen sensiblen Metadaten.
- Behandle Abbruch und Fehler als reguläre Produktzustände.

## Lokalisierung

Diese Regeln gelten für jede benutzersichtbare Zeichenkette in GUI und CLI.

- Keine sichtbare Zeichenkette wird im Quelltext oder in XAML fest verdrahtet. Sie stammt aus den Ressourcen unter `src/PicCompressor.Gui/Localization/Strings*.resx`.
- Jeder neue Text wird im selben Change in **allen** gepflegten Sprachen ergänzt: `Strings.resx` (Englisch, neutral) und `Strings.de.resx` (Deutsch). Ein Schlüssel ohne Übersetzung ist ein Fehler, kein offener Punkt.
- Schlüssel folgen `Bereich_Bezeichnung` und beschreiben die Bedeutung, nicht den Text.
- In XAML wird `{l:Localize Schlüssel}` verwendet, in ViewModels `Localizer.Instance[...]` beziehungsweise `Localizer.Instance.Format(...)`.
- Ein Sprachwechsel MUSS ohne Neustart wirken. Berechnete Texte in ViewModels melden sich über `ObservableObject` neu; wer `INotifyPropertyChanged` selbst implementiert, meldet sie ebenfalls.
- Texte werden nicht aus Teilstücken zusammengesetzt. Variable Anteile laufen über Platzhalter (`{0}`) im Ressourcentext.
- Ausgenommen sind Eigennamen (`PicCompressor`, `Jpegli`, `Guetzli`), stabile Bezeichner (Fehlerkategorien, Exit-Codes, JSON-Felder, Engine-IDs) und Logtexte. Diese bleiben unübersetzt und plattformidentisch.
- Startsprache ist die Betriebssystemsprache; liegt dafür keine Übersetzung vor, gilt Englisch.
- Zahlen, Größen und Zeitpunkte werden über die aktive Kultur formatiert, nicht über feste Formate.

## Verifikation

Vor einer Abschlussbehauptung:

- führe die kleinsten aussagekräftigen Tests aus,
- prüfe Formatierung und statische Analyse,
- teste betroffene CLI-Exit-Codes,
- teste Datenintegrität bei Fehler und Abbruch,
- verifiziere jede behauptete Zielplattform oder kennzeichne sie als ungeprüft,
- prüfe, dass kein neuer sichtbarer Text fest verdrahtet ist und jeder Schlüssel in allen Sprachen vorliegt,
- prüfe die Konsistenz von `docs/requirements.md`.

Wenn Verifikation technisch nicht möglich ist, dokumentiere exakt, was nicht geprüft wurde und warum.

## Dokumentation

- Dokumentiere Verhalten und Invarianten, keine flüchtigen Quellcodezeilen.
- Halte den Haupttext aktuell; historische Entscheidungen gehören in die Entscheidungstabelle.
- Verwende stabile Requirement- oder Decision-IDs, wenn eine Änderung sonst schwer nachvollziehbar wäre.
- Beispiele dürfen verbindliche Regeln nicht ersetzen.
