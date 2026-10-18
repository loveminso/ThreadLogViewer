# ThreadLog Viewer 현재 구조

이 문서는 `main`의 현재 코드가 어떻게 구성되는지 적는다. 적힌 경로와 형식 이름은 그 파일에서 확인할 수 있다. 후속 후보와 아직 없는 화면은 [계획](PLAN.md)에 있고, 여기서는 구현된 동작만 적는다.

## 목적과 제약

Windows x64용 읽기 전용 로그 뷰어다. UI는 `src/ThreadLogViewer.App`, 파일·파싱·필터·검색은 `src/ThreadLogViewer.Core`다. 화면은 WPF와 AvalonEdit다 (`src/ThreadLogViewer.App/ThreadLogViewer.App.csproj`).

실행 중 앱은 로컬 파일만 다룬다. `src/`에는 HTTP 클라이언트, 소켓, 텔레메트리 호출이 없다. 로그인, API, 서버, DB도 없다.

원본 로그는 읽기 전용으로 연다. `LogFileReader.ReadSnapshotAsync`가 `FileAccess.Read`와 `FileShare.ReadWrite | FileShare.Delete`로 연다 (`src/ThreadLogViewer.Core/LogFileReader.cs`). 같은 공유로 로그를 연 다른 프로그램이 이어서 쓰거나, 회전을 위해 이름을 바꾸거나 지워도 열기 자체가 공유 위반으로 막히지 않는다. 다른 프로그램이 파일을 독점으로 열면 열 수 없다. 길이는 연 직후 한 번만 잡고, 그 길이만 읽는다. 그 다음 늘어난 바이트는 이번 읽기에 넣지 않는다. 읽기 도중 짧아져 그 길이를 채우지 못하면 `읽는 도중 파일 길이가 변경되었습니다. 다시 열어 주세요.`로 실패한다.

내보내기는 새 파일만 만든다. `LogExporter.ValidateDestination`은 원본과 같은 경로와 이미 있는 파일을 거절한다 (`src/ThreadLogViewer.Core/LogExporter.cs`). 화면 설정은 `SettingsStore`가 실행 폴더의 `user-data\ui-settings.json`에 쓰고, 실행 폴더에 설정 파일이 없고 그 위치에 쓸 수 없을 때만 사용자 로컬 앱 데이터 아래 `ThreadLogViewer\ui-settings.json`에 쓴다. 처리되지 않은 예외 기록은 `LocalExceptionLog`가 사용자 로컬 앱 데이터 아래 `ThreadLogViewer` 폴더의 `unhandled-exceptions.log`에만 쓴다. 둘 다 열린 로그 파일에는 쓰지 않는다.

## 프로젝트 배치

| 경로 | 역할 |
|---|---|
| `src/ThreadLogViewer.App/` | WPF 창, AvalonEdit, 설정 저장, 예외 창 |
| `src/ThreadLogViewer.Core/` | UI 없는 읽기, 인코딩, 파싱, 투영, 검색, 내보내기 |
| `tests/ThreadLogViewer.Tests/` | xUnit 단위 테스트. `ThreadLogViewer.slnx`에 포함 |
| `tests/ThreadLogViewer.UiTests/` | 게시한 `ThreadLogViewer.exe`를 FlaUI UIA3로 조작. 패키지 참조는 `tests/ThreadLogViewer.UiTests/ThreadLogViewer.UiTests.csproj`. 솔루션에는 넣지 않음 |
| `scripts/publish.ps1` | `win-x64` self-contained 폴더와 그 폴더를 묶은 ZIP |
| `scripts/verify-release.ps1` | 릴리스 전에 테스트 통과, 런타임, 버전, ZIP 항목을 검사 |

`ThreadLogViewer.slnx`를 대상으로 한 `dotnet test`는 단위 테스트만 실행한다. UI 테스트는 게시 뒤에 `tests/ThreadLogViewer.UiTests/ThreadLogViewer.UiTests.csproj`를 대상으로 따로 실행한다. 절차와 시나리오 표는 [검증 안내](TESTING.md), 기능별 조작과 매핑은 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)을 따른다.

## 구성과 데이터 흐름

한 탭의 원문은 `LogData`다. 디코딩된 문자열, 물리 줄 `LogLine`, 시간 헤더 단위 `LogEntry`, 스레드 요약 `ThreadSummary`를 가진다 (`src/ThreadLogViewer.Core/LogModel.cs`). 화면에 보이는 줄은 `LogProjection`이다. 원본 줄 번호와 표시 줄 번호를 따로 둔다 (`src/ThreadLogViewer.Core/LogProjection.cs`). 탭 상태는 `MainWindow` 안의 `LogSession`이다 (`src/ThreadLogViewer.App/MainWindow.Sessions.cs`).

### 파일 열기

