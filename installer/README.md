# Building the Bookworm installer

This produces `output/Bookworm-Setup.exe` — a single installer that pre-seeds **your** Anthropic, Azure
Speech, and Azure Storage credentials (never the end user's) and asks the end user only for their own
Vision Australia Library login on first run. See `docs/decisions.md`'s Phase 5 section for why.

**The compiled `Bookworm-Setup.exe` contains your real secrets baked in.** Treat it like a password —
send it directly and privately to the one person it's for (email, USB drive, a private link), and never
upload it anywhere public (a GitHub release, a public file share, etc.). `.gitignore` already excludes
`installer/secrets.local.iss`, `installer/publish/`, `installer/output/`, and `*.exe` so none of this can
be committed by accident, but that only protects the repo — you're still responsible for how you hand the
compiled file to your father.

## One-time setup

Install the Inno Setup compiler if you don't have it:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## Rebuild steps

Run these from the repo root whenever you've changed the app and want a new installer:

```bash
# 1. Pull your own already-saved credentials out of Windows Credential Manager into a local,
#    git-ignored include file. Never prints the actual values.
dotnet run --project src/Bookworm.Console -- exportsecretsforinstaller

# 2. Publish both apps as self-contained single-file executables (no .NET runtime needed on the
#    target machine).
dotnet publish src/Bookworm.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer/publish/Bookworm.Windows
dotnet publish src/Bookworm.Console -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer/publish/Bookworm.Console

# 3. Compile the installer (adjust the path if Inno Setup installed somewhere else).
"C:\Users\<you>\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer\Bookworm.iss
```

The result is `installer/output/Bookworm-Setup.exe`.

## What it does on the end user's machine

1. Installs to `%LOCALAPPDATA%\Programs\Bookworm` (no admin rights needed — `PrivilegesRequired=lowest`).
2. Silently runs `Console\Bookworm.Console.exe seedsecrets ...` to write your Anthropic/Azure Speech/Azure
   Storage credentials into *their* Windows Credential Manager, using the values baked in at compile time.
3. Offers to launch Bookworm, which will show `SetupWindow` on first run — the end user enters their own
   Vision Australia login there, validated live before being saved.

## Testing a rebuild yourself first

Before sending a new build to your father, verify it on your own machine:

```bash
installer\output\Bookworm-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
"%LOCALAPPDATA%\Programs\Bookworm\Bookworm.Windows.exe"
```

Since your own credentials are what got seeded, this should skip straight to "Ready" (or, if you want to
test the first-run VA login screen specifically, delete the `Bookworm:VisionAustralia` entry from Windows
Credential Manager first). Check `%LOCALAPPDATA%\Bookworm\logs\` afterward for anything unexpected.
