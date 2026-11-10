# Stufe 0 – Machbarkeitstest mit FFmpeg

Ziel: Bevor Code geschrieben wird, prüfen, ob der **Intel N100** im Shack-PC einen Monitor per
Desktop Duplication aufnehmen und per **Quick Sync** schnell genug in H.264 kodieren kann –
auch während die Funk-Software läuft – und wie hoch die Latenz bis zum Laptop ist.

Die Aufnahme-Befehle folgen dem Beispiel für Intel Quick Sync im
[FFmpeg-Wiki „Capture/Desktop“](https://trac.ffmpeg.org/wiki/Capture/Desktop)
(Filter `ddagrab`, verfügbar ab FFmpeg 6.0).
Die Stream-Variante (UDP/MPEG-TS + ffplay) ist daraus abgeleitet und noch ungetestet.

## Vorbereitung

1. Aktuelles FFmpeg für Windows besorgen (mindestens **7.1.1** – ältere Versionen hatten laut
   [FFmpeg-Ticket #10385](https://trac.ffmpeg.org/ticket/10385) Probleme mit Nicht-Hauptmonitoren).
   Der Build muss `h264_qsv` enthalten; prüfen mit `ffmpeg -hide_banner -encoders | findstr qsv`.
2. `ffmpeg.exe` (Shack-PC) bzw. `ffplay.exe` (Laptop) in diesen Ordner kopieren oder in den PATH legen.
   Die Exe-Dateien sind per `.gitignore` vom Repository ausgeschlossen.

## Test 1 – Aufnahme lokal (Shack-PC)

```
01-aufnahme-lokal.cmd 0 60 30
01-aufnahme-lokal.cmd 1 60 30
01-aufnahme-lokal.cmd 2 60 30
```

Einmal ohne und einmal mit laufender Funk-Software (z. B. WSJT-X/JTDX). Beobachten:
FFmpeg-Ausgabe `fps=` und `speed=`, Task-Manager CPU-Last und GPU „Video Encode“.
Danach die `.mkv` am Laptop ansehen: Ist kleine Schrift lesbar?

## Test 2 – Latenz zum Laptop

1. Laptop: `03-stream-client.cmd` starten.
2. Shack-PC: `02-stream-server.cmd <Laptop-IP> 0 60` starten.
3. Auf dem Shack-PC eine Stoppuhr mit Millisekunden anzeigen (z. B. Online-Stoppuhr im Browser),
   beide Bildschirme gemeinsam mit dem Handy fotografieren. Differenz = Ende-zu-Ende-Latenz (ca.).
4. Mehrmals wiederholen, Mittelwert notieren. Einmal per LAN-Kabel, einmal per WLAN, wenn möglich.

## Messprotokoll

| Test | Monitor | fps | Funk-Software an? | fps erreicht | CPU % | Video Encode % | Latenz ms | Schrift lesbar? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 0 | 60 | nein | | | | – | |
| 1 | 0 | 60 | ja | | | | – | |
| 2 | 0 | 60 | ja | | | | | |

**Bewertung:** Erreicht der N100 stabil 60 fps (oder zumindest 30 fps) bei moderater CPU-Last und
liegt die Latenz deutlich unter ca. 150 ms, ist der Kern von ShackRemote machbar und es geht mit
Stufe 1a weiter. Die Grenzwerte sind Startwerte für die Bewertung, keine Messungen.
