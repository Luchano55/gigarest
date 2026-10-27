; Установщик GigaRest (Inno Setup 6). Решение D-INSTALL.
;
; Собирает CI (.github/workflows/build.yml) из готовой папки out\GigaRest:
;   iscc /DAppVersion=0.2.0-dev.40 /DFileVersion=0.2.40.0 /DAppSource=<папка программы> /DOutputDir=<куда> GigaRest.iss
; Результат: GigaRest-Setup.exe.
;
; Установка — только для текущего пользователя, без прав администратора, в %LocalAppData%\Programs\GigaRest:
; папка доступна на запись, поэтому самообновление из трея работает как в zip-версии.
; Программа появляется в меню «Пуск» и в «Установленных приложениях» (там же — удаление).
; Удаление: закрывает GigaRest (GigaRest.exe --exit: отдых сам возвращает «Не беспокоить»), убирает автозапуск.
; Настройки, сессия и журнал (%AppData%\GigaRest) остаются.

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef AppSource
  #define AppSource "..\..\out\GigaRest"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\out\installer"
#endif

#define AppName "GigaRest"
#define AppExeName "GigaRest.exe"
#define AppPublisher "Luchano55"
#define AppUrl "https://github.com/Luchano55/gigarest"

[Setup]
; AppId не менять никогда: по нему установщик узнаёт уже установленную программу (обновление поверх).
AppId={{E0DAD557-D14F-4439-A728-F1B0D5F76FCD}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; Без администратора, только для себя.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
UsePreviousAppDir=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 и новее — как у самой программы (SupportedOSPlatformVersion 10.0.19041).
MinVersion=10.0.19041

OutputDir={#OutputDir}
OutputBaseFilename=GigaRest-Setup
SetupIconFile=..\..\src\GigaRest.App\Assets\GigaRest.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
ShowLanguageDialog=no

; Запущенный GigaRest закрываем сами (PrepareToInstall: --exit), Restart Manager — запасной путь.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
ru.AutoStartTask=Запускать вместе с Windows (в трее)
ru.OtherTasks=Прочее:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:OtherTasks}"

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Тот же автозапуск, что включает сама программа (Autostart.cs): значение GigaRest в HKCU\…\Run.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GigaRest"; ValueData: """{app}\{#AppExeName}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Файлы, появившиеся после установки (самообновление, остатки *.old), — только известные типы.
Type: files; Name: "{app}\*.old"
Type: files; Name: "{app}\*.exe"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pri"
Type: dirifempty; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function AppExePath: String;
begin
  Result := ExpandConstant('{app}\{#AppExeName}');
end;

{ Вежливо закрыть запущенный GigaRest (любой копии, в том числе распакованной из zip):
  --exit просит его выйти и ждёт. Код 2 — был запущен и закрылся, 0 — не был запущен, 1 — не закрылся. }
procedure ExitRunning(const Exe: String);
var
  Code: Integer;
begin
  if not FileExists(Exe) then Exit;
  Log('Closing GigaRest: ' + Exe + ' --exit');
  if Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Log(Format('--exit finished with code %d', [Code]))
  else
    Log('--exit could not be started: ' + SysErrorMessage(Code));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Новый exe из установщика ещё не распакован — просим выйти через уже установленный (если есть). }
  ExitRunning(AppExePath);
  Result := '';
end;

{ Автозапуск убираем, только если он указывает на эту папку (у zip-копии в другой папке он свой). }
procedure RemoveAutoStart;
var
  Value: String;
begin
  if RegQueryStringValue(HKCU, RunKey, 'GigaRest', Value) then
    if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) > 0 then
    begin
      RegDeleteValue(HKCU, RunKey, 'GigaRest');
      Log('Autostart value removed');
    end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    ExitRunning(AppExePath);
    RemoveAutoStart;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    { Профиль встроенного браузера (вход в Яндекс Музыку) — служебный. Настройки в %AppData%\GigaRest не трогаем. }
    DelTree(ExpandConstant('{localappdata}\GigaRest\WebView2'), True, True, True);
    RemoveDir(ExpandConstant('{localappdata}\GigaRest'));
  end;
end;
