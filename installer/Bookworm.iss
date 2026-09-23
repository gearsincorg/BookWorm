; Bookworm installer — builds a single Setup.exe that pre-seeds the developer's own Anthropic, Azure
; Speech, and Azure Storage credentials (via secrets.local.iss, generated locally and NEVER committed —
; see installer/README.md) and asks the end user only for their own Vision Australia Library login on
; first run. The compiled Setup.exe contains those secrets — treat it as sensitive, send it directly to
; the end user, never publish it anywhere public (see docs/decisions.md Phase 5).

#include "secrets.local.iss"

#define MyAppName "Bookworm"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Phil Malone"
#define MyAppExeName "Bookworm.Windows.exe"

[Setup]
AppId={{3B671DB0-2306-4DE9-BA89-ACFF60A7F271}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=Bookworm-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "publish\Bookworm.Windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "publish\Bookworm.Console\*"; DestDir: "{app}\Console"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Seeds the developer's own cloud credentials before the app is ever launched — the end user never
; sees or enters these. runhidden + waituntilterminated: this must finish before Bookworm itself starts.
Filename: "{app}\Console\Bookworm.Console.exe"; \
    Parameters: "seedsecrets --claude-key=""{#ClaudeApiKey}"" --speech-key=""{#SpeechKey}"" --speech-region=""{#SpeechRegion}"" --storage-connection=""{#StorageConnectionString}"" --storage-container=""{#StorageContainer}"""; \
    StatusMsg: "Setting up Bookworm's cloud services..."; \
    Flags: runhidden waituntilterminated

Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
