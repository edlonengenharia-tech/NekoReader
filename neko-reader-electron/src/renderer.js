/* NekoReader - renderer: navegação, mangá RTL, dupla, zoom, fit, animação, HUD, biblioteca. */
'use strict';

const $ = (s) => document.querySelector(s);
const el = {
  body: document.body,
  dropzone: $('#dropzone'), pages: $('#pages'), empty: $('#empty-state'),
  slot0: $('#slot0'), slot1: $('#slot1'),
  img0: $('#slot0 img'), img1: $('#slot1 img'),
  ind: $('#page-ind'), hud: $('#hud'), hudPage: $('#hud-page'), progFill: $('#progress-fill'),
  err: $('#error-banner'), bookTitle: $('#book-title'), sidebar: $('#sidebar'),
  libGrid: $('#lib-grid'), contRow: $('#continue-row'), search: $('#search'),
  btnManga: $('#btn-manga'), btnDouble: $('#btn-double'), btnFit: $('#btn-fit'),
  zoomPct: $('#zoom-pct'), lastCover: $('#last-cover')
};

// ---------------- estado ----------------
let S = null;                 // settings persistidos
let positions = {};           // file -> {page,total}
let libraryFolders = [];
let libItems = [];
let book = null;              // { file, pages:[names], count }
let view = 0;                 // indice de exibicao (ja considera RTL)
let manga = false, dbl = false, fit = 'width', zoom = 1;
let pageCache = new Map();    // physIdx -> dataUrl (LRU simples)
let filter = 'all';

const FIT_LABEL = { width: 'Largura', height: 'Altura', page: 'Página' };

// ---------------- helpers RTL ----------------
const phys = (v) => manga ? (book.count - 1 - v) : v;      // exibicao -> fisica
const disp = (p) => manga ? (book.count - 1 - p) : p;      // fisica -> exibicao

// ---------------- persistencia ----------------
async function loadState() {
  const st = await neko.getState();
  S = st.settings; positions = st.positions || {}; libraryFolders = st.libraryFolders || [];
  el.body.dataset.theme = S.theme;
  document.documentElement.style.setProperty('--side', S.sidebarWidth + 'px');
  manga = !!S.mangaByDefault; dbl = !!S.doublePageByDefault; fit = S.fit || 'width';
  syncToggles();
}
async function savePositions() {
  await neko.setState({ positions, libraryFolders });
}

// ---------------- abrir livro ----------------
async function openPath(p) {
  hideError();
  try {
    if (/\.pdf$/i.test(p) || /\.(cbz|cbr|cbt|cb7|zip|rar|7z|tar)$/i.test(p)) {
      const info = await neko.openBook(p);
      book = { file: p, count: info.pages ? info.pages.length : info.pageCount, pages: info.pages || [] };
      el.bookTitle.textContent = p.split(/[\\/]/).pop();
      // metadados podem definir mangá
      const meta = await neko.metaOf(p);
      if (meta && meta.manga) { manga = true; syncToggles(); }
      // restaura posição
      const pos = positions[p];
      view = pos ? Math.min(Math.max(0, disp(pos.page)), book.count - 1) : 0;
      el.empty.hidden = true; el.pages.hidden = false; el.hud.hidden = false;
      ensureClickZones();
      render(true);
      prefetch();
    }
  } catch (e) {
    showError(e.message || String(e));
  }
}

neko.onOpenPath(openPath);

function closeBook() {
  book = null; view = 0; pageCache.clear();
  el.pages.hidden = true; el.hud.hidden = true; el.bookTitle.textContent = '';
  el.empty.hidden = false; removeClickZones();
}

// ---------------- render ----------------
async function render(animate) {
  if (!book) return;
  const pair = dbl ? [view, view + 1] : [view];
  const slots = [el.slot0, el.slot1];
  for (let i = 0; i < 2; i++) {
    const v = pair[i];
    if (v === undefined || v >= book.count) { slots[i].hidden = true; continue; }
    slots[i].hidden = false;
    const img = slots[i].querySelector('img');
    const url = await getPage(phys(v));
    if (img.src !== url) img.src = url;
    applyFit(img);
    if (animate && S.flipAnimation) {
      slots[i].classList.remove('flipping'); void slots[i].offsetWidth; slots[i].classList.add('flipping');
    }
  }
  // ordem visual em RTL: espelha os slots
  el.pages.style.flexDirection = (manga && dbl) ? 'row-reverse' : 'row';

  const cur = Math.min(phys(view), book.count - 1);
  el.ind.textContent = `${cur + 1} / ${book.count}`;
  el.hudPage.textContent = `${cur + 1} / ${book.count}`;
  el.progFill.style.width = ((cur + 1) / book.count * 100) + '%';
  el.progFill.parentElement.style.transform = manga ? 'scaleX(-1)' : '';

  positions[book.file] = { page: cur, total: book.count, ts: Date.now() };
  debouncedSave();
}

