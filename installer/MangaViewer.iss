#ifndef PublishDir
  #error PublishDir must point to the published application directory.
#endif
#ifndef AppVersion
  #define AppVersion "0.1.2"
#endif

[Setup]
AppId=MangaViewer.Desktop
AppName=Komado
AppVersion={#AppVersion}
DefaultDirName={autopf}\MangaViewer
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts\installer
OutputBaseFilename=Komado-Setup
SetupIconFile=..\src\MangaViewer\Assets\app-icon.ico
UninstallDisplayIcon={app}\MangaViewer.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{commonprograms}\MangaViewer.lnk"

[Icons]
Name: "{commonprograms}\Komado"; Filename: "{app}\MangaViewer.exe"; WorkingDir: "{app}"; AppUserModelID: "MangaViewer.Desktop"

[Run]
Filename: "{app}\MangaViewer.exe"; Description: "Komado を起動する"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CacheDirectory: String;
begin
  if (CurUninstallStep <> usPostUninstall) or UninstallSilent then
    Exit;

  CacheDirectory := ExpandConstant('{localappdata}\MangaViewer\Cache\Thumbnails');
  if not DirExists(CacheDirectory) then
    Exit;

  if MsgBox('このユーザーの表紙サムネイルのキャッシュも削除しますか？' + #13#10 +
      CacheDirectory + #13#10#13#10 +
      '元の ZIP・フォルダ、最近開いたファイルの履歴、ウィンドウ設定は削除しません。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    if not DelTree(CacheDirectory, True, True, True) then
      MsgBox('キャッシュの一部を削除できませんでした。' + #13#10 + CacheDirectory,
        mbError, MB_OK);
  end;
end;
