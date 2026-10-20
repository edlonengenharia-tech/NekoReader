@echo off
setlocal
title NekoReader - teste local (Electron, sem .NET)
cd /d "%~dp0"

echo ============================================================
echo  NekoReader - TESTE  (npm install + start + smoke test)
echo ============================================================

where node >nul 2>nul || (echo [ERRO] Node.js nao encontrado no PATH. Instale em https://nodejs.org & pause & exit /b 1)
for /f "delims=" %%v in ('node --version') do echo [OK] node %%v

if not exist node_modules (
  echo.
  echo --- Instalando dependencias npm ^(primeira vez demora um pouco^) ---
  call npm install --no-audit --no-fund
  if errorlevel 1 ( echo [ERRO] npm install falhou. & pause & exit /b 1 )
)

echo.
echo --- Smoke test: abre um CBZ de exemplo e confere paginas (sem janela) ---
node scripts\smoke.mjs
if errorlevel 1 ( echo [ERRO] smoke test falhou. & pause & exit /b 1 )
echo [OK] descompactacao OK

echo.
echo --- Iniciando o app Electron (feche a janela para voltar) ---
call npm start

echo.
echo Fim do teste.
pause
