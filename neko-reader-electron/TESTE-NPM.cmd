@echo off
setlocal
title NekoReader - teste local + EXE portatil (Electron, sem .NET)
cd /d "%~dp0"

echo ============================================================
echo  NekoReader - TESTE  (npm install + smoke + app + EXE)
echo ============================================================

where node >nul 2>nul || (echo [ERRO] Node.js nao encontrado no PATH. Instale em https://nodejs.org & pause & exit /b 1)
for /f "delims=" %%v in ('node --version') do echo [OK] node %%v

rem npm install roda SEMPRE (idempotente e rapido quando ja instalado).
echo.
echo --- Dependencias npm (npm install) ---
call npm install --no-audit --no-fund
if errorlevel 1 ( echo [ERRO] npm install falhou. & pause & exit /b 1 )

echo.
echo --- Smoke test: CBZ de exemplo + texto de PDF (sem janela) ---
node scripts\smoke.mjs
if errorlevel 1 ( echo [ERRO] smoke test falhou. & pause & exit /b 1 )

echo.
echo --- Iniciando o app Electron (feche a janela para continuar) ---
if exist node_modules\.bin\electron.cmd (
  call node_modules\.bin\electron.cmd .
) else (
  echo [ERRO] electron nao em node_modules\.bin - rode "npm install" nesta pasta.
  pause & exit /b 1
)

echo.
echo --- Gerando o EXE portatil (dist\NekoReader-portable.exe) ---
echo     (primeira vez baixa binarios do Electron/NSIS ~100MB; pode demorar)
if exist node_modules\.bin\electron-builder.cmd (
  call node_modules\.bin\electron-builder.cmd --win portable
) else (
  call npm run dist
)
if errorlevel 1 (
  echo.
  echo [ERRO] electron-builder falhou. Causas comuns:
  echo   - Antivirus bloqueando: adicione excecao para a pasta do projeto.
  echo   - Sem internet: o download do NSIS/electron falha na primeira vez.
  echo   - Arquivo em uso: feche o NekoReader-portable.exe se estiver aberto.
  pause & exit /b 1
)

echo.
echo ============================================================
echo  PRONTO!
echo   App de teste:  node_modules\.bin\electron.cmd .   (ou TESTE-NPM)
echo   EXE portavel:  dist\NekoReader-portable.exe
echo  Copie o EXE para qualquer PC Windows x64 e execute: nada a instalar.
echo ============================================================
pause
