$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'TruckHub'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TruckHub.lnk'

if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }
if (Test-Path $installRoot) { Remove-Item $installRoot -Recurse -Force }

Write-Host 'TruckHub removido.'