명령줄, 파일 대화상자, 끌어 놓기, 클립보드 파일은 `MainWindow.LoadIntoSessionAsync`로 모인다 (`src/ThreadLogViewer.App/MainWindow.xaml.cs`). 경로로 여는 경우 `OpenAsync`가 먼저 같은 경로의 전체 파일 탭을 찾고, 자동 인코딩이면 그 탭으로 옮긴다. 인코딩을 지정해 다시 읽기(`자동 판별`·`한국어 (CP949)` 메뉴)는 `ReloadCurrentAsync`가 현재 탭을 그 인코딩으로 다시 읽는다. 빈 탭이면 그 탭을 채운다. 읽기·파싱·투영·AvalonEdit 문서 생성은 `Task.Run` 안에서 한다. `LogFileReader.ReadAsync`가 스냅샷을 읽고 `LogParser.Parse`로 `LogData`를 만든다. 이어서 `LogProjection.Create`와 `CancellableDocumentFactory.Create`가 표시 문자열과 문서를 만든다. 최신 세대만 `PublishView`로 에디터에 올린다.

붙여넣은 텍스트는 파일 경로가 없다. `LogParser.ParsePastedText`가 `클립보드 Unicode · 원본 인코딩 알 수 없음`으로 파싱한다.

### 인코딩

`LogFileReader.Decode`가 바이트를 본다.

- UTF-8 BOM(`EF BB BF`)이면 BOM을 떼고 엄격한 UTF-8로 디코딩한다. 설명은 `UTF-8 (BOM 확인)`.
- UTF-16 LE/BE BOM이면 그 인코딩으로 디코딩한다.
- UTF-32 BOM이면 `UTF-32는 지원하지 않습니다.`로 거절한다.
- BOM이 없으면 엄격한 UTF-8을 먼저 시도한다. 설명은 `UTF-8로 해석 (BOM 없음; 추정)`.
- BOM 없는 UTF-8이 파일 끝의 1~3바이트 미완성 문자 때문에만 실패하면 그 꼬리만 버리고 `(끝의 미완성 문자 제외)`를 붙인다. 그 외 실패는 엄격한 CP949로 다시 읽는다. 설명은 `CP949로 해석 (UTF-8 실패 후 대체; 추정)`.
- UTF-8과 CP949가 모두 실패하면 거절한다.
- 메뉴의 수동 선택은 `EncodingMode.Cp949`다. 설명은 `CP949 (사용자 지정; 자동 판별 아님)`.

BOM이 있는 UTF-8은 끝의 미완성 문자를 버리지 않는다. 이 차이는 아래 수정 4다.

### 줄 색인

`LogParser.Parse`는 물리 줄 수를 먼저 세고 `LogLine` 배열을 채운다. 줄 앞의 시간 대괄호가 기록 경계다. 한 기록은 그 헤더부터 다음 헤더 직전까지다. 첫 헤더 앞의 줄은 줄마다 한 기록이다. `LogData`는 줄 시작 오프셋 배열을 만들어 원본 오프셋과 줄 번호를 오간다. 연속 본문 줄은 파싱 실패 건수에 넣지 않는다.

### 쓰는 중인 파일과 F5

F5와 `파일 → 현재 파일 새로 고침`은 `RefreshCurrentSessionAsync`다 (`src/ThreadLogViewer.App/MainWindow.Refresh.cs`). `CanRefreshCurrentSession`이 참일 때만 동작한다. 경로가 있는 전체 파일 탭이어야 하고, 빈 탭과 범위 분리 탭은 제외한다. 같은 `EncodingMode`로 `LogFileReader.ReadAsync`를 다시 호출한다. 백그라운드에서 `SourceLineRemap.Create`로 이전 줄과 새 줄을 맞춘 뒤, 필터·주변 보기·문서를 다시 만든다. 위치 복원 중에 사용자가 바꾼 검색·선택·위치·필터·강조·시간·문맥·탐색 이력은 `SameRefreshState`로 감지하고, 이미 만든 같은 원문·투영으로 다시 맞춘다. 사라지거나 중복된 위치는 개수를 상태줄에 알린다. 취소·실패 시 이전 세션을 유지하고, 실패 제목은 `파일 새로 고침 실패`다.

### 검색

`src/ThreadLogViewer.App/MainWindow.Search.cs`의 `SearchAsync`는 검색창·대소문자·단어·정규식·범위가 바뀔 때 검색을 실행한다. Enter, F3, Shift+F3, F4는 같은 파일의 `NavigateSearchAsync`가 이미 나온 결과만 이동한다. 약 180ms 뒤 `Task.Run`으로 `LogSearch.Find`를 호출한다. 범위는 현재 표시 기록(주변 보기 중이면 그 문맥), 전체 원본 기록, 고정한 선택 구간이고, 선택은 `SearchScopeBox`의 인덱스가 정한다. `SearchOptions`는 대소문자·단어 단위·정규식이다. 분리 탭은 `LogSlice` 범위 안으로 자른다. 강조·이동은 첫 `LogSearch.MaxHighlights`(100,000)개까지이고 초과는 `Limited`로 표시한다. 정규식 제한 시간은 `LogSearch.RegexTimeout`(250ms)이다. 결과 목록은 원본 줄 번호와 소유 헤더를 보여 준다.

