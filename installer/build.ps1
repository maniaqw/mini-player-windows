param([string]$Dotnet = 'dotnet', [string]$Iscc = 'ISCC.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & $Dotnet publish MiniPlayer.csproj -c Release -r win-x64 --self-contained true -o dist/GlassPlayer
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }
    & $Iscc installer/GlassPlayer.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
} finally { Pop-Location }
