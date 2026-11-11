# ShackRemote – Quellcode

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
