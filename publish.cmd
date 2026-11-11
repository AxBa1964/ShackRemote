@echo off
rem ------------------------------------------------------------------
rem Erzeugt publish\ShackRemote.exe: eine Datei, ohne installiertes .NET lauffaehig.
rem ------------------------------------------------------------------
setlocal
cd /d "%~dp0"
dotnet publish src\ShackRemote\ShackRemote.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo.
  echo Fehler beim Veroeffentlichen.
  pause
  exit /b 1
)
echo.
echo Fertig: %~dp0publish\ShackRemote.exe
pause