### 내용 필터

왼쪽 포함·제외 문구는 `ReadDraftFilter`가 `EntryFilter`로 만든다 (`src/ThreadLogViewer.App/MainWindow.Navigation.cs`). `필터 적용`은 `FilterAsync`다. 스레드 체크 변경도 같은 메서드로 간다. 백그라운드에서 `LogProjection.CreateFiltered`가 고른 스레드와 `EntryFilter`를 함께 적용한다. 포함은 하나라도 또는 모두, 제외는 하나라도 맞으면 기록 전체를 뺀다 (`src/ThreadLogViewer.Core/EntryFilter.cs`). 입력만 바꾼 상태는 `UpdateFilterDraftStatus`가 미적용으로 표시한다. 결과 0건의 해제 동작은 `src/ThreadLogViewer.App/MainWindow.Feedback.cs`에 있다.

### 강조 규칙

규칙은 탭마다 최대 8개다 (`HighlightPrompt`). 새 규칙은 Shift+F8과 같이 0부터 5까지 아직 쓰지 않은 색을 고른다. 여섯 색이 모두 쓰이면 규칙 개수의 나머지를 쓴다. ‘강조 문구 관리’의 새 문구 색도 그 값에서 시작하고, 추가나 삭제 뒤에 다음 빈 색으로 옮긴다. `RefreshKeywordsAsync`가 켠 규칙을 하나씩 준비한다 (`src/ThreadLogViewer.App/MainWindow.Analysis.cs`). 준비는 `Task.Run` 안에서 `KeywordRefreshFailure.CanPrepare`로 검사한 뒤 `LogSearch.Find`로 찾는다. 짝 없는 UTF-16 서로게이트는 그 규칙만 `SkippedRule`로 건너뛴다. 나머지 규칙은 계속 강조한다. 건너뛴 규칙이 있으면 상태줄 `OperationStatus`에 `KeywordRefreshFailure.SkippedNotice`가 만든 문장이 남는다. 그 문장은 `강조 준비 실패`로 시작하고, 목록 번호·색 이름·읽을 수 있는 문구를 잇는다. `SkippedNotice`는 `FailedKeyword` 상수를 쓰지 않는다. 상수 `KeywordRefreshFailure.FailedKeyword`는 `강조 준비 실패`만이고, 강조 칸 `KeywordStatus`에 붙이거나, 켠 규칙이 모두 건너뛰면 그 칸을 그 상수로 바꾼다. 준비 중 `OperationCanceledException`이 아니면 `KeywordRefreshFailure.Apply`가 상태 문자열로 바꾼다. 취소는 실패로 보지 않아, 강조 칸이 `강조 준비 중…`인 채로 남을 수 있다. 메모리 부족은 `강조 메모리 부족 · 규칙을 줄여 주세요.`다. 그 외 예외는 강조 칸 `강조 준비 실패`, 상태줄 `강조 준비 실패 · 규칙을 확인한 뒤 다시 시도하세요.`다.

`README.md`와 `docs/PLAN.md`의 v0.8.2 목록에 남은 `강조 규칙 중 하나에 깨진 문자가 있으면 모든 강조가 표시되지 않습니다`는 그 이전 동작이다. 현재 동작은 두 문서 맨 위의 `main(미출시)` 문장과 `src/ThreadLogViewer.App/MainWindow.Analysis.cs`다.

### 주변 로그

분석 메뉴·우클릭 메뉴·주변 로그 패널의 버튼과, 숨긴 줄로 가는 검색·이동·이력에서 `ShowContextAsync`가 호출된다 (`src/ThreadLogViewer.App/MainWindow.Navigation.cs`). 반경은 0~10,000 기록이고 기본 입력은 20이다. 백그라운드에서 `LogContext.Create`가 기준 기록의 앞뒤 기록을 모든 스레드에서 원본 순서로 모은다 (`src/ThreadLogViewer.Core/LogContext.cs`). 보는 동안 스레드·내용 필터는 잠그고, `기존 필터로 돌아가기`는 저장해 둔 필터와 위치로 `FilterAsync`를 다시 호출한다.

### 시간 차이

줄의 소유 헤더는 `LogTimeAnalysis.ResolveAnchor`가 `TimeAnchor`로 만든다 (`src/ThreadLogViewer.Core/LogTimeAnalysis.cs`). 유효한 시각이 없으면 이전 지정을 유지한다. 차이는 `DifferenceTicks`로 비교에서 기준을 뺀 틱이고, `FormatTicks`가 부호 있는 문자열로 만든다. 비교 시각이 기준보다 이르면 화면 문구에 `자정 보정 안 함`을 붙인다 (`src/ThreadLogViewer.App/MainWindow.Analysis.cs`). 여백의 A/B 표식은 `OriginalLineMargin`이 그린다.

