# Cómo instalar Nsound

Nsound es gratuita y no tiene cuentas ni suscripciones. Los paquetes no están firmados por una tienda
(Microsoft Store, Google Play, App Store), así que cada sistema te pedirá una confirmación al instalar.
Es normal: no significa que haya nada raro, sino que la app no pasa por una tienda.

**Dónde conseguirlos:** en la [beta 4 (Releases)](https://github.com/vhkp259r94-cmyk/spotios/releases/tag/v0.8.0-beta.4) o en la pestaña *Actions* → ejecución de «Build Android APK» / «Build unsigned IPA» / «Build Windows app»
→ *Artifacts* (hace falta sesión iniciada en GitHub).

**Comprobar que el archivo no está alterado (opcional):** cada paquete trae un `.sha256`.
En Windows: `Get-FileHash .\archivo -Algorithm SHA256` y compara el resultado con el del `.sha256`.

---

## Windows 10 / 11

Necesitas: Windows 10 (1809) o superior y el *WebView2 Runtime* (ya viene con Windows 11 y con Windows 10 actualizado).

1. Descarga `Nsound-<versión>-windows-x64.zip` (o `-arm64.zip` si tu PC es ARM).
2. **Extrae todo el contenido** del zip en una carpeta (clic derecho → *Extraer todo*). No lo ejecutes desde dentro del zip.
3. Abre `Nsound.exe`.
4. Si aparece *«Windows protegió su PC»*: pulsa **Más información → Ejecutar de todas formas**
   (SmartScreen avisa porque el programa no está firmado).

No necesita instalador ni permisos de administrador. Para desinstalar, borra la carpeta.

- Tus datos (biblioteca, listas, ajustes) están en `%LocalAppData%\LibreStream`.
- Las descargas van por defecto a `Vídeos\LibreStream` (se cambia en *Ajustes*).
- Si algo falla, mira `%LocalAppData%\LibreStream\crash.log` y cuéntalo.

## Android 7 o superior

1. Descarga `Nsound-<versión>-android.apk` en el móvil (o pásalo desde el PC por cable, Drive, etc.).
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
6. Abre Nsound.

### Opción B: SideStore (se renueva sola, sin ordenador) — la recomendada para no reinstalar cada semana
SideStore es una tienda de código abierto que firma y **renueva la app en el propio iPhone**.
1. Instala SideStore siguiendo su guía oficial: <https://docs.sidestore.io> (el primer arranque necesita un ordenador, solo una vez).
2. En el iPhone, descarga el `.ipa` de Nsound. Ábrelo con **SideStore** (menú Compartir → SideStore) o, dentro de SideStore, **My Apps → +** y elígelo.
3. SideStore renueva la firma antes de que caduque. Para renovar necesita su VPN local activada (la guía de SideStore explica cuál) y que abras SideStore de vez en cuando.
4. Con un Apple ID gratuito sigue habiendo el límite de **3 apps** y el de 7 días: la diferencia es que la renovación es automática.

### Opción C: AltStore (renueva por wifi con el ordenador)
Instala [AltStore](https://altstore.io) en el ordenador (AltServer) y en el iPhone, abre AltStore → **My Apps → +** y elige el `.ipa`.
Mientras el ordenador esté encendido y en la misma wifi, AltStore renueva la firma solo.

### Opción D: TrollStore (permanente, solo en algunos iOS)
TrollStore instala sin que caduque nunca, pero **solo funciona en ciertas versiones de iOS**: desde la 14.0 hasta la 16.6.1,
la 16.7 RC (20H18) y la 17.0. En versiones más nuevas no se puede usar; no actualices el iPhone si quieres conservarlo.
Instálalo con su guía oficial: <https://ios.cfw.guide/installing-trollstore/>. Después, abre el `.ipa` con TrollStore (Compartir → TrollStore) y pulsa **Install**.

### ¿Cuál elijo?

| Opción | ¿Se renueva sola? | Necesita ordenador | Funciona en |
|---|---|---|---|
| A. Sideloadly | No (cada 7 días a mano) | Sí, cada vez | iOS 16+ |
| **B. SideStore** | **Sí, en el iPhone** | Solo para instalar SideStore | iOS 16+ |
| C. AltStore | Sí, con el ordenador en la misma wifi | Sí, encendido | iOS 16+ |
| D. TrollStore | No hace falta: no caduca | Solo para instalar TrollStore | iOS 14.0–16.6.1 y 17.0 |

**Tiendas con certificados «empresariales» (ESign, Scarlet…):** no se recomiendan. Apple revoca esos certificados de golpe
y la app deja de abrirse, y pasas tus datos por terceros que no controlas.

**Limitaciones con Apple ID gratuito (opciones A, B y C):** la firma **caduca a los 7 días** y puedes tener como máximo
3 apps firmadas así a la vez. Los datos se conservan si reinstalas encima.

> **Fuente de Nsound:** en SideStore o AltStore, añade esta fuente y Nsound se instala y se actualiza desde ahí:
> `https://vhkp259r94-cmyk.github.io/spotios/source.json`

---

## Qué esperar en el móvil (versión de pruebas)

- Las apps móviles se han probado en móviles reales y en un emulador, pero es una versión de pruebas:
  puede haber fallos. Si ves uno, apunta en qué pantalla estabas y qué texto de error aparece.
- Si YouTube no devuelve resultados, la pantalla muestra el motivo en rojo.

### Segundo plano y pantalla bloqueada (iPhone y Android)

- **Micro pausa al salir o bloquear.** Al salir de la app, apagar la pantalla o desbloquear, el sistema puede pausar por un instante el reproductor
  de YouTube (va incrustado y el sistema lo trata como un vídeo). Nsound lo reanuda solo con varios intentos seguidos, así que puede notarse
  un corte de aproximadamente un segundo. Es una limitación del sistema con el reproductor oficial de YouTube, no un fallo que se pueda quitar del todo.
- **Si pausas tú, no se reanuda.** Lo que pausas en la app, en la pantalla de bloqueo o con los cascos se respeta.
- **iPhone: la pantalla de bloqueo en pausa.** Mientras suena, el reproductor de la pantalla de bloqueo lo da YouTube; en pausa lo sostiene la propia
  app con el título, la portada y los botones de anterior y siguiente. Si la música lleva **mucho rato en pausa** y el móvil bloqueado, **iOS cierra la
  app** para ahorrar batería (la deja dormida y luego la termina): entonces el reproductor desaparece o los botones tardan, y hay que abrir Nsound para seguir.
  Esto lo decide iOS y no se puede evitar con una app que no es de la tienda.
- **Android:** una notificación de reproducción mantiene la música con la pantalla apagada. Si cierras Nsound desde las apps recientes, se para a propósito.
  Algunos móviles con ahorro de batería agresivo pueden cerrarla igualmente: en ese caso quita la restricción de batería para Nsound.
- El **ecualizador** y las **descargas sin conexión** son lo menos probado.
- YouTube no se descarga: solo se reproduce con su reproductor oficial. Las descargas son del contenido libre
  (Creative Commons).
