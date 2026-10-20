/*
 * archives.js - abre CBZ/ZIP, CBT/TAR, CB7/7Z e CBR/RAR sem .NET.
 *
 * Estratégia RAR (a que dava NRE no SharpCompress):
 *   1) sevenzip (7z Bindings nativos embutidos no proprio pacote -> app portavel)
 *   2) 7z.exe / UnRAR.exe encontrados no sistema (WinRAR/7-Zip instalados)
 *   3) unrar.js (WASM) se o vendor estiver presente
 * Todas as falhas viram mensagens amigaveis, nunca estouram na UI.
 */
const fs = require('fs');
const path = require('path');
const { execFile } = require('child_process');
const os = require('os');
const AdmZip = require('adm-zip');
const tar = require('tar');

const IMG_RE = /\.(jpe?g|png|gif|webp|bmp|avif)$/i;

// ordenacao natural de nomes de paginas (001.jpg < 002.jpg ... < page10)
function naturalCompare(a, b) {
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
}

function dataUrl(buf, name) {
  const ext = (path.extname(name) || '.jpg').toLowerCase();
  const mime = ext === '.png' ? 'image/png' : ext === '.gif' ? 'image/gif'
    : ext === '.webp' ? 'image/webp' : ext === '.bmp' ? 'image/bmp' : 'image/jpeg';
  return `data:${mime};base64,${buf.toString('base64')}`;
}

// ---------- cache de listas por arquivo (mtime como invalidacao) ----------
const listCache = new Map(); // file -> { mtimeMs, kind, pages:[names] }

async function detectKind(file) {
  const fd = fs.openSync(file, 'r');
  try {
    const b = Buffer.alloc(8);
    fs.readSync(fd, b, 0, 8, 0);
    if (b[0] === 0x50 && b[1] === 0x4b) return 'zip';          // PK.. (cbz/zip)
    if (b.slice(0, 6).toString('latin1').startsWith('Rar!')) return 'rar';
    if (b[0] === 0x37 && b[1] === 0x37 && b[2] === 0xBC) return '7z'; // 7z..
    if (b.slice(0, 2).toString('hex') === '1f8b') return 'tgz';       // gzip(tar)
    // ustar magic no offset 257
    const b2 = Buffer.alloc(6);
    fs.readSync(fd, b2, 0, 6, 257);
    if (b2.toString('latin1').startsWith('ustar')) return 'tar';
    return 'unknown';
  } finally { fs.closeSync(fd); }
}

// ================= ZIP (CBZ) =================
function zipListPages(file) {
  const zip = new AdmZip(file);
  const names = zip.getEntries()
    .filter(e => !e.isDirectory && IMG_RE.test(e.entryName))
    .map(e => e.entryName)
    .sort(naturalCompare);
  return names;
}
function zipReadPage(file, index, names) {
  const zip = new AdmZip(file);
  const entry = zip.getEntry(names[index]);
  if (!entry) throw new Error('pagina nao encontrada no arquivo');
  return entry.getData();
}

// ================= TAR (CBT) =================
async function tarListPages(file) {
  const entries = [];
  await tar.list({
    file,
    onEntry: (e) => { if (e.type === 'File' && IMG_RE.test(e.path)) entries.push(e.path); }
  });
  return entries.sort(naturalCompare);
}
async function tarReadPage(file, index, names) {
  // extrai apenas o membro desejado para uma pasta temporaria
  const tmp = path.join(os.tmpdir(), 'neko-tar-' + Date.now() + '-' + index);
  await tar.x({ file, cwd: tmp, filter: (p) => p === names[index] });
  try {
    const abs = path.join(tmp, names[index]);
    if (!fs.existsSync(abs)) throw new Error('membro nao encontrado no TAR');
    return fs.readFileSync(abs);
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
}

// ================= 7Z (CB7) via sevenzip (bindings embutidos) =================
let _sz = null;
function sevenzip() {
  if (_sz === null) {
    try { _sz = require('sevenzip'); } catch (e) { _sz = false; }
  }
  return _sz;
}
async function szListPages(file) {
  const sz = sevenzip();
  if (!sz) throw new Error('sevenzip indisponivel');
  const arch = await sz.open(file);
  try {
    const list = await arch.list();
    return list.filter(e => !e.isFolder && IMG_RE.test(e.filename))
      .map(e => e.filename).sort(naturalCompare);
  } finally { await arch.close(); }
}
async function szReadPage(file, index, names) {
  const sz = sevenzip();
  const arch = await sz.open(file);
  try {
    const buf = await arch.extract(names[index]);
    return buf;
  } finally { await arch.close(); }
}

// ================= CLI externa (fallback universal p/ RAR e 7Z) =================
function findTool() {
  const cands = [
    process.env.NEKO_7Z,
    'C:\\Program Files\\7-Zip\\7z.exe',
    'C:\\Program Files (x86)\\7-Zip\\7z.exe',
    'C:\\Program Files\\WinRAR\\UnRAR.exe',
    'C:\\Program Files\\WinRAR\\Rar.exe',
    'C:\\Program Files (x86)\\WinRAR\\UnRAR.exe'
  ].filter(Boolean);
  for (const c of cands) { try { if (fs.existsSync(c)) return c; } catch (_) {} }
  // PATH
  const which = process.platform === 'win32' ? 'where' : 'which';
  for (const name of ['7z', 'unrar', '7za']) {
    try {
      const out = require('child_process').execSync(`${which} ${name}`, { stdio: ['ignore', 'pipe', 'ignore'] }).toString();
      const p = out.split(/\r?\n/)[0].trim();
      if (p && fs.existsSync(p)) return p;
    } catch (_) {}
  }
  return null;
}

function runTool(tool, args) {
  return new Promise((resolve, reject) => {
    execFile(tool, args, { windowsHide: true, maxBuffer: 64 * 1024 * 1024 }, (err, stdout, stderr) => {
      if (err) reject(new Error((stderr || err.message).slice(0, 400)));
      else resolve(stdout);
    });
  });
}

async function cliListPages(file, tool) {
  const isUnrar = /unrar|rar\.exe$/i.test(tool);
  const out = await runTool(tool, isUnrar ? ['lb', '-y', file] : ['l', '-slt', '-ba', file]);
  const lines = out.split(/\r?\n/);
  const names = [];
  if (isUnrar) {
    for (const l of lines) { const m = l.match(/^\s*\d+\s+.*\s(\S+\.(jpe?g|png|gif|webp|bmp))$/i); if (m) names.push(m[1]); }
    if (!names.length) { // formato alternativo da saida do unrar lb
      for (const l of lines) if (IMG_RE.test(l.trim())) names.push(l.trim());
    }
  } else {
    for (const l of lines) {
      const m = l.match(/^\s*Path\s*=\s*(.+)$/);
      if (m && IMG_RE.test(m[1].trim())) names.push(m[1].trim());
    }
  }
  return names.sort(naturalCompare);
}

async function cliReadPage(file, index, names, tool) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'neko-cli-'));
  try {
    const isUnrar = /unrar|rar\.exe$/i.test(tool);
    const args = isUnrar
      ? ['x', '-y', '-idw', file, names[index], tmp + path.sep]
      : ['x', '-y', `-o${tmp}`, file, names[index]];
    await runTool(tool, args);
    const abs = path.join(tmp, names[index]);
    if (!fs.existsSync(abs)) throw new Error('extracao nao produziu arquivo');
    return fs.readFileSync(abs);
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
}

