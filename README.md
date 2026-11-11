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
| 1c | Bedienung und Ton: Maus, Tastatur, Monitorleiste 1/2/3, WASAPI/Opus | **erledigt** (11.11.2026): Maus/Tastatur 0.4.0, Ton 0.7.0 (Opus, Quelle wählbar) |
| 1d | Dienstbetrieb: Sperr-/Anmeldebildschirm, Strg+Alt+Entf, Sperren, Start mit Windows | **erledigt** (11.11.2026, 0.5.x) |
| 1e | Setup (Inno Setup): Startmenü/Desktop-Symbol, Dienst und Firewall per Haken | **neu** (11.11.2026, 1.0.0) |
| 2 | Zugriff von unterwegs über Tailscale | **läuft** (11.11.2026): Verbindung über Handy-Hotspot erfolgreich; 1.1.0 findet Server über `tailscale status` |
| 3 | optional: Windows-Dienst (Anmeldebildschirm/UAC), Zwischenablage, Dateien | offen |

## Setup bauen und installieren

Einmalig: Inno Setup 7 installieren (kostenlos, https://jrsoftware.org/isdl.php) und `tools\get-ffmpeg.cmd` ausführen.

```
cd C:\ABDevelop\ShackRemote
setup.cmd
```

Ergebnis: `publish\setup\ShackRemote-Setup-<Version>.exe` – eine Datei, läuft ohne installiertes .NET.

- **Shack-PC:** beim Setup den Haken „Dieser PC ist der Shack-PC …“ setzen. Dann wird der Dienst eingerichtet
  (startet mit Windows, auch am Anmeldebildschirm) und die Windows-Firewall für ShackRemote freigegeben (Profil Privat/Domäne).
- **Laptop:** Haken weglassen; es gibt nur das Symbol im Startmenü bzw. auf dem Desktop.
- **Update:** neues Setup einfach darüber installieren; ein vorhandener Dienst wird angehalten, aktualisiert und neu gestartet.
- **Deinstallieren:** über Windows-Einstellungen → Apps; Dienst und Firewall-Regel werden entfernt.
  Kopplungen und Einstellungen (`C:\ProgramData\ShackRemote`, `%APPDATA%\ShackRemote`) bleiben liegen.

## Neu in 1.1.0

- **Tailscale:** Die Suche fragt alle Geräte, die in Tailscale online sind, gezielt an (über Tailscale gibt es keinen Broadcast).
  Im Server-Fenster erscheint ein Warnhinweis, wenn die Tailscale-Adresse abgewählt ist.
- **Dienst und Adressen:** Das Verwaltungsfenster zeigt die Adressauswahl des *Dienstes*. Weicht sie ab, erscheint
  „Für Dienst übernehmen“ (Adminrechte, Dienst startet neu). „Dienst aktualisieren“ und das Setup übernehmen die Auswahl immer.
- **Gekoppelte Geräte:** Liste im Server-Fenster mit „Entfernen“.
- **Monitore:** Ein-/Ausstecken während der Sitzung wird erkannt (alle 3 s), die Monitorknöpfe passen sich an.
- **Fehlermeldungen:** Bei Zeitüberschreitung/Ablehnung erklärt der Client mögliche Ursachen.
- **Dateifenster:** Spalte „Name“ lässt Platz für die Bildlaufleiste (keine waagerechte Leiste mehr).
