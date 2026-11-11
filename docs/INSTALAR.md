# Cómo instalar Nimbo

Nimbo es gratuita y no tiene cuentas ni suscripciones. Los paquetes no están firmados por una tienda
(Microsoft Store, Google Play, App Store), así que cada sistema te pedirá una confirmación al instalar.
Es normal: no significa que haya nada raro, sino que la app no pasa por una tienda.

**Dónde conseguirlos:** en [Releases](https://github.com/vhkp259r94-cmyk/spotios/releases) (cuando haya una versión
publicada) o en la pestaña *Actions* → ejecución de «Build Android APK» / «Build unsigned IPA» / «Build Windows app»
→ *Artifacts* (hace falta sesión iniciada en GitHub).

**Comprobar que el archivo no está alterado (opcional):** cada paquete trae un `.sha256`.
En Windows: `Get-FileHash .\archivo -Algorithm SHA256` y compara el resultado con el del `.sha256`.

---

## Windows 10 / 11

Necesitas: Windows 10 (1809) o superior y el *WebView2 Runtime* (ya viene con Windows 11 y con Windows 10 actualizado).

1. Descarga `Nimbo-<versión>-windows-x64.zip` (o `-arm64.zip` si tu PC es ARM).
2. **Extrae todo el contenido** del zip en una carpeta (clic derecho → *Extraer todo*). No lo ejecutes desde dentro del zip.
3. Abre `Nimbo.exe`.
4. Si aparece *«Windows protegió su PC»*: pulsa **Más información → Ejecutar de todas formas**
   (SmartScreen avisa porque el programa no está firmado).

No necesita instalador ni permisos de administrador. Para desinstalar, borra la carpeta.

- Tus datos (biblioteca, listas, ajustes) están en `%LocalAppData%\LibreStream`.
- Las descargas van por defecto a `Vídeos\LibreStream` (se cambia en *Ajustes*).
- Si algo falla, mira `%LocalAppData%\LibreStream\crash.log` y cuéntalo.

## Android 7 o superior

1. Descarga `Nimbo-<versión>-android.apk` en el móvil (o pásalo desde el PC por cable, Drive, etc.).
2. Ábrelo. Android te pedirá permitir instalar apps desde esa fuente (el navegador o el gestor de archivos que uses):
   **Ajustes → Permitir de esta fuente**. Es un permiso que puedes quitar después.
3. Si Google Play Protect dice *«App no verificada»* o *«Bloqueada»*: pulsa **Más detalles → Instalar de todas formas**
   (es un APK de depuración firmado con una clave de pruebas).
4. Pulsa **Instalar** y luego **Abrir**.

Para desinstalar: mantén pulsado el icono → *Desinstalar*. Se borra también la biblioteca y las descargas de la app.

> Necesita que el **Android System WebView** y **Chrome** estén actualizados (Play Store → Mis apps).

## iPhone / iPad (iOS 16 o superior)

El `.ipa` **no está firmado**: iOS no permite instalarlo directamente. Hay que firmarlo con tu Apple ID al instalar.
Elige **una** de estas formas:

### Opción A: Sideloadly (Windows o Mac, la más sencilla)
1. Instala [Sideloadly](https://sideloadly.io) en el ordenador (en Windows necesitas iTunes y iCloud de Apple.com, **no** de la Microsoft Store).
2. Conecta el iPhone por cable y pulsa **Confiar** en el teléfono.
3. Abre Sideloadly, arrastra el `.ipa` y escribe tu Apple ID. Pulsa **Start**.
4. En el iPhone: **Ajustes → General → VPN y gestión de dispositivos →** tu Apple ID **→ Confiar**.
5. iOS 16 o superior: **Ajustes → Privacidad y seguridad → Modo desarrollador → Activar** (pide reiniciar).
6. Abre Nimbo.

### Opción B: AltStore
Instala [AltStore](https://altstore.io) en el ordenador y en el iPhone, abre AltStore → **My Apps → +** y elige el `.ipa`.
Mientras el ordenador esté en la misma wifi, AltStore renueva la firma solo.

### Opción C: TrollStore
Solo en versiones de iOS compatibles con TrollStore. Abre el `.ipa` con TrollStore y pulsa **Install**.
La app queda instalada de forma permanente, sin caducar.

**Limitaciones de las opciones A y B con Apple ID gratuito:**
la app **caduca a los 7 días** (hay que volver a firmarla con el mismo procedimiento) y puedes tener como máximo
3 apps firmadas así a la vez. Los datos se conservan si reinstalas encima.

---

## Qué esperar en el móvil (versión de pruebas)

- Las apps móviles se han compilado y probado en un emulador y un simulador, pero **no en un teléfono real**:
  puede haber fallos. Si ves uno, apunta en qué pantalla estabas y qué texto de error aparece.
- Si YouTube no devuelve resultados, la pantalla muestra el motivo en rojo.
- El sonido **con la pantalla apagada** puede cortarse: YouTube pausa su reproductor en segundo plano.
- El **ecualizador** y las **descargas sin conexión** son lo menos probado.
- YouTube no se descarga: solo se reproduce con su reproductor oficial. Las descargas son del contenido libre
  (Creative Commons).
