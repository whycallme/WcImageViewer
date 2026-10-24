# WC Image Viewer — 개발 계획 (C# WPF)

전제: **Windows 10/11 x64 데스크톱**, 설치 없이 실행하는 portable 이미지 뷰어.
확정 사항: **Windows 전용**, **C# + WPF**, **v0.1부터 압축파일 지원**.
목표: 꿀뷰처럼 "폴더/압축파일을 열면 바로 뜨는" 가볍고 빠른 로컬 이미지 뷰어.

> 이 문서는 초기 개발 계획과 결정을 기록한 문서입니다. 현재 구현된 기능과 진행 상태는 마지막 장의 **10. 진행 상태**를 기준으로 확인하세요.

---

## 1. 스택

**.NET 10 LTS + WPF + C#**

| 역할 | 선택 |
|---|---|
| 런타임 | .NET 10 LTS (`net10.0-windows`) |
| UI | WPF + XAML, CommunityToolkit.Mvvm |
| 이미지 디코딩 | WPF `BitmapImage` / `BitmapDecoder` (내부적으로 WIC 사용) |
| WebP 애니메이션 | **Imazen.WebP** (libwebp, MIT) — 긴 애니메이션 프레임 지연 디코딩 |
| 압축파일 | **SharpCompress** (MIT, ZIP/7Z/RAR 읽기) |
| 설정 | `System.Text.Json` |
| 테스트 | xUnit |
| 배포 | `dotnet publish` + Inno Setup(설치본) + portable zip |

선택 이유:

- WPF의 이미지 스택이 WIC를 그대로 사용하므로 JPEG/PNG/BMP/GIF/TIFF를 별도 코덱 없이 처리한다.
- XAML + 데이터 바인딩 + async/await로 UI 개발 속도가 C++ 대비 2배 이상 빠르다.
- Windows 전용이 확정되어 WPF의 크로스플랫폼 부재가 문제되지 않는다.
- 트레이드오프: self-contained 배포 시 **70~100MB**, 콜드 스타트 **0.5~1.0초**. "가벼움"은 exe 크기가 아니라 **실행 후 반응성·낮은 유휴 CPU·인덱싱 없음**으로 정의한다.

배포 프로파일 2종:

| 프로파일 | 크기 | 요구사항 | 용도 |
|---|---|---|---|
| `portable` (기본) | 70~100MB | 없음 | 단독 실행, 설치 불필요 |
| `lite` | 2~5MB | .NET 10 Desktop Runtime | 런타임이 설치된 PC |

- `PublishTrimmed`는 WPF에서 지원되지 않으므로 사용하지 않는다.
- 시작 속도는 `PublishReadyToRun`으로 확보하고, 압축 single-file은 시작이 느려지므로 측정 후 결정한다.
- NuGet: SharpCompress, CommunityToolkit.Mvvm, xUnit(테스트 전용). MahApps/HandyControl/ImageSharp/Magick.NET은 v0.1에서 쓰지 않는다.

개발 PC에는 **.NET 10 SDK 10.0.402**를 설치했다 (`winget install Microsoft.DotNet.SDK.10`).

---

## 2. 제품 원칙

1. 실행 즉시 뜬다. 콜드 스타트 1초 이내.
2. 폴더나 압축파일을 열면 바로 첫 이미지가 보인다. 가져오기·인덱싱 단계 없음.
3. 키보드만으로 모든 탐색이 가능하다.
4. UI 스레드는 파일 I/O·디코딩·압축 해제를 기다리지 않는다.
5. 설치·계정·클라우드 없이 동작한다.
6. 기능 추가보다 시작 속도와 안정성을 우선한다.

---

## 3. 기능 범위

### v0.1 (MVP, 압축파일 포함)

