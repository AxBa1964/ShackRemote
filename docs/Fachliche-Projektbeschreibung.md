# ShackRemote – Fachliche Projektbeschreibung

| | |
|---|---|
| **Projekt** | ShackRemote – Fernsteuerung des Shack-PCs |
| **Version** | 1.2.0 |
| **Stand** | 12.11.2026 |
| **Autor / Verantwortlich** | Axel Baruschke, DK7EE – dk7ee@darc.de |
| **Lizenz** | GNU General Public License, Version 3 |
| **Quellcode** | https://github.com/AxBa1964/ShackRemote |
| **Webseite** | https://dk7ee.de/shackremote.php |
| **Ergänzende Dokumente** | [Technische Projektbeschreibung](Technische-Projektbeschreibung.md), Anleitung für Anwender (Word/PDF), [`README.md`](../README.md) |

Dieses Dokument beschreibt, **was** ShackRemote leisten soll und **warum** es so gebaut ist. Wie es technisch
umgesetzt ist, steht in der technischen Projektbeschreibung. Beide zusammen sollen es einer dritten Person
ermöglichen, das Projekt zu übernehmen und weiterzuführen.

---

## 1. Ausgangslage und Ziel

Funkamateure betreiben ihre Station („Shack“) häufig mit einem eigenen PC, an dem Funkgerät, Soundkarte und
Funksoftware (z. B. WSJT-X, JTDX, Logbuch) hängen – oft mit mehreren Monitoren. Dieser PC soll bequem von einem
anderen Rechner aus bedient werden können: vom Laptop im Wohnzimmer, aus dem Urlaub oder beim Funkfreund.

Kommerzielle Fernwartungsprogramme sind dafür oft zu teuer (Abo), an ein Konto und fremde Vermittlungsserver
gebunden oder für private Nutzung eingeschränkt.

**Ziel von ShackRemote:** ein einfaches, kostenloses Werkzeug mit allem, was man für die Fernbedienung des
eigenen Shack-PCs braucht – nicht mehr.

> Motivation des Autors: „Ein einfaches Werkzeug ohne Abokosten zur Verfügung zu stellen – mit allem, was man für
> die Fernwartung des eigenen PCs braucht.“

### 1.1 Zielgruppe

- Funkamateure mit einem Windows-PC im Shack und einem zweiten Windows-Rechner (meist Laptop).
- Keine Programmier- oder Netzwerkkenntnisse vorausgesetzt: Installation per Setup, Kopplung per Code.
- Weitergabe an Funkfreunde ausdrücklich erwünscht (freie Software).

### 1.2 Abgrenzung (bewusst nicht Teil des Projekts)

| Nicht enthalten | Begründung |
|---|---|
| macOS, Linux, Raspberry Pi | Rund 90 % der Shack-PCs laufen unter Windows. Bildaufnahme, Anzeige, Eingabe, Ton und Dienstbetrieb sind betriebssystemspezifisch; ein Port wäre nahezu eine Neuentwicklung (geprüft am 12.11.2026, siehe Abschnitt 9). |
| Eigener Vermittlungsserver / Zugriff über das Internet ohne VPN | Sicherheit und Datenschutz: ShackRemote öffnet keine Ports ins Internet. Für unterwegs wird ein VPN genutzt. |
| Mikrofon vom Laptop zum Shack (Rückkanal) | Nicht gefordert; Ton läuft nur vom Shack zum Laptop. |
| Zwischenablage, Druckerumleitung, Sitzungsaufzeichnung, Mehrbenutzerbetrieb | Nicht gefordert; Umfang bewusst klein gehalten. |
| Fernwartung fremder Rechner (Support-Szenario) | Zweck ist der **eigene** Shack-PC bzw. Rechner mit Erlaubnis des Besitzers. |

---

## 2. Begriffe

