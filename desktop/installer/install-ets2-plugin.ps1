param(
    [Parameter(Mandatory=$true)][string]$PluginSource,
    [string]$AutoStartPluginSource = ''
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PluginSource -PathType Leaf)) { throw "DLL do VehicleControl nao encontrada no pacote: $PluginSource" }

$steamRoots = New-Object System.Collections.Generic.List[string]
function Add-UniqueRoot([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    $expanded = [Environment]::ExpandEnvironmentVariables($path.Trim().Trim('"'))
    if ((Test-Path -LiteralPath $expanded -PathType Container) -and -not $steamRoots.Contains($expanded)) { $steamRoots.Add($expanded) }
}
foreach ($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam','HKLM:\SOFTWARE\Valve\Steam')) {
    try {
        $p = Get-ItemProperty -Path $key -ErrorAction Stop
        Add-UniqueRoot $p.SteamPath
        Add-UniqueRoot $p.InstallPath
    } catch { }
}
Add-UniqueRoot (Join-Path ${env:ProgramFiles(x86)} 'Steam')
Add-UniqueRoot (Join-Path $env:ProgramFiles 'Steam')

$libraryRoots = New-Object System.Collections.Generic.List[string]
foreach ($steamRoot in @($steamRoots)) {
    if (-not $libraryRoots.Contains($steamRoot)) { $libraryRoots.Add($steamRoot) }
    $vdf = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path -LiteralPath $vdf)) { continue }
    try {
        foreach ($line in Get-Content -LiteralPath $vdf -ErrorAction Stop) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $library = $matches[1] -replace '\\\\','\'
                if ((Test-Path -LiteralPath $library -PathType Container) -and -not $libraryRoots.Contains($library)) { $libraryRoots.Add($library) }
            }
        }
    } catch { }
}

$installed = 0
foreach ($library in @($libraryRoots)) {
    $game = Join-Path $library 'steamapps\common\Euro Truck Simulator 2'
    if (-not (Test-Path -LiteralPath $game -PathType Container)) { continue }
    $pluginDir = Join-Path $game 'bin\win_x64\plugins'
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    $destination = Join-Path $pluginDir 'TransPoli.VehicleControl.dll'
    Copy-Item -LiteralPath $PluginSource -Destination $destination -Force
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Falha ao confirmar a DLL instalada em $destination" }
    if (-not [string]::IsNullOrWhiteSpace($AutoStartPluginSource) -and (Test-Path -LiteralPath $AutoStartPluginSource -PathType Leaf)) {
        $autoStartDestination = Join-Path $pluginDir 'TransPoli.AutoStart.dll'
        Copy-Item -LiteralPath $AutoStartPluginSource -Destination $autoStartDestination -Force
        if (-not (Test-Path -LiteralPath $autoStartDestination -PathType Leaf)) { throw "Falha ao confirmar o AutoStart instalado em $autoStartDestination" }
    }
    $installed++
}
if ($installed -eq 0) {
    Write-Host 'ETS2 nao localizado nas bibliotecas Steam. VehicleControl preservado no pacote TransPoli.'
} else {
    Write-Host "Plugins TransPoli instalados automaticamente em $installed instalacao(oes) do ETS2."
}