### 화면 설정

`SettingsStore`가 실행 폴더의 `user-data` 디렉터리에 `ui-settings.json`을 읽고 쓴다 (`src/ThreadLogViewer.App/SettingsStore.cs`). 그 폴더를 만들 수 없거나, 접근이 거부되거나, 폴더가 읽기 전용이거나, 설정 파일이 아직 없을 때 쓰기 또는 바꾸기가 실패하면 같은 형식의 파일을 `%LOCALAPPDATA%\ThreadLogViewer\ui-settings.json`에 저장한다. 대체 루트는 `SettingsStore` 생성자의 두 번째 인자로만 켠다. 생략하면 대체 위치는 없다. 앱의 기본 생성자는 `Environment.SpecialFolder.LocalApplicationData`를 넘긴다. 읽을 때는 실행 폴더에 파일이 있으면 그 파일을 쓰고, 없을 때만 대체 파일을 읽는다. 두 파일을 복사하거나 옮기지 않는다. 실행 폴더에 설정 파일이 이미 있으면, 실패 원인이 잠금·읽기 전용 속성·권한·디스크 중 무엇이든 대체 위치에 쓰지 않고 실패로 돌린다. 다음 읽기가 그 파일을 고르기 때문이다. 그 파일이 계속 쓸 수 없는 상태면 변경은 저장되지 않고 상태 줄에 안내만 남는다. 내용 형식은 `UiSettings`다. 테마, 간격, 줄바꿈, 글자 크기, 스레드 패널 폭, 검색 결과 높이·접힘, 스레드 배경, 긴 줄 구간 표시만 들어 있다 (`src/ThreadLogViewer.App/UiSettings.cs`). 로그 본문, 경로, 검색어는 넣지 않는다. 두 위치 모두 `OwnerMarker`(`ThreadLogViewer.UiSettings`)와 `SchemaVersion` 1을 확인하고, 64KB를 넘는 파일, 다른 소유자, 다른 스키마, 손상된 JSON, 링크·reparse point는 그대로 두고 기본 화면으로 연다. 갱신은 같은 폴더의 임시 파일에 쓴 뒤 `File.Replace` 또는 `File.Move`로 바꾼다. 창에서는 `src/ThreadLogViewer.App/MainWindow.Settings.cs`가 마지막 변경 500ms 뒤에 저장한다. 대체 위치 저장이 성공하면 오류를 보이지 않는다. 두 위치가 모두 실패하면 상태줄에 경로가 없는 짧은 안내를 남기고 예외로 앱을 닫지 않는다. 실패하면 횟수를 올리고, 창이 열려 있고 횟수가 3 이하일 때만 2초·4초·8초 뒤에 다시 시도한다. 네 번째 실패부터는 창을 연 동안 다시 예약하지 않는다. 종료 때는 `viewReady`를 끈 뒤 `SaveSettingsNow`를 한 번 더 호출하고 타이머를 멈춘다. 파일 위치와 형식은 옮기지 않는다. 이 내용은 `AGENTS.md`와 아래 수정 3에 있다.

분석 상태와 필터·강조 프리셋은 별도 JSON이다. `AnalysisFiles`가 새 파일만 만들고 8 MiB를 넘기면 거절한다 (`src/ThreadLogViewer.Core/AnalysisFiles.cs`). 시작 시 작업 공간을 자동으로 복원하지 않는다.

### 전역 예외

`App` 생성자가 세 경로를 받는다 (`src/ThreadLogViewer.App/App.xaml.cs`).

- `DispatcherUnhandledException`: 화면 작업. `UserExceptionReport.Decide`에 `ExceptionSource.Dispatcher`를 넘긴다.
- `TaskScheduler.UnobservedTaskException`: `ExceptionSource.UnobservedTask`.
- `AppDomain.CurrentDomain.UnhandledException`: `ExceptionSource.AppDomain`.

UI 이벤트에서 시작한 비동기 작업은 `AsyncEvent.Run`이 감싼다 (`src/ThreadLogViewer.App/AsyncEvent.cs`). 여기서 새는 예외는 `ExceptionSource.AsyncHandler`다. `UserExceptionReport`는 취소는 사용자에게 보이지 않게 하고, `OutOfMemoryException`, `AccessViolationException`, `StackOverflowException`을 치명적 오류로 본다. 화면 작업의 치명적 오류와 AppDomain 예외는 계속 실행하지 않는다. 그 외 화면 작업, 관측되지 않은 Task, `AsyncEvent` 예외는 경고 후 계속 실행한다. 창 문구는 `ExceptionWindow.Show`가 UI 스레드에서 띄운다. 호출 스레드가 UI가 아니면 디스패처에 올린다. AppDomain 예외는 프로세스가 끝나는 중이라 그 창이 보이지 않을 수 있다. `LocalExceptionLog.TryWrite`는 시각, 경로 이름(`dispatcher`, `unobserved-task`, `appdomain`, `async-handler`), 예외 형식 이름만 남긴다. 로그 내용과 파일 경로는 기록하지 않는다.

