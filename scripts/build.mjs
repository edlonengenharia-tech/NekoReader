// ============================================================
// build.mjs - orquestrador Node do MangaReader (zero dependências npm)
// Uso:
//   node scripts/build.mjs              -> compila (Release)
//   node scripts/build.mjs --test       -> compila + roda os testes xUnit
//   node scripts/build.mjs --run        -> abre o app com samples\sample-cbz.cbz
//   node scripts/build.mjs --dist       -> publish self-contained single-file em dist\MangaReader
//   node scripts/build.mjs --dist --zip -> tambem gera dist\MangaReader-win-x64.zip
// Se faltar o .NET SDK, rode antes: npm run setup  (baixa via dotnet-install.ps1)
// ============================================================
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync, copyFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { findDotnet } from './dotnet-lib.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, '..');
const OUT_DIR = path.join(ROOT, 'dist', 'MangaReader');
const RID = 'win-x64';

const flags = new Set(process.argv.slice(2).map((a) => a.toLowerCase()));
const doTest = flags.has('--test');
const doRun = flags.has('--run');
const doDist = flags.has('--dist');
const doZip = flags.has('--zip');

function die(msg) { console.error(`[ERRO] ${msg}`); process.exit(1); }

function run(cmd, args, opts = {}) {
  console.log(`\n> ${path.basename(cmd)} ${args.join(' ')}`);
  const r = spawnSync(cmd, args, { cwd: ROOT, stdio: 'inherit', shell: false, ...opts });
  if (r.error) die(`Falha ao executar "${cmd}": ${r.error.message}`);
  if (r.status !== 0) die(`Comando terminou com código ${r.status}.`);
}

// ---------- 1) Localiza o dotnet ----------
const dotnet = findDotnet();
if (!dotnet) {
  die('.NET SDK não encontrado.\n' +
      '  Opção A: instale em https://dotnet.microsoft.com/download\n' +
      '  Opção B: npm run setup   (baixa o SDK para .dotnet/ automaticamente)');
}
console.log(`[OK] dotnet: ${dotnet}`);

// ---------- 2) Compila a solução ----------
run(dotnet, ['build', 'MangaReader.sln', '-c', 'Release', '-v', 'minimal']);

// ---------- 3) Testes (opcional) ----------
if (doTest) {
  run(dotnet, ['test', path.join('MangaReader.Tests', 'MangaReader.Tests.csproj'),
               '-c', 'Release', '--no-build', '--nologo', '-v', 'minimal']);
}

// ---------- 4) Roda o app com amostra (opcional) ----------
if (doRun) {
  const sample = path.join(ROOT, 'samples', 'sample-cbz.cbz');
  if (!existsSync(sample)) die(`Amostra ausente: ${sample}`);
  // Windows-only: WinForms exige desktop Windows.
  if (process.platform !== 'win32') die('"--run" só funciona no Windows (WinForms).');
  run(dotnet, ['run', '--project', 'MangaReader', '-c', 'Release', '--no-build', '--', sample]);
}

// ---------- 5) Transforma em executável portátil (opcional) ----------
if (doDist) {
  if (existsSync(OUT_DIR)) rmSync(OUT_DIR, { recursive: true, force: true });
  mkdirSync(OUT_DIR, { recursive: true });

  run(dotnet, [
    'publish', path.join('MangaReader', 'MangaReader.csproj'),
    '-c', 'Release', '-r', RID, '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-o', OUT_DIR,
  ]);

  // Copia as amostras junto do exe
  const samplesDir = path.join(ROOT, 'samples');
  if (existsSync(samplesDir)) {
    const dst = path.join(OUT_DIR, 'samples');
    mkdirSync(dst, { recursive: true });
    for (const f of readdirSync(samplesDir)) copyFileSync(path.join(samplesDir, f), path.join(dst, f));
  }

  const exe = path.join(OUT_DIR, 'MangaReader.exe');
  if (existsSync(exe)) {
    const mb = (statSync(exe).size / 1024 / 1024).toFixed(1);
    console.log(`\n[OK] Executável portátil: ${exe} (${mb} MB)`);
  } else {
    console.warn('\n[AVISO] MangaReader.exe não apareceu em dist/ — confira a saída do publish acima.');
  }

  if (doZip) {
    // ZIP nativo do Windows via PowerShell (sem dependência npm)
    const zipPath = path.join(ROOT, 'dist', 'MangaReader-win-x64.zip');
    run('powershell', ['-NoProfile', '-Command',
      `Compress-Archive -Path '${OUT_DIR}${path.sep}*' -DestinationPath '${zipPath}' -Force`]);
    console.log(`[OK] Arquivo gerado: ${zipPath}`);
  }
}

console.log('\n===== Concluído sem erros =====');
