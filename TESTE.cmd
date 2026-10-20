@echo off
rem ============================================================
rem  TESTE.cmd - compila, roda os testes xUnit e abre o app
rem  com um CBZ de exemplo (samples\sample-cbz.cbz).
rem  Uso: duplo-clique ou "TESTE.cmd" no terminal.
rem  Variaveis opcionais:
rem    DOTNET_HOME=C:\caminho\para\dotnet.exe  (forca um SDK especifico)
rem    MANGAREADER_NODE_DIR=D:\no-de-node      (procura node.exe aqui)
rem ============================================================
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

set "CFG=Debug"
if /i "%~1"=="--release" set "CFG=Release"

echo ============================================================
echo  MangaReader - teste local  ^(config: %CFG%^)
echo ============================================================

rem ---------- [1/5] Localiza o dotnet ----------
set "DOTNET="
if defined DOTNET_HOME if exist "%DOTNET_HOME%" set "DOTNET=%DOTNET_HOME%"
if not defined DOTNET for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET set "DOTNET=%%D"
if not defined DOTNET for %%P in ("%ProgramFiles%\dotnet\dotnet.exe" "%ProgramFiles(x86)%\dotnet\dotnet.exe") do if not defined DOTNET if exist %%P set "DOTNET=%%~P"
if defined DOTNET goto dotnet_ok
echo [ERRO] .NET SDK nao encontrado no PATH.
echo        Instale o SDK 8 em https://dotnet.microsoft.com/download
echo        ou defina: set DOTNET_HOME=C:\caminho\para\dotnet.exe
pause & exit /b 1
:dotnet_ok
echo [OK] dotnet: %DOTNET%

rem ---------- [2/5] Node (informativo) ----------
set "NODE="
for /f "delims=" %%N in ('where node 2^>nul') do if not defined NODE set "NODE=%%N"
if defined MANGAREADER_NODE_DIR if exist "%MANGAREADER_NODE_DIR%\node.exe" set "NODE=%MANGAREADER_NODE_DIR%\node.exe"
if defined NODE goto node_ok
echo [INFO] Node nao detectado - use RECOMPILAR.cmd ^(caminho direto com dotnet^).
goto node_done
:node_ok
for /f "delims=" %%V in ('"%NODE%" --version') do echo [OK] node %%V detectado - build/portavel via npm: RECOMPILAR-NPM.cmd
:node_done

rem ---------- [3/5] Compila ----------
echo.
echo --- Compilando a solucao (%CFG%) ---
"%DOTNET%" build MangaReader.sln -c %CFG% -v minimal
if not errorlevel 1 goto build_ok
echo [ERRO] Falha na compilacao. Corrija os erros acima e rode de novo.
pause & exit /b 1
:build_ok

rem ---------- [4/5] Testes unitarios ----------
echo.
echo --- Executando testes (xUnit) ---
"%DOTNET%" test MangaReader.Tests\MangaReader.Tests.csproj -c %CFG% --nologo -v minimal
if not errorlevel 1 goto test_ok
echo [ERRO] Testes falharam.
pause & exit /b 1
:test_ok

rem ---------- [5/5] Smoke test: abre o app com as amostras ----------
echo.
echo --- Verificando engine de PDF (pdfium.dll) ---
if exist "MangaReader\bin\%CFG%\net8.0-windows\pdfium.dll" (
    echo     [OK] pdfium.dll presente - PDFs serao abertos.
) else (
    echo     [AVISO] pdfium.dll ausente no build - abra apenas CBZ/CBR neste teste.
)
set "ARG1=%CD%\samples\sample-cbz.cbz"
set "ARG2="
if exist "%CD%\samples\exemplo-teste.pdf" set "ARG2=%CD%\samples\exemplo-teste.pdf"
echo --- Abrindo MangaReader com: sample-cbz.cbz exemplo-teste.pdf ---
echo     (feche a janela do app para o script continuar)
set "EXE=MangaReader\bin\%CFG%\net8.0-windows\MangaReader.exe"
if not exist "%EXE%" set "EXE="
for /f "delims=" %%F in ('dir /s /b /a-d MangaReader\bin\%CFG%\*MangaReader.exe 2^>nul') do if not defined EXE if /i not "%%~nxF"=="MangaReader.dll" set "EXE=%%F"
if defined EXE goto launch_exe
call "%DOTNET%" run --project MangaReader -c %CFG% --no-build -- "%ARG1%" "%ARG2%"
goto run_checked
:launch_exe
echo     exe: %EXE%
call "%EXE%" "%ARG1%" "%ARG2%"
:run_checked
if not errorlevel 1 goto run_ok
echo [ERRO] O aplicativo terminou com codigo de erro.
pause & exit /b 1
:run_ok

echo.
echo ============================================================
echo  TUDO OK: compilacao, testes e execucao passaram.
echo ============================================================
pause
endlocal
