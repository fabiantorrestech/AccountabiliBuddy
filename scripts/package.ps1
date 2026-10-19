<#
.SYNOPSIS
Rebuilds AccountabiliBuddy and packages it as an installer and a portable zip with Velopack (ADR 10).

.DESCRIPTION
1. Picks the version: -Version if given, otherwise the newest package in releases\ with the patch number + 1
   (Velopack needs a higher version for every update), otherwise the App project's <Version>.
2. Publishes the app self-contained for win-x64 into publish\, and the browser native host too, so neither needs
   .NET installed.
3. Packs publish\ with vpk into releases\: AccountabiliBuddyApp-win-Setup.exe (installer), a portable zip and the
   update packages.
4. Writes the version into the App project, so development builds show the last packaged version.

Running the new Setup.exe installs over the previous version. The user's data lives in
%LocalAppData%\AccountabiliBuddy, outside the install folder, and is kept (and copied to its backups folder before
the new version first runs).

Needs the vpk tool once: dotnet tool install -g vpk --version 1.2.161

.EXAMPLE
pwsh scripts/package.ps1
pwsh scripts/package.ps1 -Version 0.2.0
#>
param(
    [string]$Version,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'src\AccountabiliBuddy.App\AccountabiliBuddy.App.csproj'
$hostProject = Join-Path $root 'src\AccountabiliBuddy.NativeHost\AccountabiliBuddy.NativeHost.csproj'
$icon = Join-Path $root 'src\AccountabiliBuddy.App\Assets\app-icon.ico'
$publish = Join-Path $root 'publish'
$releases = Join-Path $root 'releases'
$packId = 'AccountabiliBuddyApp'   # Not "AccountabiliBuddy": that is the data folder's name (ADR 10).

function Invoke-Step([string]$name, [scriptblock]$command) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)." }
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk was not found. Install it once with: dotnet tool install -g vpk --version 1.2.161'
}

if (-not $Version) {
    $newest = Get-ChildItem $releases -Filter "$packId-*-full.nupkg" -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Name -match "^$([regex]::Escape($packId))-(\d+\.\d+\.\d+)-full\.nupkg$") { [version]$Matches[1] } } |
        Sort-Object | Select-Object -Last 1
    if ($newest) {
        $Version = "$($newest.Major).$($newest.Minor).$($newest.Build + 1)"
    }
    else {
        $Version = ([xml](Get-Content $appProject -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must look like 1.2.3." }
Write-Host "Packaging AccountabiliBuddy $Version" -ForegroundColor Green

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

Invoke-Step 'Publish app' {
    dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o $publish --nologo -v q
}
Invoke-Step 'Publish browser native host' {
    dotnet publish $hostProject -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o $publish --nologo -v q
}
Invoke-Step 'Pack installer' {
    vpk pack --packId $packId --packVersion $Version --packDir $publish --mainExe 'AccountabiliBuddy.App.exe' --runtime win-x64 `
        --packTitle 'AccountabiliBuddy' --packAuthors 'AccountabiliBuddy' --icon $icon --outputDir $releases
}

# Keep the project's version in step with the newest package.
$projectText = Get-Content $appProject -Raw
$updated = [regex]::Replace($projectText, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
if ($updated -ne $projectText) { Set-Content $appProject $updated -NoNewline }

Write-Host ''
Write-Host "Done: AccountabiliBuddy $Version" -ForegroundColor Green
Get-ChildItem $releases -File | Where-Object { $_.Name -like "*$Version*" -or $_.Name -like '*Setup.exe' -or $_.Name -like '*Portable.zip' } |
    ForEach-Object { '  {0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
Write-Host ''
Write-Host "Install or update: run $(Join-Path $releases "$packId-win-Setup.exe")"
