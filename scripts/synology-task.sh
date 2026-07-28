#!/bin/sh
# Vorlage für einen DSM-Aufgabenplaner-Task (MP-005). Sie trifft keine Annahme über ein
# interaktives Benutzerprofil: alle Pfade sind absolut, und der Task läuft ohne `root` unter
# einem Benutzer mit Lesen im Fotoordner sowie Schreiben in Ausgabe-, Zustands- und
# Logverzeichnis. Vor dem Einrichten die vier Pfade anpassen.
#
# Exit-Codes: 0 = Erfolg oder nichts zu tun, 4 = einzelne Dateien fehlgeschlagen,
# 8 = Datei-/Ausgabefehler. Ein bereits laufender Scan endet mit 0, ohne erneut zu kodieren.
#
# Diese Vorlage ist auf realer Synology-Hardware noch nicht verifiziert.
set -eu

PICCOMPRESSOR_HOME=/volume1/apps/piccompressor
INPUT_DIRECTORY=/volume1/photo
OUTPUT_DIRECTORY=/volume1/photo-compressed
WORK_DIRECTORY=/volume1/apps/piccompressor/var

CLI="$PICCOMPRESSOR_HOME/PicCompressor.Cli"
if [ ! -x "$CLI" ]; then
    echo "PicCompressor CLI not found or not executable: $CLI" >&2
    exit 1
fi

mkdir -p "$OUTPUT_DIRECTORY" "$WORK_DIRECTORY"

# --state macht wiederkehrende Scans idempotent, --lock verhindert überlappende Läufe und
# --stable-for lässt Dateien liegen, die gerade hochgeladen oder synchronisiert werden.
# Parallelität und Zeitlimit sind für einen Hintergrundlauf bewusst begrenzt; der lokale
# Verlauf bleibt aus, weil ein Scheduler-Benutzer kein Anwendungsdatenverzeichnis braucht.
exec "$CLI" "$INPUT_DIRECTORY" \
    --recursive \
    --output-dir "$OUTPUT_DIRECTORY" \
    --state "$WORK_DIRECTORY/scan-state.json" \
    --lock "$WORK_DIRECTORY/scan.lock" \
    --log "$WORK_DIRECTORY/piccompressor.jsonl" \
    --stable-for 120 \
    --parallelism 2 \
    --timeout 300 \
    --no-history
