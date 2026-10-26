param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/WcImageViewer/WcImageViewer.csproj"
$publish = Join-Path $root "publish"

# The updater is a separate .NET Framework 4.8 WinExe so it can replace the
# running app after it exits. It is compiled by the OS csc.exe, not dotnet build,
# and is only used as the app's embedded resource: it is never copied next to the
# app, so a release folder holds just the app plus the two documents.
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw ".NET Framework 4.x csc.exe를 찾을 수 없습니다. Windows에 .NET Framework 4.8이 필요합니다."
}

$updaterSource = Join-Path $root "src/WcImageViewer.Updater/Program.cs"
$updaterDirectory = Join-Path $root "src/WcImageViewer.Updater/obj/publish"
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

foreach ($profile in @("portable", "lite")) {
    dotnet publish $project -c $Configuration -p:PublishProfile=$profile -p:UpdaterResourcePath="$updaterExe"
    if ($LASTEXITCODE -ne 0) {
        throw "$profile publish 실패 (종료 코드 $LASTEXITCODE)."
    }
}

$portableDirectory = Join-Path $publish "portable"
$liteDirectory = Join-Path $publish "lite"

# Builds up to v0.4.1 copied the updater next to the app for v0.4.0
# compatibility. Remove those leftovers (and the old publish\updater folder)
# before zipping so no release folder contains an updater file.
$staleUpdaters = @(
    (Join-Path $portableDirectory "WcImageViewer.Updater.exe"),
    (Join-Path $liteDirectory "WcImageViewer.Updater.exe"),
    (Join-Path (Join-Path $publish "updater") "WcImageViewer.Updater.exe")
)
foreach ($staleUpdater in $staleUpdaters) {
    if (Test-Path -LiteralPath $staleUpdater) {
        Remove-Item -LiteralPath $staleUpdater
    }
}

$legacyUpdaterDirectory = Join-Path $publish "updater"
if ((Test-Path -LiteralPath $legacyUpdaterDirectory) -and
    -not (Get-ChildItem -LiteralPath $legacyUpdaterDirectory -Force)) {
    Remove-Item -LiteralPath $legacyUpdaterDirectory
}

# The app must carry the updater inside its own assembly; that is the only
# updater a v0.4.1+ install runs, so assert it before and after publishing.
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

$intermediateAssembly = Join-Path $root "src/WcImageViewer/obj/$Configuration/net10.0-windows/win-x64/WcImageViewer.dll"
$expectedFileVersion = [regex]::Match(
    [System.IO.File]::ReadAllText($project),
    '<FileVersion>([^<]+)</FileVersion>').Groups[1].Value

Assert-EmbeddedUpdater $intermediateAssembly
Assert-EmbeddedUpdater (Join-Path $portableDirectory "WcImageViewer.exe")
Assert-EmbeddedUpdater (Join-Path $liteDirectory "WcImageViewer.exe")

foreach ($exe in @((Join-Path $portableDirectory "WcImageViewer.exe"), (Join-Path $liteDirectory "WcImageViewer.exe"))) {
    $fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    if ($fileVersion -ne $expectedFileVersion) {
        throw "예상하지 못한 FileVersion: $exe ($fileVersion != $expectedFileVersion)"
    }

    $updaterNextToApp = Join-Path (Split-Path -Parent $exe) "WcImageViewer.Updater.exe"
    if (Test-Path -LiteralPath $updaterNextToApp) {
        throw "게시 폴더에 updater가 남아 있습니다: $updaterNextToApp"
    }
}

$portableFiles = @(
    (Join-Path $portableDirectory "WcImageViewer.exe"),
    (Join-Path $root "LICENSE"),
    (Join-Path $root "THIRD-PARTY-NOTICES.md")
)
$liteFiles = @(
    (Join-Path $liteDirectory "WcImageViewer.exe"),
    (Join-Path $root "LICENSE"),
    (Join-Path $root "THIRD-PARTY-NOTICES.md")
)

foreach ($file in $portableFiles + $liteFiles) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "배포 파일이 없습니다: $file"
    }
}

$portableZip = Join-Path $publish "WcImageViewer-portable-win-x64.zip"
$liteZip = Join-Path $publish "WcImageViewer-lite-win-x64.zip"

Compress-Archive -Path $portableFiles -DestinationPath $portableZip -Force
Compress-Archive -Path $liteFiles -DestinationPath $liteZip -Force

# A release ZIP is exactly the app plus the two documents. No updater entry:
# v0.4.1+ runs the updater embedded in its own exe.
function Assert-ReleaseZip([string]$zipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $archive.Dispose()
    }

    if ($names.Count -ne 3) {
        throw "ZIP 항목이 3개가 아닙니다: $zipPath ($($names -join ', '))"
    }

    foreach ($required in @("WcImageViewer.exe", "LICENSE", "THIRD-PARTY-NOTICES.md")) {
        if ($names -notcontains $required) {
            throw "ZIP에 $required 이(가) 없습니다: $zipPath ($($names -join ', '))"
        }
    }

    if ($names -contains "WcImageViewer.Updater.exe") {
        throw "ZIP에 updater가 들어 있습니다: $zipPath"
    }
}

Assert-ReleaseZip $portableZip
Assert-ReleaseZip $liteZip

Get-ChildItem $publish -File |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            Name   = $_.Name
            Length = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    } |
    Format-Table -AutoSize
