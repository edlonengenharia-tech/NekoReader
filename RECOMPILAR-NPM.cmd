@echo off
rem ============================================================
rem  RECOMPILAR-NPM.cmd - mesma coisa, mas passando pelo Node
rem  (orquestrador em scripts\build.mjs). Requer Node no PATH.
rem  Uso:
rem    RECOMPILAR-NPM.cmd            -> build + testes
rem    RECOMPILAR-NPM.cmd dist       -> gera exe portavel (single-file)
rem    RECOMPILAR-NPM.cmd package    -> exe portavel + ZIP distribuivel
rem    RECOMPILAR-NPM.cmd setup      -> baixa o .NET SDK p/ .dotnet\ se faltar
rem ============================================================
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

where node >nul 2>&1
if errorlevel 1 (
    echo [ERRO] Node nao encontrado no PATH. Instale em https://nodejs.org
    echo        Dica: sem Node tambem funciona - use RECOMPILAR.cmd
    pause & exit /b 1
)
for /f "delims=" %%V in ('node --version') do echo [OK] node %%V

set "TASK=%~1"
if "%TASK%"=="" set "TASK=test"

echo --- npm run %TASK% ---
call npm run %TASK%
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
    echo [ERRO] npm run %TASK% falhou (codigo %RC%).
    pause & exit /b %RC%
)
echo [OK] Concluido com sucesso.
pause
endlocal
