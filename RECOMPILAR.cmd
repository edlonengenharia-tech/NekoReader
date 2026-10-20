@echo off
rem ============================================================
rem  RECOMPILAR.cmd - recompila e TRANSFORMA o app em executavel
rem  autonomo (self-contained): dist\MangaReader\MangaReader.exe
rem  Nao exige .NET instalado na maquina de destino.
rem  Se houver Node no PC, use tambem: RECOMPILAR-NPM.cmd
rem  Uso: duplo-clique ou "RECOMPILAR.cmd [--zip]"
rem    --zip  alem da pasta, gera dist\MangaReader-win-x64.zip
rem ============================================================
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

set "RID=win-x64"
set "OUT=dist\MangaReader"
set "MAKEZIP="
if /i "%~1"=="--zip" set "MAKEZIP=1"

echo ============================================================
echo  MangaReader - build portavel (self-contained, %RID%)
echo ============================================================

rem ---------- Localiza o dotnet ----------
set "DOTNET="
if defined DOTNET_HOME if exist "%DOTNET_HOME%" set "DOTNET=%DOTNET_HOME%"
if not defined DOTNET for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET set "DOTNET=%%D"
if not defined DOTNET for %%P in ("%ProgramFiles%\dotnet\dotnet.exe" "%ProgramFiles(x86)%\dotnet\dotnet.exe") do if not defined DOTNET if exist %%P set "DOTNET=%%~P"
if defined DOTNET goto dotnet_ok
echo [ERRO] .NET SDK nao encontrado. Instale o SDK 8:
echo        https://dotnet.microsoft.com/download
pause & exit /b 1
:dotnet_ok
echo [OK] dotnet: %DOTNET%

rem ---------- Limpa saida antiga ----------
if exist "%OUT%" rd /s /q "%OUT%"
if not exist dist mkdir dist

rem ---------- Publish single-file self-contained ----------
echo.
echo --- Publicando (primeira vez baixa o runtime ~150MB) ---
"%DOTNET%" publish MangaReader\MangaReader.csproj -c Release -r %RID% --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -o "%OUT%"
if not errorlevel 1 goto pub_ok
echo [ERRO] Falha no publish.
pause & exit /b 1
:pub_ok

rem ---------- Copia amostras junto do exe ----------
if not exist samples goto samples_done
if not exist "%OUT%\samples" mkdir "%OUT%\samples"
copy /y samples\*.cbz "%OUT%\samples\" >nul 2>&1
:samples_done

echo.
echo ============================================================
echo  PRONTO! Executavel: %CD%\%OUT%\MangaReader.exe
echo  Duplo-clique para abrir; CBZ/CBR/PDF podem ser passados
echo  como argumento ou arrastados sobre o icone.
echo ============================================================

if not defined MAKEZIP goto zip_done
echo --- Gerando dist\MangaReader-win-x64.zip ---
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath 'dist\MangaReader-win-x64.zip' -Force"
if not errorlevel 1 goto zip_ok
echo [AVISO] Falha ao gerar o ZIP.
goto zip_done
:zip_ok
echo [OK] dist\MangaReader-win-x64.zip
:zip_done

choice /c SN /m "Abrir a pasta do resultado no Explorer agora?"
if not errorlevel 2 goto no_explorer
start "" explorer "%OUT%"
:no_explorer
pause
endlocal
