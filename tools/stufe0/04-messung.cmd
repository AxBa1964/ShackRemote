@echo off
rem Startet 04-messung.ps1 ohne Aenderung der PowerShell-Ausfuehrungsrichtlinie.
rem Aufruf:  04-messung.cmd [Sekunden] [Bezeichnung]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp004-messung.ps1" %*
pause
