; Inno Setup script voor TaskbarStats
; Bouwen: dubbelklik  build-installer.bat  in de projectmap (publiceert de app en compileert dit script).
; Handmatig: 1) dotnet publish -c Release -r win-x64 --self-contained true
;                 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
;            2) ISCC.exe installer\TaskbarStats.iss
; Resultaat: installer\Output\TaskbarStats-Setup-<versie>.exe

#define AppName "TaskbarStats"
#define AppVersion "1.6.8"
#define Publisher "Eric Bruggema"
#define ExeName "TaskbarStats.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"
; De (optionele) plugin-host: een aparte, niet-verhoogde exe; build-installer.bat publiceert hem eerst.
#define PluginsDir "..\plugins\TaskbarStats.Plugins\publish"

[Setup]
AppId={{7B3A1F44-9C2E-4E5B-9F1A-5441B5C0A001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://taskbarstats.com
AppSupportURL=https://github.com/ericbruggema/TaskbarStats/issues
AppUpdatesURL=https://github.com/ericbruggema/TaskbarStats/releases
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
dutch.TaskDesktop=Snelkoppeling op het bureaublad
english.TaskDesktop=Desktop shortcut
dutch.TaskGroup=Extra:
english.TaskGroup=Extra:
dutch.TaskPlugins=Optionele plugins installeren (Claude-verbruik, weer, locatie/GPS, uptime-monitor, script). Ze staan na het installeren nog uit.
english.TaskPlugins=Install the optional plugins (Claude usage, weather, location/GPS, uptime monitor, script). They stay off until you enable them.
dutch.IconReadme=Leesmij (uitleg en credits)
english.IconReadme=Readme (explanation and credits)
dutch.IconUninstall=verwijderen
english.IconUninstall=Uninstall
dutch.IconGitHub=TaskbarStats op GitHub
english.IconGitHub=TaskbarStats on GitHub
dutch.IconWebsite=TaskbarStats website
english.IconWebsite=TaskbarStats website
dutch.AutoTitle=Meestarten met Windows
english.AutoTitle=Start with Windows
dutch.AutoSub=Moet TaskbarStats automatisch starten als je inlogt?
english.AutoSub=Should TaskbarStats start automatically when you sign in?
dutch.AutoQuestion=TaskbarStats staat dan na het inloggen meteen klaar, zonder UAC-melding (via een geplande taak). Je kunt dit later altijd wijzigen bij Instellingen, Algemeen.
english.AutoQuestion=TaskbarStats is then ready right after you sign in, without a UAC prompt (through a scheduled task). You can change this at any time under Settings, General.
dutch.AutoYes=Ja, TaskbarStats automatisch met Windows starten (aanbevolen)
english.AutoYes=Yes, start TaskbarStats automatically with Windows (recommended)
dutch.AutoNo=Nee, ik start TaskbarStats zelf
english.AutoNo=No, I will start TaskbarStats myself
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
; Check: bij "Geen Startmenu-map maken" (WizardNoIcons) of een lege/gedeelde map zou {group} naar het Startmenu zelf wijzen.
Type: filesandordirs; Name: "{group}"; Check: GroupIsSafeToWipe

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked
; Plugins: Setup onthoudt de keuze bij een volgende installatie; stil installeren kan met /TASKS="plugins".
Name: "plugins"; Description: "{cm:TaskPlugins}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Leesmij.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#PluginsDir}\TaskbarStats.Plugins.exe"; DestDir: "{app}\plugins"; Flags: ignoreversion; Tasks: plugins
Source: "{#PluginsDir}\defaults\*"; DestDir: "{app}\plugins\defaults"; Flags: ignoreversion recursesubdirs; Tasks: plugins

[Icons]
; Startmenu
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\{cm:IconReadme}"; Filename: "{app}\Leesmij.txt"
Name: "{group}\{#AppName} {cm:IconUninstall}"; Filename: "{uninstallexe}"
Name: "{group}\{cm:IconWebsite}"; Filename: "https://taskbarstats.com"; IconFilename: "{app}\{#ExeName}"
Name: "{group}\{cm:IconGitHub}"; Filename: "https://github.com/ericbruggema/TaskbarStats"; IconFilename: "{app}\{#ExeName}"
; Bureaublad (optioneel)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
; Autostart-taak aanmaken of juist weghalen, afhankelijk van het antwoord op de wizardpagina "Meestarten met Windows"
; shellexec: TaskbarStats.exe vraagt zelf om administrator-rechten (manifest); CreateProcess (het Inno-standaard)
; kan zo'n programma niet starten en geeft dan code 740, ShellExecute wel.
Filename: "{app}\{#ExeName}"; Parameters: "--autostart-on"; Flags: runhidden waituntilterminated shellexec; Check: AutostartWanted
Filename: "{app}\{#ExeName}"; Parameters: "--autostart-off"; Flags: runhidden waituntilterminated shellexec; Check: not AutostartWanted
Filename: "{app}\Leesmij.txt"; Description: "{cm:RunReadme}"; Flags: postinstall shellexec skipifsilent unchecked
Filename: "{app}\{#ExeName}"; Description: "{#AppName} {cm:RunStart}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; App afsluiten en de autostart-taak weghalen voordat de bestanden verdwijnen.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#ExeName}"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""TaskbarStats"" /F"; Flags: runhidden; RunOnceId: "DelTask"
; Plugin-host (als die er is): stoppen en zijn taak weghalen.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM TaskbarStats.Plugins.exe"; Flags: runhidden; RunOnceId: "StopPlugins"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""TaskbarStats Plugins"" /F"; Flags: runhidden; RunOnceId: "DelPluginsTask"

[UninstallDelete]
Type: dirifempty; Name: "{app}\plugins\defaults"
Type: dirifempty; Name: "{app}\plugins"

[Code]
var
  AutoPage: TInputOptionWizardPage;

const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7B3A1F44-9C2E-4E5B-9F1A-5441B5C0A001}_is1';

function InstalledVersion: String;
begin
  Result := '';
  if not RegQueryStringValue(HKLM64, UninstallKey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM32, UninstallKey, 'DisplayVersion', Result) then
      Result := '';
end;

function IsSilentInstall: Boolean;
begin
  Result := Pos('SILENT', UpperCase(GetCmdTail)) > 0;   // /SILENT en /VERYSILENT
end;

// Taal voor de vraag in InitializeSetup: die loopt voor de taalkeuze van Setup, dus de Windows-taal beslist.
function Txt(const Nl, En: String): String;
begin
  if (GetUILanguage and $3FF) = $13 then Result := Nl else Result := En;
end;

// Staat er al een installatie? Dan eerst vragen of die overschreven mag worden (niet bij /SILENT of /VERYSILENT).
function InitializeSetup: Boolean;
var
  Old: String;
  vOld, vNew: Int64;
  Cmp: Integer;
  Msg: String;
begin
  Result := True;
  if IsSilentInstall then Exit;
  Old := InstalledVersion;
  if Old = '' then Exit;
  Cmp := 0;
  if StrToVersion(Old, vOld) and StrToVersion('{#AppVersion}', vNew) then Cmp := ComparePackedVersion(vOld, vNew);
  if Cmp < 0 then
    Msg := Txt('TaskbarStats ' + Old + ' is al ge' + #239 + 'nstalleerd.' + #13#10#13#10 + 'Wil je het bijwerken naar versie {#AppVersion}? Je instellingen blijven behouden. TaskbarStats wordt daarvoor even afgesloten.',
               'TaskbarStats ' + Old + ' is already installed.' + #13#10#13#10 + 'Do you want to update it to version {#AppVersion}? Your settings are kept. TaskbarStats is closed for a moment to do so.')
  else if Cmp = 0 then
    Msg := Txt('TaskbarStats ' + Old + ' is al ge' + #239 + 'nstalleerd.' + #13#10#13#10 + 'Wil je het opnieuw installeren (overschrijven)? Je instellingen blijven behouden. TaskbarStats wordt daarvoor even afgesloten.',
               'TaskbarStats ' + Old + ' is already installed.' + #13#10#13#10 + 'Do you want to install it again (overwrite)? Your settings are kept. TaskbarStats is closed for a moment to do so.')
  else
    Msg := Txt('Er is een NIEUWERE versie van TaskbarStats ge' + #239 + 'nstalleerd (' + Old + ').' + #13#10#13#10 + 'Wil je die vervangen door de oudere versie {#AppVersion}?',
               'A NEWER version of TaskbarStats is installed (' + Old + ').' + #13#10#13#10 + 'Do you want to replace it with the older version {#AppVersion}?');
  Result := MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES;
end;

// Een bestaande autostart-taak? (de naam is vast en geldt voor de hele pc)
function AutostartTaskExists: Boolean;
var
  rc: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN TaskbarStats', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
end;

// Wizardpagina "Meestarten met Windows": standaard ja bij een nieuwe installatie, bij een update zoals het nu staat;
// /AUTOSTART=yes of /AUTOSTART=no (voor stille installaties) beslist anders.
procedure InitializeWizard;
var
  Param: String;
  Yes: Boolean;
begin
  AutoPage := CreateInputOptionPage(wpSelectTasks, CustomMessage('AutoTitle'), CustomMessage('AutoSub'),
    CustomMessage('AutoQuestion'), True, False);
  AutoPage.Add(CustomMessage('AutoYes'));
  AutoPage.Add(CustomMessage('AutoNo'));

  if InstalledVersion = '' then Yes := True else Yes := AutostartTaskExists;
  Param := LowerCase(ExpandConstant('{param:AUTOSTART|}'));
  if (Param = 'yes') or (Param = '1') or (Param = 'on') then Yes := True;
  if (Param = 'no') or (Param = '0') or (Param = 'off') then Yes := False;
  if Yes then AutoPage.SelectedValueIndex := 0 else AutoPage.SelectedValueIndex := 1;
end;

function AutostartWanted: Boolean;
begin
  Result := AutoPage.SelectedValueIndex = 0;
end;

// {group} mag alleen worden leeggeveegd als het echt een eigen map is: niet bij "Geen Startmenu-map maken" en nooit het
// Startmenu (of Programma's) zelf.
function GroupIsSafeToWipe: Boolean;
var
  G: String;
begin
  G := RemoveBackslashUnlessRoot(ExpandConstant('{group}'));
  Result := (not WizardNoIcons) and (G <> '')
    and (CompareText(G, RemoveBackslashUnlessRoot(ExpandConstant('{commonprograms}'))) <> 0)
    and (CompareText(G, RemoveBackslashUnlessRoot(ExpandConstant('{userprograms}'))) <> 0)
    and (CompareText(G, RemoveBackslashUnlessRoot(ExpandConstant('{commonstartmenu}'))) <> 0)
    and (CompareText(G, RemoveBackslashUnlessRoot(ExpandConstant('{userstartmenu}'))) <> 0);
end;

procedure StopApp;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#ExeName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
  // de plugin-host ook (anders blijft zijn exe vergrendeld bij een update)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TaskbarStats.Plugins.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
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
