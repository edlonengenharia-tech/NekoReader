@echo off
setlocal
title NekoReader - build portable (Electron + electron-builder)
cd /d "%~dp0"

echo ============================================================
echo  NekoReader - RECOMPILAR PORTAVEL (.exe unico, sem .NET)
echo ============================================================

where node >nul 2>nul || (echo [ERRO] Node.js nao encontrado. Instale em https://nodejs.org & pause & exit /b 1)

if not exist node_modules (
  echo --- Instalando dependencias ---
  call npm install --no-audit --no-fund || (echo [ERRO] npm install falhou. & pause & exit /b 1)
)

echo.
echo --- Smoke test antes de empacotar ---
node scripts\smoke.mjs || (echo [ERRO] smoke test falhou, abortando build. & pause & exit /b 1)

echo.
echo --- Gerando EXE portavel (baixa binarios Electron/NSIS na primeira vez) ---
call npm run dist
if errorlevel 1 ( echo [ERRO] electron-builder falhou. Veja acima. & pause & exit /b 1 )

echo.
echo ============================================================
echo  PRONTO! Executavel portavel em:  dist\NekoReader-portable.exe
echo  Copie esse .exe para qualquer lugar e execute (nao precisa
echo  instalar nada). Para instalador NSIS: npm run dist:installer
echo ============================================================
pause
