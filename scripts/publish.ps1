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

dotnet publish $project -c $Configuration -p:PublishProfile=portable -p:UpdaterResourcePath="$updaterExe"
if ($LASTEXITCODE -ne 0) {
    throw "portable publish 실패 (종료 코드 $LASTEXITCODE)."
}

dotnet publish $project -c $Configuration -p:PublishProfile=lite -p:UpdaterResourcePath="$updaterExe"
if ($LASTEXITCODE -ne 0) {
    throw "lite publish 실패 (종료 코드 $LASTEXITCODE)."
}

# The app must carry the updater inside its own assembly; the copies below are
# only for v0.4.0 compatibility and are deleted on first launch by the app.
function Assert-EmbeddedUpdater([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "임베드 확인 대상이 없습니다: $path"
    }

    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    if (-not $text.Contains("WcImageViewer.Updater.exe")) {
        throw "임베드된 updater 리소스를 찾을 수 없습니다: $path"
    }
}

$portableDirectory = Join-Path $publish "portable"
$liteDirectory = Join-Path $publish "lite"
$intermediateAssembly = Join-Path $root "src/WcImageViewer/obj/$Configuration/net10.0-windows/win-x64/WcImageViewer.dll"
$expectedFileVersion = [regex]::Match(
    [System.IO.File]::ReadAllText($project),
    '<FileVersion>([^<]+)</FileVersion>').Groups[1].Value

Assert-EmbeddedUpdater $intermediateAssembly
Assert-EmbeddedUpdater (Join-Path $liteDirectory "WcImageViewer.exe")

foreach ($exe in @((Join-Path $portableDirectory "WcImageViewer.exe"), (Join-Path $liteDirectory "WcImageViewer.exe"))) {
    $fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    if ($fileVersion -ne $expectedFileVersion) {
        throw "예상하지 못한 FileVersion: $exe ($fileVersion != $expectedFileVersion)"
    }
}

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
