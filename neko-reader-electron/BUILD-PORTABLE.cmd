@echo off
setlocal
title NekoReader - build portable (Electron + electron-builder)
cd /d "%~dp0"

echo ============================================================
echo  NekoReader - RECOMPILAR PORTAVEL (.exe unico, sem .NET)
echo ============================================================

where node >nul 2>nul || (echo [ERRO] Node.js nao encontrado. Instale em https://nodejs.org & pause & exit /b 1)

echo --- Dependencias npm (npm install) ---
call npm install --no-audit --no-fund
if errorlevel 1 ( echo [ERRO] npm install falhou. & pause & exit /b 1 )

echo.
echo --- Smoke test antes de empacotar ---
node scripts\smoke.mjs
if errorlevel 1 ( echo [ERRO] smoke test falhou, abortando build. & pause & exit /b 1 )

echo.
echo --- Limpando saida antiga ---
if exist dist rd /s /q dist

echo.
echo --- Gerando EXE portavel (log completo em build.log) ---
if not exist node_modules\.bin\electron-builder.cmd (
  echo [ERRO] electron-builder nao instalado. Rode: npm install
  pause & exit /b 1
)
call node_modules\.bin\electron-builder.cmd --win portable --x64 > build.log 2>&1
set RC=%ERRORLEVEL%
type build.log
if not %RC%==0 (
  echo.
  echo [ERRO] electron-builder falhou (codigo %RC%). Causas comuns:
  echo   - Antivirus bloqueia winCodeSign/nsis: adicione excecao ou desative temporariamente.
  echo   - Sem conexao: ele baixa nsis/electron na primeira execucao.
  echo   - dist\NekoReader-portable.exe em uso: feche-o e tente de novo.
  echo Log salvo em build.log - envie esse arquivo se precisar de ajuda.
  pause & exit /b 1
)

echo.
echo ============================================================
echo  PRONTO! Executavel portavel em:  dist\NekoReader-portable.exe
echo  Copie esse .exe para qualquer lugar e execute (nao precisa
echo  instalar nada). Para instalador NSIS: npm run dist:installer
echo ============================================================
pause
