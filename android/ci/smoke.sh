#!/usr/bin/env bash
# Prueba de humo en el emulador de Android: arranca la app, abre pantallas, reproduce música y mide.
# Se ejecuta desde la raíz del repositorio dentro de reactivecircus/android-emulator-runner.
set -u
OUT=out-android
mkdir -p "$OUT"
PKG=org.librestream.app
APK=android/app/build/outputs/apk/debug/app-debug.apk

adb install -r "$APK" | tee "$OUT/install.txt"
adb logcat -c

shot()  { adb exec-out screencap -p > "$OUT/$1.png"; }
start() { adb shell am force-stop "$PKG"; adb shell am start -n "$PKG/.MainActivity" "$@" > /dev/null; }
stats() {
  { echo "== $1 =="; date -u +%T
    echo "-- CPU (dumpsys cpuinfo)"; adb shell dumpsys cpuinfo | grep -E "$PKG|TOTAL|Load" | head -8
    echo "-- Memoria (PSS)"; adb shell dumpsys meminfo "$PKG" | grep -E "TOTAL PSS|TOTAL:|Java Heap|Native Heap|Views|WebViews" | head -8
    echo "-- Procesos"; adb shell ps -A 2>/dev/null | grep -E "librestream|webview|sandboxed" | head -6
  } >> "$OUT/medidas.txt"
}

# 1) Inicio en reposo (solo carga y espera)
start --es route home
sleep 45; shot 01-inicio; stats "Inicio en reposo (45 s)"
sleep 30; stats "Inicio en reposo (75 s)"

# 2) Música: elige la primera canción y la deja sonando
start --es route music --es action play
sleep 40; shot 02-musica-sonando; stats "Musica sonando (40 s)"
sleep 60; shot 03-musica-sonando-100s; stats "Musica sonando (100 s)"

# 3) Resto de pantallas
for r in videos library settings; do
  start --es route "$r"; sleep 30; shot "04-$r"
done

# 4) Segundo arranque: ¿se usa la caché? ¿arranca rápido?
start --es route home; sleep 20; shot 05-inicio-segundo-arranque

# Registro: errores de la app y del WebView
adb logcat -d > "$OUT/logcat-completo.txt"
grep -E "AndroidRuntime|FATAL|ANR in|Process: $PKG|chromium.*(Uncaught|ERROR)|CONSOLE.*(Error|error)" "$OUT/logcat-completo.txt" | head -60 > "$OUT/errores.txt"
{ echo "Líneas de error: $(wc -l < "$OUT/errores.txt")"; echo "App viva: $(adb shell pidof "$PKG" || echo NO)"; } >> "$OUT/medidas.txt"
adb shell wm size >> "$OUT/medidas.txt"
cat "$OUT/medidas.txt"