| Begriff | Bedeutung |
|---|---|
| **Shack-PC / Server** | Der Rechner, der ferngesteuert wird. |
| **Laptop / Client** | Der Rechner, von dem aus gesteuert wird. |
| **Rolle** | Server oder Client. Ein und dasselbe Programm übernimmt je nach Wahl beim Start die eine oder andere Rolle. |
| **Dienst** | Windows-Dienst „ShackRemote“ auf dem Shack-PC. Startet mit Windows, auch ohne Anmeldung. |
| **Agent** | Der eigentliche Server im Dienstbetrieb. Der Dienst startet ihn in der Sitzung am Bildschirm des Shack-PCs. Er zeigt ein kleines Fenster mit dem Zusatz „(Dienst)“. |
| **Verwaltungsfenster** | Normal gestartetes ShackRemote in der Rolle Server, während der Dienst läuft. Dient nur zum Einstellen (Adressen, gekoppelte Geräte, Dienst). |
| **Kopplung** | Einmaliges gegenseitiges Bekanntmachen von Laptop und Shack-PC per 6-stelligem Code. |
| **Kopplungscode** | Zufälliger 6-stelliger Code, der nur auf dem Bildschirm des Shack-PCs erscheint, 2 Minuten gültig. |
| **Gekoppeltes Gerät** | Ein Client, dem der Server vertraut. |
| **Monitor** | Ein Bildschirm des Shack-PCs. Es wird immer genau ein Monitor übertragen; umgeschaltet wird per Knopf. |
| **Tonquelle** | Was am Shack-PC abgespielt wird (Wiedergabe) oder ein Aufnahmegerät, z. B. die USB-Soundkarte des Funkgeräts. |
| **VPN / Tailscale** | Verschlüsseltes privates Netz zwischen den Rechnern für den Zugriff von unterwegs. Tailscale wird empfohlen, jedes andere VPN mit direkter Erreichbarkeit funktioniert auch. |

---

## 3. Funktionen im Überblick

| Bereich | Funktion |
|---|---|
| **Bild** | Bild eines Monitors des Shack-PCs in Echtzeit, skaliert auf das Fenster des Laptops. Umschalten zwischen allen Monitoren. Monitore werden automatisch erkannt, auch beim Ein- und Ausstecken während der Sitzung. |
| **Bedienung** | Maus (inkl. Rad und Seitentasten) und Tastatur einschließlich Windows-Taste, Alt+Tab, Alt+F4. Strg+Alt+Entf und Sperren über ein Menü. |
| **Ton** | Ton des Shack-PCs (Wiedergabe) oder eines Aufnahmegeräts, wählbar im laufenden Betrieb. |
| **Dateien** | Zweispaltiges Dateifenster (Laptop / Shack-PC): kopieren, verschieben, umbenennen, löschen, Ordner anlegen, Ziehen mit der Maus, auch aus dem Windows-Explorer. |
| **Dienstbetrieb** | Shack-PC ist ab Windows-Start erreichbar; Anmelde- und Sperrbildschirm bedienbar. |
| **Finden** | Automatische Suche im Heimnetz und über Tailscale; bekannte Server werden gemerkt; Adresse auch von Hand eingebbar. |
| **Sicherheit** | Verschlüsselung jeder Verbindung, Kopplung per Code, Kopplungssperre nach 3 Fehlversuchen, keine offenen Ports ins Internet. |
| **Installation** | Ein Setup für beide Rechner; ein Haken macht den Rechner zum Shack-PC (Dienst und Firewall). Kein installiertes .NET nötig. |
| **Darstellung** | Hell, Dunkel oder wie Windows. Startfenster mit Version, Lizenz und Haftungsausschluss. |

---

## 4. Abläufe aus Anwendersicht

### 4.1 Installation

**Shack-PC**
1. Setup (`shackremote-setup.zip` von der Webseite) entpacken und starten. Die SmartScreen-Warnung erscheint,
   weil das Setup nicht kostenpflichtig signiert ist.
2. Haken **„Dieser PC ist der Shack-PC …“** setzen.
3. Das Setup installiert nach `C:\Program Files\ShackRemote`, richtet den Dienst ein, gibt ShackRemote in der
   Windows-Firewall frei (Netzwerkprofile Privat und Domäne) und startet den Dienst.

**Laptop**
1. Dasselbe Setup ohne Haken. Danach ShackRemote über das Startmenü starten und die Rolle **Client** wählen.

**Aktualisieren:** neues Setup darüber installieren. Ein vorhandener Dienst wird angehalten, aktualisiert und
neu gestartet; Kopplungen und Einstellungen bleiben erhalten.

**Deinstallieren:** über die Windows-Einstellungen (Apps). Dienst und Firewall-Regel werden entfernt;
Kopplungen und Einstellungen bleiben liegen, bis man sie von Hand löscht.

### 4.2 Erste Verbindung und Kopplung

1. Client startet und sucht sofort Server (Heimnetz per Broadcast, bekannte Adressen, Tailscale-Geräte).
2. Benutzer wählt den Shack-PC und klickt „Verbinden“ (oder gibt die Adresse von Hand ein).
3. Ist der Laptop noch nicht gekoppelt, erscheint am Shack-PC ein 6-stelliger Code (2 Minuten gültig).
4. Benutzer gibt den Code am Laptop ein (mit oder ohne Leerzeichen; Eingabemaske „123 456“).
5. Beide Rechner merken sich einander dauerhaft. Danach ist für weitere Verbindungen kein Code nötig.