async function getPage(idx) {
  if (pageCache.has(idx)) { const v = pageCache.get(idx); pageCache.delete(idx); pageCache.set(idx, v); return v; }
  const url = await neko.pageImage(book.file, idx);
  pageCache.set(idx, url);
  while (pageCache.size > 8) pageCache.delete(pageCache.keys().next().value);
  return url;
}

function prefetch() {
  if (!book || !S.prefetch) return;
  for (let d = 1; d <= S.prefetch; d++) {
    for (const v of [view + d, view - d]) {
      if (v >= 0 && v < book.count) getPage(phys(v)).catch(() => {});
    }
  }
}

function applyFit(img) {
  img.style.width = ''; img.style.height = ''; img.style.maxWidth = ''; img.style.maxHeight = '';
  const zone = el.dropzone.getBoundingClientRect();
  const maxH = zone.height - 40, maxW = (dbl ? zone.width / 2 : zone.width) - 40;
  let mode = fit;
  img.onload = () => {
    const iw = img.naturalWidth, ih = img.naturalHeight;
    let scale = 1;
    if (mode === 'width') scale = maxW / iw;
    else if (mode === 'height') scale = maxH / ih;
    else scale = Math.min(maxW / iw, maxH / ih);
    scale = Math.max(0.05, scale) * zoom;
    img.style.width = Math.round(iw * scale) + 'px';
    img.style.height = Math.round(ih * scale) + 'px';
    img.style.maxWidth = 'none'; img.style.maxHeight = 'none';
  };
  if (img.complete && img.naturalWidth) img.onload();
}

// ---------------- navegacao ----------------
function go(delta) {
  if (!book) return;
  const step = dbl ? 2 : 1;
  view = Math.min(Math.max(0, view + delta * step), book.count - (dbl ? 2 : 1));
  render(true); prefetch();
}
function goFirst() { if (book) { view = 0; render(true); prefetch(); } }
function goLast() { if (book) { view = book.count - (dbl ? 2 : 1); render(true); prefetch(); } }

$('#btn-prev').onclick = () => go(-1);
$('#btn-next').onclick = () => go(+1);
$('#btn-first').onclick = goFirst;
$('#btn-last').onclick = goLast;

// ---------------- toggles ----------------
function syncToggles() {
  el.btnManga.classList.toggle('on', manga);
  el.btnDouble.classList.toggle('on', dbl);
  el.btnFit.textContent = 'Ajuste: ' + FIT_LABEL[fit];
  el.zoomPct.textContent = Math.round(zoom * 100) + '%';
  removeClickZones(); ensureClickZones();
}
el.btnManga.onclick = () => { manga = !manga; if (book) view = disp(phys(view)); syncToggles(); render(false); };
el.btnDouble.onclick = () => { dbl = !dbl; syncToggles(); if (book) render(false); };
el.btnFit.onclick = () => { fit = fit === 'width' ? 'height' : fit === 'height' ? 'page' : 'width'; syncToggles(); if (book) render(false); };
$('#btn-zin').onclick = () => { zoom = Math.min(4, zoom * 1.25); syncToggles(); if (book) render(false); };
$('#btn-zout').onclick = () => { zoom = Math.max(.25, zoom / 1.25); syncToggles(); if (book) render(false); };
$('#btn-close-book').onclick = closeBook;

// ---------------- fullscreen / janela ----------------
let fs = false;
function setFS(on) {
  fs = on; neko.setFullScreen(on);
  $('#btn-fs').classList.toggle('on', on);
  $('#titlebar').style.visibility = on ? 'hidden' : '';
  $('#bottombar').style.visibility = on ? 'hidden' : '';
  setTimeout(() => book && render(false), 120);
}
$('#btn-fs').onclick = () => setFS(!fs);
$('#tb-min').onclick = () => neko.winMinimize();
$('#tb-max').onclick = () => neko.winMaximize();
$('#tb-close').onclick = () => neko.winClose();

