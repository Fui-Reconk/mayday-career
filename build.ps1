# Build MaydayCareer and optionally deploy it into the game folder.
#
#   .\build.ps1                 # auto-detect the game through Steam
#   .\build.ps1 -Game "D:\...\Mayday Airways Demo"
#   .\build.ps1 -Deploy         # build, then copy the dll into BepInEx\plugins

[CmdletBinding()]
param(
    [string]$Game,
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\MaydayCareer.csproj'

function Find-Game {
    $candidates = @()

    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        $prop = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if (-not $prop -or -not $prop.SteamPath) { continue }
        $steam = $prop.SteamPath -replace '/', '\'
        $candidates += Join-Path $steam 'steamapps\common\Mayday Airways Demo'

        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches | ForEach-Object {
                foreach ($m in $_.Matches) {
                    $lib = $m.Groups[1].Value -replace '\\\\', '\'
                    $candidates += Join-Path $lib 'steamapps\common\Mayday Airways Demo'
                }
            }
        }
    }

    $candidates += 'C:\Game\steam\steamapps\common\Mayday Airways Demo'

    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'MaydayAirways.exe')) { return $c }
    }
    throw 'Game folder not found. Pass it explicitly: .\build.ps1 -Game "<path>"'
}

if (-not $Game) { $Game = Find-Game }
if (-not (Test-Path $proj)) { throw "Missing project file: $proj" }

$interop = Join-Path $Game 'BepInEx\interop\Assembly-CSharp.dll'
if (-not (Test-Path $interop)) {
    throw "Missing BepInEx\interop. Launch the game once with BepInEx installed so it generates the interop assemblies."
}

Write-Host "Game : $Game" -ForegroundColor Cyan
Write-Host "Proj : $proj" -ForegroundColor Cyan

dotnet build $proj -c Release "-p:GameDir=$Game"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$dll = Join-Path $root 'src\bin\Release\net6.0\MaydayCareer.dll'
if (-not (Test-Path $dll)) { throw "Build output missing: $dll" }

if ($Deploy) {
    $dest = Join-Path $Game 'BepInEx\plugins'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll (Join-Path $dest 'MaydayCareer.dll') -Force
    Write-Host "Deployed -> $(Join-Path $dest 'MaydayCareer.dll')" -ForegroundColor Green
}
