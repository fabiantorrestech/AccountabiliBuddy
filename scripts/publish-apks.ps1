<#
.SYNOPSIS
    Attaches the phone and watch APKs to a release in the public releases repo (ADR 12).

.DESCRIPTION
    Copies android/app and android/wear release APKs to releases/ as AccountabiliBuddy-phone-<version>.apk and
    AccountabiliBuddy-watch-<version>.apk (version from android/app/build.gradle.kts) and uploads them to the given
    release tag, by default the latest one. The APKs go on a PC release rather than one of their own so the
    desktop app's update feed (releases.win.json) stays on every release. Older APKs on that release are removed.
    Build them first: cd android; ./gradlew assembleRelease. Needs gh auth login.

.EXAMPLE
    pwsh scripts/publish-apks.ps1                # the latest release
    pwsh scripts/publish-apks.ps1 -Tag v0.1.104
#>
param(
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$repo = 'fabiantorrestech/AccountabiliBuddy-releases'
$root = Split-Path $PSScriptRoot -Parent

$gradle = Get-Content (Join-Path $root 'android/app/build.gradle.kts') -Raw
if ($gradle -notmatch 'versionName\s*=\s*"([^"]+)"') { throw 'No versionName in android/app/build.gradle.kts.' }
$version = $Matches[1]

if (-not $Tag) {
    $Tag = gh release list --repo $repo --limit 1 --json tagName --jq '.[0].tagName'
    if (-not $Tag) { throw "No release found in $repo." }
}

$phone = Join-Path $root 'android/app/build/outputs/apk/release/app-release.apk'
$watch = Join-Path $root 'android/wear/build/outputs/apk/release/wear-release.apk'
foreach ($apk in $phone, $watch) {
    if (-not (Test-Path $apk)) { throw "Missing $apk (run ./gradlew assembleRelease in android/)." }
}

$outDir = Join-Path $root 'releases'
New-Item -ItemType Directory -Force $outDir | Out-Null
$phoneOut = Join-Path $outDir "AccountabiliBuddy-phone-$version.apk"
$watchOut = Join-Path $outDir "AccountabiliBuddy-watch-$version.apk"
Copy-Item $phone $phoneOut -Force
Copy-Item $watch $watchOut -Force

# One phone and one watch APK per release: drop older ones before uploading.
$assets = gh release view $Tag --repo $repo --json assets --jq '.assets[].name'
foreach ($name in $assets) {
    if ($name -match '^AccountabiliBuddy-(phone|watch)-.+\.apk$' -and $name -ne (Split-Path $phoneOut -Leaf) -and $name -ne (Split-Path $watchOut -Leaf)) {
        gh release delete-asset $Tag $name --repo $repo --yes
    }
}

gh release upload $Tag $phoneOut $watchOut --repo $repo --clobber
Write-Output "Uploaded phone and watch $version to $Tag."
