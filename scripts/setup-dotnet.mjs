// ============================================================
// setup-dotnet.mjs - baixa o .NET SDK 8 para .\.dotnet\ usando o
// script oficial dotnet-install.ps1 (PowerShell embutido no Windows).
// Não precisa de npm install nem de admin. Rode:  npm run setup
// ============================================================
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { findDotnet } from './dotnet-lib.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

if (findDotnet()) {
  console.log('[OK] .NET já disponível — nada a fazer.');
  process.exit(0);
}
if (process.platform !== 'win32') {
  console.error('[ERRO] Este setup baixa o SDK para Windows. Em outro SO, use o instalador oficial.');
  process.exit(1);
}

const target = path.join(ROOT, '.dotnet');
const ps = `
  $ErrorActionPreference = 'Stop';
  if (-not (Test-Path '${target}')) { New-Item -ItemType Directory -Path '${target}' | Out-Null };
  $installer = Join-Path '${target}' 'dotnet-install.ps1';
  if (-not (Test-Path $installer)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12;
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer;
  };
  & $installer -Channel 8.0 -InstallDir '${target}' -Runtime dotnet;
  & $installer -Channel 8.0 -InstallDir '${target}';
`;

console.log('[..] Baixando .NET SDK 8 para', target, '(~230MB, aguarde)...');
const r = spawnSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps],
  { stdio: 'inherit', shell: false });
if (r.status !== 0) { console.error('[ERRO] Falha no download do SDK.'); process.exit(1); }

if (findDotnet()) console.log('[OK] SDK instalado em .\\dotnet — os próximos builds usarão ele automaticamente.');
else { console.error('[ERRO] SDK baixado mas não localizado. Verifique .dotnet\\.'); process.exit(1); }
