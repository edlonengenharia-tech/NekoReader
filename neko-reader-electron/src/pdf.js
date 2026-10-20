/*
 * pdf.js - leitura de PDF sem .NET e sem binario extra.
 * Usa o pdf.js (Mozilla) empacotado via npm; aqui carregamos de vendor/pdfjs se existir,
 * senão tentamos require('pdfjs-dist/legacy/build/pdf.js'). Se nada existir, devolve
 * uma pagina-placeholder com aviso (o app nunca quebra).
 */
const fs = require('fs');
const path = require('path');

// No Node/Electron-main o pdfjs tenta BAIXAR as fontes padrao (Helvetica, Courier...)
// por URL -> falha silenciosa e a pagina sai em branco ("abre mas nao le").
// Solucao: apontar standardFontDataUrl para a pasta LOCAL do pacote pdfjs-dist,
// em formato file:// (com barra final), que o fetcher do Node aceita.
let _stdFontPatched = false;
function patchStandardFonts(lib) {
  if (_stdFontPatched) return;
  _stdFontPatched = true;
  try {
    const path = require('path');
    const fsx = require('fs');
    let dir = path.join(path.dirname(require.resolve('pdfjs-dist/legacy/build/pdf.js')), '..', '..', 'standard_fonts');
    if (!fsx.existsSync(dir)) dir = path.join(path.dirname(require.resolve('pdfjs-dist/package.json')), 'standard_fonts');
    const url = 'file:///' + dir.split(path.sep).join('/').replace(/^\/+/, '') + '/';
    lib.__nekoStandardFontDir = url;
  } catch (_) {}
}

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

  // Cabeçalho de segurança: PDF real? (arquivos .pdf que são HTML/EPUB disfarçado etc.)
  const head = Buffer.from(data.subarray(0, Math.min(1024, data.length))).toString('latin1');
  if (!head.startsWith('%PDF-')) {
    throw new Error('Este arquivo não é um PDF válido (cabeçalho ausente). Se for um mangá protegido de loja (Kobo/Kindle), converta-o para CBZ/PDF padrão.');
  }

  // Força o worker em processo no Node (Electron main): sem isso o pdfjs tenta
  // criar Worker via URL de arquivo e falha silenciosamente -> "não abre PDF".
  try {
    lib.GlobalWorkerOptions.workerSrc = require.resolve('pdfjs-dist/legacy/build/pdf.worker.js');
    const PdfWorker = lib.PDFWorker || lib.Worker;
    var worker = PdfWorker ? new PdfWorker({ name: 'neko-pdf-worker' }) : null;
  } catch (_) { var worker = null; }

  patchStandardFonts(lib);
  const params = {
    data,
    useSystemFonts: true,
    isEvalSupported: false,
    disableFontFace: true,
    // pasta LOCAL das fontes padrao (vem no pacote npm pdfjs-dist): sem isto,
    // PDFs de texto simples saem em branco no render Node (@napi-rs/canvas).
    standardFontDataUrl: lib.__nekoStandardFontDir || undefined,
    // nunca usar canvas de DOM (inexistente no main)
    enableXfa: false
  };
  if (worker) params.worker = worker;
  try {
    _doc = await lib.getDocument(params).promise;
  } catch (e) {
    // senha / formato exótico -> mensagem clara em vez de erro cru do pdfjs
    const msg = String(e && e.message || e);
    if (/password/i.test(msg)) throw new Error('Este PDF exige senha. Remova a proteção ou abra outro arquivo.');
    throw new Error('Não foi possível abrir o PDF: ' + msg);
  }
  _docKey = file + ':' + st.mtimeMs;
  return _doc;
}

async function pageCount(file) {
  const doc = await getDoc(file);
  return doc.numPages;
}

/** Renderiza a pagina (0-based) num PNG data-url ~110 DPI x escala. */
function loadCanvas() {
  // Ordem de preferencia: @napi-rs/canvas (prebuilds p/ Windows x64, nada a compilar) > canvas (nativo).
  try { return require('@napi-rs/canvas'); } catch (_) { /* tenta o proximo */ }
  try { return require('canvas'); } catch (_) { return null; }
}

async function renderPageDataUrl(file, index) {
  const doc = await getDoc(file);
  const page = await doc.getPage(index + 1);
  const vp = page.getViewport({ scale: 1 });
  const scale = Math.min(2, Math.max(1, 1400 / vp.width)); // resolucao boa p/ leitura
  const viewport = page.getViewport({ scale });

  // Canvas offscreen: pacote opcional. Aceita 'canvas' ou '@napi-rs/canvas'
  // (este ultimo tem prebuilds prontos para Windows x64 - nada compilar).
  const Canvas = loadCanvas();
  if (!Canvas) {
    return placeholderSvg(`PDF ${path.basename(file)} - pagina ${index + 1}\nRenderizador de PDF indisponivel neste PC (opcional).`);
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

/** Extrai o texto de uma pagina (0-based). '' = pagina escaneada/sem camada de texto. */
async function pageText(file, index) {
  const doc = await getDoc(file);
  const page = await doc.getPage(index + 1);
  const tc = await page.getTextContent();
  return tc.items.map(i => i.str).join(' ').replace(/\s+/g, ' ').trim();
}

module.exports = { pageCount, renderPageDataUrl, pageText, getDoc };
