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

Ergebnisse vom 11.11.2026 (Shack-PC Intel N100, FFmpeg 9.0.2 gyan.dev full, Monitor 1920 × 1080, WLAN):

| Test | Bildinhalt | fps erreicht | speed | Bitrate | GPU 3D | CPU Mittel / Max | RAM |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 lokal | ruhig | 56 | 0,981x | 630 kbit/s | kurz grob 10–20 % | – | 83 % |
| 1 lokal | ruhig | 57 | 0,981x | 457 kbit/s | ca. 20–30 % | – | 85 % |
| 1 lokal | mit Bewegung | 54 (3 verworfen) | 0,98x | 975 kbit/s | ca. 20–30 % | – | 87 % |
| 2 Stream | Monitor 1 | – | – | – | – | 49,6 % / 91 % | 81 % |
| Vergleich | ohne Stream | – | – | – | – | 41,9 % / 95 % | 81 % |

- Anteil Aufnahme + Kodierung + Versand an der CPU: **ca. 8 Prozentpunkte**. Die Spitzen über 90 % entstehen auch ohne Stream.
- Kodierung erscheint im Task-Manager unter „Video Decode“ (vermutlich gemeinsame Video-Einheit der Intel-Grafik).
- Ping Shack-PC → Laptop über WLAN: 5–12 ms.
- Gefühlte Latenz mit Standardeinstellungen: unter 1 s, aber bei Maus und Tippen spürbar.
  Mit `-async_depth 1 -bf 0 -g 60` (Server) und `-probesize 32 -analyzeduration 0 -sync ext` (Client): spürbar schneller.
  Diese Einstellungen sind jetzt in den Skripten.

**Lehren für ShackRemote**

- `udp://@:5000` bindet unter Windows nur an `[::]` (IPv6) – IPv4 muss ausdrücklich gebunden werden (`0.0.0.0` bzw. die konkrete Adresse).
- Auf beiden Rechnern läuft die Bitdefender-Firewall; für ShackRemote werden Ausnahmen nötig.
- Ende-zu-Ende-Latenz wird in Stufe 1b per Zeitstempel im Programm gemessen.

**Bewertung:** Machbarkeit bestätigt – weiter mit Stufe 1a.

**Bewertung:** Erreicht der N100 stabil 60 fps (oder zumindest 30 fps) bei moderater CPU-Last und
liegt die Latenz deutlich unter ca. 150 ms, ist der Kern von ShackRemote machbar und es geht mit
Stufe 1a weiter. Die Grenzwerte sind Startwerte für die Bewertung, keine Messungen.
