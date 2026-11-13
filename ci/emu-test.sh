#!/usr/bin/env bash
# Prueba en el emulador de Android: instala el APK, abre Nsound, mira la pantalla de inicio y los artistas,
# lo manda a segundo plano, lo vuelve a abrir y guarda capturas y el registro (errores de la página incluidos).
# Cada orden tiene límite de tiempo y el progreso queda anotado, para que nada se quede colgado sin dejar rastro.
set -u
OUT=emu-out; mkdir -p "$OUT"
P() { echo "$(date +%T) $*" | tee -a "$OUT/progreso.txt"; }
A() { timeout 90 adb "$@"; }
SHOT() { timeout 60 adb exec-out screencap -p > "$OUT/$1" 2>/dev/null || P "captura $1 falló"; P "captura $1"; }
APK=android/app/build/outputs/apk/debug/app-debug.apk
P "instalar"; A install -r "$APK" 2>&1 | tee "$OUT/install.txt"
A logcat -c
P "abrir"; A shell am start -n org.librestream.app/.MainActivity 2>&1 | tee "$OUT/start.txt"
sleep 30; SHOT 1-inicio.png
A shell input swipe 540 1700 540 600 400; sleep 3
A shell input swipe 540 1700 540 600 400; sleep 5; SHOT 2-artistas.png
P "segundo plano"; A shell input keyevent 3; sleep 10; SHOT 3-fuera.png
P "volver"; A shell am start -n org.librestream.app/.MainActivity; sleep 10; SHOT 4-vuelta.png
P "pantalla apagada y encendida"; A shell input keyevent 26; sleep 5; A shell input keyevent 26; sleep 3
A shell input keyevent 82; sleep 4; SHOT 5-pantalla.png
A logcat -d > "$OUT/logcat.txt" 2>&1
grep -iE "chromium|console|Uncaught|FATAL|AndroidRuntime|org.librestream" "$OUT/logcat.txt" | tail -80 > "$OUT/resumen-log.txt"
A shell pidof org.librestream.app > "$OUT/pid.txt" 2>&1 || echo "SIN PROCESO (se cerró)" > "$OUT/pid.txt"
P "fin"
