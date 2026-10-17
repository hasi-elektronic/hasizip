# HasiZip

Schnelles, einfaches Pack- und Entpack-Programm für Windows – im Stil von HasiShot.
**Ein Fenster, keine Fragen:** Ordner hineinziehen → ZIP. Archiv hineinziehen → entpackt.

![Icon](src/HasiZip.App/Assets/hasizip.png)

## Warum schneller als 7-Zip?

| | HasiZip | 7-Zip |
|---|---|---|
| ZIP packen | **alle Kerne, auch bei einer einzigen großen Datei** (1-MiB-Blöcke parallel, Prinzip wie pigz) | ein Kern pro Datei |
| Schon komprimierte Daten (JPG, MP4, ZIP, verschlüsselte Backups) | werden erkannt und nur gespeichert – kein CPU-Aufwand | werden erneut komprimiert |
| ZIP entpacken | mehrere Leser gleichzeitig, native zlib, CRC-Prüfung | sequenziell |
| Ziel-Namen | automatisch, nie überschreiben („Name (2)“) | Dialog |

Messung (4 Kerne, Linux-Container, gleiche Daten, `hasizip bench` / `7z`):

| Aufgabe | HasiZip | 7-Zip (ZIP) | 7-Zip (.7z) |
|---|---|---|---|
| Ordner packen, 127 MB / 3.078 Dateien | **2,1 s** | 4,5 s | 24,3 s |
| Eine Datei packen, 273 MB | **3,0 s** | 22,3 s | – |
| Ordner-ZIP entpacken | **0,75 s** | 1,2 s | – |
| Große ZIP entpacken | **1,3 s** | 3,4 s | – |

Ehrlich: Das .7z-Format von 7-Zip bleibt kleiner (LZMA2), ist aber ~10× langsamer. Bei einer großen,
gemischten Datei ist HasiZips ZIP ca. 2 % größer als 7-Zips ZIP (Preis der Parallelisierung).
Unter Windows nutzt .NET 10 zlib-ng, dort ist HasiZip eher noch schneller als hier gemessen.

## Bedienung

- **Fenster:** Dateien/Ordner hineinziehen oder „Ordner packen…“, „Dateien packen…“, „Entpacken…“.
  Fortschritt mit MB/s und Restzeit, auch in der Taskleiste. „Abbrechen“ räumt halbfertige Dateien weg.
- **Explorer-Rechtsklick** (Windows 11: „Weitere Optionen anzeigen“):
  - „Mit HasiZip packen“ – auf Dateien/Ordner. Mehrfachauswahl ergibt **eine** ZIP.
  - „Hier entpacken (HasiZip)“ – auf `.zip .7z .rar .tar .gz .tgz .bz2 .xz …`
  - „Entpacken nach … (HasiZip)“ – Zielordner wählen.
- **Senden an → HasiZip (als ZIP packen)**.
- **Intelligentes Entpacken:** Enthält das Archiv genau einen Ordner, landet er direkt daneben;
  sonst entsteht ein Ordner mit dem Archivnamen. Nichts wird überschrieben.
- **Passwort:** verschlüsselte ZIP (ZipCrypto/AES), 7z und RAR fragen nach dem Passwort.
- **Gesperrte Dateien** (z. B. geöffnete Outlook-PST) werden übersprungen und am Ende gemeldet,
  statt den ganzen Vorgang abzubrechen.
- **Sicherheit:** Eintragsnamen mit `..`, absoluten Pfaden oder Laufwerksbuchstaben können nicht
  außerhalb des Zielordners schreiben („Zip Slip“); Symlinks aus TAR werden nicht angelegt.
- Kompression in der Fußzeile: Schnell / Normal / Maximal. Einstellungen in `%APPDATA%\HasiZip\settings.json`.

## Kommandozeile (`hasizip.exe`, auch Linux/macOS)

```bash
hasizip a [-l fast|normal|max] [-t threads] [-o ziel.zip] quelle…   # packen
hasizip x [-p passwort] archiv [zielordner]                          # entpacken (ohne Ziel: intelligent)
hasizip bench ordner                                                 # Geschwindigkeit messen
```

Exit-Codes: 0 ok, 1 Aufruf, 2 Lese-/Schreibfehler, 3 Passwort, 4 Dateien übersprungen, 130 abgebrochen.

## Aufbau

```
src/HasiZip.Core/   plattformneutral: ZipPacker (paralleles Deflate, eigener ZIP/ZIP64-Writer),
                    Extractor (ZIP parallel; 7z/RAR/TAR/GZ/BZ2/XZ über SharpCompress), SafePath, SmartNames,
                    InstanceCollector (bündelt Explorer-Mehrfachauswahl über eine Named Pipe)
src/HasiZip.App/    WPF-Oberfläche für Windows (Code-only, wie HasiShot)
src/HasiZip.Cli/    hasizip.exe
tests/              xUnit-Tests (Roundtrip, ZIP64, Zip Slip, Passwort, Abbruch, CRC-Fehler …)
installer/          Inno Setup 6 inkl. Kontextmenü (HKCU, keine Adminrechte nötig)
scripts/            make-icon.py, release.sh
```

## Bauen & Testen

Voraussetzung: .NET 10 SDK.

```bash
dotnet test tests/HasiZip.Core.Tests          # läuft auch auf Linux/macOS
dotnet build src/HasiZip.App -c Release       # auf Mac/Linux nur kompilierbar
HasiZip.exe --selftest C:\Temp\hz                     # rendert alle Fensterzustände als PNG + Roundtrip
```

- **CI:** `.github/workflows/ci.yml` – bei jedem Push/PR: Core-Tests auf Ubuntu,
  App-Build + Selbsttest auf Windows (Screenshots als Artefakt „HasiZip-Screenshots“).
- **Release:** `scripts/release.sh 1.1.0` setzt die Version, taggt `v1.1.0` und pusht.
  Alternativ: GitHub → Actions → „Build & Release“ → „Run workflow“ (legt Tag und Release selbst an).
  `.github/workflows/release.yml` baut daraus `HasiZip-Setup-1.1.0.exe` und `HasiZip-CLI-1.1.0-win-x64.zip`
  und legt ein GitHub Release an.
- **Icon neu erzeugen:** `python3 scripts/make-icon.py` (Pillow).

## Grenzen (v1.0)

- Packen nur als ZIP (maximal kompatibel – Windows öffnet es ohne Zusatzsoftware). Kein .7z-Schreiben.
- Keine verschlüsselten ZIPs beim **Packen** (Entpacken mit Passwort geht).
- Windows-11-Kontextmenü der ersten Ebene braucht ein signiertes MSIX-Paket – bisher nur klassisches Menü.
- Lizenzen: SharpCompress (MIT), System.IO.Hashing (MIT).
