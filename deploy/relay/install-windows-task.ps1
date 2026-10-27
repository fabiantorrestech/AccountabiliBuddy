<#
.SYNOPSIS
Installs the sealed relay (ADR 46) as a Task Scheduler task that starts at boot and runs as a service account.

.DESCRIPTION
Run from an elevated PowerShell. Copies the relay exe (the win-x64 build from scripts/publish-relay.ps1) to the install
folder, makes the data folder writable for the service account, registers the task "AccountabiliBuddy Relay" (at
startup, restarts on failure, no time limit) and starts it. The address + pin lines the apps need are written to
relay-address-and-pin.txt in the data folder on every start. -Proxy runs plain HTTP on 127.0.0.1 for a reverse proxy
(see Caddyfile).

.EXAMPLE
pwsh deploy/relay/install-windows-task.ps1 -ExePath .\publish\relay\win-x64\AccountabiliBuddy.Relay.exe -OpenFirewall
pwsh deploy/relay/install-windows-task.ps1 -Remove
#>
param(
    [string]$ExePath,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'AccountabiliBuddyRelay'),
    [string]$DataDir = (Join-Path $env:ProgramData 'AccountabiliBuddyRelay'),
    [int]$Port = 8743,
    [switch]$Proxy,
    [string]$RunAs = 'NT AUTHORITY\LOCAL SERVICE',
    [string]$TaskName = 'AccountabiliBuddy Relay',
    [switch]$OpenFirewall,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an elevated (administrator) PowerShell.'
}

if ($Remove) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName $TaskName -ErrorAction SilentlyContinue
    Write-Host "Removed the task. The install folder ($InstallDir) and the data ($DataDir) were left in place."
    return
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    throw 'Give -ExePath, the path to AccountabiliBuddy.Relay.exe (build it with scripts/publish-relay.ps1).'
}

New-Item -ItemType Directory -Force -Path $InstallDir, $DataDir | Out-Null
$exe = Join-Path $InstallDir 'AccountabiliBuddy.Relay.exe'
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Copy-Item $ExePath $exe -Force

# The service account reads the program and writes the data (the stored files and the certificate).
icacls $InstallDir /grant "${RunAs}:(OI)(CI)RX" | Out-Null
icacls $DataDir /grant "${RunAs}:(OI)(CI)M" | Out-Null

$arguments = "--data `"$DataDir`""
if ($Proxy) { $arguments += " --proxy --urls http://127.0.0.1:$Port" } else { $arguments += " --urls https://0.0.0.0:$Port" }

$action = New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtStartup
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $RunAs -LogonType ServiceAccount -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null

if ($OpenFirewall -and -not $Proxy) {
    Remove-NetFirewallRule -DisplayName $TaskName -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName $TaskName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Program $exe | Out-Null
    Write-Host "Opened TCP $Port in the Windows firewall for the relay."
}

Start-ScheduledTask -TaskName $TaskName
Write-Host "Installed and started '$TaskName' as $RunAs. Program: $exe  Data: $DataDir  Arguments: $arguments"
if (-not $Proxy) {
    Write-Host "The address + pin lines for the apps are written to $(Join-Path $DataDir 'relay-address-and-pin.txt') once the relay has started."
}