**Regeln:**
- Jeder neue Verbindungsversuch erzeugt einen neuen Code.
- Nach **3 falschen Codes** nimmt der Server **5 Minuten** lang keine neuen Kopplungen an und schreibt eine
  Warnung ins Protokoll. Bereits gekoppelte Geräte sind davon nicht betroffen.
- Meldet sich ein bekannter Server später mit einem anderen Zertifikat, bricht der Client aus Sicherheitsgründen ab.

### 4.3 Fernsteuern

- Das Fernsteuerungsfenster zeigt oben die Monitorknöpfe (Links / Mitte / Rechts, je nach Anzahl; ★ = Hauptbildschirm),
  „Dateien“, die Tonquelle und rechts unauffällig „…“ mit „Strg+Alt+Entf senden“ und „Shack-PC sperren“.
- Unten zeigt eine Statuszeile Bildrate, Datenrate, Umlaufzeit (Ping) und den verwendeten Encoder.
- Solange das Fenster im Vordergrund ist, gehen **alle** Tasten an den Shack-PC.
- Strg+Alt+F1/F2/F3 schaltet lokal zwischen den Monitoren um.
- Strg+Alt+Entf und Win+L lassen sich von Windows aus nicht abfangen und wirken immer am Laptop – deshalb das Menü „…“.

### 4.4 Dateien übertragen

Links „Dieser PC“, rechts „Shack-PC“. Laufwerke erscheinen sofort, langsame Netzlaufwerke werden nachgeladen.
Tasten: F5 Kopieren, F6 Verschieben, F7 Neuer Ordner, F2 Umbenennen, Entf Löschen (endgültig, ohne Papierkorb),
Rücktaste eine Ebene höher. Alle Dateizugriffe am Shack-PC laufen mit den Rechten des dort angemeldeten Benutzers.

### 4.5 Zugriff von unterwegs

- Im Heimnetz ist nichts einzurichten.
- Von unterwegs: VPN auf beiden Rechnern. Mit **Tailscale** findet „Suchen“ den Shack-PC automatisch.
  Bei anderen VPNs gibt man die VPN-Adresse des Shack-PCs einmal von Hand ein; danach wird sie gemerkt.
- Der Shack-PC muss eingeschaltet und erreichbar sein (kein Energiesparmodus).

### 4.6 Verwaltung am Shack-PC

ShackRemote am Shack-PC normal starten und „Server“ wählen → Verwaltungsfenster:
- **Adressen**, auf denen der Shack-PC erreichbar ist (Heimnetz, Tailscale …). Ist eine Tailscale-Adresse
  abgewählt, erscheint ein Warnhinweis. Weicht die Auswahl von der des Dienstes ab, erscheint „Für Dienst übernehmen“.
- **Gekoppelte Geräte** mit „Entfernen“.
- **Dienst aktualisieren / entfernen** (nur nötig ohne Setup).
- Das Verwaltungsfenster darf geschlossen werden; das Fenster mit „(Dienst)“ im Titel nur minimieren.

---

## 5. Anforderungen und Qualitätsziele

| Ziel | Anforderung | Erreicht (gemessen im eigenen Shack) |
|---|---|---|
| Geringe Verzögerung | Bedienung soll sich „wie vor Ort“ anfühlen | Umlaufzeit im WLAN ca. 10 ms, über Tailscale/Mobilfunk ca. 17–47 ms; Bildaufbau flüssig |
| Bildqualität | Schrift der Funksoftware gut lesbar, auch bei anderer Auflösung des Laptops | H.264 mit Hardware-Encoder, Skalierung auf der Grafikkarte |
| Bildrate | Flüssige Wasserfall-Anzeigen | Aufnahme mit 60 fps; kodiert werden auf dem Intel N100 je nach Bildinhalt bis rund 50 fps (ist der Encoder noch beschäftigt, ersetzt das neueste Bild das ältere – geringste Verzögerung statt Stau) |
| Einfachheit | Installation ohne Fachwissen | Setup mit einem Haken; Kopplung per Code |
| Sicherheit | Kein unbefugter Zugriff, keine offenen Ports ins Internet | TLS, Zertifikatsprüfung, Kopplung, Kopplungssperre, VPN |
| Ressourcen | Shack-PC (Intel N100) darf durch Fernsteuerung nicht ausgebremst werden | Aufnahme und Kodierung auf der Grafikkarte |
| Portabilität | Läuft auch beim Funkfreund mit 1–n Monitoren und anderer Grafikkarte | Monitore automatisch erkannt; Encoder je Grafikkarte automatisch gewählt |

