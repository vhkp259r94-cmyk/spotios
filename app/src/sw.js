/* Nsound: copia de la interfaz para abrirla sin conexión. Solo guarda la propia página; nunca las llamadas a YouTube ni a otros servicios. */
const CACHE = 'nsound-shell-v1';

self.addEventListener('install', (e) => { self.skipWaiting(); e.waitUntil(caches.open(CACHE).then((c) => c.add('./'))); });
self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys().then((ks) => Promise.all(ks.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', (e) => {
  const req = e.request, url = new URL(req.url);
  if (req.method !== 'GET' || url.origin !== location.origin || url.pathname.startsWith('/__')) return;   // lo demás va directo a la red
  if (req.mode !== 'navigate') return;
  // La página: primero la red (así siempre es la última versión) y, sin conexión, la copia guardada
  e.respondWith(fetch(req).then((r) => { const copy = r.clone(); caches.open(CACHE).then((c) => c.put('./', copy)); return r; }).catch(() => caches.match('./')));
});
