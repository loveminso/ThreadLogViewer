# v0.5.0 메뉴·로그 탭·붙여넣기 검증 기록 — 2026-10-03

상태: 기능 구현, 전체 Release 빌드, 최종 자동 회귀 132개 통과와 새 portable 폴더·ZIP 검증을 완료했다. 실제 GUI는 실행·제어하지 않았다. 이번 실측은 합성 10 MiB의 디코딩 단계만 포함한다. 이전 [v0.4.1 검증](VALIDATION-v0.4.1.md)은 역사 기록으로 보존하며 이번 변경의 최종 결과로 대신 사용하지 않는다.

## 구현 범위

- 파일 / 편집 / 보기 / 분석 / 도움말 드롭다운과 동작 옆 단축키 힌트. 테마·글자 크기·행 간격·자동 줄바꿈을 보기 메뉴로 이동했다.
- 파일 다중 선택, 여러 파일 드롭, 클립보드 파일·텍스트를 각각 읽기 전용 탭으로 연다. 같은 파일은 기존 탭으로 이동한다.
- 탭마다 원본과 표시 문서, 스레드·내용 필터와 미적용 입력, 검색 조건·범위, 북마크·이름, 강조 규칙, A/B, 주변 보기와 복귀 조건, 현재 위치·선택을 현재 실행 중에 보관한다.
- 탭 클릭 또는 Ctrl+Tab / Ctrl+Shift+Tab으로 이동하고 Ctrl+W 또는 ×로 닫는다. 본문 편집·원본 저장·종료 후 세션 복원은 제공하지 않는다.
- BOM 없는 입력은 엄격한 UTF-8을 먼저 시도한 뒤 실패하면 엄격한 CP949로 대체하며 추정임을 표시한다. 직접 선택은 파일 → 인코딩으로 다시 읽기 안에 둔다. 잘못된 BOM 입력·UTF-32·두 해석 모두 실패하는 입력은 계속 거절한다.
- 클립보드 자료 읽기와 로그 열기의 예외 처리를 보완한다. 붙여넣기 실패는 상태줄에 안내하고 기존 로그를 유지한다. 오류 문구에는 로그 내용·경로·클립보드 제공자의 상세 자료를 포함하지 않는다.

런타임 로컬·오프라인, 원본 읽기 전용, UTF-8 새 파일 내보내기와 원본·기존 파일 덮어쓰기 차단 원칙을 유지한다. 메뉴의 ‘편집’은 복사·붙여넣은 로그 열기·선택·탐색 기능을 묶는 이름이다.

## 확인한 붙여넣기 종료 원인

이 PC의 Windows 오류 이벤트에서 2026-10-03 17:55:51에 실행된 v0.4.0.0의 붙여넣기 경로에 처리되지 않은 `System.IO.InvalidDataException`이 있었음을 확인했다. `ReadClipboard → PasteAsync → Paste_Click` 경로에서 발생했다. 이 예외는 `IOException`의 하위 형식이 아니므로 기존 `IOException` 예외 필터로 잡히지 않았다.

v0.5.0은 클립보드 경계에서 빈·지원하지 않는 자료와 제공자 예외를 처리하고, 파일 읽기·드롭의 `InvalidDataException` 경계도 보완한다. 실제 클립보드를 재현 조작하는 대신 합성 제공자와 입력으로 회귀를 검증한다. 오류 이벤트 원문, 실행 경로, 로그 내용은 이 문서에 복사하지 않았다.

## 빌드와 배포

| 항목 | 상태 | 결과 |
|---|---|---|
| 전체 Release 빌드 | 통과 | 기존 프로젝트 내부 SDK·NuGet 캐시로 `--no-restore`. 경고 0 / 오류 0. |
| 새 v0.5.0 portable 폴더 | 통과 | `artifacts/ThreadLogViewer-v0.5.0-win-x64`에 self-contained Release Windows x64 publish 성공. EXE FileVersion `0.5.0.0`, 런타임·AvalonEdit/.NET runtime/WPF 라이선스를 포함한 필수 10개 파일 확인. |
| 새 v0.5.0 ZIP | 통과 | `artifacts/ThreadLogViewer-v0.5.0-win-x64.zip`의 505개 파일 SHA-256이 portable 폴더와 일치한다. EXE FileVersion `0.5.0.0`, 필수 런타임·라이선스·문서·합성 샘플 포함과 `user-data/`, `local-data/`, `TestResults/`, Git 자료 제외를 확인했다. |

패키지 검증 결과는 [verification-v0.5.0.json](../artifacts/verification-v0.5.0.json)에 있다. 파일·ZIP 항목·해시·버전 메타데이터로 검사했으며 배포 EXE는 실행하지 않았다. 기존 배포 폴더와 사용자 파일을 보존했고 새 개발 도구 설치, GitHub 릴리스 업로드 또는 push는 수행하지 않았다.

## 자동 테스트

