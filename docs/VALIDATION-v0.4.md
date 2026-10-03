# v0.4.0 검증 기록 — 2026-10-03

상태: 구현, 전체 Release 빌드·자동 테스트, 별도 v0.4 배포 폴더·ZIP 생성과 패키지 확인을 완료했다. 실제 사용자 화면 검증은 수행하지 않았다. 이전 v0.3 결과는 이번 버전의 검증 결과로 사용하지 않는다.

## 구현 범위

[높은 우선순위 상세 설계](HIGH-PRIORITY-FEATURES.md)의 1~11번을 대상으로 한다.

- 원본 줄 이동, 현재 위치·선택 정보, 세션 북마크·이름.
- 대소문자·단어·정규식·범위 검색과 검색 결과 목록.
- 여러 키워드 강조와 기록 단위 포함·제외 필터.
- 모든 스레드의 앞뒤 문맥, 필터 변경 후 원본 위치 유지.
- 표시 설정 저장과 두 기준점의 기록 시간 차이.
- 익숙한 고정 [단축키](SHORTCUTS.md). 사용자 단축키 지정 기능은 이번 범위에서 제외한다.

원본 보기와 내보내기 보호, 로컬·오프라인 실행 원칙을 유지한다. samples·테스트·성능 입력에는 합성 데이터만 사용한다.

## 빌드

| 항목 | 상태 | 결과 / 조건 |
|---|---|---|
| Windows x64 Release 빌드 | 통과 | 첫 전체 빌드와 최종 테스트의 빌드 성공, 경고 0 / 오류 0. |
| 별도 v0.4 배포 폴더 생성 | 통과 | self-contained `artifacts/ThreadLogViewer-v0.4.0-win-x64` 생성. 기존 v0.3 배포 폴더와 사용자 파일을 보존했다. |
| 배포 ZIP·의존성 확인 | 통과 | `artifacts/ThreadLogViewer-v0.4.0-win-x64.zip` 생성. 503개 ZIP 항목에서 실행 파일, AvalonEdit·.NET/WPF 의존성, 라이선스, 합성 샘플, 문서, README를 확인했다. |

오프라인 검증은 이미 설치된 프로젝트 SDK·NuGet 캐시를 이용해 `--no-restore`로 수행했다. 별도 개발 도구 설치·업로드·push는 수행하지 않았다.

배포 EXE와 앱 어셈블리의 실제 버전은 `0.4.0.0`이다. ZIP에는 `user-data/`, `local-data/`, `TestResults/`를 포함하지 않는다. 패키지 검사는 파일·ZIP 항목과 버전 메타데이터로 수행했으며 배포 EXE를 실행하지 않았다.