// ---------------- teclado ----------------
window.addEventListener('keydown', (e) => {
  if (e.target.matches('input, select, textarea')) return;
  switch (e.key) {
    case 'ArrowRight': go(manga ? -1 : +1); e.preventDefault(); break;
    case 'ArrowLeft': go(manga ? +1 : -1); e.preventDefault(); break;
    case 'Home': goFirst(); break;
    case 'End': goLast(); break;
    case 'f': case 'F': setFS(!fs); break;
    case 'm': case 'M': el.btnManga.click(); break;
    case 'd': case 'D': el.btnDouble.click(); break;
    case 'w': case 'W': el.btnFit.click(); break;
    case 'Tab': toggleSidebar(); e.preventDefault(); break;
    case 'Escape': if (fs) setFS(false); break;
    case '+': case '=': $('#btn-zin').click(); break;
    case '-': $('#btn-zout').click(); break;
    case ',': if (e.ctrlKey) openOptions(); break;
  }
});

// roda do mouse com Ctrl = zoom
el.dropzone.addEventListener('wheel', (e) => {
  if (!e.ctrlKey || !book) return;
  e.preventDefault();
  zoom = Math.min(4, Math.max(.25, zoom * (e.deltaY < 0 ? 1.1 : 0.9)));
  syncToggles(); render(false);
}, { passive: false });

// ---------------- zonas de clique ----------------
let zones = [];
function ensureClickZones() {
  if (!S.clickZones || !book || zones.length) return;
  const mk = (side) => {
    const z = document.createElement('div');
    z.className = 'click-zone ' + side;
    z.onclick = () => {
      const forward = (side === 'right') !== manga;
      go(forward ? +1 : -1);
    };
    el.dropzone.appendChild(z); zones.push(z);
  };
  mk('left'); mk('right');
}
function removeClickZones() { zones.forEach(z => z.remove()); zones = []; }

// ---------------- drag & drop ----------------
['dragenter', 'dragover'].forEach(ev => el.dropzone.addEventListener(ev, (e) => {
  e.preventDefault(); el.dropzone.classList.add('dragover');
}));
['dragleave', 'drop'].forEach(ev => el.dropzone.addEventListener(ev, (e) => {
  e.preventDefault(); el.dropzone.classList.remove('dragover');
}));
el.dropzone.addEventListener('drop', async (e) => {
  const f = e.dataTransfer.files && e.dataTransfer.files[0];
  if (!f) return;
  const p = window.neko ? f.path : f.name;   // Electron expoe .path
  if (/\.(cbz|cbr|cbt|cb7|zip|rar|7z|tar|pdf)$/i.test(p)) openPath(p);
  else if (!/\./.test(p.split(/[\\/]/).pop())) addFolder(p); // soltou pasta
});

$('#btn-open').onclick = async () => {
  const files = await neko.openFiles();
  if (files.length) { await openPath(files[0]); refreshLibrarySoon(); }
};

// ---------------- sidebar / biblioteca ----------------
function toggleSidebar() { el.sidebar.classList.toggle('hidden'); }
$('#btn-lib').onclick = toggleSidebar;

async function refreshLibrary() {
  libItems = await neko.scanLibrary(libraryFolders);
  renderLibrary();
}
let libTimer = null;
function refreshLibrarySoon() { clearTimeout(libTimer); libTimer = setTimeout(refreshLibrary, 600); }

$('#btn-addfolder').onclick = async () => {
  const f = await neko.openFolder();
  if (f && !libraryFolders.includes(f)) {
    libraryFolders.push(f);
    await neko.setState({ libraryFolders });
    refreshLibrary();
  }
};

el.search.addEventListener('input', renderLibrary);
document.querySelectorAll('.chip').forEach(c => c.onclick = () => {
  document.querySelectorAll('.chip').forEach(x => x.classList.remove('active'));
  c.classList.add('active'); filter = c.dataset.filter; renderLibrary();
});

function filteredItems() {
  const q = el.search.value.trim().toLowerCase();
  return libItems.filter(it => {
    if (q && !it.name.toLowerCase().includes(q)) return false;
    if (filter === 'manga') return it.manga;
    if (filter === 'unread') return !positions[it.file];
    if (filter === 'recent') return !!positions[it.file];
    return true;
  });
}

