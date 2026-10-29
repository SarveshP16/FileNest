# FileNest

A small Windows app that keeps your Downloads folder tidy. When a download finishes, FileNest moves it into a
"nest" folder for its type: PDFs, Images, Installers, Archives, Documents, Media, or one you make yourself.

- Sorts new files only after the browser has finished writing them (`.crdownload`, `.part`, ... are skipped)
- Never overwrites: a second `report.pdf` becomes `report (1).pdf`
- Undo any move from the pop-up or the Activity page
- Organize now sorts what's already in the folder (asks first)
- Cleanup: spots installers for apps you've already installed (reads Windows' installed-apps list and each
  installer's product name) and moves the ones you pick to the Recycle Bin; optional weekly reminder
- Updates itself from GitHub Releases (Velopack), in the background if you like
- Lives in the tray, can start with Windows, light/dark theme, seven accent colors

## Build and run

Needs the .NET 10 SDK.

```bash
dotnet build
dotnet run --project src/FileNest
dotnet test
```

## Installer

```powershell
.\build-installer.ps1 -Version 1.0.0
```

This makes `artifacts\installer\FileNest-win-Setup.exe` with [Velopack](https://velopack.io). Setup installs FileNest for
the current user (no admin), adds Start menu and desktop shortcuts and an entry in Installed apps, installs the .NET 10
Desktop Runtime if the PC doesn't have it, and starts FileNest. Uninstalling removes the "Start with Windows" entry;
settings in `%APPDATA%\FileNest` are kept. Bump `-Version` for each release; the other files in `artifacts\installer`
are what the in-app updater will read later.

The installer isn't code-signed yet, so Windows SmartScreen shows "Windows protected your PC" the first time
(More info, then Run anyway).

### Publishing an update

Installed copies check `https://github.com/SarveshP16/FileNest` releases (set in
`src/FileNest/Services/AppUpdater.cs` and `build-installer.ps1`; the repo must be public).

```powershell
.\build-installer.ps1 -Version 1.0.1 -ReleaseNotes notes.md -Publish
```

`-Publish` downloads the previous release (so the update is a small delta), packs, and uploads a GitHub release
using `GITHUB_TOKEN` or your `gh` login. Keep `artifacts\installer` between releases.

To try it without touching your real settings, point it at another data folder; put a `settings.json` there with
`{"watchFolder": "C:\\some\\test\\folder"}` to watch a test folder instead of Downloads:

```powershell
$env:FILENEST_DATA = "C:\temp\filenest-test"; .\artifacts\publish\FileNest.exe
```

Settings and history normally live in `%APPDATA%\FileNest`.

## Layout

| Path | What's there |
|---|---|
| `src/FileNest.Core` | Sorting engine with no UI: rules, file watcher, activity log, JSON storage |
| `src/FileNest` | WPF app: window, pages, dialogs, toast, tray icon, theming |
| `tests/FileNest.Tests` | xUnit tests for the engine |
| `tools/make-icon.cs` | Regenerates `src/FileNest/Assets/FileNest.ico` (`dotnet run tools/make-icon.cs`) |
| `build-installer.ps1`, `dotnet-tools.json` | Installer build; pins the Velopack `vpk` tool |

Icons are from [Tabler Icons](https://tabler.io/icons) (MIT), bundled as a font.
