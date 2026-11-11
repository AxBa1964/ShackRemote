# ShackRemote – Quellcode

## Dateien (Stand 0.8.0)

- **Dateifenster** (Knopf „Dateien“ im Fernsteuerungsfenster): links Laptop, rechts Shack-PC, jeweils Laufwerksübersicht,
  Pfadzeile, Liste. Kopieren/Verschieben zwischen den Seiten (F5/F6), Neuer Ordner (F7), Umbenennen (F2), Löschen (Entf),
  Rücktaste = eine Ebene höher. Ziehen zwischen den Seiten kopiert, auf einen Ordner derselben Seite verschiebt
  (mit Strg kopiert), aus dem Windows-Explorer kopiert bzw. lädt hoch; von der Laptop-Seite in den Explorer geht auch.
- **Übertragung** über die bestehende TLS-Verbindung (`MsgType.File`), Stücke à 64 KB, Fortschritt und Abbrechen,
  Rückfrage beim Überschreiben (Ja / Ja, alle / Nein / Abbrechen). Verschieben löscht die Quelle erst nach dem Kopieren.
- **Sicherheit:** Der Server greift im Namen des am Shack-PC angemeldeten Benutzers zu (auch im Dienstbetrieb).
- **Einschränkungen:** kein Dateizugriff, solange am Shack-PC niemand angemeldet ist; Löschen endgültig (kein Papierkorb);
  vom Shack-PC direkt in den Explorer des Laptops ziehen geht nicht; Netzlaufwerke des Benutzers im Dienstbetrieb ungeprüft.

## Ton (Stand 0.7.0)

- **Server:** Tonquelle wählbar: Wiedergabegerät per Loopback (was der Shack-PC abspielt) oder Aufnahmegerät
  (z. B. „USB Audio CODEC“ des Funkgeräts, geteilter Modus – WSJT-X kann parallel lesen). Umrechnung auf
  48 kHz Stereo, Opus (`libopus`, `lowdelay`, 10-ms-Pakete, 96 kbit/s), Kanal `MsgType.Audio`.
- **Client:** Auswahlfeld in der Leiste (🔇 aus / 🔊 Wiedergabe / 🎙 Aufnahme), Wahl wird gemerkt.
  Opus-Dekodierung, Wiedergabe per WASAPI; Puffer über 250 ms wird verworfen (Verzögerung bleibt klein).
- **Einschränkungen:** Im Dienstbetrieb (SYSTEM) noch ungetestet, ob dieselben Geräte sichtbar sind; Geräte im
  Exklusivmodus anderer Programme sind gesperrt; kein Mikrofon-Rückkanal (nicht gewünscht).

## Andere PCs: Grafikkarten und Encoder (Stand 0.6.0)

- **Alle Grafikkarten** werden nach Monitoren abgefragt (z. B. Prozessorgrafik + Steckkarte). Jeder Monitor wird auf
  der Grafikkarte aufgenommen, an der er hängt.
- **Encoder je Hersteller:** Intel → Quick Sync (`h264_qsv`), NVIDIA → NVENC (`h264_nvenc`), AMD → AMF (`h264_amf`);
  klappt das nicht, **Prozessor** (`libx264`, superfast/zerolatency) als letzte Rettung. Der zuletzt funktionierende
  Encoder je Grafikkarte wird beim nächsten Mal zuerst probiert. Der Client zeigt den Encoder in der Infozeile.
- **Getestet** ist bisher nur Intel Quick Sync (N100). NVENC und AMF sind nach FFmpeg-Quelltext/-Doku umgesetzt,
  aber mangels Hardware **ungetestet**.
- **Test 11.11.2026, Laptop als Server** (Intel Arc + NVIDIA, Optimus): eingebauter Bildschirm 3200×2000 hängt an der
  Intel-Grafik → Quick Sync, kodiert 16–51 fps (Kodieren 16–63 ms je Bild), Bild beim Client flüssig. NVENC nur mit
  Monitor an einem NVIDIA-Anschluss testbar.
- **Bekannte Grenze hohe Auflösung:** Bei WQHD/4K/3200×2000 sinkt die Bildrate. Möglich wäre, auf der Grafikkarte
  vor dem Kodieren zu verkleinern (FFmpeg 9 hat `vpp_qsv` und `scale_d3d11`) – bisher nicht nötig.
- **Noch offen:** Monitore werden nur beim Verbinden gezählt (An-/Abstecken während der Sitzung → neu verbinden).

## Dienstbetrieb (Stand 0.5.0)

Wie AnyDesk/RustDesk/Sunshine: Ein Windows-Dienst sorgt für Sperrbildschirm, Anmeldung und Strg+Alt+Entf.

