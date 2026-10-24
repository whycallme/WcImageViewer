param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/WcImageViewer/WcImageViewer.csproj"
$publish = Join-Path $root "publish"

# The updater is a separate .NET Framework 4.8 WinExe so it can replace the
# running app after it exits. It is compiled by the OS csc.exe, not dotnet build.
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw ".NET Framework 4.x csc.exe를 찾을 수 없습니다. Windows에 .NET Framework 4.8이 필요합니다."
}

$updaterSource = Join-Path $root "src/WcImageViewer.Updater/Program.cs"
$updaterDirectory = Join-Path $publish "updater"
New-Item -ItemType Directory -Force -Path $updaterDirectory | Out-Null
$updaterExe = Join-Path $updaterDirectory "WcImageViewer.Updater.exe"

$cscArguments = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/codepage:65001",
    "/out:$updaterExe",
    $updaterSource,
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Windows.Forms.dll"
)

& $csc @cscArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $updaterExe)) {
    throw "WcImageViewer.Updater.exe 컴파일 실패 (csc 종료 코드 $LASTEXITCODE)."
}

dotnet publish $project -c $Configuration -p:PublishProfile=portable
if ($LASTEXITCODE -ne 0) {
    throw "portable publish 실패 (종료 코드 $LASTEXITCODE)."
}

dotnet publish $project -c $Configuration -p:PublishProfile=lite
if ($LASTEXITCODE -ne 0) {
    throw "lite publish 실패 (종료 코드 $LASTEXITCODE)."
}

$portableDirectory = Join-Path $publish "portable"
$liteDirectory = Join-Path $publish "lite"
Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $portableDirectory "WcImageViewer.Updater.exe") -Force
Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $liteDirectory "WcImageViewer.Updater.exe") -Force

$portableFiles = @(
    (Join-Path $portableDirectory "WcImageViewer.exe"),
    (Join-Path $portableDirectory "WcImageViewer.Updater.exe"),
    (Join-Path $root "LICENSE"),
    (Join-Path $root "THIRD-PARTY-NOTICES.md")
)
$liteFiles = @(
    (Join-Path $liteDirectory "WcImageViewer.exe"),
    (Join-Path $liteDirectory "WcImageViewer.Updater.exe"),
    (Join-Path $root "LICENSE"),
    (Join-Path $root "THIRD-PARTY-NOTICES.md")
)

foreach ($file in $portableFiles + $liteFiles) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "배포 파일이 없습니다: $file"
    }
}

Compress-Archive -Path $portableFiles `
    -DestinationPath (Join-Path $publish "WcImageViewer-portable-win-x64.zip") -Force
Compress-Archive -Path $liteFiles `
    -DestinationPath (Join-Path $publish "WcImageViewer-lite-win-x64.zip") -Force

Get-ChildItem $publish | Select-Object Name, Length
