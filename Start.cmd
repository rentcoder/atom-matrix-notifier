@echo off
powershell.exe -NoProfile -Command "$p = Get-AppxPackage -Name 'Local.AtomNotifier'; if ($p) { Start-Process explorer.exe ('shell:AppsFolder\' + $p.PackageFamilyName + '!App') } else { Write-Host 'Run Install.cmd first.'; Read-Host 'Press Enter' }"
