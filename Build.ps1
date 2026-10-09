param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'windows/AtomNotifier.csproj'
$output = Join-Path $PSScriptRoot 'app'
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed. Install .NET 8 SDK and retry.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows/AppxManifest.xml') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows/Assets') -Destination $output -Recurse -Force
Write-Host 'Build complete. Run Install.ps1, then launch from Start menu.'
