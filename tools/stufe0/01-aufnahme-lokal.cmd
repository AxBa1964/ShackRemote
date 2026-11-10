@echo off
rem ------------------------------------------------------------------
rem ShackRemote Stufe 0 - Test 1: Aufnahme + Quick Sync lokal auf dem Shack-PC
rem Aufruf:  01-aufnahme-lokal.cmd [Monitor] [FPS] [Sekunden]
rem Beispiel: 01-aufnahme-lokal.cmd 1 60 30
rem Monitor: 0, 1 oder 2 (DXGI-Ausgang, nicht zwingend gleich der Windows-Nummer)
rem ------------------------------------------------------------------
setlocal
set "MON=%~1"
if "%MON%"=="" set "MON=0"
set "FPS=%~2"
if "%FPS%"=="" set "FPS=60"
set "SEK=%~3"
if "%SEK%"=="" set "SEK=30"

set "FFMPEG=%~dp0ffmpeg.exe"
if not exist "%FFMPEG%" set "FFMPEG=ffmpeg"

echo Monitor %MON%, %FPS% fps, %SEK% Sekunden. Waehrenddessen Task-Manager beobachten:
echo   - CPU-Last gesamt
echo   - Leistung ^> GPU ^> "Video Encode"
echo   - in der FFmpeg-Ausgabe: fps= muss bei %FPS% bleiben, speed= bei ca. 1x
echo.

"%FFMPEG%" -hide_banner -init_hw_device d3d11va:,vendor_id=0x8086 ^
  -filter_complex ddagrab=output_idx=%MON%:framerate=%FPS%,hwmap=derive_device=qsv,format=qsv ^
  -c:v h264_qsv -global_quality 20 -t %SEK% ^
  -y "%~dp0test_mon%MON%_%FPS%fps.mkv"

echo.
echo Fertig. Ergebnisdatei: %~dp0test_mon%MON%_%FPS%fps.mkv
pause
