<#
.SYNOPSIS
    Builds, tests and packages HolyBeat Live Guard for Windows (x64 / arm64).

.DESCRIPTION
    Runs the full pipeline:
      1. dotnet restore   (app + test projects)
      2. dotnet build     (application, configuration-dependent)
      3. dotnet test      (skipped with -SkipTests)
      4. dotnet publish   (self-contained, win-x64 / win-arm64)
      5. Compress-Archive -> dist\HolyBeatLiveGuard-<runtime>.zip

    The script is safe to re-run: the publish folder and the package are
    recreated from scratch on every invocation.

    The two .csproj files are used as build inputs instead of HolyBeat.slnx so
    that the build stays deterministic no matter how the solution file is
    populated.

.PARAMETER Configuration
    Build configuration: Release (default) or Debug.

.PARAMETER Runtime
    Target runtime identifier: win-x64 (default) or win-arm64.

.PARAMETER SkipTests
    Skip the 'dotnet test' stage.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug -Runtime win-arm64 -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppProject = Join-Path $RepoRoot 'src\HolyBeat.LiveGuard.App\HolyBeat.LiveGuard.App.csproj'
$TestProject = Join-Path $RepoRoot 'tests\HolyBeat.LiveGuard.Tests\HolyBeat.LiveGuard.Tests.csproj'
$DistDir = Join-Path $RepoRoot 'dist'
$PublishDir = Join-Path $DistDir 'publish'
$ZipPath = Join-Path $DistDir ('HolyBeatLiveGuard-' + $Runtime + '.zip')

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

function Invoke-Dotnet {
    param([string[]] $Arguments)

    Write-Host ('  > dotnet ' + ($Arguments -join ' '))
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ('dotnet ' + ($Arguments -join ' ') + ' failed with exit code ' + $LASTEXITCODE + '.')
    }
    Write-Host ''
}

try {
    Write-Stage ('HolyBeat Live Guard build  -  ' + $Configuration + ' / ' + $Runtime)
    Write-Note ('Repository: ' + $RepoRoot)

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK (dotnet) was not found on PATH. Install the .NET 10 SDK and reopen the terminal.'
    }

    if (-not (Test-Path -LiteralPath $AppProject -PathType Leaf)) {
        throw ('Application project not found: ' + $AppProject)
    }

    # ---------------------------------------------------------------- stage 1
    Write-Stage 'Stage 1/5  Restore'
    Invoke-Dotnet -Arguments @('restore', $AppProject)
    if (Test-Path -LiteralPath $TestProject -PathType Leaf) {
        Invoke-Dotnet -Arguments @('restore', $TestProject)
    } else {
        Write-Note 'Test project not found, restore skipped for tests.'
    }

    # ---------------------------------------------------------------- stage 2
    Write-Stage ('Stage 2/5  Build (' + $Configuration + ')')
    Invoke-Dotnet -Arguments @('build', $AppProject, '-c', $Configuration)

    # ---------------------------------------------------------------- stage 3
    Write-Stage 'Stage 3/5  Test'
    if ($SkipTests) {
        Write-Note 'Skipped (-SkipTests).'
    } elseif (-not (Test-Path -LiteralPath $TestProject -PathType Leaf)) {
        Write-Note 'Test project not found, tests skipped.'
    } else {
        Invoke-Dotnet -Arguments @('test', $TestProject, '-c', $Configuration)
    }

    # ---------------------------------------------------------------- stage 4
    Write-Stage ('Stage 4/5  Publish (' + $Runtime + ', self-contained)')
    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    Invoke-Dotnet -Arguments @(
        'publish', $AppProject,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishTrimmed=false',
        '-p:PublishSingleFile=false',
        '-o', $PublishDir
    )

    $publishedExe = Join-Path $PublishDir 'HolyBeat.LiveGuard.exe'
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw ('Publish finished but ' + $publishedExe + ' is missing. The build output looks incomplete.')
    }

    # ---------------------------------------------------------------- stage 5
    Write-Stage 'Stage 5/5  Package'
    if (-not (Test-Path -LiteralPath $DistDir)) {
        New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
    }
    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -Force

    $zipInfo = Get-Item -LiteralPath $ZipPath

    Write-Host ''
    Write-Host '  Build succeeded.'
    Write-Host ('  Package   : ' + $zipInfo.FullName)
    Write-Host ('  Size      : ' + [math]::Round($zipInfo.Length / 1MB, 1) + ' MB')
    Write-Host ('  Published : ' + $PublishDir)
    Write-Host ''
    Write-Host '  Next: powershell -ExecutionPolicy Bypass -File .\install\install.ps1'
    Write-Host ''
} catch {
    Write-Host ''
    Write-Host ('  BUILD FAILED: ' + $_.Exception.Message)
    Write-Host ''
    throw
}
