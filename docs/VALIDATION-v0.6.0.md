# v0.6.0 줄 작업·범위 탭 검증 기록 — 2026-10-03

상태: 구현을 완료하고 기존 SDK·캐시를 사용한 Release 빌드에서 경고 0 / 오류 0, 전체 자동 테스트 170/170 통과를 확인했다. 새 self-contained portable 폴더·EXE 파일 버전 0.6.0.0 및 ZIP 506개 파일 전체의 배포 폴더 대비 SHA-256 일치를 확인했다. 줄 작업·기존 분석 로직의 집중 회귀와 합성 콘텐츠 비표시 렌더링도 통과했다. 실제 GUI는 실행·제어하지 않았으며 이번 변경의 성능은 측정하지 않았다. 이전 [v0.5.0 검증](VALIDATION-v0.5.0.md)은 당시의 결과로 보존한다.

## 구현 범위

- 고정 분석 패널을 없애고 실제 우클릭한 원본 물리적 줄을 작업 대상으로 고정한다. 이전 커서 위치를 사용하지 않으며 탭·표시 투영이 바뀐 오래된 메뉴 대상은 거절한다.
- 시작/끝 원본 줄을 포함한 범위를 새 읽기 전용 탭으로 분리한다. 필터로 숨긴 중간 줄도 포함한다. 반대 순서·1줄·부분 기록·범위 안의 중첩 분리를 지원하고 원본 개행·줄 번호를 유지한다.
- 범위 탭의 필터·전체 원본 검색·선택 검색·주변 보기·원본 이동·내보내기는 그 범위로 제한한다. 원본 전체 정보는 공유하지만 범위 밖 본문을 표시하거나 내보내지 않는다.
- 시간 기준 줄 지정 후 비교 줄의 시간 차이를 간단한 결과 줄에 표시한다. 비교 − 기준의 부호, 원본 위치, A/B 여백 표식과 결과 복사·이동·해제를 제공한다. 잘못된 시각은 이전 유효한 지정값을 유지한다.
- 분리 시작의 원본 번호와 다음 조작을 간단한 표시줄로 안내한다. 주변 범위는 줄 우클릭에서 5/20/100기록 또는 직접 지정 0~10,000으로 정한다. 시간 결과·이동/북마크·텍스트 작업을 하위 메뉴로 묶는다.
- Shift+F8 문구 강조 창은 선택 문구를 미리 채우고 최대 8개 규칙을 관리한다. 적용 전 초안과 취소를 현재 탭의 확정 규칙과 분리한다.
- F3 다음 / F4 이전 검색, Shift+F3 이전 별칭을 유지한다. 분리 시작·시간·강조·위치·필터 상태는 탭마다 독립적이다.

본문 읽기 전용·원본/기존 파일 덮어쓰기 차단·실행 중 로컬 오프라인 원칙을 유지한다. 범위를 새 탭으로 여는 작업은 원본 파일을 수정하거나 따로 저장하지 않는다. 새 설치·업로드·push를 수행하지 않는다.

## 빌드와 배포

| 항목 | 상태 | 결과 |
|---|---|---|
| Release 빌드 | 통과 | 프로젝트 내부 SDK·NuGet 캐시, `--no-restore`. 경고 0 / 오류 0. |
| 새 portable 폴더 | 통과 | `artifacts/ThreadLogViewer-v0.6.0-win-x64`, self-contained Windows x64 폴더형. 기존 SDK·캐시로 오프라인 publish. EXE 파일 버전 0.6.0.0, runtime·라이선스 파일 480개. |
| 새 배포 ZIP | 통과 | `artifacts/ThreadLogViewer-v0.6.0-win-x64.zip`. 506개 파일 모두 배포 폴더와 SHA-256 일치. 사용자 자료·개발 환경 제외 확인. 파일 버전과 해시 메타데이터는 [verification-v0.6.0.json](../artifacts/verification-v0.6.0.json)에 기록. |

기존 v0.5.0 및 과거 배포 폴더·ZIP·사용자 파일을 보존한다. 배포 EXE를 실행하는 검사는 수행하지 않는다.

배포에는 실행에 필요한 runtime·라이선스, 합성 샘플, README·문서를 포함한다. `user-data`, `local-data`, `TestResults`, Git 자료, 개발 SDK·패키지 캐시를 포함하지 않는다. ZIP 자체의 정확한 해시는 배포 바깥 검증 JSON에 기록하며 묶인 문서에는 넣지 않는다.

## 자동 테스트

