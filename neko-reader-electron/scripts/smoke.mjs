/* Smoke test sem janela: garante que o descompactador lê o CBZ de exemplo. */
import { createRequire } from 'module';
import fs from 'fs';
const require = createRequire(import.meta.url);
const archives = require('../src/archives.js');

if (!fs.existsSync('samples/exemplo.cbz')) { console.error('[ERRO] samples/exemplo.cbz nao existe. Rode: node scripts/make-sample.mjs'); process.exit(1); }
try {
  const info = await archives.listPages('samples/exemplo.cbz');
  if (info.pages.length !== 6) throw new Error('esperava 6 paginas, veio ' + info.pages.length);
  const url = await archives.readPageDataUrl('samples/exemplo.cbz', 0);
  if (!url.startsWith('data:image/png;base64,')) throw new Error('pagina 0 nao decodificou como PNG');
  console.log('[OK] CBZ: 6 paginas listadas, pagina 1 extraida (' + Math.round(url.length/1024) + ' KB base64).');
} catch (e) { console.error('[ERRO] smoke:', e.message); process.exit(1); }
