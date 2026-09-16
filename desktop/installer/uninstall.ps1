$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'TransPoli'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TransPoli.lnk'

if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }
if (Test-Path $installRoot) { Remove-Item $installRoot -Recurse -Force }

Write-Host 'TransPoli removido.'
