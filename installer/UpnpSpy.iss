; Inno Setup script for UpnpSpy.
;
; Build with installer\build-installer.ps1 (which runs `dotnet publish` first,
; then `iscc.exe` against this file). Compiling this file directly will fail
; until artifacts\publish\win-x64\ exists.
;
; Requires Inno Setup 6 (https://jrsoftware.org/isdl.php). The compiler is
; "C:\Program Files (x86)\Inno Setup 6\iscc.exe" by default.

#define AppName        "UpnpSpy"
#define AppPublisher   "OpenHome"
#define AppExeName     "UpnpSpy.exe"
#define AppDescription "UPnP network device browser and diagnostic"

; AppVersion and PublishDir are passed in from build-installer.ps1 via /D flags.
; Defaults below let `iscc UpnpSpy.iss` work for a one-off if those are already
; on disk.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
; A stable, app-specific GUID so upgrades replace prior installs instead of
; producing a second entry in Apps & Features. Regenerate ONCE if you fork
; this app; never change it again afterwards.
AppId={{CE813E0D-A15B-43B9-8318-C4D8A134BDD1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppComments={#AppDescription}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; x64-only build. To ship ARM64, produce a separate installer with
; ArchitecturesAllowed=arm64 + ArchitecturesInstallIn64BitMode=arm64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Matches TargetPlatformMinVersion in UpnpSpy.App.csproj (Win10 1809).
MinVersion=10.0.17763
; Per-machine install. Switch to "lowest" if you'd rather offer per-user.
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-x64
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Sweep the entire self-contained publish output. recursesubdirs picks up
; the WindowsAppSDK runtime files, resource .pri, satellite assemblies, etc.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";    Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Description: "Launch {#AppName}"; Filename: "{app}\{#AppExeName}"; Flags: nowait postinstall skipifsilent

; Not registered here: the HttpListener URL ACL for the eventing callback
; (`netsh http add urlacl url=http://+:<port>/upnpspy/ user=Everyone`). The
; callback port is chosen dynamically at runtime, so an install-time grant
; would have to guess. The app's diagnostics surface a "bind failure" entry
; with the exact command to run if the ACL is missing — see quickstart §3 /
; §9.
