/* Preload: expoe uma API minima e segura para o renderer. */
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('neko', {
  // diálogos
  openFiles: () => ipcRenderer.invoke('dialog:openFiles'),
  openFolder: () => ipcRenderer.invoke('dialog:openFolder'),

  // biblioteca
  scanLibrary: (folders) => ipcRenderer.invoke('lib:scan', folders),
  coverOf: (filePath) => ipcRenderer.invoke('lib:cover', filePath),
  metaOf: (filePath) => ipcRenderer.invoke('lib:meta', filePath),

  // livro
  openBook: (filePath) => ipcRenderer.invoke('book:open', filePath),
  pageImage: (filePath, index) => ipcRenderer.invoke('book:page', { filePath, index }),

  // janela
  winMinimize: () => ipcRenderer.send('win:minimize'),
  winMaximize: () => ipcRenderer.send('win:maximize'),
  winClose: () => ipcRenderer.send('win:close'),
  setFullScreen: (on) => ipcRenderer.send('win:fullscreen', on),

  // estado/configuracoes (persistido em %APPDATA%/NekoReader)
  getState: () => ipcRenderer.invoke('state:get'),
  setState: (patch) => ipcRenderer.invoke('state:set', patch),

  showInFolder: (p) => ipcRenderer.invoke('shell:showItem', p),
  setAssoc: (on) => ipcRenderer.invoke('assoc:set', on),

  onOpenPath: (cb) => ipcRenderer.on('open-path', (_e, p) => cb(p))
});
