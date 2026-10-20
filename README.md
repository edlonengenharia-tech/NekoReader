# MangaReader — Leitor de Quadrinhos/Mangá para Desktop (C# / WinForms)

Leitor no estilo **CDisplayEx**, com biblioteca lateral ocultável, modo mangá
(direita→esquerda), página dupla, tela cheia, zoom/pan, persistência de posição
e suporte a **CBZ, CBR, CBT, CB7 e PDF**.

## Como compilar e rodar

Requer **.NET SDK 8+** e **Windows** (WinForms). Há três caminhos prontos:

### Opção A — Scripts .BAT na raiz (duplo-clique)

| Script | O que faz |
|---|---|
| `TESTE.cmd` | Localiza o dotnet → compila a solução → roda os testes xUnit (`MangaReader.Tests`) → abre o app com `samples\sample-cbz.cbz` (feche a janela para o script terminar). Aceita `TESTE.cmd --release`. |
| `RECOMPILAR.cmd` | Recompila e **transforma em executável portátil**: `dotnet publish -r win-x64 --self-contained /p:PublishSingleFile=true` → `dist\MangaReader\MangaReader.exe` (roda sem .NET instalado). Com `RECOMPILAR.cmd --zip` também gera `dist\MangaReader-win-x64.zip`. |
| `RECOMPILAR-NPM.cmd` | Mesmo pipeline, mas orquestrado pelo **Node.js** (zero dependências npm): `RECOMPILAR-NPM.cmd [test|dist|package|setup]`. |

### Opção B — Via Node (biblioteca já instalada no PC)

```powershell
npm run build      # compila (Release)
npm run test       # compila + testes xUnit
npm run start      # abre o app com a amostra CBZ
npm run dist       # exe portável single-file em dist\MangaReader\
npm run package    # idem + ZIP distribuível
npm run setup      # se faltar o SDK: baixa o .NET 8 p/ .dotnet\ (dotnet-install.ps1)
```

