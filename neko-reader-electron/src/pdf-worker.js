/*
 * pdf-worker.js - processo filho que faz TODO o trabalho pesado de PDF.
 * É iniciado por src/pdf.js (utilityProcess.fork dentro do Electron,
 * child_process.fork em teste puro-Node). Se este processo cair (PDF
 * corrompido, falta de memória), o app principal continua vivo e mostra
 * uma mensagem clara em vez de travar.
 *
 * Protocolo (mensagens IPC simples):
 *   pedido : { id, type: 'count'|'text'|'render', file, key, index? }
 *   resposta: { id, result } | { id, error }
 */
const fs = require('fs');

// funciona tanto como fork IPC do Node (process.send) quanto utilityProcess do Electron
const send = (msg) => {
  try { if (typeof process.send === 'function') { process.send(msg); return; } } catch (_) {}
  try { const { parentPort } = require('worker_threads'); if (parentPort) { parentPort.postMessage(msg); return; } } catch (_) {}
};

let lib = null;
function loadLib() {
  if (lib) return lib;
  // dentro do app empacotado (asar), __dirname/../../node_modules é o caminho real
  const local = require('path').join(__dirname, '..', 'node_modules', 'pdfjs-dist', 'legacy', 'build', 'pdf.js');
  try { lib = require(local); return lib; } catch (_) {}
  try { lib = require('pdfjs-dist/legacy/build/pdf.js'); return lib; } catch (_) {}
  try { lib = require('pdfjs-dist/build/pdf.js'); return lib; } catch (_) {}
  throw new Error('pdfjs-dist não encontrado. Rode "npm install" na pasta do NekoReader.');
}

const assets = (() => {
  try { return JSON.parse(process.env.NEKO_PDF_ASSETS || '{}'); } catch (_) { return {}; }
})();

function loadCanvas() {
  const nm = require('path').join(__dirname, '..', 'node_modules');
  const candidates = [
    require('path').join(nm, '@napi-rs', 'canvas', 'index.js'),
    '@napi-rs/canvas',
    require('path').join(nm, 'canvas', 'index.js'),
    'canvas'
  ];
  for (const c of candidates) { try { return require(c); } catch (_) {} }
  return null;
}

const docs = new Map(); // chave = caminho+mtime+tamanho -> PDFDocumentProxy

async function getDoc(key, file) {
  if (docs.has(key)) return docs.get(key);
  const data = new Uint8Array(fs.readFileSync(file));
  const head = Buffer.from(data.subarray(0, Math.min(1024, data.length))).toString('latin1');
  if (!head.startsWith('%PDF-')) {
    throw new Error('Este arquivo não é um PDF válido (cabeçalho ausente). Se for mangá de loja (Kobo/Kindle), converta para CBZ/PDF padrão.');
  }
  const doc = await loadLib().getDocument({
    data,
    // fontes padrao + cmaps LOCAIS: sem isto, páginas saem EM BRANCO ou caracteres sumidos
    standardFontDataUrl: assets.standardFontDataUrl || undefined,
    cMapUrl: assets.cMapUrl || undefined,
    cMapPacked: true,
    useSystemFonts: true,
    disableFontFace: true,   // Node não tem FontFace de DOM
    isEvalSupported: false,
    enableXfa: false
  }).promise;
  docs.set(key, doc);
  return doc;
}

async function handle(m) {
  if (m.type === 'count') {
    const doc = await getDoc(m.key, m.file);
    return doc.numPages;
  }
  if (m.type === 'text') {
    const doc = await getDoc(m.key, m.file);
    const page = await doc.getPage(m.index + 1);
    const tc = await page.getTextContent();
    return tc.items.map(i => i.str).join(' ').replace(/\s+/g, ' ').trim();
  }
  if (m.type === 'render') {
    const Canvas = loadCanvas();
    if (!Canvas) throw new Error('Renderizador de PDF indisponível: instale @napi-rs/canvas (npm install) neste PC.');
    const doc = await getDoc(m.key, m.file);
    const page = await doc.getPage(m.index + 1);
    const vp1 = page.getViewport({ scale: 1 });
    const scale = Math.min(2, Math.max(1, 1400 / vp1.width)); // resolução boa p/ leitura
    const viewport = page.getViewport({ scale });
    const canvas = Canvas.createCanvas(Math.round(viewport.width), Math.round(viewport.height));
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: ctx, viewport }).promise;
    return canvas.toBuffer('image/png').toString('base64');
  }
  throw new Error('comando desconhecido: ' + m.type);
}

const onMessage = (m) => {
  if (!m || m.id === undefined) return; // ignora {ready:true} e mensagens alheias
  Promise.resolve()
    .then(() => handle(m))
    .then((result) => send({ id: m.id, result }))
    .catch((e) => {
      const msg = String((e && e.message) || e);
      if (/password|encrypt/i.test(msg)) {
        send({ id: m.id, error: 'Este PDF exige senha. Remova a proteção ou abra outro arquivo.' });
      } else {
        send({ id: m.id, error: msg });
      }
    });
};

if (typeof process.send === 'function') process.on('message', onMessage); // fork Node (IPC real)
try {
  const { parentPort } = require('worker_threads');
  if (parentPort) parentPort.on('message', onMessage);                    // utilityProcess Electron
} catch (_) {}

send({ ready: true });

// em modo fork do Node, não segura o processo pai quando ele quiser encerrar
if (typeof process.unref === 'function' && typeof process.send === 'function') process.unref();
