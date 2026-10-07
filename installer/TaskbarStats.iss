; Inno Setup script voor TaskbarStats
; Bouwen: dubbelklik  build-installer.bat  in de projectmap (publiceert de app en compileert dit script).
; Handmatig: 1) dotnet publish -c Release -r win-x64 --self-contained true
;                 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
;            2) ISCC.exe installer\TaskbarStats.iss
; Resultaat: installer\Output\TaskbarStats-Setup-<versie>.exe

#define AppName "TaskbarStats"
#define AppVersion "1.6.7"
#define Publisher "Eric Bruggema"
#define ExeName "TaskbarStats.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{7B3A1F44-9C2E-4E5B-9F1A-5441B5C0A001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppComments=CPU / GPU / memory / network / disk / FPS monitor for the taskbar
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
UninstallDisplayIcon={app}\{#ExeName}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; De app vraagt zelf administrator-rechten (temperatuursensoren); de installer ook,
; zodat de autostart-taak met hoogste rechten kan worden aangemaakt.
PrivilegesRequired=admin
; Toon het leesmij-bestand (uitleg + credits) voor de installatie
InfoBeforeFile=Leesmij.txt

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
dutch.TaskStartup=Automatisch met Windows meestarten (zonder UAC-melding)
english.TaskStartup=Start automatically with Windows (no UAC prompt)
dutch.TaskDesktop=Snelkoppeling op het bureaublad
english.TaskDesktop=Desktop shortcut
dutch.TaskGroup=Extra:
english.TaskGroup=Extra:
dutch.IconReadme=Leesmij (uitleg en credits)
english.IconReadme=Readme (explanation and credits)
dutch.IconUninstall=verwijderen
english.IconUninstall=Uninstall
dutch.IconGitHub=TaskbarStats op GitHub
english.IconGitHub=TaskbarStats on GitHub
dutch.RunReadme=Leesmij (uitleg en credits) openen
english.RunReadme=Open the readme (explanation and credits)
dutch.RunStart=nu starten
english.RunStart=Start now
dutch.UninstallData=Ook je instellingen en het netwerkverbruik-log verwijderen?
english.UninstallData=Also delete your settings and the network usage log?

[InstallDelete]
; Startmenu-map eerst leegmaken: snelkoppelingsbestanden heten per taal anders ("Leesmij..." vs "Readme..."), dus een
; update in dezelfde map overschrijft alleen gelijknamige bestanden - een oudere versie of eerdere taalkeuze liet
; anders voorgoed een verweesde snelkoppeling achter (bv. naar een inmiddels verwijderde installatiemap).
Type: filesandordirs; Name: "{group}"

[Tasks]
Name: "startup"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:TaskGroup}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Leesmij.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
; Startmenu
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\{cm:IconReadme}"; Filename: "{app}\Leesmij.txt"
Name: "{group}\{#AppName} {cm:IconUninstall}"; Filename: "{uninstallexe}"
Name: "{group}\{cm:IconGitHub}"; Filename: "https://github.com/ericbruggema/TaskbarStats"; IconFilename: "{app}\{#ExeName}"
; Bureaublad (optioneel)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
; Autostart-taak aanmaken of juist weghalen, afhankelijk van de keuze
; shellexec: TaskbarStats.exe vraagt zelf om administrator-rechten (manifest); CreateProcess (het Inno-standaard)
; kan zo'n programma niet starten en geeft dan code 740, ShellExecute wel.
Filename: "{app}\{#ExeName}"; Parameters: "--autostart-on"; Flags: runhidden waituntilterminated shellexec; Tasks: startup
Filename: "{app}\{#ExeName}"; Parameters: "--autostart-off"; Flags: runhidden waituntilterminated shellexec; Tasks: not startup
Filename: "{app}\Leesmij.txt"; Description: "{cm:RunReadme}"; Flags: postinstall shellexec skipifsilent unchecked
Filename: "{app}\{#ExeName}"; Description: "{#AppName} {cm:RunStart}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; App afsluiten en de autostart-taak weghalen voordat de bestanden verdwijnen.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#ExeName}"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""TaskbarStats"" /F"; Flags: runhidden; RunOnceId: "DelTask"

[Code]
procedure StopApp;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#ExeName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

// Een draaiende versie afsluiten zodat het bestand vervangen kan worden.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\{#AppName}');
    if DirExists(DataDir) and (not UninstallSilent) then
      if MsgBox(CustomMessage('UninstallData') + #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
