<#
.SYNOPSIS
Builds the sealed relay (ADR 46) as self-contained single-file programs for win-x64, linux-x64 and linux-arm64.

.DESCRIPTION
Each build lands in publish\relay\<rid>\ (AccountabiliBuddy.Relay.exe on Windows, AccountabiliBuddy.Relay on Linux).
Nothing needs to be installed on the machine that runs it. Deployment files (systemd unit, Task Scheduler script,
Docker, Caddy) are in deploy\relay\.

.EXAMPLE
pwsh scripts/publish-relay.ps1
pwsh scripts/publish-relay.ps1 -Rids linux-x64 -Version 0.2.0
#>
param(
    [string[]]$Rids = @('win-x64', 'linux-x64', 'linux-arm64'),
    [string]$Configuration = 'Release',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\AccountabiliBuddy.Relay\AccountabiliBuddy.Relay.csproj'
$output = Join-Path $root 'publish\relay'

foreach ($rid in $Rids) {
    $folder = Join-Path $output $rid
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
    Write-Host "==> $rid" -ForegroundColor Cyan

    $arguments = @(
        'publish', $project, '-c', $Configuration, '-r', $rid, '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None',
        '-o', $folder, '--nologo', '-v', 'q'
    )
    if ($Version) { $arguments += "-p:Version=$Version" }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publishing $rid failed (exit code $LASTEXITCODE)." }
}

Write-Host ''
Get-ChildItem $output -Recurse -File | Where-Object { $_.Name -like 'AccountabiliBuddy.Relay*' -and $_.Extension -ne '.pdb' } |
    ForEach-Object { '  {0,-60} {1,8:N1} MB' -f $_.FullName.Substring($root.Length + 1), ($_.Length / 1MB) }
