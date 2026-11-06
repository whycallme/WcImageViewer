# WC Image Viewer 단독 파일 빌드 및 릴리즈 등록 규칙

## 1. 개요
WC Image Viewer는 .NET 10 기반 WPF 앱으로, 두 가지 배포 버전(lite, portable)을 생성합니다. 두 버전 모두 단독 실행 파일(.exe)으로 실행 가능해야 합니다.

## 2. 빌드 구성

| 항목 | lite | portable |
|------|------|----------|
| PublishProfile | lite | portable |
| 출력 폴더 | `build/lite/` | `build/portable/` |
| 실행 파일 | `WcImageViewer.exe` | `WcImageViewer.exe` |
| ZIP 자산 | `WcImageViewer-lite-win-x64.zip` | `WcImageViewer-portable-win-x64.zip` |

각 빌드 결과물은 실행 파일(exe)과 LICENSE, THIRD-PARTY-NOTICES.md 문서 3개로 구성됩니다. DLL이 별도로 포함되지 않도록 단독 실행 파일로 빌드합니다.

## 3. PublishProfile

- `src/WcImageViewer/Properties/PublishProfiles/lite.pubxml` — lite 빌드용
- `src/WcImageViewer/Properties/PublishProfiles/portable.pubxml` — portable 빌드용

두 프로필 모두 단독 실행 파일(exe)이 출력되도록 구성되어야 합니다. 필요하면 `PublishSingleFile`, `TrimMode` 등 csproj/PublishProfile 설정을 조정합니다.

## 4. 빌드 방법

```powershell
# 1) 업데이터(Updater.exe) 컴파일 (.NET Framework 4.8, csc.exe 사용) 및 두 프로필 게시
# 프로젝트 루트에서
.\scripts\publish.ps1 -Configuration Release

# 2) 개별 게시 (필요시)
dotnet publish src/WcImageViewer/WcImageViewer.csproj -c Release -p:PublishProfile=lite
dotnet publish src/WcImageViewer/WcImageViewer.csproj -c Release -p:PublishProfile=portable
```

`publish.ps1`은 업데이터를 exe 내부에 임베드하고, 두 PublishProfile로 게시한 후 zip을 생성합니다. zip은 실행 파일 + LICENSE + THIRD-PARTY-NOTICES.md 3개 항목만 포함합니다.

## 5. 릴리즈 등록 규칙

1. `publish.ps1`로 lite, portable zip 모두 생성 (`build/` 폴더에 생성)
2. GitHub 릴리즈 태그 생성:
   `gh release create v<버전> --title v<버전> --notes-file docs/releases/v<버전>.md <zip1> <zip2>`
3. 자산 추가/교체:
   `gh release upload v<버전> <zip> --clobber`
4. 버전 범프(`WcImageViewer.csproj`의 Version/AssemblyVersion/FileVersion)는 사용자 명시 요청 시에만 수행
5. `docs/changelog.md`는 버전 범프 시에만 항목 추가
6. zip 파일은 `.gitignore`로 관리되며, 릴리즈 자산으로만 등록됩니다 (git 추적 제외)

### 5-1. 업데이터 미내장 빌드 게시 금지 (필수)

**업데이터(`WcImageViewer.Updater.exe`)가 exe에 내장되지 않은 빌드는 절대 릴리즈에 게시하지 않습니다.** 내장되지 않은 exe는 자동 업데이트가 항상 "업데이트 프로그램을 찾을 수 없습니다"로 실패하고, 한 번 설치되면 사용자가 수동으로 exe를 교체해야만 복구됩니다. (v1.1.2 사고: 업데이터 없는 exe가 설치되어 v1.2.0 업데이트 실패)

- 릴리즈용 exe/ZIP은 반드시 `scripts/publish.ps1`로만 만듭니다. 일반 `dotnet build`, `dotnet publish`(UpdaterResourcePath 없이), `build/Debug`·`build/Release` 출력물은 업데이터가 없으므로 배포·복사·ZIP 금지입니다.
- ZIP을 손으로 만들거나 exe만 따로 복사해 올리지 않습니다.
- 릴리즈는 `scripts/release.ps1 -Version <버전>`으로만 등록합니다. 이 스크립트가 ZIP 안의 `WcImageViewer.exe`에 업데이터 리소스가 있는지, 버전이 csproj/릴리즈 버전과 같은지 검사하고, 실패하면 `gh release create` 전에 중단합니다.
- `gh release create/upload`를 직접 호출해 이 검사를 우회하지 않습니다.
- 사용자 PC에 설치하는 exe도 `build/lite/` 또는 `build/portable/`의 것만 사용합니다.

## 6. 단독 실행 조건
- lite, portable 두 버전 모두 `WcImageViewer.exe` 단일 파일로 실행 가능해야 합니다.
- 필요한 어셈블리는 게시 시 단일 파일로 번들되거나, 런타임에 포함되어야 합니다.
- 업데이터(Updater.exe)는 앱 exe 내부에 임베드된 리소스로 포함되며, 실행 파일 폴더에 별도로 복사되지 않습니다.

## 7. 참고
- `publish.ps1`은 .NET Framework 4.8의 csc.exe를 사용하여 업데이터를 컴파일합니다.
- 게시 출력물은 `build/lite/`, `build/portable/` 폴더에 생성되며, zip은 `build/` 루트에 생성됩니다.

## 8. 빌드 위치 규칙

다른 위치에 빌드 금지: 빌드는 `build/` 에서만 수행하며, `bin`/`obj`/`publish` 는 다른 위치에 생성하지 않습니다.
