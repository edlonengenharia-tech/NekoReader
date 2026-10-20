/*
 * NekoReader - processo principal (Electron)
 * Sem .NET: tudo Node.js. Janela frameless + IPC para abrir livros, biblioteca e configurações.
 */
const { app, BrowserWindow, ipcMain, dialog, shell, globalShortcut, powerSaveBlocker } = require('electron');
const path = require('path');
const fs = require('fs');

const archives = require('./archives.js');   // CBZ/CBT/CB7/ZIP/RAR -> buffers de imagem
const pdfjs = require('./pdf.js');           // PDF -> data-url por pagina
const lib = require('./library.js');         // varre pastas, gera capas, ComicInfo.xml
const state = require('./state.js');         // persistencia em %APPDATA%/NekoReader
const ai = require('./ai.js');               // Qwen / OpenAI-compativel (resumo e perguntas)

let win = null;

function createWindow() {
  win = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 800,
    minHeight: 560,
    frame: false,                 // barra custom desenhada no HTML
    backgroundColor: '#14161b',
    icon: path.join(__dirname, '..', 'assets', 'icon.png'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      spellcheck: false
    }
  });

  win.loadFile(path.join(__dirname, '..', 'src', 'index.html'));
  if (process.argv.includes('--dev')) win.webContents.openDevTools({ mode: 'detach' });

  win.on('closed', () => { win = null; });
}

// ---- arquivo passado por linha de comando / duplo clique / "Abrir com" ----
function initialFile() {
  const args = process.argv.slice(app.isPackaged ? 1 : 2);
  for (const a of args) {
    if (a && !a.startsWith('-')) {
      try { if (fs.existsSync(a)) return a; } catch (_) {}
    }
  }
  return null;
}

app.whenReady().then(() => {
  // registro de associacoes no Windows (HKCU, sem admin) quando empacotado
  if (process.platform === 'win32' && app.isPackaged) {
    try {
      for (const ext of ['.cbz', '.cbr', '.cbt', '.cb7', '.zip', '.rar', '.pdf']) {
        app.setAsDefaultProtocolClient('nekoreader-' + ext.slice(1), process.execPath, [ '--file', '%1' ]);
      }
    } catch (_) { /* melhor esforco */ }
  }

  createWindow();

  const first = initialFile();
  if (first) {
    win.webContents.once('did-finish-load', () => {
      win.webContents.send('open-path', first);
    });
  }

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });

  // segunda instancia: repassa o arquivo para a janela ja aberta
  app.on('second-instance', (_e, argv) => {
    if (!win) return;
    if (win.isMinimized()) win.restore();
    win.focus();
    const idx = argv.indexOf('--file');
    const f = idx >= 0 ? argv[idx + 1] : argv[argv.length - 1];
    if (f && fs.existsSync(f)) win.webContents.send('open-path', f);
  });
});

if (!app.requestSingleInstanceLock()) {
  app.quit();
}

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// ------------------------------ IPC ------------------------------

ipcMain.handle('dialog:openFiles', async () => {
  const r = await dialog.showOpenDialog(win, {
    title: 'Abrir quadrinho/manga',
    filters: [
      { name: 'Quadrinhos', extensions: ['cbz', 'cbr', 'cbt', 'cb7', 'zip', 'rar', '7z', 'tar', 'pdf'] },
      { name: 'Todos', extensions: ['*'] }
    ],
    properties: ['openFile', 'multiSelections']
  });
  return r.canceled ? [] : r.filePaths;
});

ipcMain.handle('dialog:openFolder', async () => {
  const r = await dialog.showOpenDialog(win, { title: 'Pasta da biblioteca', properties: ['openDirectory'] });
  return r.canceled ? null : r.filePaths[0];
});

ipcMain.handle('lib:scan', async (_e, folders) => lib.scan(folders));
ipcMain.handle('lib:cover', async (_e, filePath) => lib.coverDataUrl(filePath));
ipcMain.handle('lib:meta', async (_e, filePath) => lib.readComicInfo(filePath));

