# v0.4.1 화면 개선 검증 기록 — 2026-10-03

상태: 화면 개선 구현, 전체 Release 빌드·자동 테스트, 새 portable 배포 폴더·ZIP 검증을 완료했다. 실제 사용자 화면 검증과 이번 UI 성능 측정은 수행하지 않았다. 이전 [v0.4.0 검증](VALIDATION-v0.4.md)은 이번 변경의 빌드·테스트 결과로 대신 사용하지 않는다.

## 구현 범위

- 분석 도구를 주변 로그 / 두 기록 시간 차이 / 문구 강조로 분리하고 선택한 조작부만 표시한다.
- 현재 선택 원본 줄·소유 헤더, 주변 보기 기준·실제 앞뒤 건수·모든 스레드 상태를 안내한다.
- 주변 보기에서는 스레드·내용 필터 변경만 잠그고 목록 읽기와 북마크 사용은 유지한다.
- 기준 기록 표시와 현재 보기 범위에 맞는 내보내기 이름을 제공한다.
- A/B 시간 비교의 사용 순서와 유효한 지정값에 따른 버튼 상태를 제공한다.
- 문구 강조 입력 이름·예시·빈 목록 안내·Enter 추가를 제공한다.
- 내용 필터의 포함 조건을 자연어로 표시하고 입력 중인 조건과 현재 적용 조건을 구분한다.
- 다크·라이트 테마의 스레드 목록 대비를 개선한다.

새로운 고유 단축키나 사용자 단축키 지정 기능은 추가하지 않는다. 기존 익숙한 단축키를 유지한다. 원본 로그 읽기 전용·새 파일로 안전 내보내기·로컬 오프라인 원칙을 유지한다.

## 빌드와 배포

| 항목 | 상태 | 결과 |
|---|---|---|
| 전체 Release 빌드 | 통과 | 이미 설치된 SDK·캐시로 `--no-restore` 빌드. 경고 0 / 오류 0. |
| 새 v0.4.1 배포 폴더 | 통과 | self-contained Release publish 성공. `artifacts/ThreadLogViewer-v0.4.1-win-x64`의 EXE FileVersion `0.4.1.0`, 런타임·라이선스를 포함한 필수 10개 파일 확인. 기존 배포 폴더와 사용자 파일 보존. |
| 새 v0.4.1 배포 ZIP | 통과 | `artifacts/ThreadLogViewer-v0.4.1-win-x64.zip` 생성. 504개 파일의 SHA-256이 portable 폴더와 일치하며 EXE 버전 `0.4.1.0`, 필수 런타임·라이선스·문서·합성 샘플 포함을 확인했다. `user-data/`, `local-data/`, `TestResults/`는 제외했다. |

패키지 검증 기록은 [verification-v0.4.1.json](../artifacts/verification-v0.4.1.json)이다. 패키지 검사는 파일·ZIP 항목·해시·버전 메타데이터로 수행했으며 배포 EXE를 실행하지 않았다. 기존 배포물·원본 로그·사용자 파일을 보존했고 새 도구 설치·업로드·push는 수행하지 않았다.

## 자동 테스트

| 항목 | 상태 | 확인 내용 |
|---|---|---|
| 전체 자동 회귀 | 통과 | 기존 파싱·매핑·검색·인코딩·원본 보호 및 새 UI 회귀를 포함해 96개 통과 / 실패 0 / 건너뜀 0. |
| 분석 화면 비표시 STA WPF 회귀 | 통과 | 집중 실행 5개 통과 / 실패 0 / 건너뜀 0이며 최종 전체 실행에도 포함. 주변 건수·파일 양끝·소유 헤더 표시, 필터 잠금과 북마크 유지, 내보내기 이름·복귀, 도구 분리, 시간 버튼 상태, 문구 Enter 추가·빈 안내·상한, 필터 입력·적용 상태. |
| 테마·기준 기록 표시 회귀 | 통과 | 두 테마의 목록 배경·글자 대비, 활성화 여부와 무관한 목록 스타일, 기준 기록의 visible-line renderer 검증. 최종 전체 실행에 포함. |

비표시 WPF 테스트는 기존 STA dispatcher를 사용하며 창을 `Show`하거나 활성화하지 않는다. 사용자 클립보드나 데스크톱을 조작하지 않는다. 새 테스트 소스는 [AnalysisUxTests.cs](../tests/ThreadLogViewer.Tests/AnalysisUxTests.cs)와 [ThemeContextTests.cs](../tests/ThreadLogViewer.Tests/ThemeContextTests.cs), 집중 실행 결과는 [analysis-ux.trx](../TestResults/v0.4.1/analysis-ux.trx), 최종 전체 결과는 [all-after-binding.trx](../TestResults/v0.4.1/all-after-binding.trx)다.

첫 통합 검증의 테스트 준비 문제를 수정했다: TextView viewport를 레이아웃으로 준비하고, 합성 Enter 이벤트에 KeyDown routed event를 지정하고, dispatcher의 DataBind 전송 완료 후 바인딩을 검사했다. 공개 API 동작은 변경하지 않았다. 최종 테스트는 실제 스레드 행의 체크박스·단독 버튼과 필터 조작부가 비활성화되고, 목록과 북마크는 활성 상태임을 함께 확인한다.

프로젝트 루트에서 다음 명령으로 분석 화면 회귀를 검증했다. 새 SDK나 패키지를 설치하지 않았다.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AnalysisUxTests --logger 'trx;LogFileName=analysis-ux.trx' --results-directory TestResults\v0.4.1
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=all-after-binding.trx' --results-directory TestResults\v0.4.1
```

## 실제 GUI 확인

**미실행.** 사용자의 프로그램 제어 금지 지시를 유지한다. 앱을 자동 실행하거나 화면을 조작하지 않는다. 실제 사용자 화면의 색 대비·DPI·클릭·스크롤 확인은 자동 WPF 테스트와 구분한다. 직접 확인할 절차는 [기능 테스트 방법의 분석 화면 개선 확인](FEATURE-TEST-GUIDE.md#분석-화면-개선-확인)을 따른다.

## 실측 성능

**이번 UI 개선의 성능은 미측정.** 이전 합성 Core 콘솔 수치는 [v0.4.0 검증](VALIDATION-v0.4.md)에 보존한다. 이를 이번 UI의 렌더링·응답성 실측으로 해석하지 않는다. 실제 대용량 화면 스크롤·도구 전환·장시간 사용·다른 PC/DPI는 추가 확인 대상이다.
