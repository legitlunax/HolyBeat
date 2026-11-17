<#
.SYNOPSIS
    Removes a per-user installation of HolyBeat Live Guard.

.DESCRIPTION
    Stops a running HolyBeat Live Guard process, removes the Start Menu and
    Desktop shortcuts, removes the per-user auto-start entry and deletes the
    contents of the install folder. The configuration file is only deleted when
    -PurgeConfig is passed.

    The script never deletes anything outside the resolved -InstallDir folder,
    and it refuses to run against a drive root, a top-level folder or a known
    system folder.

.PARAMETER InstallDir
    Folder to remove. Defaults to %LOCALAPPDATA%\Programs\HolyBeatLiveGuard.

.PARAMETER PurgeConfig
    Also delete %APPDATA%\HolyBeat\config.json.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install\uninstall.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install\uninstall.ps1 -PurgeConfig
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\HolyBeatLiveGuard'),

    [switch] $PurgeConfig
)

$ErrorActionPreference = 'Stop'

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

function Assert-SafeInstallDir {
    param([string] $Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($full)

    if ($full.TrimEnd('\') -eq $root.TrimEnd('\')) {
        throw ('Refusing to work on a drive root: ' + $full)
    }

    $segments = ($full.TrimEnd('\') -split '\\')
    if ($segments.Count -lt 2) {
        throw ('Refusing to work on a top-level folder: ' + $full)
    }

    $blocked = @(
        'Windows',
        'System32',
        'Program Files',
        'Program Files (x86)',
        'ProgramData',
        'Users',
        'Program',
        'Programs',
        '$Recycle.Bin',
        'System Volume Information'
    )

    $leaf = Split-Path -Path $full -Leaf
    if ($blocked -contains $leaf) {
        throw ('Refusing to work on the protected folder "' + $leaf + '".')
    }

    return $full
}

try {
    Write-Stage 'HolyBeat Live Guard uninstaller'
    Write-Note ('Install dir : ' + $InstallDir)

    $safeInstallDir = Assert-SafeInstallDir -Path $InstallDir

    # -------------------------------------------------------------- process
    Write-Stage 'Stopping the running application'
    $processNames = @('HolyBeatLiveGuard', 'HolyBeat.LiveGuard')
    $stopped = 0
    foreach ($name in $processNames) {
        $running = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        foreach ($process in $running) {
            try {
                Stop-Process -Id $process.Id -Force -ErrorAction Stop
                Write-Note ('Stopped process ' + $process.ProcessName + ' (PID ' + $process.Id + ').')
                $stopped = $stopped + 1
            } catch {
                Write-Note ('Could not stop PID ' + $process.Id + ': ' + $_.Exception.Message)
            }
        }
    }
    if ($stopped -eq 0) {
        Write-Note 'No running instance found.'
    } else {
        Start-Sleep -Milliseconds 500
    }

    # ------------------------------------------------------------ shortcuts
    Write-Stage 'Removing shortcuts'
    $shortcutPaths = @(
        (Join-Path $StartMenuDir $ShortcutName),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) $ShortcutName)
    )
    foreach ($link in $shortcutPaths) {
        if ([string]::IsNullOrEmpty($link)) { continue }
        if (Test-Path -LiteralPath $link) {
            try {
                Remove-Item -LiteralPath $link -Force
                Write-Note ('Removed ' + $link)
            } catch {
                Write-Note ('Could not remove ' + $link + ': ' + $_.Exception.Message)
            }
        } else {
            Write-Note ('Not present: ' + $link)
        }
    }

    # ------------------------------------------------------------ auto-start
    Write-Stage 'Removing auto-start'
    if (Test-Path -LiteralPath $RunKeyPath) {
        $existing = Get-ItemProperty -Path $RunKeyPath -Name $RunValueName -ErrorAction SilentlyContinue
        if ($null -ne $existing) {
            try {
                Remove-ItemProperty -Path $RunKeyPath -Name $RunValueName -Force
                Write-Note ('Removed "' + $RunValueName + '" from ' + $RunKeyPath)
            } catch {
                Write-Note ('Could not remove the auto-start entry: ' + $_.Exception.Message)
            }
        } else {
            Write-Note 'No auto-start entry was present.'
        }
    } else {
        Write-Note 'No auto-start key was present.'
    }

    # ---------------------------------------------------------- install dir
    Write-Stage 'Removing the installation folder'
    if (Test-Path -LiteralPath $safeInstallDir) {
        Get-ChildItem -LiteralPath $safeInstallDir -Force | ForEach-Object {
            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            } catch {
                Write-Note ('Could not remove ' + $_.FullName + ': ' + $_.Exception.Message)
            }
        }
        try {
            Remove-Item -LiteralPath $safeInstallDir -Force -ErrorAction Stop
            Write-Note ('Removed ' + $safeInstallDir)
        } catch {
            Write-Note ('The folder ' + $safeInstallDir + ' was emptied but could not be deleted: ' + $_.Exception.Message)
        }
    } else {
        Write-Note ('Nothing to remove at ' + $safeInstallDir)
    }

    # --------------------------------------------------------------- config
    Write-Stage 'Configuration'
    if ($PurgeConfig) {
        if (Test-Path -LiteralPath $ConfigPath) {
            try {
                Remove-Item -LiteralPath $ConfigPath -Force
                Write-Note ('Removed ' + $ConfigPath)
            } catch {
                Write-Note ('Could not remove ' + $ConfigPath + ': ' + $_.Exception.Message)
            }
        } else {
            Write-Note ('Nothing to remove at ' + $ConfigPath)
        }
        $configDir = Split-Path -Parent $ConfigPath
        if (Test-Path -LiteralPath $configDir) {
            $remaining = @(Get-ChildItem -LiteralPath $configDir -Force)
            if ($remaining.Count -eq 0) {
                try {
                    Remove-Item -LiteralPath $configDir -Force
                    Write-Note ('Removed the empty folder ' + $configDir)
                } catch {
                    Write-Note ('Could not remove ' + $configDir + ': ' + $_.Exception.Message)
                }
            }
        }
    } else {
        Write-Note ('Kept the configuration file (' + $ConfigPath + '). Use -PurgeConfig to delete it.')
    }

    Write-Stage 'Uninstall complete'
    Write-Host '  Removed: running process, shortcuts, auto-start entry and the install folder.'
    Write-Host '  Your music, playlists and system audio settings were not touched.'
    Write-Host ''
} catch {
    Write-Host ''
    Write-Host ('  UNINSTALL FAILED: ' + $_.Exception.Message)
    Write-Host ''
    throw
}
