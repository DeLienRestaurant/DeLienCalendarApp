; DeLien Calendar for Windows インストーラ定義（Inno Setup 6）
;
; ビルド方法:
;   ISCC.exe /DAppVersion=0.1.0 setup.iss
;   （AppVersion 省略時は 0.0.0。リリースワークフローから実際のバージョンを渡す）
;
; 出力: ..\dist\DeLienCalendar-<version>-setup.exe
;
; インストール先は既定で %LOCALAPPDATA%\Programs\DeLienCalendar（管理者権限不要）。
; ウィザードの「インストール先の指定」画面で任意のフォルダへ変更できます。
; 管理者権限が必要な場所（C:\Program Files など）へ入れる場合は、
; インストーラ起動時に「管理者として実行」を選択してください。

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef AppName
  #define AppName "DeLien Calendar"
#endif

#ifndef AppExeName
  #define AppExeName "DeLienCalendar.exe"
#endif

#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; AppId はアップグレード判定に使うため変更しないこと
AppId={{4C0B7A62-9E15-4C8F-A3D6-7B2E5F91C4A8}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=DeLien
AppPublisherURL=https://calendar.delien.xyz/
AppSupportURL=https://calendar.delien.xyz/
DefaultDirName={localappdata}\Programs\DeLienCalendar
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
MinVersion=10.0.17763
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\DeLienCalendar.Desktop\Resources\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=DeLienCalendar-{#AppVersion}-setup
; 実行中のアプリがあれば終了を促す（アップグレード時）
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加のタスク:"
Name: "autostart"; Description: "PC へのサインイン時に自動起動する"; GroupDescription: "追加のタスク:"; Flags: unchecked

[InstallDelete]
; 旧バージョンの残骸（不要になった DLL など）を掃除する
Type: filesandordirs; Name: "{app}\runtimes"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\*.json"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} のアンインストール"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "DeLienCalendar"; ValueData: """{app}\{#AppExeName}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; タスクスケジューラの登録（アラーム・定期同期）と自動起動を解除する
Filename: "{app}\{#AppExeName}"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[Code]
var
  RuntimeMissing: Boolean;

function HasDotnetDesktopRuntime(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  RuntimeMissing := not HasDotnetDesktopRuntime();
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Answer: Integer;
begin
  if (CurPageID = wpReady) and RuntimeMissing then
  begin
    Answer := MsgBox(
      '.NET 8 デスクトップランタイムが見つかりませんでした。' + #13#10 +
      'このままインストールできますが、アプリの起動には次のランタイムが必要です。' + #13#10#13#10 +
      'https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10#13#10 +
      'インストールを続行しますか？', mbConfirmation, MB_YESNO);
    if Answer = IDNO then
      WizardForm.Close;
  end;
end;