| 항목 | 상태 | 확인 내용 |
|---|---|---|
| 줄 흐름·기존 분석 로직 집중 회귀 | 통과 | 11개 통과 / 실패 0 / 건너뜀 0. 결과 [line-workflow-after-binding.trx](../TestResults/v0.6.0/line-workflow-after-binding.trx). |
| 합성 콘텐츠 비표시 레이아웃 렌더링 | 통과 | 1개 통과 / 실패 0 / 건너뜀 0. 1360×860 범위 탭·시간 결과·분리 시작·강조·검색 상태에서 본문 높이 250px 초과와 범위 경계를 검사. 결과 [offscreen-layout.trx](../TestResults/v0.6.0/offscreen-layout.trx), [PNG](../TestResults/v0.6.0/offscreen-v0.6.0.png). |
| 줄 동작 집중 회귀 | 통과 | 10개 통과 / 실패 0 / 건너뜀 0. 실제 클릭 대상과 오래된 메뉴 정리, 시간 기준, 강조 적용/취소 등. |
| 지연된 파일 열기 순서 집중 회귀 | 통과 | 1개 통과 / 실패 0 / 건너뜀 0. 활성 파일을 다시 열면 이전 지연 로드를 취소하고 늦은 로그·진행 메시지를 게시하지 않으며 현재 탭의 위치·북마크·시간을 유지. |
| 최종 전체 회귀 | 통과 | 170개 통과 / 실패 0 / 건너뜀 0. 새 범위 Core·줄 동작·세션·기존 원본 보호 회귀 포함. 결과 [final-v0.6.0.trx](../TestResults/v0.6.0/final-v0.6.0.trx). |

[LineWorkflowUxTests.cs](../tests/ThreadLogViewer.Tests/LineWorkflowUxTests.cs)의 7개는 실제 클릭 줄과 이전 커서, 오래된 투영 거절, 필터로 숨긴 중간 물리적 줄, 두 끝 포함·역순·1줄·중첩 범위, 원본 번호·불변 원문, 범위 밖 검색/문맥/줄 이동/필터 차단, 탭별 분리 시작·시간, 간단한 표시줄과 A/B 표식, F3/F4 키 처리와 Shift+F8 메뉴 힌트, 비표시 합성 레이아웃을 확인한다. [AnalysisUxTests.cs](../tests/ThreadLogViewer.Tests/AnalysisUxTests.cs)의 기존 5개는 주변 보기·필터 상태·강조 상한 등 의미 있는 회귀를 유지하고 분석 패널 대신 간단한 시간 결과를 검사한다.

추가 [LineActionsTests.cs](../tests/ThreadLogViewer.Tests/LineActionsTests.cs)는 실제 클릭 대상·오래된 메뉴·시간·강조 창 적용/취소를 합성 콜백으로 확인하고, [ScopedProjectionTests.cs](../tests/ThreadLogViewer.Tests/ScopedProjectionTests.cs)는 UI와 독립적인 범위 파싱/투영·검색·원본 안전 내보내기 경계를 검증한다. [SessionOrderingTests.cs](../tests/ThreadLogViewer.Tests/SessionOrderingTests.cs)는 현재 파일 재열기와 이전 지연 로드의 게시 차단을 확인한다. 이 테스트들은 최종 전체 170개 실행에 포함된다.

창을 표시·활성화하거나 실제 클립보드를 조작하지 않았다. 합성 자료를 비표시 STA dispatcher에서 사용했다. ElementName 바인딩은 ContextIdle 전달 완료 후 검사해 비표시 창의 아직 전달되지 않은 기본값을 실제 UI 결과로 오인하지 않는다.

PNG는 숨긴 창의 콘텐츠만 `RenderTargetBitmap`으로 그린 자동 검증 자료다. 창을 화면에 띄운 실제 GUI 확인과 구분한다. 최종 전체 테스트에서 다시 생성한 1360×860 이미지를 읽어 범위 탭 제목·스레드 목록·검색 결과·간단한 시간/분리 표시줄과 로그 공간이 테마 배경에서 읽히고 잘리지 않는 것을 확인했다. 실제 창의 제목줄·DPI·우클릭 위치·팝업은 이 이미지로 검증하지 않는다.

프로젝트 루트에서 실행한 집중 검증:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~LineWorkflowUxTests|FullyQualifiedName~AnalysisUxTests' --logger 'trx;LogFileName=line-workflow-after-binding.trx' --results-directory TestResults\v0.6.0
```

## 실제 GUI 확인

**미실행.** 사용자의 프로그램 제어 금지 지시를 유지한다. 실제 우클릭 위치·DPI·메뉴 키보드·강조 관리 창·스크롤·클립보드 복사는 비표시 자동 테스트와 구분해 직접 확인해야 한다. 절차는 [줄 우클릭·세션 분리·간단한 시간 비교 확인](FEATURE-TEST-GUIDE.md#줄-우클릭세션-분리간단한-시간-비교-확인)과 [Shift+F8 강조 및 F3/F4 확인](FEATURE-TEST-GUIDE.md#shiftf8-문구-강조와-f3f4-검색-확인)에 있다. 가이드의 기대 결과를 GUI 통과 기록으로 보고하지 않는다.

## 실측 성능

**이번 변경은 미측정.** 원본 범위 투영·중첩 탭·강조 관리 창·우클릭·실제 스크롤·전체 GUI의 시간과 메모리를 별도 측정하지 않았다. 이전 [v0.5.0의 합성 10 MiB 디코딩 수치](VALIDATION-v0.5.0.md#실측-성능)는 디코딩 단계만의 결과이며 이번 범위 탭이나 GUI 반응성을 대표하지 않는다. 과거 [v0.4](VALIDATION-v0.4.md), [v0.3](VALIDATION-v0.3.md) 측정도 해당 버전과 범위를 유지한다.

원본 전체는 불변 자료로 공유하고 탭별 표시 문서를 보관하므로, 파일 수·범위 수·줄 길이에 따라 메모리 사용량이 달라진다. 재실행 후 탭 복원은 이번 범위에 포함하지 않는다.