async function renderLibrary() {
  el.libGrid.innerHTML = '';
  const items = filteredItems();
  for (const it of items.slice(0, 200)) {
    const card = document.createElement('div');
    card.className = 'lib-item';
    card.innerHTML = `<div class="ph">📄</div><div class="t" title="${esc(it.name)}">${esc(it.name)}</div>`;
    card.onclick = () => openPath(it.file);
    el.libGrid.appendChild(card);
    neko.coverOf(it.file).then(url => {
      if (!url) return;
      const img = document.createElement('img'); img.src = url; img.alt = '';
      card.replaceChild(img, card.firstChild);
    }).catch(() => {});
  }
  // continuar lendo
  el.contRow.innerHTML = '';
  const recent = Object.entries(positions)
    .sort((a, b) => (b[1].ts || 0) - (a[1].ts || 0)).slice(0, 6);
  $('#continue-wrap').style.display = recent.length ? '' : 'none';
  for (const [file, pos] of recent) {
    const name = file.split(/[\\/]/).pop().replace(/\.[^.]+$/, '');
    const card = document.createElement('div');
    card.className = 'cont-card';
    card.innerHTML = `<img alt=""><div class="t">${esc(name)}</div>
      <div class="bar"><i style="width:${Math.round((pos.page + 1) / pos.total * 100)}%"></i></div>`;
    card.onclick = () => openPath(file);
    el.contRow.appendChild(card);
    neko.coverOf(file).then(u => { if (u) card.querySelector('img').src = u; }).catch(() => {});
  }
  // capa da tela vazia
  if (!book && S.lastFile !== undefined) { /* noop */ }
}
function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;'); }

// ---------------- opcoes ----------------
const dlg = $('#options');
function openOptions() {
  $('#opt-manga').checked = S.mangaByDefault;
  $('#opt-double').checked = S.doublePageByDefault;
  $('#opt-fit').value = S.fit;
  $('#opt-flip').checked = S.flipAnimation;
  $('#opt-prefetch').value = S.prefetch;
  $('#opt-zones').checked = S.clickZones;
  $('#opt-theme').value = S.theme;
  $('#opt-showlib').checked = S.showLibraryOnStart;
  $('#opt-side').value = S.sidebarWidth;
  $('#opt-assoc').checked = S.associateFiles;
  dlg.showModal();
}
$('#btn-opts').onclick = openOptions;
$('#opt-cancel').onclick = () => dlg.close();

dlg.querySelectorAll('.tab').forEach(t => t.onclick = () => {
  dlg.querySelectorAll('.tab').forEach(x => x.classList.remove('active'));
  t.classList.add('active');
  dlg.querySelectorAll('section[data-pane]').forEach(s => s.hidden = s.dataset.pane !== t.dataset.tab);
});

$('#options form').addEventListener('submit', async () => {
  S.mangaByDefault = $('#opt-manga').checked;
  S.doublePageByDefault = $('#opt-double').checked;
  S.fit = $('#opt-fit').value;
  S.flipAnimation = $('#opt-flip').checked;
  S.prefetch = parseInt($('#opt-prefetch').value, 10) || 3;
  S.clickZones = $('#opt-zones').checked;
  S.theme = $('#opt-theme').value;
  S.showLibraryOnStart = $('#opt-showlib').checked;
  S.sidebarWidth = parseInt($('#opt-side').value, 10) || 300;
  S.associateFiles = $('#opt-assoc').checked;
  await neko.setState({ settings: S });
  el.body.dataset.theme = S.theme;
  document.documentElement.style.setProperty('--side', S.sidebarWidth + 'px');
  fit = S.fit; syncToggles(); book && render(false);
});

// ---------------- erro ----------------
function showError(msg) { el.err.textContent = '⚠ ' + msg; el.err.hidden = false; }
function hideError() { el.err.hidden = true; }

// ---------------- salvamento com debounce / pasta solta ----------------
let saveTimer = null;
function debouncedSave() { clearTimeout(saveTimer); saveTimer = setTimeout(savePositions, 400); }

async function addFolder(p) {
  if (!libraryFolders.includes(p)) {
    libraryFolders.push(p);
    await neko.setState({ libraryFolders });
    refreshLibrary();
  }
}

// ---------------- boot ----------------
(async function boot() {
  await loadState();
  await refreshLibrary();
  if (!S.showLibraryOnStart) el.sidebar.classList.add('hidden');
})();