프로젝트 루트에서 사용한 PowerShell 명령은 다음과 같다. 배포 출력 경로는 최초 생성 전에 프로젝트 내부의 링크 없는 새 경로임을 확인했다. 다시 배포할 때는 기존 폴더를 재사용하기 전에 사용자 파일 여부를 확인하고 새 출력 폴더를 선택한다.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
& .\.tools\dotnet\dotnet.exe build ThreadLogViewer.slnx -c Release --no-restore
& .\.tools\dotnet\dotnet.exe test ThreadLogViewer.slnx -c Release --no-restore --logger 'trx;LogFileName=v0.4-final.trx' --results-directory TestResults/v0.4
& .\.tools\dotnet\dotnet.exe publish src\ThreadLogViewer.App\ThreadLogViewer.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o 'C:\Users\kasta\OneDrive\문서\code\ThreadLogViewer\artifacts\ThreadLogViewer-v0.4.0-win-x64'
```

## 자동 테스트

| 대상 | 상태 | 확인할 경계 |
|---|---|---|
| 기존 파싱·인코딩·원문 보존 | 통과 | CRLF/LF/CR, UTF-8/UTF-16/CP949, 본문 기록 경계. |
| 원본 줄 매핑·탐색 | 통과 | 필터 불연속 줄, 숨긴 줄, 가까운 줄, 빈 결과, 가상 마지막 행. |
| 검색·강조 | 통과 | 대소문자·단어·정규식, 선택 경계, 기록 간 가짜 일치, timeout, 0길이 일치, 겹친 일치. |
| 포함·제외 필터·문맥 | 통과 | AND/OR, 본문 일치, 기록 전체 보존, 중심 기록과 양끝 경계. |
| 시간 차이 | 통과 | 본문 소유 헤더, 1 tick·12ms, 누적 시간, 음수, 유효 시간 없음. |
| 설정 저장 | 통과 | 정상 왕복, 잘못된 값, 손상·미래 스키마 보존, 원본·하드링크 별칭 보호, 저장 실패 정리. |
| source-safe 내보내기 | 통과 | 원본·기존 파일 차단, 취소·실패 정리, 원본 바이트 보존. |
| UI 자동 회귀 | 통과 | 기존 회귀에 추가된 비표시 WPF STA 테스트 7개가 전체 테스트에 포함된다. 실제 사용자 화면 조작과 구분한다. |

최종 전체 Release 자동 테스트는 **87개 통과, 실패 0, 건너뜀 0**이다. 결과 파일은 [v0.4-final.trx](../TestResults/v0.4/v0.4-final.trx)다. 첫 전체 테스트의 80개는 [v0.4-initial.trx](../TestResults/v0.4/v0.4-initial.trx)에, 비표시 WPF 7개 단독 실행은 [window-features.trx](../TestResults/window-features.trx)에 기록했다.

추가 WPF 테스트는 창을 `Show`하거나 활성화하지 않은 STA dispatcher에서 다음을 확인했다: 표시 설정이 실제 editor에 적용됨, 문서 성공 교체와 취소의 상태 수명, 근접 줄 이동과 0줄 화면의 앵커 복원, CRLF 안의 LF 정규식 일치 위치와 편집기 선택 경계, 최소 1040×600에서 도구·8개 강조 규칙·검색 결과를 모두 펼쳐도 로그 영역 70px 초과 유지, 숨긴 결과의 문맥 전환 후 F3 탐색 계속, 0길이 결과 이동과 사각형 강조 제외. 클립보드와 사용자 데스크톱은 제어하지 않았다.

## 실제 GUI 확인

**미실행.** 사용자가 프로그램 제어를 하지 말라고 지시했으므로 앱 실행·화면 조작으로 확인하지 않는다. 자동 WPF 검증과 실제 사용자 화면 확인은 구분한다.

사용자가 확인할 주요 흐름: 합성 로그 열기 → `Ctrl+G` 이동 → `Ctrl+F2` 북마크와 `F2` 이동 → 검색 옵션과 결과 클릭 → 포함·제외 필터 → 문맥 보기·복귀 → A/B 지정·교환 → 표시 설정 변경·재실행. 자세한 기대 결과는 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)을 따른다.

## 합성 성능 실측

**Core 콘솔 실측 완료, GUI 성능 미측정.** 합성 10/100 MiB를 각각 새 콘솔 프로세스에서 한 번 측정했다. 합성 입력 생성은 시간 측정에서 제외했다. 전체·필터 투영과 두 검색 결과를 측정 종료까지 유지했다. AvalonEdit 문서 생성, WPF 배치·렌더링, 가상화 결과 목록의 시간·메모리는 포함하지 않는다.

| 항목 | 10 MiB | 100 MiB |
|---|---:|---:|
| 실제 파일 크기 (바이트) | 10,485,818 | 104,857,662 |
| 기록 / 원본 줄 수 | 111,288 / 117,835 | 1,112,876 / 1,178,340 |
| 선택 기록 / 원본 줄 수 | 18,969 / 20,084 | 189,695 / 200,854 |
| 읽기·디코딩·파싱 (ms) | 314.17 | 1,561.91 |
| 전체 Core 투영 (ms) | 21.84 | 67.47 |
| 기록 포함·제외 필터 (ms) | 11.84 | 150.54 |
| 필터 결과의 단어 검색 (ms) | 12.79 | 48.03 |
| 전체 원본 정규식 검색, 첫 100,000개까지 (ms) | 41.60 | 46.25 |
| 단어 검색 결과 수 | 3,825 | 38,259 |
| 측정 종료 작업 집합 (바이트) | 142,110,720 | 733,585,408 |
| 프로세스 최대 작업 집합 (바이트) | 142,110,720 | 1,000,009,728 |

필터 조건은 스레드 3·7·11, 포함 `timeout` 또는 `retry`, 제외 `heartbeat`다. 단어 검색은 필터 결과에서 `timeout`을 단어 단위·대소문자 무시로 찾았으며 상한에 걸리지 않았다. 정규식은 전체 원본에서 `LBA=\d{9}`를 대소문자 무시·CultureInvariant·250ms match timeout으로 검색했고, Multiline/Singleline은 끈 상태다. 100,001번째 실제 일치를 확인한 후 첫 100,000개만 반환했다. 따라서 정규식 시간은 그 뒤의 파일 전체를 검색하는 시간이 아니다.

환경: .NET 10.0.12, 논리 프로세서 12개. 원시 결과는 [10 MiB JSON](../TestResults/core-performance/run-10/advanced-core-10MiB.json), [100 MiB JSON](../TestResults/core-performance/run-100/advanced-core-100MiB.json)이다. 단회 측정이며 반복 평균·다른 PC의 성능·실제 GUI 응답성을 보장하지 않는다. 입력과 측정 범위가 기존 v0.3과 다르므로 수치를 직접 비교해 앱 메모리가 개선됐다고 해석하지 않는다.

합성 입력 생성기와 측정 코드는 [Core 콘솔 측정 소스](../TestResults/core-performance/Program.cs), 프로젝트는 [CorePerformance.csproj](../TestResults/core-performance/CorePerformance.csproj)에 보존했다. 입력은 16개 스레드, 반복되는 합성 메시지와 덤프 본문으로 구성된다. `TestResults/`는 로컬 검증 산출물이며 배포본에는 포함하지 않는다.

## 남은 확인과 알려진 제한

- 실제 사용자 데스크톱의 창 배치·클릭·스크롤·색 대비·설정 복원은 미확인.
- 다른 PC·DPI·고대비·장시간 사용은 미확인.
- 전체 메모리 로딩 구조의 대용량 개선, 다중 탭, 파일 비교, 실시간 추적, 사용자 단축키 지정은 이번 범위가 아니다.
- 날짜 없는 기록 시간은 자정 통과나 시계 역행을 자동 보정하지 않는다.
- 정규식 검색 결과는 원본 오프셋·길이를 정확히 보존한다. 다만 AvalonEdit는 CRLF의 한 문자만 선택하려고 하면 CRLF 전체를 선택하는 자체 개행 경계 규칙을 적용할 수 있다. 비표시 WPF 테스트로 이 동작을 확인했으며 원문 변경은 없다.
