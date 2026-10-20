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
let excluded = [];
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
  excluded = st.excluded || [];
  el.body.dataset.theme = S.theme;
  document.documentElement.style.setProperty('--side', S.sidebarWidth + 'px');
  manga = !!S.mangaByDefault; dbl = !!S.doublePageByDefault; fit = S.fit || 'width';
  syncToggles();
}
async function savePositions() {
  await neko.setState({ positions, libraryFolders, excluded });
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
      applyKeepAwake();
    }
  } catch (e) {
    showError(e.message || String(e));
  }
}

neko.onOpenPath(openPath);

function applyKeepAwake() { neko.setKeepAwake(!!(S.keepAwake && book)); }

function closeBook() {
  book = null; view = 0; pageCache.clear();
  el.pages.hidden = true; el.hud.hidden = true; el.bookTitle.textContent = '';
  el.empty.hidden = false; removeClickZones();
  applyKeepAwake();
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
      const slot = slots[i];
      slot.setAttribute('dir', manga ? 'rtl' : 'ltr');
      slot.classList.remove('turning'); void slot.offsetWidth; slot.classList.add('turning');
      setTimeout(() => slot.classList.remove('turning'), 360);
    } else {
      slots[i].classList.remove('turning');
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
    case 'i': case 'I': if (e.ctrlKey) { aiDrawer.hidden = !aiDrawer.hidden; e.preventDefault(); } break;
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

// alca de redimensionamento: arrastar muda --side e persiste
(function setupResizer(){
  const rz = $('#resizer');
  if (!rz) return;
  let startX = 0, startW = 0, dragging = false;
  rz.addEventListener('mousedown', (e) => {
    dragging = true; startX = e.clientX;
    startW = el.sidebar.getBoundingClientRect().width;
    document.body.classList.add('resizing'); rz.classList.add('active');
    e.preventDefault();
  });
  window.addEventListener('mousemove', (e) => {
    if (!dragging) return;
    const w = Math.min(720, Math.max(200, Math.round(startW + (e.clientX - startX))));
    document.documentElement.style.setProperty('--side', w + 'px');
  });
  window.addEventListener('mouseup', async () => {
    if (!dragging) return;
    dragging = false;
    document.body.classList.remove('resizing'); rz.classList.remove('active');
    const w = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--side'), 10) || 300;
    S.sidebarWidth = w; await neko.setState({ settings: S });
    book && render(false);
  });
})();

async function refreshLibrary() {
  const all = await neko.scanLibrary(libraryFolders);
  libItems = all.filter(x => !excluded.includes(x.file));
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

async function removeItem(it) {
  if (!confirm(`Remover "${it.name}" da biblioteca?\n(somente da lista — o arquivo NÃO é apagado do disco)`)) return;
  excluded.push(it.file);
  await neko.setState({ excluded });
  libItems = libItems.filter(x => x.file !== it.file);
  delete positions[it.file];
  await savePositions();
  renderLibrary();
}

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
  const cnt = $('#lib-count'); if (cnt) cnt.textContent = `(${items.length})`;
  for (const it of items.slice(0, 200)) {
    const card = document.createElement('div');
    card.className = 'lib-item';
    card.innerHTML = `<div class="ph">📄</div><div class="t" title="${esc(it.name)}">${esc(it.name)}</div>
      <button class="rm" title="Remover da biblioteca (apenas some da lista; o arquivo fica no disco)">✕</button>`;
    card.querySelector('.rm').onclick = (ev) => { ev.stopPropagation(); removeItem(it); };
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
  $('#opt-aiprov').value = S.aiProvider || 'qwen';
  $('#opt-aikey').value = S.aiKey || '';
  $('#opt-aibase').value = S.aiBaseUrl || '';
  $('#opt-aimodel').value = S.aiModel || '';
  wasAssoc = !!S.associateFiles;
  $('#assoc-status').textContent = S.associateFiles ? 'Associação ativa no Explorer.' : 'Associação desativada.';
  renderFoldersList();
  dlg.showModal();
}
$('#btn-opts').onclick = openOptions;
$('#opt-cancel').onclick = () => dlg.close();

// ---- opcoes: aba Windows (lista de pastas com remover) e IA ----
function renderFoldersList() {
  const box = $('#folders-list'); if (!box) return;
  box.innerHTML = '';
  if (!libraryFolders.length) { box.innerHTML = '<span class="hint">Nenhuma pasta na biblioteca ainda.</span>'; return; }
  for (const f of libraryFolders) {
    const row = document.createElement('div');
    row.className = 'folder-row';
    row.innerHTML = `<span title="${esc(f)}">📁 ${esc(f)}</span><button title="Remover da biblioteca">✕</button>`;
    row.querySelector('button').onclick = async () => {
      libraryFolders = libraryFolders.filter(x => x !== f);
      await neko.setState({ libraryFolders });
      renderFoldersList(); refreshLibrary();
    };
    box.appendChild(row);
  }
}
$('#opt-addfolder').onclick = async () => {
  const f = await neko.openFolder();
  if (f && !libraryFolders.includes(f)) {
    libraryFolders.push(f);
    await neko.setState({ libraryFolders });
    renderFoldersList(); refreshLibrary();
  }
};
$('#ai-link-dashscope').onclick = (e) => { e.preventDefault(); neko.openExternal('https://dashscope.console.aliyun.com/apiKey'); };
$('#ai-link-ocr').onclick = (e) => { e.preventDefault(); neko.openExternal('https://github.com/Jesky/DigitalOCR'); };
$('#opt-aitest').onclick = async () => {
  // salva antes para o teste usar os valores digitados
  collectOptions(); await neko.setState({ settings: S });
  const out = $('#ai-test-out');
  out.textContent = 'Testando…';
  try { out.textContent = '[OK] ' + (await neko.aiTest()); }
  catch (e) { out.textContent = '[ERRO] ' + (e.message || e); }
};

dlg.querySelectorAll('.tab').forEach(t => t.onclick = () => {
  dlg.querySelectorAll('.tab').forEach(x => x.classList.remove('active'));
  t.classList.add('active');
  dlg.querySelectorAll('section[data-pane]').forEach(s => s.hidden = s.dataset.pane !== t.dataset.tab);
});

function collectOptions() {
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
  S.aiProvider = $('#opt-aiprov').value;
  S.aiKey = $('#opt-aikey').value.trim();
  S.aiBaseUrl = $('#opt-aibase').value.trim();
  S.aiModel = $('#opt-aimodel').value.trim();
  return S;
}

$('#options form').addEventListener('submit', async (ev) => {
  ev.preventDefault();           // NAO fechar via method=dialog antes de aplicar
  collectOptions();
  await neko.setState({ settings: S });

  // ----- Interface: tema + sidebar + layout em tempo real -----
  el.body.dataset.theme = S.theme;
  document.documentElement.style.setProperty('--side', S.sidebarWidth + 'px');
  el.sidebar.classList.toggle('hidden', !S.showLibraryOnStart);

  // ----- Leitura: fit/prefetch/zonas/animação valem imediatamente -----
  manga = S.mangaByDefault || manga; dbl = S.doublePageByDefault || dbl;
  fit = S.fit; removeClickZones(); ensureClickZones();
  syncToggles(); book && render(false);

  // ----- Windows: aplicar associacao se mudou -----
  if (wasAssoc !== S.associateFiles) {
    const r = await neko.setAssoc(S.associateFiles);
    $('#assoc-status').textContent = r.msg || '';
    wasAssoc = S.associateFiles;
  }

  dlg.close();
});
let wasAssoc = false;

// ---------------- painel de IA (Qwen) ----------------
const aiDrawer = $('#ai-drawer');
$('#btn-ai').onclick = () => { aiDrawer.hidden = !aiDrawer.hidden; };
$('#ai-close').onclick = () => { aiDrawer.hidden = true; };

function currentPhysPage() { return book ? Math.min(phys(view), book.count - 1) : 0; }

function mdLite(txt) {
  // markdown minimo: **negrito**, linhas "- " viram bullets, quebras -> <br>
  let h = esc(txt);
  h = h.replace(/\*\*(.+?)\*\*/g, '<b>$1</b>');
  h = h.replace(/^#{1,3}\s*(.+)$/gm, '<h3>$1</h3>');
  h = h.replace(/^[-*]\s+(.+)$/gm, '<li>$1</li>');
  h = h.replace(/(<li>.*?<\/li>)(?!<li>)/gs, '$1');
  return h.replace(/\n/g, '<br/>');
}

async function aiRun(kind) {
  if (!book) { aiSay('Abra um PDF/quadrinho primeiro.'); return; }
  const out = $('#ai-out');
  out.innerHTML = '<p class="thinking">🤔 Consultando a IA…</p>';
  try {
    const reply = await neko.aiAsk({
      kind, file: book.file, pageIdx: currentPhysPage(),
      question: $('#ai-question').value.trim() || 'Explique o conteúdo.',
      rangePages: (kind === 'summary-doc' || kind === 'ask-doc') ? 'all' : 'page'
    });
    out.innerHTML = '<div class="md">' + mdLite(reply) + '</div>';
  } catch (e) {
    out.innerHTML = '<p style="color:var(--danger)">⚠ ' + esc(e.message || String(e)) + '</p>';
  }
}
function aiSay(msg) { $('#ai-out').innerHTML = '<p class="hint">' + esc(msg) + '</p>'; }

$('#ai-sum-page').onclick = () => aiRun('summary');
$('#ai-sum-doc').onclick = () => aiRun('summary-doc');
$('#ai-send').onclick = () => aiRun('ask');
$('#ai-question').addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && e.ctrlKey) aiRun('ask');
  e.stopPropagation(); // nao navegar com setas enquanto digita
});
$('#ai-ocr-copy').onclick = async () => {
  if (!book) return aiSay('Nenhum livro aberto.');
  const txt = await neko.pageText(book.file, currentPhysPage());
  if (txt) {
    await navigator.clipboard.writeText(txt);
    aiSay('Texto da página copiado! Cole em qualquer lugar — ou pergunte à IA sobre ele aqui em cima.');
  } else {
    aiSay('Esta página é imagem pura (PDF escaneado). Rode OCR antes: PowerToys Text Extractor (Win+Shift+T) ou DigitalOCR, depois cole o texto na pergunta acima.');
  }
};

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
