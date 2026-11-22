; DeskFlow インストーラー（Inno Setup 6）
;   scripts\build-installer.ps1 から実行します。手動なら:
;   ISCC.exe /DAppVersion=1.0.0 installer\DeskFlow.iss
;
; 管理者権限なしで %LOCALAPPDATA%\Programs\DeskFlow にインストールします。

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "DeskFlow"
#define AppExe "DeskFlow.exe"
#define Root ".."

[Setup]
; AppId は変更しないこと（アップデート・アンインストールの識別に使われる）
AppId={{10A77AD4-72FA-4971-AFBF-92C1785D406F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=DeskFlow の作者
AppCopyright=Copyright (c) 2026 DeskFlow の作者
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} セットアップ
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile={#Root}\dist\LICENSE.txt
SetupIconFile={#Root}\assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#Root}\publish\installer
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 起動中の DeskFlow を閉じてから上書き・削除する
CloseApplications=force
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加のショートカット:"
Name: "autostart"; Description: "Windows の起動時に DeskFlow を自動で開始する"; GroupDescription: "起動:"; Flags: unchecked

[Files]
Source: "{#Root}\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\dist\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#Root}\dist\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\dist\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\{#AppName} の説明書"; Filename: "{app}\README.txt"
Name: "{group}\{#AppName} のアンインストール"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; アプリ内の「Windows の起動時に自動で開始」と同じ場所・同じ値
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskFlow"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "DeskFlow を起動する"; Flags: nowait postinstall skipifsilent

[Code]
// アンインストール時: 自動起動の登録を解除し、設定を消すか確認する
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir, CacheDir: String;
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DeskFlow');

  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\DeskFlow');
    CacheDir := ExpandConstant('{localappdata}\DeskFlow');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox('プレイリストや設定も削除しますか？' + #13#10 +
                '（再インストールしたときに使い続ける場合は「いいえ」を選んでください）',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
        DelTree(CacheDir, True, True, True);
      end;
    end;
  end;
end;
