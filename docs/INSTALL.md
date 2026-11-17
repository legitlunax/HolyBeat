# Installation

This guide covers building HolyBeat Live Guard, installing it for the current
Windows user, and fixing the problems that can happen along the way.

## 1. Requirements

| Requirement | Details |
| --- | --- |
| Operating system | Windows 10 or 11, x64 or arm64 |
| Build toolchain | .NET 10 SDK (10.0.403 or newer), `dotnet` on `PATH` |
| Runtime | None - the published build is self-contained |
| Rights | None. The installer writes only to your own user profile. |

No driver is installed, no service is registered and nothing is downloaded
during installation.

## 2. Build

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` runs five stages and stops at the first failure:

1. **Restore** - `dotnet restore` for the app and test projects.
2. **Build** - `dotnet build -c Release`.
3. **Test** - `dotnet test` (use `-SkipTests` to skip it).
4. **Publish** - self-contained publish for `win-x64` into `dist\publish`.
5. **Package** - zips the published output to
   `dist\HolyBeatLiveGuard-win-x64.zip` and prints its absolute path.

Options:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Runtime win-arm64
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipTests
```

The script is re-runnable: the publish folder and the package are recreated
from scratch every time.

## 3. Install

```powershell
powershell -ExecutionPolicy Bypass -File .\install\install.ps1
```

Options:

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-SourcePath` | `E:\HolyBeat\dist\publish` | Folder with the published output |
| `-InstallDir` | `%LOCALAPPDATA%\Programs\HolyBeatLiveGuard` | Target folder |
| `-EnableAutoStart` | off | Register the app to start with Windows |
| `-SkipShortcuts` | off | Do not create the Start Menu and Desktop shortcuts |

The installer validates that `HolyBeat.LiveGuard.exe` exists in the source
folder before copying anything; if it does not, it tells you to run
`build.ps1` first. It is idempotent - run it twice and you get the same
result.

### What gets installed where

| Item | Location |
| --- | --- |
| Application files | `%LOCALAPPDATA%\Programs\HolyBeatLiveGuard` |
| Executable | `...\HolyBeatLiveGuard\HolyBeat.LiveGuard.exe` |
| Start Menu shortcut | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HolyBeat Live Guard.lnk` |
| Desktop shortcut | `%USERPROFILE%\Desktop\HolyBeat Live Guard.lnk` |
| Auto-start entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` -> `HolyBeatLiveGuard` |
| Configuration | `%APPDATA%\HolyBeat\config.json` (created on first launch) |

### Auto-start

With `-EnableAutoStart` the installer writes a per-user `Run` value pointing
at the installed executable. Without the switch it removes that value if it
exists, so the switch is fully reversible and the two states never conflict.
No scheduled task and no service is used.

## 4. Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\install\uninstall.ps1
```

This stops a running HolyBeat Live Guard process, removes both shortcuts,
removes the auto-start entry and deletes the contents of the install folder.
Your configuration is kept unless you ask for it to go:

```powershell
powershell -ExecutionPolicy Bypass -File .\install\uninstall.ps1 -PurgeConfig
```

The uninstaller refuses to operate on a drive root, a top-level folder or a
known system folder, and it only ever deletes inside the resolved install
directory.

## 5. Troubleshooting

### "cannot be loaded because running scripts is disabled on this system"

Windows blocks unsigned scripts by default. Allow them for the current
terminal only:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

Then run the script again. This affects only the current window; nothing
permanent is changed and no administrator rights are needed.

### SmartScreen says the app is from an "unknown publisher"

The build is not code-signed, so Windows shows the standard "Windows protected
your PC" warning. Choose **More info** -> **Run anyway**. This appears once
per file. For a signed release the publisher name would be shown instead.

### The installer says the source folder does not exist

The publish output is missing. Run `build.ps1` first and confirm that
`dist\publish\HolyBeat.LiveGuard.exe` exists. If you moved the repository,
point the installer at the right folder with `-SourcePath`.

### The device or port is not available

This is expected in two situations:

- **System-wide mode without a virtual cable.** The app can only re-route
  audio it can capture. Install a virtual audio cable yourself (for example
  VB-CABLE, which you download and install from its vendor yourself) and
  select its playback endpoint as the input.
- **The device was unplugged or the endpoint changed.** Reopen the device
  selector and pick the endpoint again.

The app never downloads or installs a driver on your behalf.

### There is no sound

- Check the **output device selector** in the app - it must point at the
  device you are actually listening on.
- In **Player** mode the built-in player is the only processed source; other
  applications are not routed through it.
- Make sure **Bypass** is off.
- If the output device is muted at the Windows level, the app stays silent
  too - check the Windows volume mixer.

### Player mode works but system-wide mode does not

System-wide mode needs a loopback path. Install a virtual audio cable
yourself, set it as the input device, and set your real speakers or headphones
as the output device. See the [User Guide](USER-GUIDE.md#3-the-three-routing-modes).

### The app starts but the window is not visible

The app can minimize to the tray. Look for the tray icon, or end
`HolyBeat.LiveGuard.exe` in Task Manager and start it again.

### A build fails with a locked file

Close a running copy of the app before rebuilding; the publish folder is
deleted and recreated on every run.