### 스레드

WPF 디스패처가 UI 스레드다. 파일 대화상자, 상태줄, 에디터 문서 교체, 메뉴 갱신은 그 스레드에서 한다.

오래 걸리는 일은 `Task.Run`으로 UI 동기화 컨텍스트 밖에서 돌린다. 해당하는 일은 파일 읽기·디코딩·파싱, 필터 투영, `CancellableDocumentFactory`의 문서 생성, 검색, 규칙별 강조 검색, 주변 로그 투영, F5의 줄 재매핑, 내보내기, 분석 파일 저장·불러오기, 선택 길이 계산이다. `LogFileReader` 주석은 호출자가 `Task.Run`으로 UI 컨텍스트를 피한다고 적는다.

`AsyncEvent.Protect`는 `action()`을 `ConfigureAwait(true)`로 기다린다. 이 대기는 작업이 끝난 뒤의 예외 보고를, `Protect`가 시작된 UI 컨텍스트로 되돌린다. UI 이벤트에서 시작한 `SearchAsync` 같은 메서드는 첫 `await` 전에 UI 스레드에서 시작하고, 그 안의 `await Task.Run`은 `ConfigureAwait(false)`를 쓰지 않아 결과 게시가 UI 컨텍스트로 돌아온다. `BeginWork`가 UI 스레드에서 만든 `Progress<WorkProgress>` 보고도 그 컨텍스트로 돌아오고, 핸들러는 `LatestOperation.IsCurrent`가 참일 때만 진행률을 고친다.

`LatestOperation`은 시작마다 이전 토큰을 취소하고 세대 번호를 올린다 (`src/ThreadLogViewer.Core/LatestOperation.cs`). 취소 뒤에 이미 끝난 이전 작업이 디스패처 큐에 있을 수 있으므로, 결과를 화면에 넣기 전에 `IsCurrent`와 캡처해 둔 `LogProjection` 또는 세션이 아직 현재인지 확인한다. `BeginWork`가 쓰는 `work`는 열기·필터·F5·주변 로그·내보내기·분석 파일 저장·불러오기·세션 분리가 함께 쓰는 세대다. 검색의 `searchWork`, 강조의 `highlightWork`, 선택 길이의 `selectionWork`는 각각 따로 세대를 가진다. 탭을 바꿀 때 `CancelSessionWork`가 네 세대를 모두 올린다.

## 메인 창 partial

`MainWindow`는 `partial class`다. 선언은 `src/ThreadLogViewer.App/MainWindow.xaml.cs`에 있고, 레이아웃은 `src/ThreadLogViewer.App/MainWindow.xaml`에 있다.

