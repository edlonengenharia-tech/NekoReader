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
} catch (e) { console.error('[ERRO] smoke CBZ:', e.message); process.exit(1); }

// PDF: o main só serve bytes/conta páginas; o RENDER é no renderer (Chromium).
try {
  const pdf = require('../src/pdf.js');
  if (fs.existsSync('samples/exemplo.pdf')) {
    const n = await pdf.pageCount('samples/exemplo.pdf');
    const bytes = pdf.readPdfBytes('samples/exemplo.pdf');
    if (!bytes || bytes.length < 200 || String.fromCharCode(bytes[0],bytes[1],bytes[2],bytes[3]) !== '%PDF') throw new Error('bytes do PDF invalidos');
    const txt = await pdf.pageText('samples/exemplo.pdf', 0);
    console.log('[OK] PDF: ' + n + ' paginas, ' + bytes.length + ' bytes servidos ao renderer, texto da p.1: "' + (txt || '(sem texto - escaneado)').slice(0, 40) + '"');
  } else {
    console.log('[--] samples/exemplo.pdf ausente, pulando teste de PDF');
  }
} catch (e) { console.error('[ERRO] smoke PDF:', e.message); process.exit(1); }

// vendor do pdfjs para o renderer (arquivos que o index.html carrega)
try {
  for (const f of ['vendor/pdfjs/pdf.min.js', 'vendor/pdfjs/pdf.worker.min.js']) {
    if (!fs.existsSync(f)) {
      const src = 'node_modules/pdfjs-dist/legacy/build/' + f.split('/').pop();
      if (fs.existsSync(src)) fs.copyFileSync(src, f);
      else { console.error('[ERRO] falta ' + f + ' e ' + src + '. Rode npm install.'); process.exit(1); }
    }
  }
  console.log('[OK] vendor/pdfjs presente (pdf.min.js + pdf.worker.min.js).');
} catch (e) { console.error('[ERRO] smoke vendor pdfjs:', e.message); process.exit(1); }