| 항목 | 상태 | 확인 내용 |
|---|---|---|
| 중간 전체 회귀 | 통과 | 122개 통과 / 실패 0 / 건너뜀 0. 결과 [integration-v0.5.0.trx](../TestResults/v0.5.0/integration-v0.5.0.trx). 이후 추가한 테스트의 최종 전체 결과는 별도 기록한다. |
| 최종 전체 회귀 | 통과 | 132개 통과 / 실패 0 / 건너뜀 0. 파싱·원본 줄 매핑·필터·검색·인코딩·원본 보호와 메뉴·세션·예외 경계 회귀 포함. 결과 [final-v0.5.0.trx](../TestResults/v0.5.0/final-v0.5.0.trx). |
| 세션 비표시 WPF 회귀 | 통과 | 8개 통과. 다중 파일과 독립적인 조건·위치·선택·북마크·시간·검색·강조·주변 보기, 중복 파일·닫기·재읽기·취소·늦게 끝난 작업의 게시 경계를 최종 전체 실행에서 확인. |
| 클립보드 회귀 | 통과 | 8개 통과. 빈/지원하지 않는 자료·제공자 예외·자료 배열 스냅샷·한글/개행·긴 로그 다음 짧은 로그를 합성 입력으로 확인. 실제 클립보드 사용 없음. |
| 메뉴 비표시 STA WPF 집중 회귀 | 통과 | 3개 통과 / 실패 0 / 건너뜀 0. 5개 메뉴·단축키 힌트·인코딩 메뉴 활성화, 실제 편집기 설정과 체크 동기화, 설정 파일 무변경, 두 테마의 팝업·보조 단축키 표시, 읽기 전용 본문. 결과 [menus-v0.5.0.trx](../TestResults/menu-v0.5.0/menus-v0.5.0.trx). |
| 인코딩 집중 회귀 | 통과 | 22개 통과. 자동 UTF-8/CP949, 명시적 CP949, BOM·잘못된 입력·원본 바이트 보존을 확인. 최종 전체 회귀에도 포함한다. |

합성 자료만 사용한다. WPF 테스트는 비표시 STA dispatcher에서 수행하고 창을 표시·활성화하거나 실제 클립보드를 조작하지 않는다. 새 회귀는 [MenuUxTests.cs](../tests/ThreadLogViewer.Tests/MenuUxTests.cs), [ClipboardRegressionTests.cs](../tests/ThreadLogViewer.Tests/ClipboardRegressionTests.cs), [EncodingTests.cs](../tests/ThreadLogViewer.Tests/EncodingTests.cs), [SessionTabsTests.cs](../tests/ThreadLogViewer.Tests/SessionTabsTests.cs)를 포함한다. 중간 122개 결과와 추가 회귀 후 최종 132개 결과를 구분한다.

프로젝트 루트에서 기존 도구·캐시만 사용한다.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
& .\.tools\dotnet\dotnet.exe build ThreadLogViewer.slnx -c Release --no-restore
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MenuUxTests --logger 'trx;LogFileName=menus-v0.5.0.trx' --results-directory TestResults\menu-v0.5.0
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=final-v0.5.0.trx' --results-directory TestResults\v0.5.0
```

## 실제 GUI 확인

**미실행.** 사용자의 프로그램 제어 금지 지시를 유지해 앱을 실행하거나 화면을 조작하지 않았다. 비표시 WPF 테스트와 실제 사용자 화면의 클릭·스크롤·DPI·메뉴 키보드 조작·클립보드 확인은 구분한다. 직접 확인할 절차는 [메뉴와 여러 로그 탭 확인](FEATURE-TEST-GUIDE.md#메뉴와-여러-로그-탭-확인)에 있다.

## 실측 성능

합성 **10 MiB = 10,485,760바이트** 배열의 `LogFileReader.Decode`만 측정했다. 각 조건에서 준비 실행 3회와 측정 7회를 수행하고 중앙값을 기록했다. .NET Runtime 10.0.12, Windows 10.0.26200.0, 논리 프로세서 12개인 한 PC·한 세션에서 2026-10-03 18:36:46 KST에 얻은 결과다.

| 합성 입력과 해석 조건 | 중앙값 |
|---|---:|
| BOM 없는 UTF-8 · 자동 | 7.16 ms |
| CP949 · UTF-8 초기 실패 후 자동 대체 | 18.55 ms |
| CP949 · UTF-8 후반 실패 후 자동 대체 | 21.35 ms |
| 같은 후반 실패 입력 · 명시적 CP949 | 20.77 ms |

원시 반복 결과와 측정 범위는 [encoding-performance.json](../TestResults/encoding-performance-697669fc/encoding-performance.json)에 있다. GC는 측정 타이머 밖에서 수행했다. 입력 조건마다 정상적으로 해석된 문자 수가 다르다. 디스크 읽기·파싱·투영·AvalonEdit 문서 생성·WPF 배치/렌더·GUI 반응성과 탭 전환 시간을 포함하지 않는다. 이 수치는 전체 열기 시간이나 모든 PC의 성능을 보장하지 않는다.

**전체 GUI와 여러 탭의 성능은 미측정.** 탭마다 원본·표시 문서를 메모리에 유지하므로 파일 수와 크기에 따라 메모리가 증가한다. 과거 10/100 MiB Core 및 GUI 결과는 [v0.4](VALIDATION-v0.4.md), [v0.3](VALIDATION-v0.3.md)에 보존하며 이번 디코딩 수치와 직접 비교하지 않는다.
