/*
 * pdf.js - leitura de PDF sem .NET e sem binario extra.
 * Estratégia (inspirada no pdfcraft / pdf.js no navegador): o RENDER das páginas
 * acontece no <canvas> do RENDERER (Chromium tem DOMMatrix/Path2D/fontes nativas —
 * é onde o texto SEMPRE aparece). O processo principal usa pdfjs apenas para
 * contagem de páginas e extração de texto (IA/busca), e serve os bytes do PDF.
 */
const fs = require('fs');
const path = require('path');

// Pasta LOCAL das fontes padrão do pdfjs (Helvetica/Courier/Times) — usada só
// como reforço no main (extração de texto não depende delas, render sim quando
// algo rasterizar no Node). Em app empacotado vem de resources/standard_fonts.
function standardFontDir() {
  const cands = [];
  try { cands.push(path.join(process.resourcesPath || '', 'standard_fonts')); } catch (_) {}
  try { cands.push(path.join(path.dirname(require.resolve('pdfjs-dist/package.json')), 'standard_fonts')); } catch (_) {}
  try { cands.push(path.join(__dirname, '..', 'vendor', 'standard_fonts')); } catch (_) {}
  for (const c of cands) { try { if (c && fs.existsSync(c)) return c; } catch (_) {} }
  return null;
}
function standardFontUrl() {
  const d = standardFontDir();
  if (!d) return undefined;
  return 'file:///' + d.split(path.sep).join('/').replace(/^\/+/, '') + '/';
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
  let worker = null;
  try {
    lib.GlobalWorkerOptions.workerSrc = require.resolve('pdfjs-dist/legacy/build/pdf.worker.js');
    const PdfWorker = lib.PDFWorker || lib.Worker;
    if (PdfWorker) worker = new PdfWorker({ name: 'neko-pdf-worker' });
  } catch (_) { worker = null; }

  const params = {
    data,
    useSystemFonts: true,
    isEvalSupported: false,
    disableFontFace: true,
    // pasta LOCAL das fontes padrao (reforço; o render principal é no renderer)
    standardFontDataUrl: standardFontUrl(),
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

/** Bytes do PDF (o renderer faz o parse/render com pdfjs no Chromium). */
function readPdfBytes(file) {
  return new Uint8Array(fs.readFileSync(file));
}

/** Extrai o texto de uma pagina (0-based). '' = pagina escaneada/sem camada de texto. */
async function pageText(file, index) {
  const doc = await getDoc(file);
  const page = await doc.getPage(index + 1);
  const tc = await page.getTextContent();
  return tc.items.map(i => i.str).join(' ').replace(/\s+/g, ' ').trim();
}

module.exports = { pageCount, readPdfBytes, pageText, getDoc };
