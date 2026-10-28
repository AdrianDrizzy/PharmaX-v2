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
#define AppVersion   "1.0.20"
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
; build.ps1 -TestBuild: named apart, so it is never mistaken for the
; pharmacy's installer (its first administrator sign-in is an easy one).
#ifdef TestBuild
OutputBaseFilename=PharmaX-Setup-{#AppVersion}-test
#else
OutputBaseFilename=PharmaX-Setup-{#AppVersion}
#endif
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; The capsule from the logo: on setup.exe, beside PharmaX in Apps and
; Features, and down the side of the wizard. Each image at the sizes Inno
; picks between for 100, 150 and 200 per cent display scaling.
SetupIconFile=..\PharmaX.Desktop\Resources\pharmax.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardImageFile=wizard-large-100.bmp,wizard-large-150.bmp,wizard-large-200.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-150.bmp,wizard-small-200.bmp
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

; Ticked on one machine per pharmacy, the one the tills connect to.
; Installs MariaDB, makes PharmaX's database login and opens port 3306
; to the local network - see ServerSetup.cs. Off by default: a till or
; a single PC needs none of it.
Name: "mainserver"; \
    Description: "This is the pharmacy's main computer - install the database the tills share"; \
    GroupDescription: "Database:"; Flags: unchecked

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

; The pharmacy's database, already holding everything taken over from
; the previous system: build.ps1 imports the export into it and stops if
; any total disagrees. Where appsettings.json looks for a single PC's
; database, and per machine, so whoever signs in to Windows on shift sees
; the same records. Only where there is none yet - an upgrade or a
; reinstall must never put the change-over back over a pharmacy's trading
; - and never removed, like everything else in ProgramData.
Source: "..\dist\seed\pharmax.db"; DestDir: "{commonappdata}\{#AppName}\data"; \
    Permissions: users-modify; Flags: onlyifdoesntexist uninsneveruninstall

; What that import found for the pharmacist to look at.
Source: "..\dist\seed\import-report.txt"; DestDir: "{commonappdata}\{#AppName}\data"; \
    Flags: onlyifdoesntexist uninsneveruninstall

; MariaDB, downloaded and checked against its published hash by
; build.ps1. Only unpacked on the main computer, and only for as long as
; setup runs.
Source: "redist\mariadb.msi"; DestDir: "{tmp}"; Tasks: mainserver; \
    Flags: deleteafterinstall

[Dirs]
; Created here so that the first-run wizard, and every later upgrade,
; find it already there with the right permissions. Users need to write
; to it: the logs, the branding image, scanned prescriptions and the
; machine's own profile all live under it.
Name: "{commonappdata}\{#AppName}"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\logs"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\branding"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\attachments"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\data"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; \
    Tasks: desktopicon
Name: "{commonstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; \
    Tasks: startup

[Run]
; Before the finish page, elevated, so that the wizard started from it
; finds the database ready and its details filled in. MariaDB itself is
; left installed on uninstall: it holds the pharmacy's data.
Filename: "{app}\{#AppExe}"; \
    Parameters: "--setup-server ""{tmp}\mariadb.msi"""; \
    StatusMsg: "Installing the pharmacy's database. This takes a minute or two..."; \
    Tasks: mainserver; Flags: waituntilterminated

; A copy of the database every night at half past nine, on the main
; computer, as SYSTEM so it runs whoever is signed in or nobody is.
; Fourteen are kept in ProgramData\PharmaX\backups (DatabaseBackup.cs).
; /F replaces the task an earlier install made, so an upgrade keeps one.
Filename: "{sys}\schtasks.exe"; \
    Parameters: "/Create /F /SC DAILY /ST 21:30 /RU SYSTEM /RL HIGHEST /TN ""PharmaX nightly backup"" /TR ""\""{app}\{#AppExe}\"" --backup"""; \
    StatusMsg: "Scheduling the nightly backup..."; \
    Tasks: mainserver; Flags: runhidden waituntilterminated

; An upgrade of a machine that is already set up. Scripts the
; previous system left on Scripts to Collect are taken off that queue.
; The dispensing stays. A new install has not been set up yet, so this
; exits at once and the first start of PharmaX does the same job.
Filename: "{app}\{#AppExe}"; \
    Parameters: "--clear-waiting"; \
    StatusMsg: "Taking old waiting scripts off the collect queue..."; \
    Flags: waituntilterminated

Filename: "{app}\{#AppExe}"; \
    Description: "Set up PharmaX on this computer now"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; The schedule goes with the program; the backups it made stay.
Filename: "{sys}\schtasks.exe"; \
    Parameters: "/Delete /F /TN ""PharmaX nightly backup"""; \
    Flags: runhidden; RunOnceId: "DeleteBackupTask"

[UninstallDelete]
; Nothing under ProgramData is removed. It holds the pharmacy's logs,
; its scanned prescriptions and - on the main computer - nothing less
; than the identity the machine trades under. An uninstall is usually
; the first half of an upgrade, and deleting any of that would turn a
; reinstall into a data loss.
Type: files; Name: "{app}\*.log"