Os scripts estão em `scripts/build.mjs` (orquestrador), `scripts/dotnet-lib.mjs`
(descoberta do dotnet: `DOTNET_HOME` → `.dotnet\` local → PATH → Program Files)
e `scripts/setup-dotnet.mjs` ("outro método": instala o SDK automaticamente via
PowerShell, sem admin e sem `npm install`).

### Opção C — Manual

```powershell
cd MangaReader
dotnet restore
dotnet run            # abre a interface
dotnet run -- "D:\Comics\One Piece - 104.cbz"   # abre direto um arquivo
```

## Testes automatizados

Projeto `MangaReader.Tests` (xUnit, `dotnet test`): gera CBZs válidos em memória
(PNG 1×1 construído byte a byte) e cobre abertura/contagem de páginas, extração
de bytes, ordenação natural (02 antes de 10), parser de ComicInfo.xml, mapeamento
RTL/LTR da engine (inclusive página dupla) e round-trip do estado de leitura.
`ReadingStateService` ganhou um construtor com diretório injetável para os
testes rodarem isolados, sem tocar no `%APPDATA%` real.

## Estrutura do projeto

```
MangaReader/
├── MangaReader.csproj          # .NET 8 WinForms + pacotes NuGet
├── Program.cs                  # entry point, DPI PerMonitorV2, args de CLI
├── app.manifest                # dpi awareness p/ manifest
├── Models/
│   ├── PageInfo.cs             # enums FitMode/PageLayout + PageInfo
│   ├── BookMetadata.cs         # metadados (ComicInfo.xml compatível)
│   └── ReadingState.cs         # estado persistido por livro
├── Services/
│   ├── ArchiveBook.cs          # CBZ/CBR/CBT/CB7 via SharpCompress (streaming)
│   ├── PdfBook.cs              # PDF via Docnet.Core (MuPDF embutido)
│   ├── IBook.cs                # abstração comum + BookFactory
│   ├── MetadataService.cs      # parser de ComicInfo.xml + fallback do nome
│   ├── NaturalFileNameComparer.cs  # ordenação 001,002,...,010 correta
│   ├── PageCache.cs            # cache LRU de bitmaps decodificados
│   ├── ReaderEngine.cs         # índices exibição⇄físicos, RTL, spread, prefetch
│   ├── ReadingStateService.cs  # JSON por livro em %APPDATA%\MangaReader
│   └── LibraryService.cs       # varredura de pastas + miniaturas de capa
├── Views/
│   ├── MainForm.cs             # layout, barra inferior, atalhos, fullscreen
│   ├── PagePanel.cs            # GDI+ duplo-buffered: fit, zoom/pan, flip anim.
│   └── LibraryPanel.cs         # grade de capas ocultável (Tab)
MangaReader.Tests/
├── MangaReader.Tests.csproj    # xUnit + referência ao projeto principal
└── CoreTests.cs                # CBZ sintético, RTL, ordenação, metadados, estado
samples/
├── sample-cbz.cbz              # gibi de teste (12 páginas PNG 800×1200)
└── manga-one-piece-104.cbz     # teste de parsing "Série - Número" (10 páginas)
scripts/                        # orquestradores Node (build/test/dist/setup)
tools/
├── shellext-reg/               # .reg de associação de arquivos (sem COM)
└── shellext-src/               # esqueleto de IThumbnailProvider (miniatura real)
TESTE.cmd · RECOMPILAR.cmd · RECOMPILAR-NPM.cmd · package.json · MangaReader.sln
```

## Bibliotecas NuGet escolhidas (e por quê)

| Formato | Biblioteca | Motivo |
|---|---|---|
| CBZ / CBT / CBR / CB7 | **SharpCompress 0.38** | Uma única API lê ZIP, TAR, RAR e 7z; detecta o formato pelo header (não pela extensão); leitura em streaming sem extrair para disco. Alternativa popular: `DotNetZip`/`System.IO.Compression` só cobre ZIP. |
| PDF | **Docnet.Core.SkiaSharp 1.1** | Wrapper gerenciado do MuPDF; renderiza páginas como PNG sem depender de Adobe/poppler. Alternativas: `PdfiumViewer` (mantém apenas imagens já rasterizadas), `PDFsharp` (não renderiza conteúdo). |
| Serialização | **Newtonsoft.Json 13** | Persistência simples dos estados de leitura (`System.Text.Json` também serviria). |

## Decisões de arquitetura

- **Nada é extraído para disco.** Páginas são descomprimidas direto do stream →
  `byte[]` → `Bitmap`, e ficam num **cache LRU** (12 páginas). As vizinhas são
  pré-carregadas em background (`ReaderEngine.PrefetchAsync`).
- **Modo mangá (RTL):** os índices *físicos* (ordem do arquivo) nunca mudam;
  `ReaderEngine` traduz índice de exibição ⇄ físico:
  `físico = total − 1 − exibição` quando RTL. Em página dupla, a página de
  **maior** índice fica à **esquerda** da tela — exatamente como um mangá físico.
  Atalho `M` inverte mantendo a mesma página visível.
- **Persistência:** um JSON por livro em `%APPDATA%\MangaReader\positions\`,
  chaveado por caminho+tamanho do arquivo (mudou o arquivo, invalidou). Salva
  automaticamente a cada navegação e restaura ao abrir.
- **Metadados:** padrão da comunidade `ComicInfo.xml` dentro do arquivo
  (compatível com CDisplayEx/YACReader/Komga); fallback de parsing
  `"Série - Número.cbz"` no nome do arquivo. `<Manga>Yes</Manga>` liga o RTL
  automaticamente.
- **Tela:** painel central custom desenhado com GDI+ (`DoubleBuffered`) — mais
  rápido que PictureBox para zoom/pan; zonas de clique esquerda/direita estilo
  CDisplayEx (invertidas em RTL); roda do mouse navega; Ctrl+roda dá zoom.

## Animação de virada de página

Implementada em `PagePanel.DrawFlipEffect`: um timer de ~60fps anima um gradiente
escuro deslizante sobre a dobra da página (efeito "folha passando"). É leve e não
exige GPU. **Para um curl realista com perspectiva**, as opções recomendadas são:

1. Migrar o painel para **Direct2D** (via Vortice.Windows) e usar
   `ID2D1TransformedGeometry` com matriz de perspectiva durante a animação; ou
2. Reescrever a UI em **WPF**, onde uma animação de page-flip sai quase de graça
   com `Viewport3D`/`RotateTransform3D` sobre duas `Image`s (a técnica usada pelo
   `FlipView` do WinUI).

A estrutura atual (engine desacoplado do painel) permite trocar só a camada
`Views/` sem tocar na lógica.

## Miniaturas no Windows Explorer

Dois níveis, em `tools/`:

1. **Sem código (já pronto):** `shellext-reg/MangaReader_Associacoes.reg` associa
   `.cbz/.cbr/.cbt/.cb7` ao executável e define o ícone. Duplo-clique no Explorer
   abre o livro (o `Program.cs` aceita o caminho como argumento).
2. **Miniatura real da capa (thumbnail preview):** `shellext-src/ComicThumbnailProvider.cs`
   é um esqueleto comentado de `IThumbnailProvider` em **C#/.NET Framework 4.8**
   (shell extensions COM não rodam no .NET Core). Passos:
   - compile `MangaReader.ShellExt.csproj`;
   - assine/como GUID próprio nos atributos `[Guid]`;
   - registre: `regasm /codebase MangaReader.ShellExt.dll` (como admin);
   - reinicie o Explorer. O painel de detalhes passará a mostrar a primeira
     página do CBZ. Para CBR/CB7, inclua SharpCompress na extensão também.

## Atalhos de teclado

| Tecla | Ação | | Tecla | Ação |
|---|---|---|---|---|
| `→` `↓` Espaço PgDn | próxima página | | `M` | modo mangá (RTL) |
| `←` ↑ PgUp Backspace | anterior | | `D` | página dupla |
| Home / End | primeira/última | | `W` | ciclo de ajuste (largura/altura/página/100%) |
| `F` / F11 | tela cheia (Esc sai) | | `+` / `-` / Ctrl+roda | zoom |
| Tab | mostra/oculta biblioteca | | Ctrl+O | abrir arquivo |
| Clique esq. nas bordas | navega (zonas invertidas em RTL) | | Ctrl+L | trocar pasta da biblioteca |

## Próximos passos sugeridos

- Configuração persistente (pastas da biblioteca, atalhos remapeáveis) em
  `settings.json` ao lado das pastas de estado.
- Arrastar-e-soltar arquivos na janela (`AllowDrop`).
- Modo "webtoon": rolagem contínua vertical (o `PagePanel` já centraliza blocos;
  basta concatenar bitmaps numa tira).
- Empacotamento MSIX/single-file: `dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true`.
