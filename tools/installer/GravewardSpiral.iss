; GRAVEWARD SPIRAL のインストーラー(Inno Setup 6)。
; 直接は使わず、python tools/make_installer.py から作る(配布用のビルドをしてから、ISCC にこのファイルを渡す)。
; ISCC に渡す定義: AppVersion(版)、SourceDir(配布用のビルドのフォルダ)、OutputDir(できたインストーラーを置くフォルダ)、RepoDir(リポジトリの根)。
; 管理者の権限は要らない(ユーザーのフォルダに入れる)。セーブと設定は %LOCALAPPDATA%\GravewardSpiral にあり、消しても残る。

#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif

[Setup]
AppId={{6B1C8E2A-4F7D-4C3B-9A51-2E8D7F0C3A61}
AppName=GRAVEWARD SPIRAL
AppVersion={#AppVersion}
AppVerName=GRAVEWARD SPIRAL {#AppVersion}
AppPublisher=caronyan
DefaultDirName={autopf}\GravewardSpiral
DefaultGroupName=GRAVEWARD SPIRAL
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=GravewardSpiral-Setup-{#AppVersion}
SetupIconFile={#RepoDir}\src\GravewardSpiral.Game\Icon.ico
UninstallDisplayIcon={app}\GravewardSpiral.exe
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
InfoBeforeFile={#RepoDir}\Content\CREDITS.txt
ChangesAssociations=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.EditorName=GRAVEWARD SPIRAL シナリオエディタ
english.EditorName=GRAVEWARD SPIRAL Scenario Editor
japanese.ClanFile=GRAVEWARD SPIRAL のクラン
english.ClanFile=GRAVEWARD SPIRAL clan
japanese.ScenarioFile=GRAVEWARD SPIRAL のシナリオ
english.ScenarioFile=GRAVEWARD SPIRAL scenario

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; 上書きで入れ直すとき、前の版のデータ(消えたファイル・動いたファイル)が残らないように、先に消す。セーブは %LOCALAPPDATA% にあるので消えない
Type: filesandordirs; Name: "{app}\Content"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; .gsclan(書き出したクラン)と .gssnro(シナリオ)を、ダブルクリックでゲームに渡す(ゲームが動いていれば、そちらへ渡す)。
; 管理者なしで入れたときはそのユーザーだけ(HKA は、権限に合わせて HKCU か HKLM)。アンインストールで消す
Root: HKA; Subkey: "Software\Classes\.gsclan"; ValueType: string; ValueName: ""; ValueData: "GravewardSpiral.Clan"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\GravewardSpiral.Clan"; ValueType: string; ValueName: ""; ValueData: "{cm:ClanFile}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\GravewardSpiral.Clan\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GravewardSpiral.exe,0"
Root: HKA; Subkey: "Software\Classes\GravewardSpiral.Clan\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GravewardSpiral.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.gssnro"; ValueType: string; ValueName: ""; ValueData: "GravewardSpiral.Scenario"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\GravewardSpiral.Scenario"; ValueType: string; ValueName: ""; ValueData: "{cm:ScenarioFile}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\GravewardSpiral.Scenario\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GravewardSpiral.exe,0"
Root: HKA; Subkey: "Software\Classes\GravewardSpiral.Scenario\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GravewardSpiral.exe"" ""%1"""

[Icons]
Name: "{group}\GRAVEWARD SPIRAL"; Filename: "{app}\GravewardSpiral.exe"
Name: "{group}\{cm:EditorName}"; Filename: "{app}\GravewardSpiralEditor.exe"
Name: "{group}\{cm:UninstallProgram,GRAVEWARD SPIRAL}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\GRAVEWARD SPIRAL"; Filename: "{app}\GravewardSpiral.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GravewardSpiral.exe"; Description: "{cm:LaunchProgram,GRAVEWARD SPIRAL}"; Flags: nowait postinstall skipifsilent
