/*
 * pdf.js - leitura de PDF sem .NET e sem binario extra.
 * Usa o pdf.js (Mozilla) empacotado via npm; aqui carregamos de vendor/pdfjs se existir,
 * senão tentamos require('pdfjs-dist/legacy/build/pdf.js'). Se nada existir, devolve
 * uma pagina-placeholder com aviso (o app nunca quebra).
 */
const fs = require('fs');
const path = require('path');

let _doc = null;      // documento aberto em cache
let _docKey = null;
let _pdfjs = null;

function loadPdfjs() {
  if (_pdfjs !== null) return _pdfjs;
  const candidates = [
    'pdfjs-dist/legacy/build/pdf.js',   // npm i pdfjs-dist@3
    'pdfjs-dist/build/pdf.js'
  ];
  for (const c of candidates) {
    try { _pdfjs = require(c); return _pdfjs; } catch (_) {}
  }
  _pdfjs = false;
  return _pdfjs;
}

async function getDoc(file) {
  const lib = loadPdfjs();
  if (!lib) throw new Error('Para ler PDFs rode: npm install pdfjs-dist@3 (a pasta node_modules deve ficar junto do app portavel) ou converta para CBZ.');
  const st = fs.statSync(file);
  if (_doc && _docKey === file + ':' + st.mtimeMs) return _doc;
  const data = new Uint8Array(fs.readFileSync(file));
  _doc = await lib.getDocument({ data, useSystemFonts: true, isEvalSupported: false }).promise;
  _docKey = file + ':' + st.mtimeMs;
  return _doc;
}

async function pageCount(file) {
  const doc = await getDoc(file);
  return doc.numPages;
}

/** Renderiza a pagina (0-based) num PNG data-url ~110 DPI x escala. */
async function renderPageDataUrl(file, index) {
  const doc = await getDoc(file);
  const page = await doc.getPage(index + 1);
  const vp = page.getViewport({ scale: 1 });
  const scale = Math.min(2, Math.max(1, 1400 / vp.width)); // resolucao boa p/ leitura
  const viewport = page.getViewport({ scale });

  // Canvas offscreen via canvas package (opcional). Sem ele, usamos um rasterizador simples interno?
  // Para manter zero-nativo: se 'canvas' nao existir, retornamos placeholder informativo.
  let Canvas;
  try { Canvas = require('canvas'); } catch (_) { Canvas = null; }
  if (!Canvas) {
    return placeholderSvg(`PDF ${path.basename(file)} - pagina ${index + 1}\nInstale "npm i canvas" para renderizar PDFs.`);
  }
  const canvas = Canvas.createCanvas(Math.round(viewport.width), Math.round(viewport.height));
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  await page.render({ canvasContext: ctx, viewport }).promise;
  return canvas.toBuffer ? `data:image/png;base64,${canvas.toBuffer('image/png').toString('base64')}`
                          : canvas.toDataURL('image/png');
}

function placeholderSvg(text) {
  const esc = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/\n/g, '&#10;');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="1100"><rect width="100%" height="100%" fill="#222"/><text x="40" y="80" fill="#eee" font-family="sans-serif" font-size="28" xml:space="preserve">${esc}</text></svg>`;
  return 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
}

module.exports = { pageCount, renderPageDataUrl };
