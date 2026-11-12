// Servidor de pruebas: sirve dist/ y hace de puente para leer YouTube/LRCLIB y otros servicios (en un navegador no hay CORS).
// Uso local:  node app/build.mjs && node app/dev/server.mjs [puerto]
// En la nube (Render, ver render.yaml): usa PORT, escucha en 0.0.0.0 y exige la clave LS_TOKEN (la primera vez: /?k=CLAVE).
import { createServer } from 'node:http';
import { readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const dist = join(dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const port = Number(process.env.PORT) || Number(process.argv[2]) || 5173;
const host = process.env.LS_HOST || (process.env.PORT ? '0.0.0.0' : '127.0.0.1');
const TOKEN = process.env.LS_TOKEN || '';                     // vacío = sin clave (solo en local)
const ALLOWED = ['www.youtube.com', 'lrclib.net', 'vhkp259r94-cmyk.github.io', 'open.spotify.com', 'api.deezer.com', 'music.apple.com', 'soundcloud.com'];
const POST_ALLOWED = ['music.youtube.com'];
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const TYPES = { '.js': 'text/javascript; charset=utf-8', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg', '.html': 'text/html; charset=utf-8' };
const MAX_BODY = 200 * 1024;

const cookieKey = (req) => /(?:^|;\s*)ls=([^;]+)/.exec(req.headers.cookie || '')?.[1];

createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname === '/healthz') { res.writeHead(200, { 'Content-Type': 'text/plain' }).end('ok'); return; }

  // Clave de acceso: en la nube nadie usa el puente sin ella
  if (TOKEN) {
    if (url.searchParams.get('k') === TOKEN) {
      res.writeHead(302, { 'Set-Cookie': `ls=${TOKEN}; Path=/; Max-Age=31536000; HttpOnly; Secure; SameSite=Lax`, Location: '/' }).end();
      return;
    }
    if (cookieKey(req) !== TOKEN) { res.writeHead(401, { 'Content-Type': 'text/plain; charset=utf-8' }).end('Acceso privado. Abre el enlace con la clave: /?k=CLAVE'); return; }
  }

  if (url.pathname === '/__proxy') {
    try {
      const target = new URL(url.searchParams.get('url') || '');
      if (target.protocol !== 'https:' || !ALLOWED.includes(target.hostname)) { res.writeHead(403).end('host no permitido'); return; }
      const r = await fetch(target, { headers: { 'User-Agent': UA, 'Accept-Language': 'es-ES,es;q=0.9' } });
      const t = await r.text(); res.writeHead(r.status, { 'Content-Type': r.headers.get('content-type') || 'text/plain' }).end(t);
    } catch (e) { if (!res.headersSent) res.writeHead(502).end(String(e)); else res.destroy(); }
    return;
  }
  if (url.pathname === '/__post' && req.method === 'POST') {
    try {
      const target = new URL(url.searchParams.get('url') || '');
      if (target.protocol !== 'https:' || !POST_ALLOWED.includes(target.hostname)) { res.writeHead(403).end('host no permitido'); return; }
      let body = '';
      for await (const chunk of req) { body += chunk; if (body.length > MAX_BODY) { res.writeHead(413).end('demasiado grande'); return; } }
      const r = await fetch(target, { method: 'POST', body, headers: { 'User-Agent': UA, 'Content-Type': 'application/json', Origin: 'https://music.youtube.com', Referer: 'https://music.youtube.com/', 'Accept-Language': 'es-ES,es;q=0.9' } });
      const t = await r.text(); res.writeHead(r.status, { 'Content-Type': 'application/json' }).end(t);
    } catch (e) { if (!res.headersSent) res.writeHead(502).end(String(e)); else res.destroy(); }
    return;
  }
  const rel = url.pathname === '/' ? 'index.html' : url.pathname.replace(/\.\./g, '');
  const file = join(dist, rel);
  if (!existsSync(file)) { res.writeHead(404).end('no encontrado'); return; }
  const ext = rel.slice(rel.lastIndexOf('.'));
  res.writeHead(200, { 'Content-Type': TYPES[ext] || 'application/octet-stream' }).end(readFileSync(file));
}).listen(port, host, () => console.log(`Nsound (pruebas) en http://${host}:${port}${TOKEN ? ' (con clave)' : ''}`));

process.on('uncaughtException', (e) => console.error('Error no controlado (el servidor sigue):', e));
process.on('unhandledRejection', (e) => console.error('Promesa rechazada (el servidor sigue):', e));
