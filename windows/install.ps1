# Installs (or updates) the latest AI Usage release for Windows and starts it:
#   irm https://raw.githubusercontent.com/OWNER/REPO/main/windows/install.ps1 | iex
# Uninstall:
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/OWNER/REPO/main/windows/install.ps1))) -Uninstall
# Files downloaded from PowerShell aren't flagged by SmartScreen, so the unsigned .exe starts without a warning.
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes large downloads very slow in Windows PowerShell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = if ($env:AIUSAGE_REPO) { $env:AIUSAGE_REPO } else { 'OWNER/REPO' }
$dir = Join-Path $env:LOCALAPPDATA 'Programs\AIUsage'
$exe = Join-Path $dir 'AIUsage.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Usage.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process AIUsage -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $runKey -Name 'AIUsage' -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:APPDATA 'AIUsage') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'AI Usage has been removed.'
    return
}

New-Item -ItemType Directory -Force -Path $dir | Out-Null
$url = "https://github.com/$repo/releases/latest/download/AI-Usage-Windows-x64.exe"
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.Description = 'Claude and Codex usage in the system tray'
$link.Save()

Start-Process $exe
Write-Host "Installed to $dir. Look for two small bars in the system tray (click ^ next to the clock if it's hidden)."
