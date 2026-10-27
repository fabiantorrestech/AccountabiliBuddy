<#
.SYNOPSIS
Rebuilds AccountabiliBuddy and packages it as an installer and a portable zip with Velopack (ADR 10).

.DESCRIPTION
1. Picks the version: -Version if given, otherwise the newest package in releases\ with the patch number + 1
   (Velopack needs a higher version for every update), otherwise the App project's <Version>.
2. Publishes the app self-contained for win-x64 into publish\, and the browser native host too, so neither needs
   .NET installed. The sealed relay (ADR 46) goes into publish\relay\ the same way: the "Run a relay on this PC"
   switch starts it from there.
3. Packs publish\ with vpk into releases\: AccountabiliBuddyApp-win-Setup.exe (installer), a portable zip and the
   update packages.
4. Writes the version into the App project, so development builds show the last packaged version.
5. With -Publish: uploads the installer and update packages as a GitHub release of the public releases-only repo
   (ADR 12), where installed apps find updates. Uses the GitHub CLI's sign-in (gh auth login).

Running the new Setup.exe installs over the previous version. The user's data lives in
%LocalAppData%\AccountabiliBuddy, outside the install folder, and is kept (and copied to its backups folder before
the new version first runs).

Needs the vpk tool once: dotnet tool install -g vpk --version 1.2.161

.EXAMPLE
pwsh scripts/package.ps1
pwsh scripts/package.ps1 -Version 0.2.0
pwsh scripts/package.ps1 -Publish
#>
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'src\AccountabiliBuddy.App\AccountabiliBuddy.App.csproj'
$hostProject = Join-Path $root 'src\AccountabiliBuddy.NativeHost\AccountabiliBuddy.NativeHost.csproj'
$relayProject = Join-Path $root 'src\AccountabiliBuddy.Relay\AccountabiliBuddy.Relay.csproj'
$icon = Join-Path $root 'src\AccountabiliBuddy.App\Assets\app-icon.ico'
$publishFolder = Join-Path $root 'publish'
$releases = Join-Path $root 'releases'
$packId = 'AccountabiliBuddyApp'   # Not "AccountabiliBuddy": that is the data folder's name (ADR 10).
$releasesRepo = 'https://github.com/fabiantorrestech/AccountabiliBuddy-releases'   # Public, release files only (ADR 12).

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

if (Test-Path $publishFolder) { Remove-Item -Recurse -Force $publishFolder }

Invoke-Step 'Publish app' {
    dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o $publishFolder --nologo -v q
}
Invoke-Step 'Publish browser native host' {
    dotnet publish $hostProject -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o $publishFolder --nologo -v q
}
Invoke-Step 'Publish relay' {
    # Its own folder and one compressed file (about 48 MB instead of 105 MB in 334 files): it brings the ASP.NET Core runtime.
    dotnet publish $relayProject -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:Version=$Version -p:DebugType=None -o (Join-Path $publishFolder 'relay') --nologo -v q
}
Invoke-Step 'Pack installer' {
    vpk pack --packId $packId --packVersion $Version --packDir $publishFolder --mainExe 'AccountabiliBuddy.App.exe' --runtime win-x64 `
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
if ($Publish) {
    $token = (gh auth token)
    if (-not $token) { throw 'Not signed in to GitHub: run gh auth login first.' }
    Invoke-Step 'Publish to GitHub releases' {
        vpk upload github --outputDir $releases --repoUrl $releasesRepo --token $token --publish `
            --releaseName "AccountabiliBuddy $Version" --tag "v$Version"
    }
    Write-Host "Published: $releasesRepo/releases/tag/v$Version" -ForegroundColor Green
}

Write-Host ''
Write-Host "Install or update: run $(Join-Path $releases "$packId-win-Setup.exe")"
