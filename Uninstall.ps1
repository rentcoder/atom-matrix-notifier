$ErrorActionPreference = 'Stop'
Get-AppxPackage -Name 'Local.AtomNotifier' | Remove-AppxPackage
Write-Host 'ATOM Notifier registration removed. Source files remain; Windows may delete app data.'
