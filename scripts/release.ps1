param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"
$notes = Join-Path $root "docs\releases\$tag.md"
$publish = Join-Path $root "publish"

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

# Title is the version tag only, never the app name.
gh release create $tag --title $tag --notes-file $notes @assets
