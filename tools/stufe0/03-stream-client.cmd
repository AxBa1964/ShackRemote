@echo off
rem ------------------------------------------------------------------
rem ShackRemote Stufe 0 - Test 2 (Laptop): Bild vom Shack-PC anzeigen
rem Aufruf:  03-stream-client.cmd
rem Zuerst starten, dann auf dem Shack-PC 02-stream-server.cmd.
rem Hinweis: udp://0.0.0.0 erzwingt IPv4 (udp://@ lauscht unter Windows nur auf IPv6).
rem Beim ersten Start fragt die Windows-Firewall: fuer private Netzwerke zulassen.
rem ------------------------------------------------------------------
setlocal
set "FFPLAY=%~dp0ffplay.exe"
if not exist "%FFPLAY%" set "FFPLAY=ffplay"

echo Warte auf Bild an UDP-Port 5000 ...
"%FFPLAY%" -hide_banner -fflags nobuffer -flags low_delay -framedrop ^
  -window_title "ShackRemote Stufe 0" "udp://0.0.0.0:5000"
pause
