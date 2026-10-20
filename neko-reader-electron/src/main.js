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

ipcMain.handle('book:open', async (_e, filePath) => {
  const info = await archives.listPages(filePath).catch(async (err) => {
    if (/\.pdf$/i.test(filePath)) return { kind: 'pdf', pages: await pdfjs.pageCount(filePath) };
    throw err;
  });
  return info; // { kind, pageNames:[], pageCount }
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
