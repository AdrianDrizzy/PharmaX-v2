; PharmaX installer.
;
; Built with Inno Setup 6 (https://jrsoftware.org/isinfo.php). Run
; installer\build.ps1, which publishes the application and then calls
; ISCC on this file.
;
; What this installs is one self-contained executable: no .NET runtime
; to install first, no prerequisites, no configuration. Everything the
; machine needs to know about itself is asked once by the first-run
; wizard and written to ProgramData, which is deliberately outside
; everything this installer owns - see the comment on {app} below.

#define AppName      "PharmaX"
#define AppVersion   "1.0.0"
#define AppPublisher "PharmaX"
#define AppExe       "PharmaX.Desktop.exe"

[Setup]
AppId={{7C3E6A21-9F4D-4E8B-A6C5-2D1B8F0A4E77}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=PharmaX-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Per-machine, because PharmaX is a till rather than somebody's personal
; application: it has to start the same way for whoever is on shift.
PrivilegesRequired=admin

; Shown on the last page, because the machine is not finished when the
; installer is: the first run is where it is told what it is.
InfoAfterFile=readme-after-install.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a shortcut on the desktop"; \
    GroupDescription: "Shortcuts:"

; Off by default. A till should come up with PharmaX already running
; after a power cut, but the pharmacist's PC often should not, so the
; question is asked rather than assumed.
Name: "startup"; Description: "Start PharmaX when this computer starts"; \
    GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The whole published folder: the executable plus the native libraries
; it needs - SQLite, the PDF renderer, the graphics used by printing.
; Self-contained, so there is no .NET runtime to install first.
;
; Any appsettings.*.json is excluded. A developer's file would carry a
; real database password and would stop the first-run wizard from ever
; appearing, so the machine would arrive configured as somebody else's.
; build.ps1 refuses to get this far if one is present; this is the
; second lock on the same door.
Source: "..\dist\publish\*"; DestDir: "{app}"; \
    Excludes: "appsettings.*.json"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

; The shipped defaults. Deliberately carries no workstation identity:
; the WorkstationId in this file is a placeholder, and a real one is
; generated per machine by the first-run wizard. An installer that
; shipped a configured identity would give three tills the same one,
; and they would collide on the first receipt they both issued.
Source: "..\PharmaX.Desktop\appsettings.json"; DestDir: "{app}"; \
    Flags: ignoreversion

Source: "readme-after-install.txt"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
; Created here so that the first-run wizard, and every later upgrade,
; find it already there with the right permissions. Users need to write
; to it: the logs, the branding image, scanned prescriptions and the
; machine's own profile all live under it.
Name: "{commonappdata}\{#AppName}"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\logs"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\branding"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\attachments"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; \
    Tasks: desktopicon
Name: "{commonstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; \
    Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; \
    Description: "Set up PharmaX on this computer now"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Nothing under ProgramData is removed. It holds the pharmacy's logs,
; its scanned prescriptions and - on the main computer - nothing less
; than the identity the machine trades under. An uninstall is usually
; the first half of an upgrade, and deleting any of that would turn a
; reinstall into a data loss.
Type: files; Name: "{app}\*.log"
