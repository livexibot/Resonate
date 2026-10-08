<#
.SYNOPSIS
  Pulls a branch (main by default) and rebuilds the local copy of Resonate
  that runs beside the installed one on the owner's PC.

.DESCRIPTION
  The local build goes to artifacts\local\app in this repository and keeps
  everything it stores (settings, caches, plugins, the WebView2 folder and
  its own Spotify sign-in in the Credential Manager) in
  %LocalAppData%\Resonate-local, through the app's "--data" switch. It never
  reads or writes the installed copy's files, except that the first run
  copies the Spotify Client ID from the installed copy's settings so the
  sign-in page is filled in.

  Two shortcuts on the desktop start it: "Resonate (local)" with the real
  Spotify account, and "Resonate (local demo)" with made-up music.

  Needs the .NET 10 SDK, and Visual Studio Build Tools with the C++ tools and
  the Windows SDK (Native AOT links with them). Plugins are not packed, so
  the local build's Settings says it has none.

.PARAMETER Branch
  The branch to build. Default: main.

.PARAMETER NoPull
  Build what is checked out now, uncommitted changes included, without
  switching branch or pulling.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\update-local.ps1
  powershell -ExecutionPolicy Bypass -File tools\update-local.ps1 -Branch claude/some-change
#>
[CmdletBinding()]
param(
    [string]$Branch = 'main',
    [switch]$NoPull
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repo 'artifacts\local\app'
$newDir = Join-Path $repo 'artifacts\local\app-new'
$dataDir = Join-Path $env:LOCALAPPDATA 'Resonate-local'

function Invoke-Native([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

Push-Location $repo
try {
    if (-not $NoPull) {
        if (git status --porcelain --untracked-files=no) {
            throw 'This folder has uncommitted changes. Commit or stash them, or run with -NoPull to build them as they are.'
        }
        Invoke-Native 'git fetch' { git fetch origin $Branch }
        Invoke-Native 'git switch' { git switch $Branch }
        Invoke-Native 'git pull' { git pull --ff-only origin $Branch }
    }
    $commit = git log -1 --format='%h %s'

    # Without "--data" the local build would use the installed copy's settings and sign-in.
    if (-not (Select-String -Path src\Resonate.App\Program.cs -SimpleMatch '"--data"' -Quiet)) {
        throw "This code has no --data switch, so its build would share the installed copy's settings and sign-in. Build a branch that has it."
    }
    Write-Host "Building $commit"

    # Publish beside the current build, so a failed build leaves the old one usable.
    if (Test-Path $newDir) { Remove-Item -Recurse -Force $newDir }
    Invoke-Native 'Publishing Resonate' {
        dotnet publish src\Resonate.App\Resonate.App.csproj -c Release -r win-x64 -p:Platform=x64 -o $newDir -nologo
    }

    # Close the local build (never the installed copy) so its folder can be replaced.
    $running = Get-Process | Where-Object {
        $_.Path -and ($_.Path.StartsWith($appDir, [StringComparison]::OrdinalIgnoreCase) -or
                      $_.Path.StartsWith($dataDir, [StringComparison]::OrdinalIgnoreCase))
    }
    foreach ($process in $running) {
        Write-Host "Closing the running local build ($($process.ProcessName), $($process.Id))"
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }

    if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
    Move-Item $newDir $appDir

    # First run: start with the installed copy's Spotify Client ID and nothing else.
    $settings = Join-Path $dataDir 'settings.json'
    $installedSettings = Join-Path $env:APPDATA 'Resonate\settings.json'
    if (-not (Test-Path $settings) -and (Test-Path $installedSettings)) {
        $clientId = (Get-Content $installedSettings -Raw | ConvertFrom-Json).clientId
        if ($clientId) {
            New-Item -ItemType Directory -Force $dataDir | Out-Null
            $json = @{ clientId = $clientId } | ConvertTo-Json
            [IO.File]::WriteAllText($settings, $json, (New-Object Text.UTF8Encoding($false)))
            Write-Host "Copied the Spotify Client ID into $dataDir"
        }
    }

    $exe = Join-Path $appDir 'Resonate.exe'
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    foreach ($shortcut in @(
            @{ Name = 'Resonate (local)'; Arguments = "--data `"$dataDir`"" },
            @{ Name = 'Resonate (local demo)'; Arguments = "--demo --data `"$dataDir`"" })) {
        $link = $shell.CreateShortcut((Join-Path $desktop "$($shortcut.Name).lnk"))
        $link.TargetPath = $exe
        $link.Arguments = $shortcut.Arguments
        $link.WorkingDirectory = $appDir
        $link.Description = "Local build of Resonate: $commit"
        $link.Save()
    }

    Write-Host "Done: $commit"
    Write-Host "Start it with the 'Resonate (local)' shortcut on the desktop, or:"
    Write-Host "  & `"$exe`" --data `"$dataDir`""
}
finally {
    Pop-Location
}
