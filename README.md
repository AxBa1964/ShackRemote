# ShackRemote

Fernsteuerung des Shack-PCs vom Laptop aus – ähnlich AnyDesk, aber für den Privatgebrauch vereinfacht:
**eine Exe, ohne Installation**, beim Start Rolle wählen (Server = Shack-PC, Client = Laptop).

- Bild, Ton (nur Shack → Laptop), Tastatur und Maus
- 3 Monitore im Shack, umschaltbar wie bei AnyDesk
- Skalierung auf die Fenstergröße des Laptops
- Server-Suche im Heimnetz per Broadcast, unterwegs über Tailscale (`tailscale status --json`)
- Einmaliges Pairing per 6-stelligem Code, danach TLS mit gespeicherten Zertifikaten

Ausführliches Machbarkeitskonzept: [Claude-Dokument](https://claude.ai/artifact/H5EYN2BP7baXKTMVeYsBqS)

## Zielsystem

| Rechner | Hardware | Rolle |
| --- | --- | --- |
| Shack-PC | Intel N100, 16 GB RAM, Intel UHD Graphics (Quick Sync), 3 Monitore 1920 × 1080 | Server |
| Laptop | Core i9, 32 GB RAM | Client |

Beide Windows 11. Technik: C# / .NET 10, veröffentlicht als Self-contained Single File.

## Stufen

| Stufe | Inhalt | Status |
| --- | --- | --- |
| 0 | Machbarkeitstest mit FFmpeg: Aufnahme + Quick Sync auf dem N100, Latenz zum Laptop | **erledigt** (11.11.2026) – siehe [`tools/stufe0`](tools/stufe0/README.md) |
| 1a | Gerüst: Rollenwahl, Adresserkennung, Server-Suche, TLS, Pairing | **erledigt** (11.11.2026, Laptop ↔ Shack-PC über WLAN, Ping ca. 10 ms) – siehe [`src/README.md`](src/README.md) |
| 1b | Bild: DXGI-Aufnahme, H.264 per Quick Sync, Anzeige im Client | **läuft** (11.11.2026, 0.3.0 GPU-Pipeline im Prozess): Bild gut lesbar, Monitorwechsel klappt, kaum Verzögerung. Offen: Server liefert erst 32–50 statt 60 fps |
| 1c | Bedienung und Ton: Maus, Tastatur, Monitorleiste 1/2/3, WASAPI/Opus | Maus/Tastatur **erledigt** (11.11.2026, 0.4.0: Position, Klicks, Rad, Sondertasten, Umlaute getestet); Ton offen |
| 2 | Zugriff von unterwegs über Tailscale | offen |
| 3 | optional: Windows-Dienst (Anmeldebildschirm/UAC), Zwischenablage, Dateien | offen |