// ================= unrar.js (WASM) - ultimo recurso =================
async function wasmListPages(file) {
  // vendor/unrar.js deve conter uma build ARQ do unrar.js (opcional)
  const mod = require('../vendor/unrar.js/open.js');
  const data = fs.readFileSync(file);
  const res = await mod.listRAR(data.buffer);
  return res.entries.map(e => e.filename).filter(n => IMG_RE.test(n)).sort(naturalCompare);
}

// ================= API publica =================
async function listPages(file) {
  const st = fs.statSync(file);
  const hit = listCache.get(file);
  if (hit && hit.mtimeMs === st.mtimeMs) return hit;

  const kind = await detectKind(file);
  let pages = [];
  let used = kind;

  if (kind === 'zip') pages = zipListPages(file);
  else if (kind === 'tar' || kind === 'tgz') pages = await tarListPages(file);
  else if (kind === '7z') {
    try { pages = await szListPages(file); }
    catch (e) {
      const tool = findTool();
      if (!tool) throw new Error('Este .cb7 precisa do 7-Zip instalado ou bindings indisponiveis.');
      pages = await cliListPages(file, tool); used = '7z-cli';
    }
  } else if (kind === 'rar') {
    // 1) sevenzip native
    try {
      const sz = sevenzip();
      if (sz) { pages = await szListPages(file); used = 'rar-sz'; }
      else throw new Error('sem sevenzip');
    } catch (e1) {
      // 2) CLI (UnRAR/7z)
      const tool = findTool();
      if (tool) { pages = await cliListPages(file, tool); used = 'rar-cli'; }
      else {
        // 3) WASM
        try { pages = await wasmListPages(file); used = 'rar-wasm'; }
        catch (e3) {
          throw new Error('Nao foi possivel abrir este RAR/CBR (formato novo, protegido por senha ou corrompido). Instale o WinRAR ou 7-Zip, ou converta o arquivo para .CBZ.');
        }
      }
    }
  } else {
    throw new Error('Formato nao reconhecido. Use CBZ, CBR, CBT, CB7 ou PDF.');
  }

  if (!pages.length) throw new Error('Nenhuma imagem encontrada dentro do arquivo.');
  const info = { mtimeMs: st.mtimeMs, kind: used, file, pages };
  listCache.set(file, info);
  return info;
}

async function readPageDataUrl(file, index) {
  const info = await listPages(file);
  const name = info.pages[index];
  if (!name) throw new Error('indice de pagina fora do intervalo');
  let buf;
  switch (info.kind) {
    case 'zip': buf = zipReadPage(file, index, info.pages); break;
    case 'tar': case 'tgz': buf = await tarReadPage(file, index, info.pages); break;
    case '7z': case 'rar-sz': buf = await szReadPage(file, index, info.pages); break;
    case '7z-cli': case 'rar-cli': buf = await cliReadPage(file, index, info.pages, findTool()); break;
    default: {
      // rar-wasm ou desconhecido: extrai via wasm
      const mod = require('../vendor/unrar.js/open.js');
      const data = fs.readFileSync(file);
      const res = await mod.unRARAll(data.buffer);
      const f = res.files.find(x => x.name === name);
      buf = Buffer.from(f.content);
    }
  }
  return dataUrl(buf, name);
}

module.exports = { listPages, readPageDataUrl, naturalCompare };
