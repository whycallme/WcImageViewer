# WC Image Viewer

Windows용 가볍고 빠른 이미지 뷰어입니다. 폴더와 압축파일(ZIP/CBZ, 7Z/CB7, RAR/CBR)을 열면 첫 이미지가 바로 표시되며, 설치 없이 실행할 수 있습니다.

v0.4.2에서는 updater를 앱 exe에 내장해 배포 폴더에 `WcImageViewer.exe` 하나만 남고, 릴리즈 ZIP도 exe와 문서 2개, 3개 파일로 줄었습니다.

## 다운로드

[릴리즈 페이지](https://github.com/whycallme/WcImageViewer/releases/latest)에서 두 가지 방식으로 받을 수 있습니다.

| 파일 | 크기 | 요구사항 |
|---|---|---|
| `WcImageViewer-portable-win-x64.zip` | 62,189,153 B (약 62MB) | 없음 (self-contained) |
| `WcImageViewer-lite-win-x64.zip` | 3,091,805 B (약 3MB) | .NET 10 Desktop Runtime |

압축을 풀고 `WcImageViewer.exe`를 실행하세요. 이미지 파일, 폴더, 압축파일을 인자로 넘기거나 창에 끌어다 놓아도 됩니다.

ZIP에는 파일 3개가 들어 있습니다: `WcImageViewer.exe`(updater 내장), `LICENSE`, `THIRD-PARTY-NOTICES.md`. 별도의 updater 파일은 없으며, 업데이트는 앱에 내장된 updater가 임시 폴더에 풀려 실행됩니다. 배포 폴더에도 updater가 남지 않습니다.

> **v0.4.1 이상에서 올라오면 자동으로 업데이트됩니다.** v0.4.0 이하는 v0.4.2를 한 번 수동으로 설치해야 합니다. v0.4.0 이하의 업데이트 코드는 ZIP 안의 `WcImageViewer.Updater.exe`를 요구하는데 v0.4.2 ZIP에는 없기 때문입니다. updater가 들어간 마지막 ZIP은 v0.4.1입니다. v0.3.0 이하는 자동 업데이트 코드가 없어 어느 버전이든 한 번은 수동 설치가 필요합니다.

```powershell
WcImageViewer.exe "C:\images"
WcImageViewer.exe "C:\images\photos.zip"
```

## 주요 기능

- 이미지 파일/폴더 열기, 드래그 앤 드롭
- JPEG, PNG, BMP, GIF(애니메이션), TIFF, ICO, WebP(애니메이션), AVIF, HEIC/HEIF
- 100MP 초과 이미지 타일 디코딩 (원본 해상도 확대, 메모리 사용 제한)
- ZIP/CBZ, 7Z/CB7, RAR/CBR 읽기 (압축파일 내부 이미지 탐색, 고체 압축 진행률 표시)
- 이전/다음, 플로팅 썸네일 도크(74px, 기본 숨김, 하단 호버·T로 표시), 슬라이드쇼
- 슬라이드쇼 반복 재생·랜덤 재생 설정 (랜덤은 한 바퀴 무작위 순서)
- 마지막/첫 파일에서 이동 방식을 `첫 파일로 가기` 또는 `아무것도 안 하기`로 선택
- 경계에서 “첫 파일입니다.” / “마지막 파일입니다.” 중앙 알림 표시 (선택)
- 맞춤/100%, 표시 배율 직접 입력, 커서 기준 Ctrl+휠 줌, 드래그 팬, 전체화면
- 이미지를 열 때 표시 크기에 맞춰 창 조정 (기본 켜짐, 작은 이미지는 확대하지 않음 기본 켜짐)
- Tab 이미지 정보 오버레이, F2 이름 바꾸기, Del 삭제(확인/휴지통 설정)
- Ctrl+E 편집 앱으로 열기, Ctrl+C 이미지 복사, F12 다른 이름으로 저장
- F6 책갈피 관리, 최근 항목 메뉴
- 이미지 영역 드래그로 창 이동
- 설정에서 편집 앱 선택, 확장자별 기본 앱 연결, 단축키 변경
- 여러 창으로 열기 / 하나의 창으로 열기 선택 (다음 실행부터 적용)
- EXIF orientation, 라이트/다크 테마, High-DPI 대응
- 자동 업데이트 확인(설정 아이콘 점), 종료 시 업데이트 창, 건너뛰기/다시 알림
- 앱 exe에 내장된 updater로 ZIP 다운로드·SHA-256 검증·파일 교체 (배포 폴더는 exe 하나)
- 28px 얇은 커스텀 창 크롬, 파일명·이미지 개수 표시, 우측 컴팩트 툴바, Windows 11 작은 라운드 모서리
- 확장자별 파일 아이콘과 앱 아이콘

> WebP/AVIF/HEIC는 Windows에 설치된 WIC 이미지 확장을 사용합니다. HEIC/HEIF는 Microsoft Store의 **HEIF 이미지 확장**과 **HEVC 비디오 확장**이 필요합니다. 100MP 초과 이미지는 보기용으로 타일 디코딩되며, 다른 이름으로 저장·클립보드 복사는 화면용 미리보기(최대 4096px)로 동작합니다.

## 단축키

| 키 | 동작 |
|---|---|
| ← / →, PageUp / PageDown | 이전 / 다음 |
| 휠 | 이전 / 다음 이미지 |
| Ctrl + 휠 | 커서 기준 확대 / 축소 |
| Home / End | 처음 / 마지막 |
| 0 / 1 | 창에 맞춤 / 100% |
| W | 이미지 크기에 창 맞춤 |
| F3 | 파일 열기 |
| F4 | 현재 이미지 닫기 |
| F5 | 설정 |
| F6 | 책갈피 관리 |
| F11 | 전체화면 |
| F12 | 다른 이름으로 저장 |
| S | 슬라이드쇼 시작·중지 |
| T | 썸네일 스트립 고정 표시·숨김 |
| Tab | 이미지 정보 오버레이 표시·숨김 |
| F2 | 파일 이름 바꾸기 |
| Delete | 파일 삭제 (설정에 따름) |
| Ctrl+E | 설정한 편집 앱으로 열기 |
| Ctrl+C | 현재 이미지를 클립보드로 복사 |
| Ctrl+, | 설정 (F5와 동일) |
| Esc | 창 닫기 |

모든 키보드 단축키는 설정의 **단축키**에서 변경할 수 있습니다.

## 설정

- 설정은 `%LOCALAPPDATA%\WcImageViewer\settings.json`에 저장됩니다. 기존 exe 옆 또는 `%APPDATA%` 설정은 처음 실행 시 자동으로 이전됩니다.
- 최근 항목, 라이트/다크 테마, 썸네일 고정 표시, 슬라이드쇼 간격·반복·랜덤 재생, 기본 표시 배율, 이미지를 열 때 창 맞춤, 작은 이미지 확대 여부, 파일 끝에서 이동 방식과 경계 알림, 삭제 전 확인·휴지통 사용, 편집 앱, 확장자별 파일 연결, 단축키, 여러 창/한 창 열기 방식을 기억합니다.
- 설정 > 업데이트에서 현재 버전, 자동으로 업데이트 확인, 지금 확인, 마지막 확인 시각, 건너뛴 버전(다시 알림)을 관리합니다. 자동 확인은 시작 3초 후 24시간에 한 번 실행됩니다.
- 앱은 이전 창 위치에서 최소 크기(800×600)로 시작합니다.
- 파일 연결은 사용자 계정(HKCU)에 등록하며, Windows 기본 앱 정책에 따라 확인이 필요할 수 있습니다.

## 개발

요구사항: Windows 10/11 x64, .NET 10 SDK 10.0.402

```powershell
dotnet build WcImageViewer.slnx
dotnet test WcImageViewer.slnx
dotnet run --project src/WcImageViewer
```

프로젝트 구조:

```text
src/WcImageViewer/     WPF 앱 (Models, Services, Controls, 창)
tests/                 xUnit 테스트
design/                아이콘/UI 시안과 생성 스크립트
scripts/publish.ps1    portable/lite 게시 및 zip 생성
docs/PLAN.md           개발 계획과 진행 상태
```

배포:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

- `publish/portable/WcImageViewer.exe`: self-contained 단일 exe (updater 내장)
- `publish/lite/WcImageViewer.exe`: .NET 10 Desktop Runtime 필요 (updater 내장)
- updater는 `src/WcImageViewer.Updater/obj/publish`에 .NET Framework 4.8 csc로 컴파일해 앱에 임베드합니다. 배포 폴더에는 파일이 남지 않습니다.
- `publish/*.zip`: 릴리즈용 압축 파일 (exe, LICENSE, THIRD-PARTY-NOTICES.md 3개).

## 라이선스

[MIT License](LICENSE)

서드파티 구성 요소 고지는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
