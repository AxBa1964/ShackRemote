@echo off
rem ------------------------------------------------------------------
rem ShackRemote Stufe 0 - Test 2 (Shack-PC): Bild per UDP an den Laptop senden
rem Aufruf:  02-stream-server.cmd <Laptop-IP> [Monitor] [FPS]
rem Beispiel: 02-stream-server.cmd 192.168.178.50 0 60
rem Vorher auf dem Laptop 03-stream-client.cmd starten. Beenden mit q.
rem ------------------------------------------------------------------
setlocal
set "IP=%~1"
if "%IP%"=="" (
  echo Bitte die IPv4-Adresse des Laptops angeben, z. B.: %~nx0 192.168.178.50
  pause
  exit /b 1
)
set "MON=%~2"
if "%MON%"=="" set "MON=0"
set "FPS=%~3"
if "%FPS%"=="" set "FPS=60"

set "FFMPEG=%~dp0ffmpeg.exe"
if not exist "%FFMPEG%" set "FFMPEG=ffmpeg"

echo Sende Monitor %MON% mit %FPS% fps an %IP%:5000 ...
"%FFMPEG%" -hide_banner -init_hw_device d3d11va:,vendor_id=0x8086 ^
  -filter_complex ddagrab=output_idx=%MON%:framerate=%FPS%,hwmap=derive_device=qsv,format=qsv ^
  -c:v h264_qsv -global_quality 20 -async_depth 1 -bf 0 -g 60 ^
  -f mpegts "udp://%IP%:5000?pkt_size=1316"
pause
