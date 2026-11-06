param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"
$notes = Join-Path $root "docs\releases\$tag.md"
$publish = Join-Path $root "build"

if (-not (Test-Path -LiteralPath $notes)) {
    throw "릴리즈 노트가 없습니다: $notes"
}

$assets = @(
    (Join-Path $publish "WcImageViewer-portable-win-x64.zip"),
    (Join-Path $publish "WcImageViewer-lite-win-x64.zip")
)
foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset)) {
        throw "배포 파일이 없습니다: $asset (먼저 scripts/publish.ps1 실행)"
    }
}

# Release guard: never publish a build whose exe lacks the embedded updater
# (e.g. a plain `dotnet build`/`dotnet publish` copied into a ZIP by hand).
# Such an app cannot self-update: it fails with "WcImageViewer.Updater.exe not found".
# Inspect the exe inside each ZIP, not build\lite, so what ships is what is checked.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$project = Join-Path $root "src/WcImageViewer/WcImageViewer.csproj"
$expectedVersion = [regex]::Match(
    [System.IO.File]::ReadAllText($project),
    '<FileVersion>([^<]+)</FileVersion>').Groups[1].Value
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("wcrelease-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir | Out-Null
try {
    foreach ($asset in $assets) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($asset)
        try {
            $entry = $archive.Entries | Where-Object { $_.FullName -eq "WcImageViewer.exe" } | Select-Object -First 1
            if (-not $entry) { throw "ZIP에 WcImageViewer.exe 가 없습니다: $asset" }
            $exePath = Join-Path $tempDir ([System.IO.Path]::GetFileNameWithoutExtension($asset) + ".exe")
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $exePath, $true)
        }
        finally {
            $archive.Dispose()
        }

        $text = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($exePath))
        if (-not $text.Contains("WcImageViewer.Updater.exe")) {
            throw "릴리즈 중단: 업데이터가 내장되지 않은 빌드입니다 ($asset). scripts/publish.ps1 로 다시 빌드하세요."
        }

        $fileVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
        if ($fileVersion -ne $expectedVersion) {
            throw "릴리즈 중단: ZIP 안 exe 버전($fileVersion)이 csproj 버전($expectedVersion)과 다릅니다 ($asset). publish.ps1 을 다시 실행하세요."
        }
        if ($fileVersion -ne "$Version.0") {
            throw "릴리즈 중단: ZIP 안 exe 버전($fileVersion)이 릴리즈 버전($Version)과 다릅니다 ($asset)."
        }
    }
}
finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Title is the version tag only, never the app name.
gh release create $tag --title $tag --notes-file $notes @assets
