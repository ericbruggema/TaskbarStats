; Inno Setup script voor TaskbarStats
; Bouwen: dubbelklik  build-installer.bat  in de projectmap (publiceert de app en compileert dit script).
; Handmatig: 1) dotnet publish -c Release -r win-x64 --self-contained true
;                 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
;            2) ISCC.exe installer\TaskbarStats.iss
; Resultaat: installer\Output\TaskbarStats-Setup-<versie>.exe

#define AppName "TaskbarStats"
#define AppVersion "1.1.0"
#define Publisher "Eric Bruggema"
#define ExeName "TaskbarStats.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{7B3A1F44-9C2E-4E5B-9F1A-5441B5C0A001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppComments=CPU / GPU / geheugen / netwerk / schijf-monitor voor de taakbalk
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

[Tasks]
Name: "startup"; Description: "Automatisch met Windows meestarten (zonder UAC-melding)"; GroupDescription: "Extra:"
Name: "desktopicon"; Description: "Snelkoppeling op het bureaublad"; GroupDescription: "Extra:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Leesmij.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
; Startmenu
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\Leesmij (uitleg en credits)"; Filename: "{app}\Leesmij.txt"
Name: "{group}\{#AppName} verwijderen"; Filename: "{uninstallexe}"
; Bureaublad (optioneel)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
; Autostart-taak aanmaken of juist weghalen, afhankelijk van de keuze
Filename: "{app}\{#ExeName}"; Parameters: "--autostart-on"; Flags: runhidden waituntilterminated; Tasks: startup
Filename: "{app}\{#ExeName}"; Parameters: "--autostart-off"; Flags: runhidden waituntilterminated; Tasks: not startup
Filename: "{app}\Leesmij.txt"; Description: "Leesmij (uitleg en credits) openen"; Flags: postinstall shellexec skipifsilent unchecked
Filename: "{app}\{#ExeName}"; Description: "{#AppName} nu starten"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; App afsluiten en de autostart-taak weghalen voordat de bestanden verdwijnen.
Filename: "{sys}	askkill.exe"; Parameters: "/F /IM {#ExeName}"; Flags: runhidden; RunOnceId: "StopApp"
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

procedure CurrentUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  rc: Integer;
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Autostart-taak verwijderen en de app afsluiten, vóórdat de bestanden verdwijnen.
    StopApp;
    // Rechtstreeks via schtasks: betrouwbaarder dan de app zelf starten tijdens het verwijderen.
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "TaskbarStats" /F', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Log('autostart-taak verwijderen, code ' + IntToStr(rc));
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\{#AppName}');
    if DirExists(DataDir) and (not UninstallSilent) then
      if MsgBox('Ook je instellingen en het netwerkverbruik-log verwijderen?' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