// associacao de arquivos no Windows (HKCU\...\OpenWithProgids - sem admin)
const ASSOC_EXTS = ['.cbz', '.cbr', '.cbt', '.cb7', '.pdf'];
function runReg(argsList) {
  const { spawnSync } = require('child_process');
  for (const a of argsList) {
    const r = spawnSync('reg', a, { windowsHide: true });
    if (r.error || r.status !== 0) return false;
  }
  return true;
}
function setAssociations(on) {
  if (process.platform !== 'win32') return { ok: false, msg: 'Disponivel apenas no Windows.' };
  try {
    const exe = process.execPath.replace(/"/g, '');
    if (on) {
      const ok = runReg([
        ['add', 'HKCU\\Software\\Classes\\NekoReader.File', '/ve', '/d', 'NekoReader Comic Book', '/f'],
        ['add', 'HKCU\\Software\\Classes\\NekoReader.File\\DefaultIcon', '/ve', '/d', `"${exe}",0`, '/f'],
        ['add', 'HKCU\\Software\\Classes\\NekoReader.File\\shell\\open\\command', '/ve', '/d', `"${exe}" "%1"`, '/f'],
      ]);
      if (!ok) return { ok: false, msg: 'Falha ao registrar o tipo NekoReader.File (comando reg.exe).' };
      for (const ext of ASSOC_EXTS) {
        runReg(['add', `HKCU\\Software\\Classes\\${ext}\\OpenWithProgids`, 'NekoReader.File', '/t', 'REG_SZ', '/d', '', '/f']);
      }
      // SHChangeNotify via PowerShell -> Explorer atualiza sem logout
      const { execSync } = require('child_process');
      try {
        execSync('powershell -NoProfile -Command "if (-not ([System.Management.Automation.PSTypeName]\'SHN\').Type) { Add-Type -Namespace N -Name SHN -MemberDefinition \'[System.Runtime.InteropServices.DllImport(\\"shell32\\")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);\'} ; [N.SHN]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)"', { windowsHide: true, stdio: 'ignore' });
      } catch (_) { /* melhor esforco */ }
      return { ok: true, msg: 'Associação ativada: duplo clique em CBZ/CBR/CBT/CB7/PDF abre no NekoReader.' };
    } else {
      runReg(['delete', 'HKCU\\Software\\Classes\\NekoReader.File', '/f']);
      for (const ext of ASSOC_EXTS) {
        runReg(['delete', `HKCU\\Software\\Classes\\${ext}\\OpenWithProgids`, 'v', 'NekoReader.File', '/f']);
      }
      return { ok: true, msg: 'Associação removida.' };
    }
  } catch (e) {
    return { ok: false, msg: 'Erro: ' + (e.message || e) };
  }
}
ipcMain.handle('assoc:set', (_e, on) => setAssociations(!!on));

ipcMain.handle('book:open', async (_e, filePath) => {
  // PDF primeiro: pageCount nao deve ser "engolido" pelo parser de arquivos compactados
  if (/\.pdf$/i.test(filePath)) {
    const info = await pdfjs.pageCount(filePath);   // excecoes viram mensagem amigavel no renderer
    return { kind: 'pdf', pages: [], pageCount: info };
  }
  return archives.listPages(filePath); // { kind, pageNames:[], pageCount }
});

// Para PDF o RENDER acontece no renderer (pdfjs + <canvas> do Chromium, estilo
// pdfcraft): devolve os bytes + URLs locais dos assets do pdfjs para o worker/
// viewer rodarem dentro da janela. Quadrinhos seguem por data-url normal.
ipcMain.handle('book:page', async (_e, { filePath, index }) => {
  if (/\.pdf$/i.test(filePath)) {
    let base;
    try { base = path.dirname(require.resolve('pdfjs-dist/package.json')); }
    catch (_) { base = path.join(__dirname, '..', 'node_modules', 'pdfjs-dist'); }
    const toUrl = (p) => 'file:///' + p.split(path.sep).join('/').replace(/^\/+/, '');
    return {
      kind: 'pdf',
      bytes: Array.from(pdfjs.readPdfBytes(filePath)),
      viewer: {
        workerSrc: toUrl(path.join(base, 'legacy', 'build', 'pdf.worker.js')),
        standardFontBase: (() => {
          const cand = [path.join(process.resourcesPath || '', 'standard_fonts'), path.join(base, 'standard_fonts')];
          for (const c of cand) { try { if (c && fs.existsSync(c)) return toUrl(c); } catch (_) {} }
          return null;
        })()
      },
      pageIndex: index
    };
  }
  return { kind: 'image', dataUrl: archives.readPageDataUrl(filePath, index) };
});

// texto da pagina (para o painel de IA); '' = paginas de imagem (escaneadas)
ipcMain.handle('book:text', async (_e, { filePath, index }) => {
  if (/\.pdf$/i.test(filePath)) {
    try { return await pdfjs.pageText(filePath, index); } catch (_) { return ''; }
  }
  return ''; // quadrinhos sao imagens; OCR e feito por ferramentas externas
});

// ---------------- IA (Qwen / compativel OpenAI) ----------------
ipcMain.handle('ai:ask', async (_e, { kind, file, pageIdx, question, rangePages }) => {
  const st = state.load();
  // sem chave/URL configurada -> erro amigavel em vez de stack no console
  const prov = st.settings.aiProvider || 'qwen';
  if (prov === 'qwen' && !(st.settings.aiKey || '').trim()) {
    throw new Error('Chave do Qwen nao configurada. Abra Opções (Ctrl+,) → aba IA e cole a chave. Para uso local, escolha "Compatível OpenAI" + URL do Ollama.');
  }
  const name = require('path').basename(file);
  let pages;
  if (/\.pdf$/i.test(file)) {
    const from = Math.max(0, pageIdx - (rangePages === 'all' ? 0 : 1));
    const to = rangePages === 'all'
      ? ((await pdfjs.getDoc(file)).numPages - 1)
      : pageIdx + (rangePages === 'page' ? 0 : 2);
    pages = await ai.extractText(file, from, to);
  } else {
    pages = [{ page: pageIdx + 1, text: '' }]; // CBZ/CBR: sem camada de texto
  }
  const messages = ai.buildMessages(kind, file, name, pages, { question });
  return ai.chat(st.settings, messages);
});

ipcMain.handle('ai:test', async () => {
  const st = state.load();
  const prov = st.settings.aiProvider || 'qwen';
  if (prov === 'qwen' && !(st.settings.aiKey || '').trim()) {
    throw new Error('Chave do Qwen nao configurada. Cole a chave no campo acima e salve antes de testar.');
  }
  return ai.chat(st.settings, [
    { role: 'system', content: 'Responda apenas com a palavra: OK' },
    { role: 'user', content: 'Teste de conexao.' }
  ]);
});

// janela frameless
ipcMain.on('win:minimize', () => win && win.minimize());
ipcMain.on('win:maximize', () => {
  if (!win) return;
  win.isMaximized() ? win.unmaximize() : win.maximize();
});
ipcMain.on('win:close', () => win && win.close());
ipcMain.on('win:fullscreen', (_e, on) => win && win.setFullScreen(!!on));

ipcMain.handle('state:get', () => state.load());
ipcMain.handle('state:set', (_e, patch) => state.save(patch));

// manter a tela acordada durante a leitura (powerSaveBlocker do Electron)
let psbId = null;
ipcMain.handle('power:awake', (_e, on) => {
  try {
    if (on && psbId === null) psbId = powerSaveBlocker.start('prevent-display-sleep');
    if (!on && psbId !== null) { powerSaveBlocker.stop(psbId); psbId = null; }
    return true;
  } catch (_) { return false; }
});

ipcMain.handle('shell:showItem', (_e, p) => shell.showItemInFolder(p));
ipcMain.handle('shell:openExternal', (_e, u) => shell.openExternal(u));