| 파일 | 담당 |
|---|---|
| `src/ThreadLogViewer.App/MainWindow.xaml.cs` | 창 수명, 열기·끌어 놓기, `LoadIntoSessionAsync`, `PublishView`, 스레드 필터, 내보내기, 테마, 진행률, `ShowError` |
| `src/ThreadLogViewer.App/MainWindow.Sessions.cs` | 탭 목록, `LogSession`, 같은 파일 활성화, 닫은 탭 보관 |
| `src/ThreadLogViewer.App/MainWindow.NewSession.cs` | 빈 탭 |
| `src/ThreadLogViewer.App/MainWindow.Refresh.cs` | F5, 인코딩·필터·강조를 유지하는 다시 읽기 |
| `src/ThreadLogViewer.App/MainWindow.Search.cs` | 검색창, 결과 목록, F3/F4 |
| `src/ThreadLogViewer.App/MainWindow.Navigation.cs` | 원본 줄 이동, 내용 필터 적용, 주변 로그, 선택 길이 계산 |
| `src/ThreadLogViewer.App/MainWindow.History.cs` | 탐색 뒤로·앞으로, F5, Ctrl+Shift+T |
| `src/ThreadLogViewer.App/MainWindow.Analysis.cs` | 강조 준비, A/B 시간 차이 |
| `src/ThreadLogViewer.App/MainWindow.AnalysisUi.cs` | 분석 도구 표시, 필터 잠금, 내보내기 단추 문구 |
| `src/ThreadLogViewer.App/MainWindow.AnalysisFiles.cs` | 분석 상태·프리셋 JSON |
| `src/ThreadLogViewer.App/MainWindow.QuickHighlight.cs` | Shift+F8 즉시 강조 |
| `src/ThreadLogViewer.App/MainWindow.LineActions.cs` | 우클릭 대상 줄, 세션 분리, 강조 관리 창 |
| `src/ThreadLogViewer.App/MainWindow.LineSelection.cs` | 줄 번호 클릭·드래그·Ctrl 선택 |
| `src/ThreadLogViewer.App/MainWindow.Clipboard.cs` | 클립보드 로그·파일 붙여넣기 |
| `src/ThreadLogViewer.App/MainWindow.Editing.cs` | 편집 메뉴가 보는 마지막 입력 대상 |
| `src/ThreadLogViewer.App/MainWindow.Menus.cs` | 메뉴 활성·체크, 글자 크기 단축키. 그 외 창 단축키는 `src/ThreadLogViewer.App/MainWindow.Search.cs`의 `WindowKeyDownAsync` |
| `src/ThreadLogViewer.App/MainWindow.Feedback.cs` | 결과 0건 안내, 필터 해제 |
| `src/ThreadLogViewer.App/MainWindow.ThreadSearch.cs` | 스레드 목록 검색. 본문 필터는 바꾸지 않음 |
| `src/ThreadLogViewer.App/MainWindow.Settings.cs` | `UiSettings` 읽기·저장, 도움말 창 열기 |
| `src/ThreadLogViewer.App/MainWindow.ResultsLayout.cs` | 검색 결과 높이와 접기 |
| `src/ThreadLogViewer.App/MainWindow.WindowLayout.cs` | 작업 영역·DPI에 맞춘 창 크기. 계산은 `WindowLayoutPolicy`. `THREADLOGVIEWER_UI_REVIEW=1`인 갤러리 프로세스만 메인 창의 모니터 작업 영역 클램프를 건너뛴다. 작업 폭이 900 DIP보다 좁으면 왼쪽 스레드 열의 최대 폭을 200 DIP 이하로 줄이고, 그 폭이 120 DIP보다 작으면 열을 접어 로그 본문 폭을 남긴다. 선호 폭 값은 바꾸지 않는다 |
| `src/ThreadLogViewer.App/OwnedDialogPlacement.cs` | 강조 문구 관리와 도움말을 소유 창 HWND 가운데에 두고, 그 모니터 작업 영역에 크기와 위치를 맞춘다. 리뷰 모드에서도 이 맞춤은 유지한다 |
| `src/ThreadLogViewer.App/HelpWindow.cs` | 단축키 도움말. 스크롤되는 창이며 `확인`으로 닫는다 |
| `src/ThreadLogViewer.App/MainWindow.Performance.cs` | 긴 줄 구간 표시. 생성기는 `LargeLineElementGenerator` |

## 테스트와 CI

풀 리퀘스트는 `.github/workflows/pr-ci.yml`의 `build-test-publish`가 처리한다. Release 빌드 다음 순서는 `ThreadLogViewer.slnx` 단위 테스트, `scripts/publish.ps1`, UI 테스트다.

UI 테스트는 한 잡에서 1, 2, 3회를 이어서 실행한다. 회차마다 워크플로가 `TestResults/pr-ci` 아래에 `ui-tests-1.trx`, `ui-tests-2.trx`, `ui-tests-3.trx`와 콘솔 로그를 남긴다. 이 결과 폴더는 실행이 만든 출력이고 저장소에는 없다. 워크플로 환경 변수 `UI_TEST_EXPECTED_PASSED`, `UI_TEST_EXPECTED_SKIPPED`, `UI_TEST_EXPECTED_TOTAL`, `UI_TEST_EXPECTED_FAILED`와 TRX `Counters`를 비교한다. 현재 값은 14, 1, 15, 0이다. 세 회차 모두 같아야 하고, 한 회차라도 다르거나 프로세스가 실패하면 잡이 실패한다. 필수 시나리오 이름은 워크플로의 `$required` 배열에 있다. 워크플로는 `TestResults/ui-screenshots/iteration-N`을 만들고 `UI_SCREENSHOT_DIR`로 넘긴다. 실패한 테스트가 그 폴더에 화면을 저장한다. 잡이 실패하면 그 폴더를 `ui-screenshots` 아티팩트로 올린다. 테스트 결과 TRX는 `pr-test-results`, 게시 폴더는 `ThreadLogViewer-win-x64`, ZIP 아티팩트 이름은 `ThreadLogViewer-win-x64-zip`이다. ZIP 단계는 `scripts/publish.ps1`이 만든 게시 폴더를 올린다. 세 회차 뒤에 `tests/ThreadLogViewer.UiTests` 실행 파일을 `--ui-review`로 한 번 실행한다. 이 경로는 xUnit 시나리오가 아니고 `Program.Main`이며, `dotnet test`는 그 진입점을 호출하지 않는다. `UI_TEST_EXPECTED_*`와 `$required`는 바꾸지 않는다. 뷰어 프로세스에만 `THREADLOGVIEWER_UI_REVIEW=1`을 넣고, 150% 촬영에는 `THREADLOGVIEWER_UI_REVIEW_SCALE=150`을 더한다. 그 프로세스는 `src/ThreadLogViewer.App/UiReviewHarness.cs`가 소프트웨어 렌더링과 내용 `LayoutTransform`을 적용하고, 메인 창 배치 코드가 모니터 클램프를 건너뛴다. 강조 문구 관리와 도움말 창은 그 모드에서도 소유 창이 있는 모니터의 작업 영역 안에 둔다. 변수가 없으면 동작은 같다. 세션 DPI는 바꾸지 않는다. 화면은 아티팩트 `ui-review-screenshots`로 올리고, 린트 경고는 그 잡의 실패 조건이 아니다. 경고는 상태·테마·배율마다 400건까지만 저장하고, 넘친 수는 생략으로 적는다. 절차와 한계는 [검증 안내](TESTING.md)에 있다.

