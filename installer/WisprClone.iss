; WisprClone — Inno Setup installer script
; Builds WisprCloneSetup.exe from the dotnet publish output.

#define MyAppName       "WisprClone"
#define MyAppVersion    "0.1.0"
#define MyAppPublisher  "Lily"
#define MyAppExeName    "WisprClone.exe"

[Setup]
; A stable GUID so future upgrades replace this app, not install side-by-side.
AppId={{8F9A4C2E-3D5B-4E1A-9F87-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
; "lowest" + "dialog" override = install per-user without UAC by default,
; but allow user to elevate if they want machine-wide.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=WisprCloneSetup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
; The publish output is large (self-contained .NET runtime ≈ 80 MB) — give the
; user a chance to see disk-space and confirm before extracting.
DisableReadyPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";   Description: "Create a &desktop shortcut";       GroupDescription: "Additional shortcuts:"
Name: "startupicon";   Description: "Start WisprClone with Windows";    GroupDescription: "Auto-start:"; Flags: unchecked

[Files]
; Publish output is produced by `dotnet publish` (see build-installer.ps1).
Source: "..\src\WisprClone.App\bin\Release\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WisprClone";       Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\WisprClone"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\WisprClone"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WisprClone"; Flags: nowait postinstall skipifsilent

; Settings + history (%APPDATA%\WisprClone) and logs (%LOCALAPPDATA%\WisprClone)
; are intentionally NOT removed on uninstall — they survive reinstalls. If the
; user wants a clean wipe, those folders are deleted manually.