---

## 6. Sicherheit und Datenschutz (fachliche Sicht)

- **Keine fremden Server:** Die Verbindung läuft direkt zwischen den eigenen Rechnern. Es gibt kein Konto, keine
  Telemetrie und keine Werbung.
- **Verschlüsselung:** Jede Verbindung ist mit TLS verschlüsselt, zusätzlich zur Verschlüsselung des VPNs.
- **Gegenseitige Prüfung:** Jeder Rechner hat ein eigenes Zertifikat. Die Kopplung bindet den Code an beide Zertifikate;
  danach prüfen sich beide Seiten bei jeder Verbindung.
- **Nur wer den Bildschirm sieht, kann koppeln:** Der Code erscheint ausschließlich am Shack-PC.
- **Kopplungssperre** gegen Durchprobieren von Codes.
- **Keine offenen Ports ins Internet;** Firewall-Freigabe nur für Netzwerke mit Profil Privat/Domäne.
- **Dateizugriffe mit Benutzerrechten**, nicht mit den Rechten des Dienstes.
- **Restrisiken:** Wer Zugang zum angemeldeten Laptop hat, kann sich mit dem Shack-PC verbinden (Kopplung liegt im
  Benutzerprofil). Wer Zugang zum VPN-Konto hat, gelangt ins private Netz – daher Zwei-Faktor-Anmeldung empfohlen.
- **Offener Quellcode** ist kein Sicherheitsrisiko: Die Sicherheit beruht auf Schlüsseln und Codes, nicht auf Geheimhaltung
  des Programms. Im Repository liegen keine Schlüssel, Passwörter oder persönlichen Adressen.

---

## 7. Rechtliches

- **Lizenz:** GNU GPL v3 (Datei `LICENSE`). Weitergabe und Änderung erlaubt, Quellcode muss verfügbar bleiben.
- **Haftung:** ohne Gewährleistung; Haftungsausschluss im Startfenster und in der Anleitung.
- **Verwendete Bausteine:** FFmpeg (mitgelieferte Fassung unter GPL v3), FFmpeg.AutoGen (MIT), NAudio (MIT).
- **Markennamen:** Fremde Produktnamen werden in Programm, Webseite und Anleitung nicht als Vergleich verwendet.
  Tailscale wird als unabhängige Empfehlung genannt, ohne Zusammenarbeit.
- **Nutzung:** Fernzugriff nur auf eigene Rechner oder mit Erlaubnis des Besitzers.

---

## 8. Entscheidungen und ihre Begründung

| Nr. | Entscheidung | Begründung / Alternativen |
|---|---|---|
| E1 | Ein Programm für beide Rollen | Einfachere Verteilung und Pflege; Rolle beim Start wählbar und merkbar. |
| E2 | Nur Windows | Zielgruppe; tiefe Windows-Integration (Dienst, Anmeldebildschirm, Grafikkarte) wäre sonst nicht möglich. Mac/Pi verworfen (12.11.2026). |
| E3 | Kein Vermittlungsserver, VPN für unterwegs | Sicherheit, keine laufenden Kosten, keine Abhängigkeit von Dritten. Tailscale empfohlen, andere VPNs möglich. |
| E4 | Kopplung per 6-stelligem Code am Bildschirm | Einfach für Laien, kein Passwort zu merken; „Wer den Shack-PC sieht, darf koppeln.“ |
| E5 | Kopplungssperre 3 Fehlversuche / 5 Minuten | Schutz gegen Durchprobieren, vor der Veröffentlichung eingeführt (1.2.0). |
| E6 | Windows-Dienst + Agent | Nur so sind Anmelde-/Sperrbildschirm und Strg+Alt+Entf möglich. Ein „Programm ohne Installation“ wurde vom Auftraggeber ausdrücklich verworfen: Adminrechte und Dienst sind in Ordnung, nur keine komplizierte Installation. |
| E7 | Setup mit einem Haken (Inno Setup) | Eine Datei für beide Rechner; Firewall und Dienst automatisch. |
| E8 | Ton nur vom Shack zum Laptop | So gefordert; Mikrofon-Rückkanal bewusst nicht umgesetzt. |
| E9 | Ein Monitor gleichzeitig, umschaltbar | Bedienkonzept wie bei bekannten Fernwartungsprogrammen; spart Bandbreite. |
| E10 | Strg+Alt+Entf und Sperren im Menü „…“ | Selten gebraucht; sollen nicht ins Auge fallen, aber auffindbar sein. |
| E11 | Freie Software unter GPL v3, Quellcode auf GitHub | Wie beim Projekt AFUfit; außerdem durch die mitgelieferte FFmpeg-Fassung (GPL) naheliegend. |
| E12 | Arbeitsweise im Projekt | Änderungen werden gesammelt und besprochen; umgesetzt wird erst auf ausdrückliches „jetzt umsetzen“ des Auftraggebers. |