`main`에 푸시하거나 `main` ref에서 수동 실행하면 `.github/workflows/windows-release.yml`의 `release` 잡이 돈다. 다른 ref의 수동 실행은 `github.ref`가 `refs/heads/main`이 아니어서 잡이 건너뛰어진다. `Directory.Build.props`의 `Version`으로 `v` 태그를 만들고, 그 태그의 릴리스가 이미 공개됐으면 `publish=false`로 빌드·테스트·ZIP을 건너뛴다. 릴리스가 없거나 아직 초안이면 `publish=true`로 Release 빌드, `ThreadLogViewer.slnx` 테스트, `scripts/publish.ps1`, `scripts/verify-release.ps1`을 진행한다. 이어서 초안에 ZIP을 올리고 자산을 확인한 뒤 `draft=false`로 공개하고, 공개 다운로드 해시를 대조한다. 같은 태그가 다른 커밋을 가리키면 버전 선택 단계에서 실패한다.

시나리오 이름과 기능 번호의 대응 표, 세 회차 기대 건수의 설명은 [검증 안내](TESTING.md)와 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)에 있다. 이 문서는 그 표를 다시 적지 않는다.

## 알려진 한계와 열린 수정

아래 번호는 코드와 현재 문서가 아직 남겨 둔 항목이다. 구현된 기능으로 적지 않는다.

**수정 3. 화면 설정 저장.** 기본 파일은 실행 폴더의 `user-data\ui-settings.json`이고 형식은 `SettingsStore`와 `UiSettings`의 schema 1 그대로다. 실행 폴더에 설정 파일이 없고 그 위치에 쓸 수 없으면 `%LOCALAPPDATA%\ThreadLogViewer\ui-settings.json`에 같은 검사를 거쳐 저장한다. 파일을 옮기거나 형식을 바꾸지 않는다. 성공한 다시 실행은 UI 테스트 `Scenario12_SettingsPersistAfterRestart`가 판정한다 (`tests/ThreadLogViewer.UiTests/UiFeatureTests.cs`). 쓸 수 없는 실행 폴더, 두 위치 모두 실패, 두 파일이 있을 때의 실행 폴더 우선은 `tests/ThreadLogViewer.Tests/SettingsFallbackTests.cs`가 판정한다. 저장이 실패하는 경로의 UI 테스트는 없다. 그 공백은 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)의 높은 우선순위 10번 설명에 있다. `README.md`, `docs/PLAN.md`, `docs/VALIDATION-v0.8.2.md`의 v0.8.2 한계 목록은 기능 10 전체를 UI 자동 테스트가 없다고 적는데, 그 문장은 `Scenario12`보다 이전이다. 현재 자동 판정 범위는 `docs/FEATURE-TEST-GUIDE.md`의 매핑 표를 따른다.

**수정 4. UTF-8 BOM을 쓰는 중의 F5.** `LogFileReader.Decode`는 BOM을 확정 인코딩으로 보고 엄격한 UTF-8로 디코딩한다. 스냅샷이 문자 중간에서 끝나면 `DecoderFallbackException`이 나고 F5는 `파일 새로 고침 실패`로 끝난다. BOM 없는 UTF-8만 끝의 미완성 문자를 버린다. `Scenario05_Utf8Bom_RefreshWhileAppending`은 이 때문에 건너뛴다. 건너뛰는 이유는 `tests/ThreadLogViewer.UiTests/UiScenarioTests.cs`의 `BomTruncationSkip`과 [검증 안내](TESTING.md), [기능 테스트 방법](FEATURE-TEST-GUIDE.md), [v0.8.2 검증 기록](VALIDATION-v0.8.2.md)에 있다. 앱 동작은 이 한계를 유지한다.

