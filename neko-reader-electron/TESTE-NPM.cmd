@echo off
setlocal
title NekoReader - teste local (Electron, sem .NET)
cd /d "%~dp0"

echo ============================================================
echo  NekoReader - TESTE  (npm install + start + smoke test)
echo ============================================================

where node >nul 2>nul || (echo [ERRO] Node.js nao encontrado no PATH. Instale em https://nodejs.org & pause & exit /b 1)
for /f "delims=" %%v in ('node --version') do echo [OK] node %%v

rem npm install roda SEMPRE (idempotente e rapido quando ja instalado).
rem Assim, se o electron nao estiver em node_modules (ex.: copia do projeto
rem sem node_modules, ou instalacao parcial), ele e baixado aqui.
echo.
echo --- Dependencias npm (npm install) ---
call npm install --no-audit --no-fund
if errorlevel 1 ( echo [ERRO] npm install falhou. & pause & exit /b 1 )

echo.
echo --- Smoke test: abre um CBZ de exemplo e confere paginas (sem janela) ---
node scripts\smoke.mjs
if errorlevel 1 ( echo [ERRO] smoke test falhou. & pause & exit /b 1 )
echo [OK] descompactacao OK

echo.
echo --- Iniciando o app Electron (feche a janela para voltar) ---
rem NAO use "npm start" se der 'electron' nao reconhecido: disparamos o
rem binario local direto, que funciona mesmo com PATH quebrado.
if exist node_modules\.bin\electron.cmd (
  call node_modules\.bin\electron.cmd .
) else (
  echo [ERRO] electron nao encontrado em node_modules\.bin - rode "npm install" nesta pasta.
  pause & exit /b 1
)

echo.
echo Fim do teste.
pause
