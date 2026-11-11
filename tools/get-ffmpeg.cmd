@echo off
rem Holt die FFmpeg-DLLs nach ffmpeg\bin (einmalig noetig)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0get-ffmpeg.ps1" %*