- **Einrichten:** Server-Fenster → Knopf **„Dienst einrichten“** (Windows fragt nach Adminrechten). Das Programm
  kopiert sich nach `C:\Program Files\ShackRemote`, übernimmt Zertifikat und Kopplungen nach
  `C:\ProgramData\ShackRemote` (nur SYSTEM/Administratoren), setzt die Richtlinie `SoftwareSASGeneration`
  (Strg+Alt+Entf durch Dienste) und legt den Dienst **ShackRemote** an (Autostart, Neustart bei Absturz).
- **Ablauf:** Dienst (SYSTEM) → startet in der Konsolensitzung `ShackRemote.exe /agent` als SYSTEM → der Agent ist der
  eigentliche Server. Aufnahme- und Eingabe-Thread hängen sich jeweils an den aktiven Desktop
  (normal „Default“, Sperr-/Anmeldebildschirm „Winlogon“).
- **Client-Leiste:** Knöpfe **Strg+Alt+Entf** und **Sperren** (Win+L und Strg+Alt+Entf lassen sich am Laptop nicht abfangen).
- **Verwaltung:** Läuft der Dienst, ist ein von Hand gestartetes Server-Fenster nur noch Verwaltung (kein zweiter Server).
- **Protokolle:** `C:\ProgramData\ShackRemote\dienst.log` und `agent.log`.
- **Entfernen:** Knopf „Dienst entfernen“. Programm- und Datenordner bleiben liegen.

| Datei | Inhalt |
| --- | --- |
| `Core/Service/ShackService.cs` | Dienst: Agent am Leben halten, Strg+Alt+Entf (SendSAS) |
| `Core/Service/ProcessLauncher.cs` | Prozess als SYSTEM bzw. als Benutzer in einer Sitzung starten |
| `Core/Service/ServiceSetup.cs` | Einrichten/Entfernen |
| `Core/Service/SystemActions.cs` | Strg+Alt+Entf und Sperren auf Befehl des Clients |
| `Core/Desktop.cs` | Eingabe-Desktop erkennen und Threads daran binden |

## Stufe 1c: Maus und Tastatur (Stand 0.4.0)

- **Maus** über dem Bild: Bewegen, Tasten (links, rechts, Mitte, X1/X2), Rad senkrecht und waagrecht.
  Position relativ zum gezeigten Monitor (0..65535), der Server rechnet sie auf den ganzen virtuellen Desktop um
  und führt sie per `SendInput` aus.
- **Tastatur** über einen globalen Tastatur-Haken (`WH_KEYBOARD_LL`), solange das Fernsteuerungsfenster vorne ist:
  Alle Tasten gehen an den Shack-PC, auch Windows-Taste, Alt+Tab und Alt+F4. Strg+Alt+Entf geht Windows-bedingt nicht.
- **Lokales Kürzel:** Strg+Alt+F1/F2/F3 = Monitor links/Mitte/rechts (wird nicht gesendet).
- Fenster verliert den Fokus oder Verbindung endet: alle gedrückten Tasten werden losgelassen (nichts „klemmt“).
- **Grenze von Windows:** Programme, die als Administrator laufen, und UAC-Abfragen nehmen keine Eingaben von einem
  normal gestarteten ShackRemote an.

| Datei | Inhalt |
| --- | --- |
| `Core/InputProtocol.cs` | Eingabe-Nachrichten (binär) |
| `Core/InputInjector.cs` | Server: SendInput, Umrechnung auf den virtuellen Desktop |
| `UI/KeyboardHook.cs` | Client: globaler Tastatur-Haken |

## Stufe 1b: Bild (Stand 0.3.0 – GPU-Pipeline im Prozess)

Seit 0.3.0 läuft das Bild wie bei Moonlight/Sunshine, RustDesk und Parsec komplett **im eigenen Prozess auf der GPU**
(vorher: ffmpeg.exe als Hilfsprozess mit Rohbild-Pipe – das staute sich bei Größenänderung und Monitorwechsel).

| Seite | Kette |
| --- | --- |
| Server | `ddagrab` (Desktop Duplication, D3D11-Textur) → `hwmap` nach QSV → `h264_qsv` (ICQ 20, keine B-Bilder, async_depth 1) → `dump_extra` → ein Paket je Video-Nachricht |
| Client | H.264 → D3D11VA-Decoder (NV12-Textur bleibt auf der GPU) → D3D11 Video Processor (NV12 → BGRA + Skalierung) → Swapchain des Fensters |

- **Schlüsselbild auf Anforderung** (`RequestKeyframe`): Beim Einstieg, nach Rückstand oder Fehler fordert der Client ein IDR an –
  kein Encoder-Neustart mehr. Beim Größe-Ändern des Fensters wird nur die Swapchain angepasst, nichts neu gestartet.
