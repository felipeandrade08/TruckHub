$ErrorActionPreference = 'Stop'

$source = Split-Path -Parent $PSScriptRoot
$installRoot = Join-Path $env:LOCALAPPDATA 'TruckHub'

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

$files = @('TruckHub.exe','TruckHubConnector.exe','TruckHubUpdater.exe')
foreach ($file in $files) {
    $path = Join-Path $source $file
    if (Test-Path $path) {
        Copy-Item $path (Join-Path $installRoot $file) -Force
    }
}

# Mantem o instalador PowerShell auxiliar com o mesmo comportamento do Setup oficial.
$pluginSource = Join-Path $source 'ETS2Plugin\TruckHub.VehicleControl.dll'
$autoStartPluginSource = Join-Path $source 'ETS2Plugin\TruckHub.AutoStart.dll'
$pluginInstaller = Join-Path $PSScriptRoot 'install-ets2-plugin.ps1'
if ((Test-Path $pluginSource) -and (Test-Path $pluginInstaller)) {
    & $pluginInstaller -PluginSource $pluginSource -AutoStartPluginSource $autoStartPluginSource
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TruckHub.lnk'
$target = Join-Path $installRoot 'TruckHub.exe'
if (Test-Path $target) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.Description = 'TruckHub - seu computador de bordo para ETS2'
    $shortcut.Save()
}

Write-Host "TruckHub instalado em: $installRoot"
