$ErrorActionPreference = 'Stop'

$source = Split-Path -Parent $PSScriptRoot
$installRoot = Join-Path $env:LOCALAPPDATA 'TransPoli'

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

$files = @('TransPoli.exe','TransPoliConnector.exe','TransPoliUpdater.exe')
foreach ($file in $files) {
    $path = Join-Path $source $file
    if (Test-Path $path) {
        Copy-Item $path (Join-Path $installRoot $file) -Force
    }
}

# Mantem o instalador PowerShell auxiliar com o mesmo comportamento do Setup oficial.
$pluginSource = Join-Path $source 'ETS2Plugin\TransPoli.VehicleControl.dll'
$autoStartPluginSource = Join-Path $source 'ETS2Plugin\TransPoli.AutoStart.dll'
$pluginInstaller = Join-Path $PSScriptRoot 'install-ets2-plugin.ps1'
if ((Test-Path $pluginSource) -and (Test-Path $pluginInstaller)) {
    & $pluginInstaller -PluginSource $pluginSource -AutoStartPluginSource $autoStartPluginSource
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TransPoli.lnk'
$target = Join-Path $installRoot 'TransPoli.exe'
if (Test-Path $target) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.Description = 'TransPoli - seu computador de bordo para ETS2'
    $shortcut.Save()
}

Write-Host "TransPoli instalado em: $installRoot"