- 폴더 열기, 드래그 앤 드롭, 파일 필터, 자연 정렬
- 압축파일 열기: **ZIP/CBZ, 7Z/CB7, RAR/CBR 읽기 전용**
- 이미지: JPEG, PNG, BMP, GIF(애니메이션), TIFF, ICO
- 줌(커서 기준 휠), 팬, 맞춤/100%, 회전/반전, EXIF orientation
- 이전/다음, 전체화면, 슬라이드쇼, 최근 폴더·창 상태 저장
- 하단 썸네일 스트립(비동기, 가상화), 현재 이미지 프리로드
- 압축파일 내부 이미지 목록, 고체(solid) 7Z/RAR은 백그라운드 추출 + 진행률/취소
- 손상 파일·손상 압축파일은 건너뛰고 상태 표시
- 한국어 UI, 다크 테마 기본

### v0.2

- WebP, AVIF, HEIC/RAW (OS 코덱 우선, 없으면 선택 라이브러리)
- 애니메이션 WebP, TIFF 다페이지 탐색, ICC 색 관리
- 100MP 초과 이미지 타일 디코딩
- 압축파일 암호·멀티볼륨, 디스크 썸네일 캐시
- 설정 창, 단축키 변경, 파일 연결, High-DPI 세부 대응

### v1.0

- 성능·안정화, Inno Setup 설치본, portable zip, 코드 서명, 크래시 로그
- 자동 업데이트는 요구가 확인되면 추가

### 하지 않을 것

- 이미지 편집, 라이브러리 DB, 클라우드 동기화, 모바일, 플러그인 SDK, 자체 이미지/압축 포맷 디코더

---

## 4. 압축파일 정책

- **ZIP/CBZ**: 엔트리를 직접 스트림으로 읽는다. 랜덤 액세스가 빨라 캐시가 필요 없다.
- **7Z/RAR**: 고체 압축이 많으므로 열 때 이미지 엔트리만 백그라운드로 임시 캐시에 추출한다. 첫 이미지가 풀리는 즉시 표시하고, 나머지는 진행률을 보여주며 계속 추출한다.
- 캐시 위치: `%LOCALAPPDATA%\WcImageViewer\cache\<archive-hash>\`
- 캐시 키: 전체 경로 + 파일 크기 + 수정 시간의 SHA-256.
- 캐시 정리: 시작 시 7일 지난 캐시 삭제, 총량 기본 2GB 제한(LRU).
- 보안/폭탄 방어:
  - 엔트리 수 최대 100,000
  - 엔트리 1개 최대 1GB
  - 전체 해제량 최대 4GB
  - 경로 traversal 금지(파일명만 사용, 인덱스 접두어로 충돌 방지)
  - 이미지 확장자만 추출, 실제 바이트 수를 제한 스트림으로 검증
- 암호 걸린 압축·멀티볼륨은 v0.1에서 명확한 오류 메시지만 표시하고 v0.2에서 지원.

---

## 5. 구조

```text
WcImageViewer.slnx
src/WcImageViewer/
  App.xaml / App.xaml.cs          진입점, 테마 리소스, 전역 예외 처리
  MainWindow.xaml / .cs           창, 툴바, 탐색, 슬라이드쇼, 오버레이
  SettingsWindow.xaml / .cs       설정 창
  BookmarkWindow.xaml / .cs       책갈피 관리
  RenameWindow.xaml / .cs         이름 바꾸기
  Models/                         ImageEntry, ImageDocument, IImageSource, AppSettings
  Services/
    FolderImageSource.cs          폴더 열거 + 자연 정렬
    ArchiveImageSource.cs         ZIP/7Z/RAR 엔트리 목록·스트림
    ArchiveSupport.cs             압축 해제 제한·보안
    ImageLoader.cs                이미지/EXIF/GIF·WebP 프레임 디코딩
    GifCompositor.cs              GIF 투명 프레임·disposal 합성
    WebPAnimation.cs              애니메이션 WebP 프레임 지연(ANMF) 파싱
    WebPFrameSource.cs            긴 애니메이션 WebP의 libwebp 지연 디코딩
    TiledImageSource.cs           100MP 초과 이미지의 레벨별 타일 디코딩
    ImageFileEnumerator.cs        지원 확장자 판별
    ImageDocumentCache.cs         현재 이미지 LRU 캐시
    ThumbnailCache.cs             메모리 LRU
    SettingsService.cs            JSON 저장(원자적 쓰기)
    ShortcutService.cs            단축키 정의·파싱
    SingleInstanceService.cs      named mutex + named pipe
    FileAssociationService.cs     확장자 연결
    FileDeletionService.cs        휴지통/영구 삭제
    FileIconService.cs            확장자 아이콘
  Controls/
    ZoomPanControl.xaml / .cs     줌/팬/맞춤/전체화면 탐색
    ThumbnailStrip.xaml / .cs     가상화된 썸네일 목록
