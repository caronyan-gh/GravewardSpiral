; GRAVEWARD SPIRAL のインストーラー(Inno Setup 6)。
; 直接は使わず、python tools/make_installer.py から作る(配布用のビルドをしてから、ISCC にこのファイルを渡す)。
; ISCC に渡す定義: AppVersion(版)、SourceDir(配布用のビルドのフォルダ)、OutputDir(できたインストーラーを置くフォルダ)、RepoDir(リポジトリの根)。
; 管理者の権限は要らない(ユーザーのフォルダに入れる)。セーブと設定は %LOCALAPPDATA%\GravewardSpiral にあり、消しても残る。

#ifndef AppVersion
  #define AppVersion "0.2.0"
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

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.EditorName=GRAVEWARD SPIRAL シナリオエディタ
english.EditorName=GRAVEWARD SPIRAL Scenario Editor

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; 上書きで入れ直すとき、前の版のデータ(消えたファイル・動いたファイル)が残らないように、先に消す。セーブは %LOCALAPPDATA% にあるので消えない
Type: filesandordirs; Name: "{app}\Content"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GRAVEWARD SPIRAL"; Filename: "{app}\GravewardSpiral.exe"
Name: "{group}\{cm:EditorName}"; Filename: "{app}\GravewardSpiralEditor.exe"
Name: "{group}\{cm:UninstallProgram,GRAVEWARD SPIRAL}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\GRAVEWARD SPIRAL"; Filename: "{app}\GravewardSpiral.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GravewardSpiral.exe"; Description: "{cm:LaunchProgram,GRAVEWARD SPIRAL}"; Flags: nowait postinstall skipifsilent
