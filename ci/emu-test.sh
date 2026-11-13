#!/usr/bin/env bash
# Prueba en el emulador de Android: instala el APK, abre Nsound, mira la pantalla de inicio y los artistas,
# lo manda a segundo plano, lo vuelve a abrir y guarda capturas y el registro (errores de la página incluidos).
set -u
OUT=emu-out; mkdir -p "$OUT"
APK=$(ls android/app/build/outputs/apk/debug/app-debug.apk)
adb wait-for-device
adb install -r "$APK" | tee "$OUT/install.txt"
adb logcat -c
adb shell am start -n org.librestream.app/.MainActivity | tee "$OUT/start.txt"
sleep 30; adb exec-out screencap -p > "$OUT/1-inicio.png"
adb shell input swipe 540 1700 540 600 400; sleep 3
adb shell input swipe 540 1700 540 600 400; sleep 5
adb exec-out screencap -p > "$OUT/2-artistas.png"
adb shell input keyevent 3; sleep 10                       # segundo plano (pantalla de inicio del móvil)
adb exec-out screencap -p > "$OUT/3-fuera.png"
adb shell am start -n org.librestream.app/.MainActivity; sleep 10
adb exec-out screencap -p > "$OUT/4-vuelta.png"
adb shell input keyevent 26; sleep 5; adb shell input keyevent 26; sleep 3   # apagar y encender la pantalla
adb shell input keyevent 82; sleep 4
adb exec-out screencap -p > "$OUT/5-pantalla.png"
adb logcat -d > "$OUT/logcat.txt"
grep -iE "chromium|console|Uncaught|FATAL|AndroidRuntime|org.librestream" "$OUT/logcat.txt" | tail -80 > "$OUT/resumen-log.txt"
adb shell pidof org.librestream.app > "$OUT/pid.txt" || echo "SIN PROCESO (se cerró)" > "$OUT/pid.txt"