tests/WcImageViewer.Tests/        xUnit 테스트
design/                           아이콘 시안과 생성 스크립트
docs/                             개발 계획 문서
scripts/publish.ps1               portable/lite 게시
```

핵심 설계:

- **IImageSource**로 폴더와 압축파일을 동일하게 다룬다: `Count`, `GetEntry(i)`, `OpenEntryAsync(i)`, `DisplayName`.
- **디코딩 흐름**: 소스 열기 → 백그라운드 열거 → 자연 정렬 → 현재 인덱스 → 미리보기 디코딩 → 표시. 앞뒤 1~2장 프리로드.
- **미리보기 우선**: `BitmapImage.DecodePixelWidth`를 뷰포트 크기로 지정해 큰 JPEG도 빠르게 표시. 100% 이상 확대할 때만 원본을 디코딩한다.
- **캐시**: 화면 크기 미리보기 LRU(기본 256MB) + 썸네일 LRU. 전체 원본은 동시에 1장만 유지.
- **스레드**: UI 1개 + 디코드 워커 최대 3~4개. `SemaphoreSlim`으로 제한, 탐색 시 이전 작업 `CancellationToken` 취소, 세대 카운터로 stale 결과 폐기.
- **렌더링**: `ZoomPanControl`이 `MatrixTransform`으로 이미지를 그리고, 100%에서는 픽셀 스냅 + NearestNeighbor, 축소 시 HighQuality 보간.
- **100% 정의**: 이미지 1픽셀 = 모니터 1디바이스 픽셀. `VisualTreeHelper.GetDpi`로 OS 배율을 보정한다.
- **단일 인스턴스**: named mutex + named pipe로 기존 창에 파일 경로 전달.
- **설정**: `%LOCALAPPDATA%\WcImageViewer\settings.json` (exe/`%APPDATA%`의 기존 설정은 자동 이전). 임시 파일 후 `File.Move(..., overwrite: true)`로 원자적 저장.
- **문자열**: 한국어 우선, 리소스로 분리해 영어 추가가 쉽게.
- **로깅**: Debug 출력 + 처리되지 않은 예외를 로그 파일과 대화상자로 남긴다.

WPF 세부:

- `BitmapImage`는 `CacheOption.OnLoad` + `Freeze()`로 파일 핸들을 즉시 닫고 스레드 간 전달.
- EXIF orientation은 `BitmapMetadata`의 `/app1/ifd/{ushort=274}`를 읽어 `TransformedBitmap`으로 적용.
- GIF는 `BitmapDecoder`의 프레임 + delay 메타데이터로 `DispatcherTimer` 재생.
- 썸네일 스트립은 `VirtualizingStackPanel` + `Recycling`으로 1,000장도 가볍게.
- 앱 매니페스트에 Per-Monitor V2 DPI, longPathAware 설정. 150%/200% 배율 수동 테스트.

---

## 6. 마일스톤

| 단계 | 기간(집중 개발) | 내용 | 완료 기준 |
|---|---|---|---|
| M-1 환경 | 0.5일 | .NET 10 SDK 설치, `dotnet new wpf`, 빌드/실행 확인 | `dotnet run`으로 빈 창 표시 |
| M0 기반 | 2~3일 | 솔루션/프로젝트, 메인 창, 파일·폴더 열기, JPEG/PNG 미리보기, 기본 줌/팬, 드롭, 매니페스트 | `WcImageViewer.exe photo.jpg`가 맞춤 표시 |
| M1 뷰어 | 1주 | 탐색, 맞춤/100%, 전체화면, EXIF 회전, GIF 애니메이션, 키보드/마우스, 상태바, 슬라이드쇼 | 폴더 100장을 키보드로 깜빡임 없이 탐색 |
| M2 폴더/썸네일 | 1주 | 비동기 썸네일, 가상화, 프리로드, LRU, 정렬/필터 | 1,000장 폴더 60fps 스크롤, 캐시된 다음 이미지 100ms 이내 |
| M3 압축 | 1~1.5주 | SharpCompress, ZIP/CBZ/7Z/CB7/RAR/CBR, 고체 추출 캐시, 진행률/취소, 제한, 압축 썸네일 | ZIP·7Z 탐색, 고체 7Z 진행률 표시, 손상 압축 예외 처리 |
| M4 마감 | 1주 | 다크 테마, High-DPI, 시작/메모리 프로파일링, 오류 처리, publish 프로파일, Inno Setup, 문서 | v0.1 portable + 설치본 배포, 목표 수치 달성 |

- v0.1 전체: 집중 개발 **5~7주**, 파트타임 **3~4개월** (1인 + AI 보조 기준).
- 압축파일이 v0.1 범위이므로 M4 끝에 첫 배포한다. M2 시점에 압축 없이 임시 빌드는 가능하지만 공식 배포는 M4.

---

## 7. 품질 기준과 테스트

- 콜드 스타트 **1초 이내**, 웜 스타트 **0.5초 이내**.
- 24MP JPEG 첫 표시 **400ms 이내**, 캐시된 다음 이미지 **100ms 이내**.
- 1,000장 폴더 썸네일 스크롤 **60fps**, 유휴 CPU **0%에 가깝게**.
- 메모리: 24MP 1장 + 기본 캐시에서 **300MB 이하**, 캐시 상한 256MB.
- 4K에서 미리보기 팬/줌 **60fps**. 50MP 원본 확대는 "사용 가능" 수준을 목표로 하고, 60fps 보장은 v0.2 타일링에서.
- 테스트:
  - 단위: 자연 정렬, 탐색 경계, 설정 원자적 저장, 압축 경로 정리, 압축 제한/zip bomb, EXIF 회전 변환, 줌 계산.
  - 통합: 런타임 생성 이미지 + `System.IO.Compression`으로 ZIP을 만들어 엔트리 목록·디코딩 검증. 7Z/RAR은 소형 샘플로 수동 검증(자동화는 샘플 확보 후).
  - UI 자동화는 초기 비용 대비 효율이 낮아 제외. 수동 체크리스트 사용.
- 측정: 시작 시간은 `Stopwatch` 마커, 메모리는 `Process.PrivateMemorySize64`, 프레임은 Visual Studio 진단 도구/PresentMon.
- CI: GitHub Actions `windows-latest`에서 restore/build/test/publish. 저장소 초기화 후 추가.

---

## 8. 리스크

| 리스크 | 대응 |
|---|---|
| WPF 시작·메모리 | R2R, 썸네일 지연 로딩, 캐시 상한, M0에서 조기 측정 |
| self-contained 크기 70~100MB | `lite` 프로파일 병행 제공, 필요 시 single-file 압축 |
| WIC 포맷 지원 한계 | v0.1은 보장 포맷만. WebP/AVIF/HEIC는 OS 코덱 확장 또는 v0.2 라이브러리 |
| SharpCompress RAR/7Z 예외 | 소형 코퍼스로 검증, 암호·멀티볼륨은 명확한 오류 후 v0.2 |
| 고체 압축 해제 시간·디스크 | 이미지 엔트리만 추출, 진행률/취소, 2GB 캐시 제한 |
| 압축 폭탄·경로 traversal | 엔트리 수/크기/총량 제한, 파일명만 사용, 제한 스트림 |
| 큰 이미지 렌더링 | 미리보기 우선, 100MP 초과는 512px 타일 디코딩·32타일 LRU로 제한 |
| GIF 타이밍/디스posal | 실제 GIF 코퍼스로 수동 검증, 프레임 메모리 상한 |
| High-DPI | Per-Monitor V2 매니페스트, 150%/200% 수동 테스트 |
| 코드 서명 비용 | 초기 미서명 배포, 정식 배포 전 인증서 확보 |

---

## 9. 확정된 결정과 다음 단계

확정:

- Windows 전용
- C# + WPF (.NET 10 LTS)
- v0.1에 ZIP/CBZ, 7Z/CB7, RAR/CBR 읽기 지원
- 기본 배포는 portable self-contained, `lite` 프로파일 병행
- 한국어 UI + 다크 테마, 편집/클라우드/모바일 제외

다음 단계:

1. 시작/메모리 프로파일링, 대형 이미지 미리보기 최적화
2. Inno Setup 설치본과 코드 서명 준비
3. TIFF 다페이지, ICC 색 관리, 애니메이션 AVIF

---

## 10. 진행 상태

- [x] .NET 10 SDK 10.0.402 설치
- [x] 솔루션 + WPF 앱 + xUnit 테스트 프로젝트 + SharpCompress 참조
- [x] JPEG/PNG 열기, 폴더 탐색, 이전/다음, 드래그 앤 드롭
- [x] 맞춤/100%, 휠 이전/다음, Ctrl+휠 커서 기준 줌, 드래그 팬, 전체화면
- [x] Canvas 기반 렌더링으로 큰 이미지가 잘리지 않고 전체 표시
- [x] 300 DPI 등 임베디드 DPI와 무관하게 픽셀 크기로 표시, 줌 중심 보정
- [x] 표시 배율 직접 입력(5~800%), 표시되는 이미지 크기에 맞춰 창 조정(기본 동작, W/버튼)
- [x] 세로로 긴 이미지는 창 폭을 최소값, 높이를 작업 영역 최대로 맞춤- [x] Tab 이미지 정보 오버레이, F2 이름 바꾸기, Del 삭제(확인/휴지통 설정)
- [x] Ctrl+E 편집 앱으로 열기, Ctrl+C 이미지 복사
- [x] 설정에서 편집 앱 선택, 확장자별 기본 앱 연결
- [x] 설정 창 카테고리(일반/보기·창/파일·편집/단축키/파일 연결)
- [x] F3 열기, F4 현재 이미지 닫기, F5 설정, F6 책갈피 관리, F11 전체화면, F12 다른 이름으로 저장
- [x] 사용자 단축키 변경(모든 키보드 단축키), 책갈피 관리, 다른 이름으로 저장
- [x] 여러 창 / 하나의 창으로 열기 선택 (다음 실행부터 적용)
- [x] 이미지 영역 드래그로 창 이동, 확대/팬 가능 시 이미지 이동
- [x] EXIF orientation 적용
- [x] 다크 테마, 제목줄에 파일명·개수·크기 통합, 단축키
- [x] 시작 시 최소 크기(800×600), 이전 창 위치 복원
- [x] GIF 애니메이션 재생
- [x] 썸네일 스트립(기본 숨김, 하단 호버·T 고정, 이미지 위 오버레이, 가상화/비동기/LRU)
- [x] 이전/다음 이미지 프리로드(현재 이미지 LRU 3장)
- [x] 슬라이드쇼(기본 3초, 설정에서 간격 변경, S 키/버튼)
- [x] 슬라이드쇼 반복 재생·랜덤 재생 설정 (랜덤은 한 바퀴 무작위 순서)
- [x] 파일 끝에서 이동 방식(첫 파일로 가기/아무것도 안 하기)과 중앙 경계 알림
- [x] ZIP/CBZ, 7Z/CB7, RAR/CBR 읽기
- [x] 고체 7Z/RAR 임시 추출 캐시, 진행률, 크기/항목 수 제한
- [x] 설정 저장(창 상태, 최근 항목, 썸네일 고정, 슬라이드 간격·반복·랜덤, 기본 배율, 이미지 열 때 창 맞춤, 파일 끝 이동·경계 알림, 삭제 확인/휴지통)
- [x] 최근 항목 메뉴, 설정 창(Ctrl+,)
- [x] portable/lite publish 프로파일과 `scripts/publish.ps1`
- [x] 테스트 49개 통과 (GIF·WebP 프레임/딜레이, GIF 투명 프레임 합성, EXIF 회전, ZIP/7Z 디코딩, 설정 저장·마이그레이션, 단축키 파싱, 타일 디코딩·회전, 맞춤 배율 포함)
- [x] RAR 실물 샘플 수동 검증
- [x] portable/lite 게시 실행 확인 (portable 약 66MB, lite 약 4MB)
- [x] WebP/AVIF/HEIC/HEIF 확장자 등록, WIC 코덱 안내 메시지
- [x] 애니메이션 WebP 재생 (WIC 메타데이터 + ANMF 지연 파싱, libwebp 지연 프레임 디코딩)
- [x] GIF 투명 프레임·disposal 합성 (WIC 프레임을 이전 캔버스 위에 누적)
- [x] 작은 이미지를 창에 맞춰 확대하지 않는 설정 (기본 켜짐)
- [x] 100MP 초과 이미지 타일 디코딩 (미리보기 + 512px 타일, 레벨 -2, 32타일 LRU)
- [ ] 시작/메모리 프로파일링, 대형 이미지 미리보기 최적화
- [ ] Inno Setup 설치본, 코드 서명

> WebP/AVIF/HEIC/HEIF는 OS의 WIC 코덱을 사용한다. 코덱이 없으면 해당 확장자에 한해 Microsoft Store 확장 설치 안내를 표시한다. HEIC/HEIF는 HEIF 이미지 확장과 HEVC 비디오 확장이 모두 필요하다.

### v0.2.0 릴리즈 (2026-10-23)

- 범위: WebP/AVIF/HEIC/HEIF, 애니메이션 WebP, GIF 투명 프레임 합성, 100MP 초과 타일링, 프레임 카운터, 작은 이미지 확대 옵션
- 릴리즈 노트: `docs/releases/v0.2.0.md`
- 다음 릴리즈(v1.0) 후보: Inno Setup 설치본, 코드 서명, 시작/메모리 프로파일링, TIFF 다페이지·ICC·애니메이션 AVIF

### v0.3.0 업데이트 (2026-10-24)

- 외관: Windows 기본 크롬을 28px 한 줄 커스텀 크롬으로 교체. 파일명·`i / N` 칩, 우측 아이콘 툴바·창 버튼, Windows 11 작은 라운드 모서리와 어두운 테두리.
- 테마: 라이트(은은한 베이지)/다크 전환. 설정 > 일반 > 테마에서 즉시 적용, 취소 시 원래 테마 복원, 재시작 후 선택 유지.
- 대화상자: Settings/Bookmark/Rename에 28px 커스텀 타이틀바와 공용 테마 컨트롤(ComboBox/CheckBox/ScrollBar/ContextMenu/ToolTip) 적용.
- 썸네일: 74px 플로팅 도크(기본 숨김, 하단 호버·`T`로 표시). Tab 정보 오버레이는 어두운 반투명 배경으로 가독성 개선.
- 테스트 49개 통과. 릴리즈 노트: `docs/releases/v0.3.0.md`

### v0.4.0 릴리즈 (2026-10-24)

- 업데이트: 시작 3초 후 GitHub 최신 릴리즈 확인(24시간 간격), 새 버전이면 설정 아이콘에 점 표시, 설정 > 업데이트에서 현재 버전/자동 확인/지금 확인/마지막 확인/건너뛴 버전(다시 알림) 관리.
- 종료 시 업데이트 창: 업데이트하고 종료 / 그냥 종료 / 이 버전 건너뛰기. ZIP을 내려받아 크기·SHA-256 digest를 검증하고, `WcImageViewer.Updater.exe`(.NET Framework 4.8, csc 컴파일)가 앱 종료 후 파일을 교체·재실행하며 실패 시 이전 버전으로 복원.
- 릴리즈 노트: GitHub Markdown 본문을 `ReleaseNotesFormatter`로 평문 변환(`#`, `**`, 백틱, 링크, 표, 코드펜스 제거)해 업데이트 창에 표시.
- 배포: ZIP 4개 엔트리(exe, updater, LICENSE, THIRD-PARTY-NOTICES.md). portable 62,188,029 B / lite 3,091,627 B.
- 테스트 87개 통과(포매터 6개 포함). 릴리즈 노트: `docs/releases/v0.4.0.md`
