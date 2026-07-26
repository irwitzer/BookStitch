; BookStitch Inno Setup script
; Generated for BookStitch 1.0.0-rc2.
; This installer uses the already published app files from publish\BookStitch-rc2.

#define MyAppName "BookStitch"
#define MyAppVersion "1.0.0-rc2"
#define MyAppPublisher "irwitzer"
#define MyAppURL "https://github.com/irwitzer/BookStitch"
#define MyAppExeName "BookStitch.exe"
#define MySourceRoot "D:\SyncPC\Proglibrary\VisualStudio_DB\BookStitch-Development"
#define MyPublishDir MySourceRoot + "\publish\BookStitch-rc2"

[Setup]
; The AppId uniquely identifies this application for install/update/uninstall.
; Keep this value stable for future BookStitch installers.
AppId={{B19ADC40-0BDF-419C-B67C-049F064395C2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; x64 / Windows on Arm64-compatible installer.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Default is an all-users install, but the user can choose current-user install at startup.
PrivilegesRequiredOverridesAllowed=dialog

DisableProgramGroupPage=yes
LicenseFile={#MySourceRoot}\LICENSE

OutputDir={#MySourceRoot}\installer
OutputBaseFilename=BookStitch-Setup-{#MyAppVersion}
SetupIconFile={#MySourceRoot}\BookStitch\Assets\Icons\BookStitchAppIcon-Simplified-multisize.ico

Compression=lzma2
SolidCompression=yes
WizardStyle=modern dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Published application files.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Project documentation and license files installed alongside the application.
Source: "{#MySourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
