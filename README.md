# WC Image Viewer

Windows용 가볍고 빠른 이미지 뷰어입니다. 폴더와 압축파일(ZIP/CBZ, 7Z/CB7, RAR/CBR)을 열면 첫 이미지가 바로 표시되며, 설치 없이 실행할 수 있습니다.

## 다운로드

[릴리즈 페이지](https://github.com/whycallme/WcImageViewer/releases/latest)에서 두 가지 방식으로 받을 수 있습니다.

| 파일 | 크기 | 요구사항 |
|---|---|---|
| `WcImageViewer-portable-win-x64.zip` | 약 59MB | 없음 (self-contained) |
| `WcImageViewer-lite-win-x64.zip` | 약 2.9MB | .NET 10 Desktop Runtime |

압축을 풀고 `WcImageViewer.exe`를 실행하세요. 이미지 파일, 폴더, 압축파일을 인자로 넘기거나 창에 끌어다 놓아도 됩니다.

```powershell
WcImageViewer.exe "C:\images"
WcImageViewer.exe "C:\images\photos.zip"
```

## 주요 기능

- 이미지 파일/폴더 열기, 드래그 앤 드롭
- JPEG, PNG, BMP, GIF(애니메이션), TIFF, ICO, WebP(애니메이션), AVIF, HEIC/HEIF
- 100MP 초과 이미지 타일 디코딩 (원본 해상도 확대, 메모리 사용 제한)
- ZIP/CBZ, 7Z/CB7, RAR/CBR 읽기 (압축파일 내부 이미지 탐색, 고체 압축 진행률 표시)
- 이전/다음, 썸네일 스트립(기본 숨김, 하단 호버·T로 표시), 슬라이드쇼
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
- EXIF orientation, 다크 테마, High-DPI 대응
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
- 최근 항목, 썸네일 고정 표시, 슬라이드쇼 간격·반복·랜덤 재생, 기본 표시 배율, 이미지를 열 때 창 맞춤, 작은 이미지 확대 여부, 파일 끝에서 이동 방식과 경계 알림, 삭제 전 확인·휴지통 사용, 편집 앱, 확장자별 파일 연결, 단축키, 여러 창/한 창 열기 방식을 기억합니다.
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
design/                아이콘 시안과 생성 스크립트
scripts/publish.ps1    portable/lite 게시 및 zip 생성
docs/PLAN.md           개발 계획과 진행 상태
```

배포:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

- `publish/portable/WcImageViewer.exe`: self-contained 단일 exe
- `publish/lite/WcImageViewer.exe`: .NET 10 Desktop Runtime 필요
- `publish/*.zip`: 릴리즈용 압축 파일

## 라이선스

[MIT License](LICENSE)

서드파티 구성 요소 고지는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
