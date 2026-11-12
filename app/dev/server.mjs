// Servidor de desarrollo: sirve dist/ y hace de puente para leer YouTube/LRCLIB (en un navegador no hay CORS).
// Uso: node app/build.mjs && node app/dev/server.mjs [puerto]
import { createServer } from 'node:http';
import { readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const dist = join(dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const port = Number(process.argv[2]) || 5173;
const ALLOWED = ['www.youtube.com', 'lrclib.net', 'vhkp259r94-cmyk.github.io', 'open.spotify.com', 'api.deezer.com', 'music.apple.com', 'soundcloud.com'];
const POST_ALLOWED = ['music.youtube.com'];
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';

createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname === '/__proxy') {
    try {
      const target = new URL(url.searchParams.get('url') || '');
      if (target.protocol !== 'https:' || !ALLOWED.includes(target.hostname)) { res.writeHead(403).end('host no permitido'); return; }
      const r = await fetch(target, { headers: { 'User-Agent': UA, 'Accept-Language': 'es-ES,es;q=0.9' } });
      res.writeHead(r.status, { 'Content-Type': r.headers.get('content-type') || 'text/plain' }).end(await r.text());
    } catch (e) { res.writeHead(502).end(String(e)); }
    return;
  }
  if (url.pathname === '/__post' && req.method === 'POST') {
    try {
      const target = new URL(url.searchParams.get('url') || '');
      if (target.protocol !== 'https:' || !POST_ALLOWED.includes(target.hostname)) { res.writeHead(403).end('host no permitido'); return; }
      let body = '';
      for await (const chunk of req) body += chunk;
      const r = await fetch(target, { method: 'POST', body, headers: { 'User-Agent': UA, 'Content-Type': 'application/json', Origin: 'https://music.youtube.com', Referer: 'https://music.youtube.com/', 'Accept-Language': 'es-ES,es;q=0.9' } });
      res.writeHead(r.status, { 'Content-Type': 'application/json' }).end(await r.text());
    } catch (e) { res.writeHead(502).end(String(e)); }
    return;
  }
  const file = join(dist, url.pathname === '/' ? 'index.html' : url.pathname.replace(/\.\./g, ''));
  if (!existsSync(file)) { res.writeHead(404).end('no encontrado'); return; }
  res.writeHead(200, { 'Content-Type': file.endsWith('.js') ? 'text/javascript; charset=utf-8' : 'text/html; charset=utf-8' }).end(readFileSync(file));
}).listen(port, process.env.LS_HOST || '127.0.0.1', () => console.log(`LibreStream (web) en http://localhost:${port}`));
