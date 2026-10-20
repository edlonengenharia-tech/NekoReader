# NekoReader (Electron) - leitor de quadrinhos/manga PORTAVEL sem .NET

Versao 100% Node.js + Electron do leitor, criada porque o build WinForms estava
travando no toolchain .NET do PC. **Nao usa dotnet em nenhum momento.**

## Requisitos
- Node.js (o seu v24 ja serve) - https://nodejs.org

## Como usar (Windows)
```bat
cd neko-reader-electron
TESTE-NPM.cmd        :: npm install + smoke test + abre o app
BUILD-PORTABLE.cmd   :: gera dist\NekoReader-portable.exe (um arquivo so, portavel)
```
Ou manualmente:
```bat
npm install
npm start            :: roda o app
npm run dist         :: EXE portavel (electron-builder, target "portable")
npm run dist:installer  :: instalador NSIS com associacao CBZ/CBR/CBT/CB7/PDF
```

## Formatos suportados
| Formato | Como le (sem .NET) |
|---|---|
| .cbz/.zip | adm-zip (JS puro) |
| .cbt/.tar(.gz) | pacote `tar` (JS puro) |
| .cb7/.7z | bindings nativos do `sevenzip`; fallback automatico para 7z.exe |
| .cbr/.rar | sevenzip -> UnRAR.exe/7z.exe -> unrar.js (vendor opcional). **Nada disso da NullReferenceException**: erros viram banner amigavel na UI |
| .pdf | pdfjs-dist (Mozilla); render precisa de `canvas` (opcional). Sem canvas mostra aviso, nunca fecha o app |

## Recursos
Manga RTL (M), pagina dupla (D), ajuste largura/altura/pagina (W), zoom (+/- e Ctrl+roda),
tela cheia (F/Esc), zonas de clique esq/dir invertidas no RTL, animacao de virada,
HUD com n/total + barra de progresso, biblioteca lateral ocultavel (Tab) com busca/filtros/
"continuar lendo", capas geradas em background, ComicInfo.xml, persistencia de posicao e
opcoes em %APPDATA%\NekoReader\state.json, janela frameless custom, temas escuro/meia-noite/claro,
arrastar-e-soltar arquivo E pasta, abrir por linha de comando/duplo clique (associacao no NSIS).

## Estrutura
```
src/main.js       processo principal (janela, IPC, CLI args, single-instance)
src/preload.js    ponte segura contextBridge
src/archives.js   descompactadores (ZIP/TAR/7Z/RAR com fallbacks)
src/pdf.js        leitura PDF (pdfjs-dist)
src/library.js    varre pastas, capas, ComicInfo.xml
src/state.js      settings + posicao de leitura (%APPDATA%/NekoReader)
src/index.html    UI (barra titulo, sidebar, leitor, opcoes)
src/style.css     temas/modernice
src/renderer.js   navegacao, RTL, dupla, zoom, HUD, drag&drop
scripts/smoke.mjs teste headless de descompactacao
samples/exemplo.cbz  livro de exemplo (gere de novo: node scripts/make-sample.mjs)
vendor/unrar.js   placeholder opcional p/ RAR via WASM
```

## Dicas
- O EXE portavel embute o runtime Node/Eletron: leve só o .exe no pendrive.
- Para RAR5 raro/corrompido sem WinRAR nem 7-Zip no PC, converta para CBZ (o app avisa isso).
- O projeto C#/WinForms antigo continua em ../MangaReader caso queira voltar a ele.
