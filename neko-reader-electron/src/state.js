/* state.js - persistencia simples em %APPDATA%/NekoReader (settings + posicao de leitura). */
const fs = require('fs');
const path = require('path');

const DIR = process.env.APPDATA || path.join(require('os').homedir(), '.config');
const FILE = path.join(DIR, 'NekoReader', 'state.json');

const DEFAULTS = {
  settings: {
    mangaByDefault: false,
    doublePageByDefault: false,
    fit: 'width',            // width | height | page
    flipAnimation: true,     // virada de pagina em 3D (realista)
    theme: 'dark',           // dark | midnight | light
    showLibraryOnStart: true,
    sidebarWidth: 300,
    prefetch: 3,
    clickZones: true,
    associateFiles: false,
    keepAwake: false,        // impede o PC de dormir durante a leitura
    // IA (Qwen / compativel OpenAI)
    aiProvider: 'qwen',      // qwen | openai_compat
    aiKey: '',
    aiBaseUrl: '',
    aiModel: ''
  },
  libraryFolders: [],
  positions: {},             // file -> { page, total, ts }
  lastFile: null
};

let cache = null;

function load() {
  if (cache) return cache;
  try {
    const raw = JSON.parse(fs.readFileSync(FILE, 'utf8'));
    cache = {
      ...DEFAULTS, ...raw,
      settings: { ...DEFAULTS.settings, ...(raw.settings || {}) }
    };
  } catch (_) {
    cache = JSON.parse(JSON.stringify(DEFAULTS));
  }
  return cache;
}

function save(patch) {
  const cur = load();
  const next = {
    ...cur, ...patch,
    settings: { ...cur.settings, ...(patch.settings || {}) }
  };
  cache = next;
  try {
    fs.mkdirSync(path.dirname(FILE), { recursive: true });
    fs.writeFileSync(FILE, JSON.stringify(next, null, 2), 'utf8');
  } catch (_) { /* melhor esforco */ }
  return next;
}

module.exports = { load, save };
