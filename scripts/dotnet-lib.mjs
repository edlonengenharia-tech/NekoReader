// dotnet-lib.mjs - helpers compartilhados (usados por build.mjs e setup-dotnet.mjs)
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** Retorna o primeiro dotnet.exe utilizável ou null. */
export function findDotnet() {
  const candidates = [];
  if (process.env.DOTNET_HOME && existsSync(process.env.DOTNET_HOME)) candidates.push(process.env.DOTNET_HOME);
  // SDK local instalado por "npm run setup"
  candidates.push(path.join(ROOT, '.dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'));
  // PATH
  const finder = process.platform === 'win32' ? 'where' : 'which';
  const r = spawnSync(finder, ['dotnet'], { encoding: 'utf8', shell: false });
  if (r.status === 0) for (const line of r.stdout.split(/\r?\n/)) if (line.trim()) candidates.push(line.trim());
  // Locais padrão do Windows
  if (process.platform === 'win32') {
    for (const base of [process.env['ProgramFiles'], process.env['ProgramFiles(x86)']])
      if (base) candidates.push(path.join(base, 'dotnet', 'dotnet.exe'));
  }
  for (const c of candidates) {
    if (!existsSync(c)) continue;
    const probe = spawnSync(c, ['--version'], { encoding: 'utf8', shell: false });
    if (probe.status === 0 && /\d+\./.test(probe.stdout || '')) return c; // SDK/runtime de verdade
  }
  return null;
}
