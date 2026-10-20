/* library.js - varre pastas da biblioteca, detecta livros, le ComicInfo.xml e gera capas. */
const fs = require('fs');
const path = require('path');
const AdmZip = require('adm-zip');

const BOOK_RE = /\.(cbz|cbr|cbt|cb7|zip|rar|7z|tar|pdf)$/i;

// extrai a primeira imagem de um arquivo suportado (para capa) -> Buffer ou null
async function firstImage(file) {
  try {
    const ext = path.extname(file).toLowerCase();
    if (ext === '.cbz' || ext === '.zip') {
      const zip = new AdmZip(file);
      const e = zip.getEntries().filter(x => !x.isDirectory && /\.(jpe?g|png|webp|gif|bmp)$/i.test(x.entryName))
        .sort((a, b) => a.entryName.localeCompare(b.entryName, undefined, { numeric: true }))[0];
      return e ? e.getData() : null;
    }
    // para outros formatos, usa o reader comum (pode acionar CLI/WASM)
    const archives = require('./archives.js');
    const info = await archives.listPages(file);
    const dataUrl = await archives.readPageDataUrl(file, 0);
    const b64 = dataUrl.split(',', 2)[1];
    return Buffer.from(b64, 'base64');
  } catch (_) {
    return null;
  }
}

function readComicInfoZip(file) {
  try {
    const zip = new AdmZip(file);
    const e = zip.getEntry('ComicInfo.xml');
    if (!e) return null;
    const xml = e.getData().toString('utf8');
    const grab = (tag) => { const m = xml.match(new RegExp(`<${tag}>([^<]*)</${tag}>`, 'i')); return m ? m[1].trim() : null; };
    return {
      title: grab('Title'), series: grab('Series'), number: grab('Number'),
      writer: grab('Writer'), publisher: grab('Publisher'), year: grab('Year'),
      manga: (grab('Manga') || '').toLowerCase() === 'yes'
    };
  } catch (_) { return null; }
}

async function scan(folders) {
  const items = [];
  for (const folder of (folders || [])) {
    let entries = [];
    try { entries = fs.readdirSync(folder, { withFileTypes: true }); } catch (_) { continue; }
    for (const ent of entries) {
      if (!ent.isFile() || !BOOK_RE.test(ent.name)) continue;
      const full = path.join(folder, ent.name);
      const base = path.basename(ent.name, path.extname(ent.name));
      let meta = null;
      if (/\.cbz$|\.zip$/i.test(ent.name)) meta = readComicInfoZip(full);
      items.push({
        file: full, name: (meta && meta.title) || base,
        folder, size: fs.statSync(full).size,
        manga: meta ? !!meta.manga : /mangá|manga/i.test(base),
        ext: path.extname(ent.name).toLowerCase()
      });
    }
  }
  items.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
  return items;
}

async function coverDataUrl(file) {
  const buf = await firstImage(file);
  if (!buf) return null;
  const ext = lookMagic(buf);
  return `data:${ext};base64,${buf.toString('base64')}`;
}

function lookMagic(b) {
  if (b[0] === 0xFF && b[1] === 0xD8) return 'image/jpeg';
  if (b[0] === 0x89 && b[1] === 0x50) return 'image/png';
  if (b.slice(0, 4).toString('latin1') === 'RIFF') return 'image/webp';
  if (b.slice(0, 3).toString('latin1') === 'GIF') return 'image/gif';
  return 'image/jpeg';
}

async function readComicInfo(file) {
  if (/\.cbz$|\.zip$/i.test(file)) return readComicInfoZip(file);
  return null;
}

module.exports = { scan, coverDataUrl, readComicInfo };