---

## 9. Bekannte Grenzen

- **Windows 11 (64 Bit)** getestet; Windows 10 nicht getestet.
- **Grafikkarten:** Intel Quick Sync gut erprobt (Shack-PC Intel N100, Laptop Intel Arc). NVIDIA und AMD sind
  umgesetzt, aber wenig getestet. Ohne passende Grafikkarte kodiert der Prozessor (höhere Last).
- **Als Administrator laufende Fenster und UAC-Abfragen** nehmen Eingaben nur im Dienstbetrieb an.
- **Strg+Alt+Entf und Win+L** am Laptop wirken immer lokal (Windows-Vorgabe) – Ersatz über das Menü „…“.
- **SmartScreen-Warnung** beim Setup, solange es nicht kostenpflichtig signiert ist.
- **Firewall-Profil:** Ist das Heimnetz in Windows als „Öffentlich“ eingestuft, kommt keine Verbindung zustande.
- **Suche über VPN** automatisch nur mit Tailscale; bei anderen VPNs Adresse einmal von Hand eingeben.
- **Mac / Raspberry Pi:** nicht vorgesehen (Raspberry Pi 5 hat zudem keinen H.264-Hardware-Encoder/-Decoder).

---

## 10. Offene Wünsche und mögliche nächste Schritte

| Nr. | Wunsch | Status |
|---|---|---|
| W1 | Neues Startbild ohne eingebauten Text, höhere Auflösung, passend zum Shack | offen – Bild liefert der Auftraggeber |
| W2 | Option „Kopplung nur aus dem Heimnetz“ (über VPN nur bereits gekoppelte Geräte) | offen |
| W3 | Server-Fenster: Statustext neben „Dienst entfernen“ wird abgeschnitten | offen |
| W4 | Server-Fenster: leere Restspalte rechts in „Gekoppelte Geräte“ | offen |
| W5 | „Dienst aktualisieren“ nur hervorheben, wenn nötig (nicht eingerichtet oder ältere Version) | offen |
| W6 | Aktuelles Bildschirmfoto des Fernsteuerungsfensters (Kopfleiste mit „…“-Menü) für Webseite/Anleitung | offen |
| W7 | Anleitung: Kapitel Lizenz auf GPL v3 umstellen, Kopplungssperre erwähnen | offen |
| W8 | Tests mit NVIDIA- und AMD-Grafikkarten bei Funkfreunden | offen |
| W9 | Mac/Raspberry Pi | verworfen |

---

## 11. Projektverlauf (Kurzfassung)

| Datum | Version | Meilenstein |
|---|---|---|
| 11.11.2026 | – | Stufe 0: Machbarkeitstest – Aufnahme und Hardware-Kodierung auf dem N100, Latenzmessung |
| 11.11.2026 | 0.1–0.2 | Stufe 1a: Gerüst, Rollenwahl, Suche, TLS, Kopplung |
| 11.11.2026 | 0.3 | Stufe 1b: Bild über GPU-Pipeline im eigenen Prozess, Monitorknöpfe |
| 11.11.2026 | 0.4 | Stufe 1c: Maus und Tastatur; Neuaufbau der Aufnahme nach Zugriffsverlust |
| 11.11.2026 | 0.5 | Stufe 1d: Windows-Dienst – Sperr-/Anmeldebildschirm, Strg+Alt+Entf, Sperren |
| 11.11.2026 | 0.6 | Alle Grafikkarten, Encoder je Hersteller (Intel/NVIDIA/AMD) mit Prozessor-Rückfall |
| 11.11.2026 | 0.7 | Ton (Opus), Quelle wählbar |
| 11.11.2026 | 0.8 | Dateifenster |
| 11.11.2026 | 0.9 | Startfenster, Design Hell/Dunkel, neue Kopfleiste |
| 11.11.2026 | 1.0 | Setup (Inno Setup), Programmsymbol, Eingabemaske für den Kopplungscode |
| 11.11.2026 | 1.1 | Stufe 2: Tailscale-Suche, Verwaltung der Dienst-Einstellungen, gekoppelte Geräte, Monitor-Hotplug |
| 12.11.2026 | 1.2 | Kopplungssperre, GPL v3, Bereinigung für die Veröffentlichung, Projektdokumentation |
