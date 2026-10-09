# Run as the current user in Windows PowerShell (not administrator).
$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot 'app/AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'app folder missing. Build with Build.ps1 first.' }
if (Get-Process -Name 'AtomNotifier' -ErrorAction SilentlyContinue) {
    throw 'ATOM Notifier is running. Use its tray icon menu > Exit, then run Install.cmd again.'
}
Write-Host 'This registers the local ATOM Notifier app for this Windows user.'
Write-Host 'Windows Developer Mode is required for unsigned local app registration.'
try {
    Add-AppxPackage -Register $manifest -ErrorAction Stop
    Write-Host 'Installed. Open ATOM Matrix notifier from the Start menu.'
} catch {
    Write-Host 'Registration failed. Read the Windows error below. For 0x80073CFF, enable Developer Mode and retry.'
    Write-Host 'No security setting was changed by this script.'
    throw
}