- **Monitorknöpfe** in derselben Reihenfolge wie beim Server (nach Lage auf dem Desktop) und danach beschriftet:
  **Links / Mitte / Rechts**, ★ = Hauptbildschirm. **Strg+Alt+F1/F2/F3** = Knopf 1/2/3 von links (seit 0.4.0).
  (Die Windows-internen Nummern `\\.\DISPLAYn` weichen von denen in den Anzeigeeinstellungen ab, daher nicht verwendet.)
- **Zwei Threads auf dem Server** (seit 0.3.1): Aufnahme und Encoder laufen parallel; ist der Encoder noch beschäftigt,
  ersetzt das neueste Bild das ältere (kein Stau, geringste Verzögerung).
- Videonachricht: `[1 Byte Merker][H.264-Paket]`, Merker Bit 0 = Schlüsselbild.
- **Voraussetzung:** FFmpeg-9.0-Bibliotheken (DLLs). Einmalig `tools\get-ffmpeg.cmd` ausführen – lädt
  `ffmpeg-9.0.1-full_build-shared` (gyan.dev) nach `ffmpeg\bin`; der Build kopiert die DLLs neben die exe.
  Ein installiertes `ffmpeg.exe` wird nicht mehr gebraucht.

| Datei | Inhalt |
| --- | --- |
| `Core/Media/ScreenEncoder.cs` | Server: Aufnahme + Kodierung (FFmpeg-API über FFmpeg.AutoGen) |
| `Core/Media/VideoRenderer.cs` | Client: Dekodierung + Anzeige (D3D11 Video Processor, Swapchain) |
| `Core/Media/DxgiOutputs.cs` | Server: Bildschirme mit DXGI-Index, Anzeigenummer und Lage |
| `Core/Media/Native.cs` | Schlanker COM-Zugriff (D3D11/DXGI), Positionen aus den Windows-IDL-Dateien |
| `Core/Media/FfmpegLib.cs` | Laden der FFmpeg-DLLs, FFmpeg-Meldungen ins Protokoll |

## Stufe 1a: Gerüst (Stand 0.1.0)

Enthalten: Rollenwahl, Adresserkennung (WLAN / LAN / Tailscale), Server-Suche per UDP-Broadcast,
TLS-Verbindung mit selbst signierten Zertifikaten, Kopplung per 6-stelligem Code, Ping/Pong-Umlaufzeit.
Noch **kein** Bild, Ton oder Eingabe (Stufe 1b/1c).

| Datei | Inhalt |
| --- | --- |
| `Program.cs` | Start, Rollenwahl, Startparameter `/server`, `/client`, `/rolle` |
| `Core/AppSettings.cs` | Einstellungen in `%APPDATA%\ShackRemote\settings.json` |
| `Core/Identity.cs` | Eigenes Zertifikat `%APPDATA%\ShackRemote\identity.pfx`, Fingerabdruck |
| `Core/NetInfo.cs` | IPv4-Adressen und Netzart; Tailscale = 100.64.0.0/10 |
| `Core/Protocol.cs` | Ports, Nachrichtenrahmen, Steuernachrichten, Kopplungsnachweis |
| `Core/Discovery.cs` | Server-Suche (UDP 47100) |
| `Core/SessionServer.cs` / `SessionClient.cs` | TLS-Sitzung (TCP 47101), Kopplung, Ping |
| `UI/*.cs` | Fenster für Rollenwahl, Server, Client, Eingabedialog |

## Bauen und starten

```
cd C:\ABDevelop\ShackRemote
tools\get-ffmpeg.cmd
dotnet build src\ShackRemote
dotnet run --project src\ShackRemote -- /server
dotnet run --project src\ShackRemote -- /client
```

Einzelne Exe für den Shack-PC: `publish.cmd` im Hauptordner → `publish\ShackRemote.exe`.

## Test Stufe 1a

1. **Ein Rechner:** Server und Client gleichzeitig auf dem Laptop starten (zwei Terminals, siehe oben).
   Der Client sollte den eigenen Rechner finden; beim ersten Verbinden erscheint im Server-Fenster ein Code,
   der im Client eingegeben wird. Danach läuft die Umlaufzeit (Ping) im Sekundentakt.
2. **Zwei Rechner:** Server auf dem Shack-PC, Client auf dem Laptop. Bitdefender beobachten –
   neue Ports UDP 47100 und TCP 47101.
3. **Zweites Verbinden:** Client trennen und neu verbinden – jetzt ohne Code.
4. **Adressen abwählen:** Im Server-Fenster eine Adresse abwählen → im Protokoll verschwindet der Listener.

Zurücksetzen der Kopplungen: `%APPDATA%\ShackRemote\settings.json` löschen (auf dem jeweiligen Rechner).
