@echo off
rem ------------------------------------------------------------------------------------------
rem Baut das Setup: publish\setup\ShackRemote-Setup-<Version>.exe
rem   1. Programm veroeffentlichen (eigenstaendig, kein .NET auf dem Ziel-PC noetig)
rem   2. Inno Setup (ISCC.exe) baut daraus die Setup-Datei
rem Voraussetzung: Inno Setup 7 installiert (https://jrsoftware.org/isdl.php), FFmpeg-DLLs in ffmpeg\bin
rem ------------------------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

if not exist "ffmpeg\bin\avcodec-*.dll" (
  echo FFmpeg-DLLs fehlen. Bitte zuerst tools\get-ffmpeg.cmd ausfuehren.
  pause
  exit /b 1
)

rem --- Inno Setup suchen ---
set "ISCC="
for %%p in ("%ProgramFiles%\Inno Setup 7\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 7\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe") do (
  if not defined ISCC if exist %%p set "ISCC=%%~p"
)
if not defined ISCC for /f "delims=" %%p in ('where iscc 2^>nul') do if not defined ISCC set "ISCC=%%p"
if not defined ISCC (
  echo Inno Setup wurde nicht gefunden. Bitte installieren: https://jrsoftware.org/isdl.php
  pause
  exit /b 1
)
echo Inno Setup: %ISCC%

rem --- Version aus der Projektdatei ---
for /f "delims=" %%v in ('powershell -NoProfile -Command "([xml](Get-Content 'src\ShackRemote\ShackRemote.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"') do set "VER=%%v"
echo Version: %VER%

rem --- 1. Veroeffentlichen ---
if exist "publish\app" rmdir /s /q "publish\app"
dotnet publish src\ShackRemote\ShackRemote.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\app
if errorlevel 1 (
  echo.
  echo Fehler beim Veroeffentlichen.
  pause
  exit /b 1
)

rem --- 2. Setup bauen ---
"%ISCC%" /DAppVersion=%VER% setup\ShackRemote.iss
if errorlevel 1 (
  echo.
  echo Fehler beim Bauen des Setups.
  pause
  exit /b 1
)

echo.
echo Fertig: %~dp0publish\setup\ShackRemote-Setup-%VER%.exe
pause
