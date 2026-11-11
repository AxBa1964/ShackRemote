# ShackRemote – Quellcode

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
