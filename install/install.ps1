<#
.SYNOPSIS
    Installs HolyBeat Live Guard for the current user (no admin rights required).

.DESCRIPTION
    Copies the published payload into the per-user install folder, creates the
    Start Menu and Desktop shortcuts and (optionally) registers the per-user
    auto-start entry.

    The script is idempotent: running it twice leaves the same result, and
    running it without -EnableAutoStart removes a previously written auto-start
    entry.

    No driver is installed and nothing is downloaded: the application works in
    'Player' mode out of the box. System-wide mode needs a virtual audio cable
    that the user installs separately.

.PARAMETER SourcePath
    Folder holding the published output (must contain HolyBeat.LiveGuard.exe).

.PARAMETER InstallDir
    Target install folder. Defaults to %LOCALAPPDATA%\Programs\HolyBeatLiveGuard.

.PARAMETER EnableAutoStart
    Register the app under HKCU:\Software\Microsoft\Windows\CurrentVersion\Run
    so it starts with Windows.

.PARAMETER SkipShortcuts
    Do not create the Start Menu and Desktop shortcuts.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install\install.ps1 -EnableAutoStart
#>
[CmdletBinding()]
param(
    [string] $SourcePath = 'E:\HolyBeat\dist\publish',

    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\HolyBeatLiveGuard'),

    [switch] $EnableAutoStart,

    [switch] $SkipShortcuts
)

$ErrorActionPreference = 'Stop'

$AppExeName = 'HolyBeat.LiveGuard.exe'
$ShortcutName = 'HolyBeat Live Guard.lnk'
$RunKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValueName = 'HolyBeatLiveGuard'
$ConfigPath = Join-Path $env:APPDATA 'HolyBeat\config.json'
$StartMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'

function Write-Stage {
    param([string] $Title)
    Write-Host ''
    Write-Host ('=' * 72)
    Write-Host ('  ' + $Title)
    Write-Host ('=' * 72)
}

function Write-Note {
    param([string] $Text)
    Write-Host ('  . ' + $Text)
}

function Copy-Tree {
    param([string] $From, [string] $To)

    if (-not (Test-Path -LiteralPath $To)) {
        New-Item -ItemType Directory -Path $To -Force | Out-Null
    }

    Get-ChildItem -LiteralPath $From -Force | ForEach-Object {
        $destination = Join-Path $To $_.Name
        if ($_.PSIsContainer) {
            Copy-Tree -From $_.FullName -To $destination
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
    }
}

function New-Shortcut {
    param(
        [string] $Path,
        [string] $Target,
        [string] $WorkingDirectory,
        [string] $Description
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($Path)
        $shortcut.TargetPath = $Target
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.Description = $Description
        $shortcut.IconLocation = $Target
        $shortcut.Save()
    } finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)
        }
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

try {
    Write-Stage 'HolyBeat Live Guard installer'
    Write-Note ('Source      : ' + $SourcePath)
    Write-Note ('Install dir : ' + $InstallDir)

    # ------------------------------------------------------------ validation
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
        throw ('The source folder "' + $SourcePath + '" does not exist. Run the build first: powershell -ExecutionPolicy Bypass -File .\build.ps1')
    }

    $sourceExe = Join-Path $SourcePath $AppExeName
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        throw ('"' + $AppExeName + '" was not found in "' + $SourcePath + '". The published output is incomplete - run the build first: powershell -ExecutionPolicy Bypass -File .\build.ps1')
    }

    # ----------------------------------------------------------------- copy
    Write-Stage 'Copying the payload'
    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }
    Copy-Tree -From $SourcePath -To $InstallDir

    $installedExe = Join-Path $InstallDir $AppExeName
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        throw ('Copy finished but "' + $installedExe + '" is missing.')
    }
    Write-Note ('Installed ' + $installedExe)

    # ------------------------------------------------------------- shortcuts
    if ($SkipShortcuts) {
        Write-Stage 'Shortcuts'
        Write-Note 'Skipped (-SkipShortcuts).'
    } else {
        Write-Stage 'Creating shortcuts'
        $startMenuLink = Join-Path $StartMenuDir $ShortcutName
        $desktopDir = [Environment]::GetFolderPath('Desktop')
        $desktopLink = Join-Path $desktopDir $ShortcutName

        New-Shortcut -Path $startMenuLink -Target $installedExe -WorkingDirectory $InstallDir -Description 'HolyBeat Live Guard - real-time acoustic safety processor'
        Write-Note ('Start Menu: ' + $startMenuLink)

        if (-not [string]::IsNullOrEmpty($desktopDir)) {
            New-Shortcut -Path $desktopLink -Target $installedExe -WorkingDirectory $InstallDir -Description 'HolyBeat Live Guard - real-time acoustic safety processor'
            Write-Note ('Desktop   : ' + $desktopLink)
        }
    }

    # ------------------------------------------------------------ auto-start
    Write-Stage 'Auto-start'
    if ($EnableAutoStart) {
        New-ItemProperty -Path $RunKeyPath -Name $RunValueName -Value ('"' + $installedExe + '"') -PropertyType String -Force | Out-Null
        Write-Note ('Enabled  : "' + $installedExe + '" in ' + $RunKeyPath)
    } else {
        $existing = $null
        if (Test-Path -LiteralPath $RunKeyPath) {
            $existing = Get-ItemProperty -Path $RunKeyPath -Name $RunValueName -ErrorAction SilentlyContinue
        }
        if ($null -ne $existing) {
            Remove-ItemProperty -Path $RunKeyPath -Name $RunValueName -Force
            Write-Note ('Removed the existing "' + $RunValueName + '" entry from ' + $RunKeyPath)
        } else {
            Write-Note 'Auto-start was already disabled.'
        }
    }

    # ---------------------------------------------------------------- report
    Write-Stage 'Installation complete'
    Write-Host ('  Executable : ' + $installedExe)
    Write-Host ('  Config     : ' + $ConfigPath + '  (created on first launch)')
    Write-Host ('  Presets    : built in, no extra downloads required')
    Write-Host ''
    Write-Host '  Next steps:'
    Write-Host '    1. Start "HolyBeat Live Guard" from the Start Menu or the Desktop shortcut.'
    Write-Host '    2. Keep the routing mode on "Player" for the first run - it works without any driver.'
    Write-Host '    3. The app writes its settings to the config file above the first time it starts.'
    Write-Host ''
} catch {
    Write-Host ''
    Write-Host ('  INSTALLATION FAILED: ' + $_.Exception.Message)
    Write-Host ''
    throw
}
