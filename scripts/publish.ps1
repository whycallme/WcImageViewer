param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/WcImageViewer/WcImageViewer.csproj"
$publish = Join-Path $root "publish"

dotnet publish $project -c $Configuration -p:PublishProfile=portable
dotnet publish $project -c $Configuration -p:PublishProfile=lite

Compress-Archive -Path (Join-Path $publish "portable\WcImageViewer.exe") `
    -DestinationPath (Join-Path $publish "WcImageViewer-portable-win-x64.zip") -Force
Compress-Archive -Path (Join-Path $publish "lite\WcImageViewer.exe") `
    -DestinationPath (Join-Path $publish "WcImageViewer-lite-win-x64.zip") -Force

Get-ChildItem $publish | Select-Object Name, Length
