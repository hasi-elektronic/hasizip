; Inno Setup 6 — HasiZip für Windows
; Aufruf: iscc installer\HasiZip.iss  (nach dotnet publish nach publish\app, siehe .github/workflows/hasizip-release.yml)
#define MyAppName "HasiZip"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Hasi Elektronic"
#define MyAppURL "https://hasi-elektronic.de"
#define MyAppExeName "HasiZip.exe"

[Setup]
AppId={{3F6B2C1D-5A7E-4C9B-8D2F-HASIZIP00001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=HasiZip-Setup-{#MyAppVersion}
SetupIconFile=..\src\HasiZip.App\Assets\hasizip.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
ChangesAssociations=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "contextmenu"; Description: "Explorer-Kontextmenü (Packen / Hier entpacken)"; GroupDescription: "Integration:"
Name: "sendto"; Description: "„Senden an → HasiZip“"; GroupDescription: "Integration:"
Name: "desktopicon"; Description: "Desktop-Verknüpfung"; GroupDescription: "Weitere Aufgaben:"; Flags: unchecked

[Files]
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} deinstallieren"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; „Senden an“ übergibt alle markierten Dateien in einem Aufruf
Name: "{usersendto}\HasiZip (als ZIP packen)"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--pack"; Tasks: sendto

[Registry]
; Packen: für alle Dateien und Ordner. Bei Mehrfachauswahl startet Explorer pro Datei einen Prozess,
; HasiZip bündelt diese (InstanceCollector) zu einer ZIP.
Root: HKA; Subkey: "Software\Classes\*\shell\HasiZip.Pack"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Mit HasiZip packen"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\HasiZip.Pack"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\HasiZip.Pack"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\HasiZip.Pack\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --pack ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\HasiZip.Pack"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Mit HasiZip packen"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\HasiZip.Pack"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\HasiZip.Pack"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\HasiZip.Pack\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --pack ""%1"""; Tasks: contextmenu
; Entpacken: je Archiv-Endung (SystemFileAssociations lässt die bestehende Standard-App unangetastet)
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.7z\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rar\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tar\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.gz\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tgz\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.bz2\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.tbz2\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.xz\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txz\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.zst\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lz\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Hier entpacken (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.Extract"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Entpacken nach … (HasiZip)"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.ExtractTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.lzma\shell\HasiZip.ExtractTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract-to ""%1"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "HasiZip jetzt starten"; Flags: nowait postinstall skipifsilent
