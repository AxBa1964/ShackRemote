; ------------------------------------------------------------------------------------------
; ShackRemote – Setup (Inno Setup 7, kostenlos: https://jrsoftware.org/isdl.php)
; Wird von setup.cmd im Hauptordner gebaut; AppVersion kommt aus ShackRemote.csproj.
; ------------------------------------------------------------------------------------------

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{3183D28B-1AF4-48BD-9C60-1C954B193FEB}
AppName=ShackRemote
AppVersion={#AppVersion}
AppPublisher=Axel Baruschke, DK7EE
AppCopyright=© Axel Baruschke, DK7EE
; Der Dienst erwartet das Programm genau hier (ServiceSetup.ProgramFolder)
DefaultDirName={autopf}\ShackRemote
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\setup
OutputBaseFilename=ShackRemote-Setup-{#AppVersion}
SetupIconFile=..\src\ShackRemote\Assets\shackremote.ico
UninstallDisplayIcon={app}\ShackRemote.exe
UninstallDisplayName=ShackRemote
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "dienst"; Description: "Dieser PC ist der Shack-PC: Dienst einrichten (startet mit Windows, auch am Anmeldebildschirm) und Freigabe in der Windows-Firewall"; Flags: unchecked
Name: "desktopicon"; Description: "Symbol auf dem Desktop anlegen"

[Files]
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ShackRemote"; Filename: "{app}\ShackRemote.exe"
Name: "{autodesktop}\ShackRemote"; Filename: "{app}\ShackRemote.exe"; Tasks: desktopicon

[Run]
; Firewall: eingehende Verbindungen für ShackRemote erlauben (Netzwerkprofil Privat und Domäne)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ShackRemote"""; Flags: runhidden; Check: WantService
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""ShackRemote"" dir=in action=allow program=""{app}\ShackRemote.exe"" enable=yes profile=private,domain"; StatusMsg: "Firewall-Freigabe wird eingerichtet ..."; Flags: runhidden; Check: WantService
; Dienst anlegen bzw. aktualisieren und starten (auch nach einem Update, wenn er schon da war)
Filename: "{app}\ShackRemote.exe"; Parameters: "/dienst-installieren /still"; StatusMsg: "Dienst wird eingerichtet ..."; Flags: waituntilterminated; Check: WantService
; Am Ende anbieten, das Programm zu starten (als normaler Benutzer, nicht als Admin)
Filename: "{app}\ShackRemote.exe"; Description: "ShackRemote jetzt starten"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\ShackRemote.exe"; Parameters: "/dienst-entfernen /still"; Flags: waituntilterminated; RunOnceId: "DienstEntfernen"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ShackRemote"""; Flags: runhidden; RunOnceId: "FirewallEntfernen"

[Code]
var
  ServiceExisted: Boolean;

// Vor dem Kopieren: laufenden Dienst (und damit den Agenten) anhalten, sonst sind die Dateien gesperrt
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  rc: Integer;
begin
  ServiceExisted := Exec(ExpandConstant('{sys}\sc.exe'), 'query ShackRemote', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
  if ServiceExisted then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop ShackRemote', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Sleep(4000);
  end;
  // Noch laufende ShackRemote-Fenster beenden (z. B. ein offener Client)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ShackRemote.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Result := '';
end;

function WantService: Boolean;
begin
  Result := WizardIsTaskSelected('dienst') or ServiceExisted;
end;
