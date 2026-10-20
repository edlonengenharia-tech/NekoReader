/*
 * NekoReader - processo principal (Electron)
 * Sem .NET: tudo Node.js. Janela frameless + IPC para abrir livros, biblioteca e configurações.
 */
const { app, BrowserWindow, ipcMain, dialog, shell, globalShortcut } = require('electron');
const path = require('path');
const fs = require('fs');

const archives = require('./archives.js');   // CBZ/CBT/CB7/ZIP/RAR -> buffers de imagem
const pdfjs = require('./pdf.js');           // PDF -> data-url por pagina
const lib = require('./library.js');         // varre pastas, gera capas, ComicInfo.xml
const state = require('./state.js');         // persistencia em %APPDATA%/NekoReader

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

ipcMain.handle('book:page', async (_e, { filePath, index }) => {
  if (/\.pdf$/i.test(filePath)) return pdfjs.renderPageDataUrl(filePath, index);
  return archives.readPageDataUrl(filePath, index);
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

ipcMain.handle('shell:showItem', (_e, p) => shell.showItemInFolder(p));
