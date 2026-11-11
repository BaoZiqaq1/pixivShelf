; pixivShelf 安装包脚本（Inno Setup 6）
; 用法：ISCC.exe /DMyAppVersion=1.0.0 pixivShelf.iss
; 载荷目录由 build-installer.ps1 先通过 publish.ps1 生成到 ..\dist\PixivShelf

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "pixivShelf"
#define MyAppPublisher "smile丶luo"
#define MyAppURL "https://github.com/BaoZiqaq1/pixivShelf"
#define MyAppExeName "PixivShelf.exe"
#define PayloadDir "..\dist\PixivShelf"

[Setup]
AppId={{93A1C843-9984-4DBE-97FB-3B8C6DE7D912}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\pixivShelf
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=pixivShelfSetup-{#MyAppVersion}
SetupIconFile=..\src\PixivShelf.WinUI\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no
ShowLanguageDialog=no

[Languages]
Name: "zh"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "startmenu"; Description: "创建开始菜单文件夹"; GroupDescription: "附加任务："; Flags: checkedonce

[Files]
; 只带程序本体：data\（配置、任务库、登录缓存）、pdb 一律排除
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "data\*,*.pdb,*.log,config.json,tasks.db,tasks.db-*,PixivShelf.exe.WebView2\*,*.WebView2\*"

[Files]
Source: "uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "uninstall.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{group}\卸载 {#MyAppName}"; Filename: "{app}\uninstall.cmd"; IconFilename: "{app}\PixivShelf.exe"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清掉运行期产生的零碎；data\ 是否删除由安装器里的复选框决定
Type: files; Name: "{app}\*.log"
Type: dirifempty; Name: "{app}"

[Code]
{ Inno 先写入标准卸载项，这里在最后一步把它改成带复选框的入口 }
procedure DeinitializeSetup();
var
  Key: String;
  Cmd: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
         ExpandConstant('{#SetupSetting("AppId")}_is1');
  Cmd := '"' + ExpandConstant('{app}\uninstall.cmd') + '"';
  RegWriteStringValue(HKCU, Key, 'UninstallString', Cmd);
  RegWriteStringValue(HKCU, Key, 'QuietUninstallString',
    '"' + ExpandConstant('{app}\unins000.exe') + '" /VERYSILENT /SUPPRESSMSGBOXES');
end;