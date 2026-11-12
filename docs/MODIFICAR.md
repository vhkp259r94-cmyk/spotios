# Guía rápida: ¿dónde toco el código?

Cada carpeta tiene una sola responsabilidad. Los tres "matchers" (Python, Swift, C#) deben
mantenerse **idénticos**: si cambias una regex, cámbiala en los tres y en sus tests.

| Quiero… | iOS (`ios/LibreStream/`) | Windows (`windows/`) | Herramientas |
|---|---|---|---|
| Cambiar la limpieza de títulos / regex | `Services/Matching/MetadataMatcher.swift` | `LibreStream.Core/Services/Matching/MetadataMatcher.cs` | `tools/title_normalizer.py` |
| Añadir casos de prueba de regex | `LibreStreamTests/MetadataMatcherTests.swift` | `LibreStream.Core.Tests/Matching/MetadataMatcherTests.cs` | `tools/test_title_regex.py` |
| Cambiar el reproductor / controles del sistema | `Services/Playback/AudioPlayerManager.swift` | `LibreStream.App/Services/AudioPlayerManager.cs` | — |
| Cambiar la descarga/caché del catálogo | `Services/Catalog/CatalogService.swift` | `LibreStream.Core/Services/Catalog/CatalogService.cs` | — |
| Añadir campos al catálogo | `Models/Models.swift` | `LibreStream.Core/Models/Catalog.cs` | `schemas/catalog.schema.json` |
| Cambiar la pantalla de biblioteca | `Views/Library/LibraryView.swift` | `LibreStream.App/Views/MainWindow.xaml(.cs)` | — |
| Cambiar el mini‑reproductor | `Views/Player/MiniPlayer.swift` | `LibreStream.App/Views/MainWindow.xaml` | — |
| Cambiar las tarjetas (portada, degradado, textos) | — | `LibreStream.App/Views/MediaCard.cs` | — |
| Cambiar la búsqueda de YouTube | (pendiente) | `LibreStream.Core/Services/YouTube/YouTubeSearch.cs` | — |
| Cambiar el reproductor embebido de YouTube | (pendiente) | `LibreStream.App/Assets/player.html` + `Views/MainWindow.xaml.cs` | — |
| Cambiar las letras sincronizadas | (pendiente) | `LibreStream.Core/Services/Lyrics/LyricsService.cs` (fuente: LRCLIB) | — |
| Cambiar las listas de reproducción | (pendiente) | `LibreStream.Core/Services/Library/PlaylistStore.cs` | — |
| Cambiar los géneros de Música | (pendiente) | `LibreStream.App/Views/MainWindow.xaml.cs` (`GenreNames`) | — |
| Cambiar las estanterías de Vídeos (Velada, series, YouTubers…) | (pendiente) | `LibreStream.Core/Services/YouTube/VideoShelves.cs` | — |
| Cambiar qué cuenta como «canción» o «vídeo» | (pendiente) | `LibreStream.Core/Services/YouTube/ContentFilter.cs` | — |
| Cambiar los ajustes y los ajustes del ecualizador | (pendiente) | `LibreStream.Core/Services/Settings/AppSettings.cs` | — |
| Cambiar el sonido del ecualizador | (pendiente) | `LibreStream.App/Assets/eq.js` | — |
| Cambiar las descargas | (pendiente) | `LibreStream.Core/Services/Downloads/Downloads.cs` | — |
| **iOS y Android:** cambiar pantallas, tarjetas o textos | `app/src/js/04-app.js` + `app/src/css/app.css` | (igual) | `node app/build.mjs` |
| **iOS y Android:** colores, animaciones y estilo «Nsound» (prueba reversible en Ajustes → Aspecto) | `app/src/css/fx.css` | (igual) | `node app/build.mjs` |
| **iOS y Android:** reglas de filtro, estanterías, letras, ajustes | `app/src/js/00-core.js` (y `01-stores.js`) | (igual) | `node --test app/test` |
| **iOS y Android:** lectura de YouTube y descargas | `ios/LibreStream/Shell/WebShell.swift` | `android/app/src/main/java/org/librestream/app/` | — |
| Probar las apps en un móvil simulado (emulador de Android + simulador de iPhone) | Actions → «Prueba en móvil simulado» (`.github/workflows/mobile-smoke.yml`); capturas y medidas en los artefactos | (igual) | `android/ci/smoke.sh`, `ios/ci/smoke.sh` |
| Probar la interfaz móvil en el PC | `node app/build.mjs && node app/dev/server.mjs` → http://localhost:5173 | (igual) | — |
| Cambiar la biblioteca guardada | (pendiente) | `LibreStream.Core/Services/Library/LibraryStore.cs` | — |
| Cambiar la URL del catálogo | `ios/project.yml` (`LSCatalogURL`) | `Views/MainWindow.xaml.cs` (`CatalogUrl`) | — |
| Añadir canciones/vídeos | `catalog/catalog.json` | (igual) | `tools/validate_catalog.py` |

## Añadir contenido al catálogo
1. Solo contenido que puedas redistribuir (dominio público, Creative Commons, material propio).
2. Calcula el `id`: `cd tools && python -c "from title_normalizer import dedupe_key as k; print(k('Artista','Título'))"`.
3. Añade el elemento a `catalog/catalog.json` y ejecuta `python tools/validate_catalog.py`.
4. `git push` a `main`: Actions valida y publica en GitHub Pages.

## Comprobaciones antes de hacer commit
```bash
python tools/test_title_regex.py
python tools/validate_catalog.py
dotnet test windows/LibreStream.Core.Tests   # si tienes .NET 8
```

## Publicar una versión
```bash
git tag v0.4.0 && git push origin v0.4.0   # genera .ipa y .zip de Windows en Releases
```
