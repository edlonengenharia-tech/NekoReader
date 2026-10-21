/*
 * pdf.js - leitura de PDF sem .NET e sem binario extra.
 * Estratégia (por que o PDF "não abria"):
 *   1. O worker do pdfjs roda NUM FILHO separado (child_process). Assim um PDF
 *      corrompido/protegido NUNCA derruba nem trava o app inteiro.
 *   2. A comunicação main<->filho usa MessagePort nativo do Node
 *      ({parentPort, port2}) — nada de JSON por stdin/stdout.
 *   3. Fontes padrao (Helvetica etc.) e cmaps (PDFs japoneses) vêm da pasta LOCAL
 *      do pacote pdfjs-dist. Sem isso a página sai EM BRANCO no Node.
 *   4. Renderização offscreen com @napi-rs/canvas (ou node-canvas). Se nenhum
 *      existir, devolve um placeholder com aviso (o app nunca quebra).
 */
const fs = require('fs');
const os = require('os');
const path = require('path');


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

// Pasta LOCAL das fontes padrao e dos cmaps (dentro do node_modules de pdfjs-dist).
// Funciona também dentro do asar (electron-builder extrai p/ um tmp).
function localAssetUrls() {
  try {
    const base = path.dirname(require.resolve('pdfjs-dist/package.json'));
    const mk = (sub) => {
      let dir = path.join(base, sub);
      if (!fs.existsSync(dir)) return undefined;
      // dentro do asar, o fetcher do Node não lê o arquivo virtual -> extrai p/ tmp
      if (dir.includes('app.asar') && dir.includes('asar.unpacked') === false) {
        try {
          const out = path.join(os.tmpdir(), 'neko-' + sub);
          fs.mkdirSync(out, { recursive: true });
          for (const f of fs.readdirSync(dir)) {
            const dst = path.join(out, f);
            if (!fs.existsSync(dst)) fs.writeFileSync(dst, fs.readFileSync(path.join(dir, f)));
          }
          dir = out;
        } catch (_) { return undefined; }
      }
      return 'file:///' + dir.split(path.sep).join('/').replace(/^\/+/, '') + '/';
    };
    return { standardFontDataUrl: mk('standard_fonts'), cMapUrl: mk('cmaps') };
  } catch (_) { return {}; }
}

// ---------------- worker em processo filho ----------------
// O código do worker vive num ARQUIVO separado (pdf-worker.js): assim ele enxerga
// pdfjs-dist e @napi-rs/canvas pelos node_modules do app (resolução normal do Node,
// inclusive dentro do asar — electron-builder desempacota os .node nativos).

let _child = null;
let _childStderr = '';
let _reqId = 0;
const _pending = new Map();

function spawnWorker() {
  const scriptPath = path.join(__dirname, 'pdf-worker.js');
  const env = { ...process.env };
  env.NEKO_PDF_ASSETS = JSON.stringify(localAssetUrls());
  // Dentro do Electron usamos utilityProcess (processo utilitário oficial);
  // fora dele (smoke test em Node puro) usamos child_process.fork.
  let electronMod = null;
  try { electronMod = require('electron'); } catch (_) {}
  if (electronMod && electronMod.utilityProcess && typeof electronMod.utilityProcess.fork === 'function') {
    return electronMod.utilityProcess.fork(scriptPath, [], { env, stdio: 'pipe' });
  }
  const { fork } = require('child_process');
  return fork(scriptPath, [], { env, stdio: ['ignore', 'pipe', 'pipe', 'ipc'] });
}

function ensureChild() {
  if (!loadPdfjs()) {
    throw new Error('Para ler PDFs rode "npm install" nesta pasta (o pacote pdfjs-dist deve ficar junto do app portátil) ou converta o PDF para CBZ.');
  }
  if (_child && _child.connected !== false) return _child;
  const old = _child;
  _child = null;
  try { if (old) old.kill(); } catch (_) {} // garante: filho antigo nunca fica zumbi
  const child = spawnWorker();
  _childStderr = '';
  if (child.stderr) child.stderr.on('data', (d) => { _childStderr += String(d); if (_childStderr.length > 8000) _childStderr = _childStderr.slice(-4000); });
  child.on('message', (m) => {
    if (!m || m.ready) return;
    const p = _pending.get(m.id);
    if (!p) return;
    _pending.delete(m.id);
    clearTimeout(p.timer);
    if (m.error) p.reject(new Error(m.error)); else p.resolve(m.result);
  });
  const failAll = () => {
    for (const [, p] of _pending) { clearTimeout(p.timer); p.reject(new Error('O leitor de PDF fechou inesperadamente.' + (_childStderr ? '\n' + _childStderr.slice(-500) : ''))); }
    _pending.clear();
    _child = null;
  };
  child.on('exit', failAll);
  child.on('error', failAll);
  _child = child;
  return child;
}

function callWorker(type, payload, timeoutMs) {
  const child = ensureChild();
  const id = ++_reqId;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      _pending.delete(id);
      reject(new Error('O PDF demorou demais para responder (arquivo grande ou corrompido?). Tente abrir novamente.'));
    }, timeoutMs || 90000);
    _pending.set(id, { resolve, reject, timer });
    try { child.postMessage ? child.postMessage({ id, type, ...payload }) : child.send({ id, type, ...payload }); }
    catch (e) { clearTimeout(timer); _pending.delete(id); reject(e); }
  });
}

async function getDocInfo(file) {
  const st = fs.statSync(file);
  return { key: file + ':' + st.mtimeMs + ':' + st.size, size: st.size };
}

async function pageCount(file) {
  loadPdfjs();
  const { key } = await getDocInfo(file);
  return callWorker('count', { file, key }, 120000);
}

function placeholderSvg(text) {
  const esc = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/\n/g, '&#10;');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="1100"><rect width="100%" height="100%" fill="#222"/><text x="40" y="80" fill="#eee" font-family="sans-serif" font-size="24" xml:space="preserve">${esc}</text></svg>`;
  return 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
}

/** Renderiza a pagina (0-based) num PNG data-url. Em falha -> placeholder legivel. */
async function renderPageDataUrl(file, index) {
  loadPdfjs();
  const { key } = await getDocInfo(file);
  try {
    const b64 = await callWorker('render', { file, key, index }, 180000);
    return `data:image/png;base64,${b64}`;
  } catch (e) {
    const msg = String(e && e.message || e);
    // erros de senha/formato merecem banner no leitor; problemas de render -> placeholder
    if (/senha|password|encrypt|não é um PDF|nao e um pdf/i.test(msg)) throw e;
    return placeholderSvg(`PDF ${path.basename(file)} - pagina ${index + 1}\nNão foi possível desenhar esta página:\n${msg.slice(0, 200)}`);
  }
}

/** Extrai o texto de uma pagina (0-based). '' = pagina escaneada/sem camada de texto. */
async function pageText(file, index) {
  loadPdfjs();
  const { key } = await getDocInfo(file);
  try { return await callWorker('text', { file, key, index }, 60000); }
  catch (_) { return ''; }
}

/** Encerra o processo filho do PDF (chamado ao sair do app). */
function shutdown() {
  try { if (_child) _child.kill(); } catch (_) {}
  _child = null;
}

// em teste puro-Node (smoke): garante que o processo filho morre junto
process.once('exit', () => { try { if (_child) _child.kill(); } catch (_) {} });

module.exports = { pageCount, renderPageDataUrl, pageText, shutdown };
