// Prueba puntual: ¿cuántas búsquedas seguidas aguanta YouTube Music desde esta IP? (3 a la vez, como la importación de listas)
const N = Number(process.argv[2]) || 300;
const names = ['queen', 'adele', 'coldplay', 'nirvana', 'shakira', 'estopa', 'quevedo', 'rosalia', 'karol', 'maluma', 'aitana', 'sabina', 'melendi', 'fito', 'extremoduro', 'dualipa', 'weeknd', 'sheeran', 'daftpunk', 'prince', 'madonna', 'eminem', 'rihanna', 'beyonce', 'drake', 'ozuna', 'anuel'];
const url = 'https://music.youtube.com/youtubei/v1/search?prettyPrint=false';
const codes = {}; let empty = 0, firstFail = null; const t0 = Date.now();
let next = 0;
async function worker() {
  while (next < N) {
    const i = next++;
    const body = JSON.stringify({ context: { client: { clientName: 'WEB_REMIX', clientVersion: '1.20241106.01.00', hl: 'es', gl: 'ES' } }, query: names[i % names.length] + ' tema ' + i, params: 'EgWKAQIIAWoMEA4QChADEAQQCRAF' });
    try {
      const r = await fetch(url, { method: 'POST', body, headers: { 'Content-Type': 'application/json', Origin: 'https://music.youtube.com', Referer: 'https://music.youtube.com/', 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36' } });
      codes[r.status] = (codes[r.status] || 0) + 1;
      const t = await r.text();
      if (r.ok && !t.includes('musicResponsiveListItemRenderer')) { empty++; firstFail ??= i; }
      if (!r.ok) firstFail ??= i;
    } catch (e) { codes[e.code || 'error'] = (codes[e.code || 'error'] || 0) + 1; firstFail ??= i; }
  }
}
await Promise.all([worker(), worker(), worker()]);
console.log(`${N} búsquedas en ${Math.round((Date.now() - t0) / 1000)} s · códigos: ${JSON.stringify(codes)} · sin resultados: ${empty} · primer fallo en la nº ${firstFail ?? 'ninguno'}`);
