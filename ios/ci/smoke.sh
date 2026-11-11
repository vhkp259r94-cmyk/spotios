#!/usr/bin/env bash
# Prueba de humo en el simulador de iPhone: arranca la app, abre pantallas, reproduce música y mide.
# Se ejecuta desde la raíz del repositorio en un runner de macOS.
set -u
OUT=out-ios
mkdir -p "$OUT"
APP=ios/build/Build/Products/Debug-iphonesimulator/LibreStream.app
BUNDLE=org.librestream.LibreStream

# iPhone 16 (o el primero disponible)
SIM=$(xcrun simctl list devices available | grep -m1 -E "iPhone 16 \(" | grep -oE '[0-9A-F]{8}-[0-9A-F-]{27}')
[ -n "$SIM" ] || SIM=$(xcrun simctl list devices available | grep -m1 -E "iPhone" | grep -oE '[0-9A-F]{8}-[0-9A-F-]{27}')
echo "Simulador: $SIM" | tee "$OUT/medidas.txt"
xcrun simctl boot "$SIM" || true
xcrun simctl bootstatus "$SIM" -b > /dev/null
xcrun simctl install "$SIM" "$APP"

shot() { xcrun simctl io "$SIM" screenshot "$OUT/$1.png" > /dev/null 2>&1; }
stats() {
  { echo "== $1 =="; date -u +%T
    echo "-- CPU %, memoria KB (ps)"
    ps -axo pid,pcpu,rss,comm | grep -E "LibreStream|com.apple.WebKit" | grep -v grep | head -8
  } >> "$OUT/medidas.txt"
}
launch() { xcrun simctl terminate "$SIM" "$BUNDLE" > /dev/null 2>&1; xcrun simctl launch "$SIM" "$BUNDLE" "$@" | tee -a "$OUT/medidas.txt"; }

launch -lsRoute home
sleep 45; shot 01-inicio; stats "Inicio en reposo (45 s)"
sleep 30; stats "Inicio en reposo (75 s)"

launch -lsRoute music -lsAction play
sleep 40; shot 02-musica-sonando; stats "Musica sonando (40 s)"
sleep 60; shot 03-musica-sonando-100s; stats "Musica sonando (100 s)"

for r in videos library settings; do
  launch -lsRoute "$r"; sleep 30; shot "04-$r"
done

launch -lsRoute home; sleep 20; shot 05-inicio-segundo-arranque

# Cierres y errores
xcrun simctl spawn "$SIM" log show --last 10m --style compact --predicate 'process == "LibreStream" OR subsystem == "com.apple.WebKit"' 2>/dev/null | grep -iE "error|fault|crash|terminat" | head -60 > "$OUT/errores.txt"
ls ~/Library/Logs/DiagnosticReports 2>/dev/null | grep -i librestream > "$OUT/informes-de-cierre.txt" || echo "ninguno" > "$OUT/informes-de-cierre.txt"
{ echo "Lineas de error: $(wc -l < "$OUT/errores.txt")"; echo "Informes de cierre: $(cat "$OUT/informes-de-cierre.txt")"; } >> "$OUT/medidas.txt"
cat "$OUT/medidas.txt"