**수정 5. 약 1GB 파일의 메모리.** `LogFileReader`는 길이가 `int.MaxValue`를 넘으면 `2GB 이상의 파일을 지원하지 않습니다.`로 거절한다. 읽기 중에는 그 길이의 바이트 배열을 잡지만, 디코딩이 끝나면 그 배열을 비우고 탭에 남기지 않는다. 탭에 남는 것은 디코딩된 `LogData.Text`, 줄 메타데이터, AvalonEdit 문서에 복사된 본문이다. 모든 줄을 보이는 투영의 `LogProjection.Text`는 `LogData.Text`와 같은 문자열이다. 필터나 범위로 일부만 보이면 `LogProjection.Text`는 따로 만든 문자열이다. 2GB 미만이라도 파일 크기에 비례해 메모리가 늘고, 1GB에 가까운 파일은 디코딩 문자열과 문서 복사본 때문에 메모리 한계에 닿을 수 있다. 구간 로딩은 없다. `README.md`의 `구조와 현재 한계`는 탭마다 디코딩 원문·행 정보·표시 문서를 유지하고, 2GB 이상을 막으며, 그 미만도 RAM과 줄 길이에 달렸다고 적는다. [v0.8.0 검증 기록](VALIDATION-v0.8.0.md)은 합성 100 MiB 측정이고, 구간 로딩과 전체 메모리 상한을 넣지 않았다고 적는다. 1GB 파일을 연 실측은 그 문서에 없다. [v0.6.3 UX 검토](UX-REVIEW-v0.6.3.md)는 100 MiB 콘솔 측정과 원본 바이트·문자열·투영·문서 유지를 적는다. [계획](PLAN.md)의 대용량 최적화 후보는 메모리 매핑·구간 로딩·투영 증분 처리다.

**CP949를 쓰는 중의 F5는 그 순간이 검증되지 않았다.** BOM 없는 UTF-8과 CP949의 F5 20회는 `Scenario05_RefreshWhileAppendingKorean`이 실행한다. CP949는 2바이트 문자가 읽기 길이에 걸리면 실패할 수 있어, 도우미는 F5 사이에만 완전한 줄을 추가하고 F5를 누르는 순간에는 쓰지 않는다. 이 한계는 [검증 안내](TESTING.md), `README.md`, `docs/PLAN.md`, [v0.8.2 검증 기록](VALIDATION-v0.8.2.md)에 있다.

**실제 Explorer 끌어 놓기는 자동화하지 않는다.** CI는 테스트 프로세스가 OLE 드래그 소스가 되어 창에 놓는다. 절차는 [검증 안내](TESTING.md)의 Windows UI 자동화 절이다. 실제 Explorer 창에서 끌어 오는 경우는 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)과 `docs/PLAN.md`가 알려진 한계로 적는다.

## 안전하게 바꾸는 방법

파일 읽기, 인코딩, 파싱, 필터, 검색, 시간, 내보내기를 바꾸면 `src/ThreadLogViewer.Core`와 `tests/ThreadLogViewer.Tests`를 같이 수정한다. 화면과 단축키는 위 표의 `src/ThreadLogViewer.App/MainWindow.*.cs` 중 그 동작이 있는 파일만 수정한다. 한 변경에서 같은 partial을 여러 갈래로 동시에 고치지 않는다. 화면 설정 파일은 `src/ThreadLogViewer.App/SettingsStore.cs`와 `src/ThreadLogViewer.App/UiSettings.cs`에 둔다.

기능을 추가하면 다음을 함께 갱신한다.

- 동작이 코어에 있으면 `tests/ThreadLogViewer.Tests`의 해당 회귀.
- 게시 실행 파일로 확인하는 화면이면 `tests/ThreadLogViewer.UiTests`의 시나리오.
- [기능 테스트 방법](FEATURE-TEST-GUIDE.md)의 조작·기대 결과와 UI 테스트 매핑 표.
- [검증 안내](TESTING.md)의 판정 범위와 회차 건수.
- 이 문서에서 경로, 형식 이름, 데이터 흐름이 바뀐 절.

UI 건수는 `.github/workflows/pr-ci.yml` 환경 변수 네 개다. `Assert-UiTrxCounts`가 회차마다 TRX의 passed, skipped, total, failed를 그 변수와 비교한다. skipped는 `Read-UiTrxStats`가 `notExecuted`로 읽고, 그 값이 비었거나 0이고, `executed > 0`이며 `total > executed`이면 skipped는 total − executed다. 하나라도 다르면 세 회차를 모두 돈 뒤 UI 테스트 단계가 실패하고 잡이 실패한다. `dotnet test` 종료 코드가 0이어도 같다. 시나리오를 추가하면 `$required`에 메서드 이름을 넣고 `UI_TEST_EXPECTED_TOTAL`과 passed 또는 skipped를 같이 올린다. `--ui-review` 갤러리 실행은 시나리오가 아니므로 그 건수에 넣지 않는다. `Scenario05_Utf8Bom_RefreshWhileAppending`을 다시 실행하면 skipped는 0, passed는 15, total은 15, failed는 0이어야 한다. 변수, `$required`, [검증 안내](TESTING.md), [기능 테스트 방법](FEATURE-TEST-GUIDE.md)의 표를 같은 변경에서 맞춘다.
