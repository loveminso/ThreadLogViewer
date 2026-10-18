# ThreadLog Viewer 검증 안내

[기능 테스트 방법](FEATURE-TEST-GUIDE.md)의 매핑 표에 적힌 범위만 FlaUI UI 테스트가 판정한다. 수동 확인은 SmartScreen만 해당한다. 실제 Explorer 끌어 놓기는 알려진 한계다. 키 조작은 [단축키 표](SHORTCUTS.md), 빌드·회귀·배포 결과는 [v0.8.2 검증 기록](VALIDATION-v0.8.2.md)을 따른다. `Scenario05_Utf8Bom_RefreshWhileAppending`은 수정 4 때문에 건너뛴다.

## 자동 회귀

프로젝트 루트에서 기존 개발 SDK와 패키지 캐시를 사용한다. 새 도구를 설치하거나 네트워크로 패키지를 받는 작업은 자동으로 수행하지 않는다.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=my-verification.trx' --results-directory TestResults\my-verification
```

겹쳐 실행 중인 빌드·배포가 없는 상태에서 실행한다. `TestResults\my-verification`은 자신의 새 결과 경로로 바꾼다. 파싱·개행·원본 매핑·필터·인코딩·원본 안전 내보내기와 합성 비표시 WPF 회귀를 함께 실행한다. WPF 테스트는 공유 STA dispatcher를 사용하며 실제 창·입력 장치·클립보드는 조작하지 않는다.

다중 줄 선택만 확인하려면 같은 명령에 `--filter FullyQualifiedName~MultiLineSelectionUxTests`를 추가한다. 결과에는 줄 번호 드래그·Ctrl 행 선택·원본 순서 복사 객체·혼합 개행·필터/범위 매핑·탭 상태·일반 텍스트 선택·두 테마의 합성 렌더링을 기록한다. 복사는 기본 AvalonEdit Copy가 사용하는 `Selection.CreateDataObject`의 UnicodeText를 검사하며 실제 Ctrl+C·클립보드는 실행하지 않는다.

## Windows UI 자동화

개발 PC에서 UI 테스트를 실행하기 전에 `%LOCALAPPDATA%\ThreadLogViewer\ui-settings.json`이 없어야 한다. UI 테스트는 게시한 실행 파일을 실제 시작 경로로 띄우므로, 그 파일이 있으면 첫 화면이 그 설정을 따를 수 있다. `tests/ThreadLogViewer.UiTests`는 FlaUI UIA3로 게시한 `ThreadLogViewer.exe`를 조작한다. 이 프로젝트는 `ThreadLogViewer.slnx`에 넣지 않는다. `dotnet test ThreadLogViewer.slnx`는 기존 단위 테스트만 실행하고, UI 테스트는 `scripts/publish.ps1` 다음에 별도 `dotnet test tests/ThreadLogViewer.UiTests/ThreadLogViewer.UiTests.csproj`로 실행한다. 솔루션에 넣으면 게시 전과 데스크톱 세션이 없는 실행이 단위 테스트 단계에 섞인다.

CI는 게시 폴더를 임시 복사본으로 띄운다. 복사본으로 실행하므로 테스트가 만드는 `user-data`가 업로드하는 게시 폴더에 들어가지 않는다. 샘플과 입력은 모두 합성 로그다. 같은 잡에서 UI 스위트를 세 번 연속 실행하고, 회차마다 `ui-tests-1.trx`·`ui-tests-2.trx`·`ui-tests-3.trx`와 콘솔 로그를 남긴다. 한 회차라도 실패하면 잡이 실패한다. 실패 화면은 `TestResults/ui-screenshots/iteration-N`에 저장하고, 워크플로는 그 폴더를 `ui-screenshots` 아티팩트로 올린다.

끌어 놓기는 Explorer 드래그를 쓰지 않는다. 테스트 프로세스가 WinForms `DataObject`(`FileDrop`)로 OLE 드래그 소스가 되고, `DoDragDrop` 동안 `SendInput`으로 왼쪽 버튼을 누른 채 포인터를 뷰어 창 중앙으로 옮긴 뒤 버튼을 놓는다. 뷰어의 기존 `PreviewDrop`이 그 경로를 연다. `WM_DROPFILES`를 붙이거나 뷰어에 드롭 이벤트를 주입하지 않고, 뷰어 프로세스에 코드를 넣지 않는다. 저장 대화상자는 UIA 값 설정과 Enter가 원래 이름(문서 폴더의 `<원본>-filtered.log`)을 그대로 확정한다. 파일 이름 칸이 포커스를 가진 뒤 대상 경로를 붙여 넣고, 이름이 `Save`인 버튼을 `Invoke`·클릭하고 Alt+S로 다시 시도한다.

## UI 리뷰 갤러리와 린트

UI 테스트 세 회차 다음에 같은 잡이 `tests/ThreadLogViewer.UiTests` 실행 파일을 `--ui-review <게시 exe> <출력 폴더>`로 한 번 실행한다. 이것은 xUnit `[Fact]`가 아니다. `dotnet test`는 `Program.Main`을 호출하지 않는다. 통과 14, 건너뜀 1, 전체 15, 실패 0과 `$required`는 그대로다. 건너뛰는 테스트는 계속 `Scenario05_Utf8Bom_RefreshWhileAppending`뿐이다.

갤러리는 임시 폴더의 합성 로그만 연다. 상태는 빈 시작, 연 로그, 검색 결과, 적용된 필터, 주변 로그, 강조 문구 관리, 강조 준비 실패 안내, 파일 메뉴, 단축키 도움말이다. 각 상태를 1024×768과 1920×1080, 배율 100%와 150%, 다크와 라이트로 찍는다. 앱에 두 테마가 이미 있으므로 테마를 추가하지 않는다. 기대 파일 수는 9×2×2×2인 72다.

CI는 Windows 세션 DPI를 바꾸지 않는다. 150%는 뷰어 프로세스에만 `THREADLOGVIEWER_UI_REVIEW=1`과 `THREADLOGVIEWER_UI_REVIEW_SCALE=150`을 넣고, 그 프로세스가 메인 창 내용에 1.5 `LayoutTransform`을 적용한다. 바깥 픽셀 크기는 요청한 창 크기이고, 배치 DIP는 대략 그 크기를 1.5로 나눈 값이다. 고정 픽셀 창을 150% 모니터에 둔 경우에 가깝고, Per-Monitor V2 글자 힌트는 거치지 않는다. 제목 표시줄은 세션 DPI다. 메뉴 팝업은 별도 창이라 이 변환으로 커지지 않는다. 강조 문구 관리와 단축키 도움말도 별도 창이며 150% `LayoutTransform`을 따르지 않는다. 두 창은 소유 창 HWND를 기준으로 두고 그 모니터 작업 영역 안에 맞춘다. 변수가 없으면 배치, 렌더 모드, 모니터 클램프는 기존과 같다. 갤러리 프로세스는 PrintWindow가 검게 찍히지 않도록 소프트웨어 렌더링을 쓰고, 1920×1080이 작은 바탕화면에도 들어가도록 작업 영역 클램프를 건너뛴다. 화면 해상도를 1920×1080으로 올리려 시도할 수 있으나 DPI는 바꾸지 않고, 실패해도 단계가 실패하지는 않는다. 바꾸기에 성공해도 갤러리 실행은 그 해상도를 원래 값으로 되돌리지 않는다. 결과는 색인에 적는다.

실패 안내는 상태 막대의 `강조 준비 실패`다. 짝 없는 서로게이트 강조 규칙을 하나 더한 뒤에 보인다. 처리되지 않은 UI 예외의 모달 MessageBox는 열지 않는다.

산출물은 아티팩트 `ui-review-screenshots`다. PNG, `index.md`, `index.html`, `ui-lint.md`, `ui-lint.json`이 들어 있다. 린트는 잘린 텍스트, 창이나 부모 밖에 있는 요소, 탭 순서, 접근 가능한 이름이 비어 있는 조작 컨트롤을 경고로 적는다. 경고는 빌드를 실패시키지 않는다. 리뷰 단계 자체의 충돌이나 빈 화면만 실패다. UIA는 WPF DesiredSize를 주지 않으므로 잘림은 Segoe UI 폭 추정이다. 상태·테마·배율마다 경고를 400건까지만 저장하고, 넘친 수는 `ui-lint.json`의 `dropped`와 `capped`에 적으며, 저장분만 전체 개수처럼 읽지 않는다. 잡 요약의 UI review 절에 범주별 저장 개수, 배율·버킷별 생략 개수, 그리고 필터로 빼 저장하지 않은 건수가 나온다.

린트가 빼는 오탐은 세 가지다. 빼기 전에 그 검사가 경고가 될 때만 종류별로 센다. 그 수는 `ui-lint.json`의 `summary.filtered`(`systemMenuBar`, `menuPopup`, `menuHeader`, `total`), `ui-lint.md`, 잡 요약에 적는다.

- `SystemMenuBar`: 자동화 ID가 `SystemMenuBar`이거나, 종류가 메뉴 막대이면서 이름이 `System`인 요소는 범위 검사에서 뺀다. 제목 표시줄의 시스템 메뉴는 부모보다 작게 보고되어 창 밖으로 잡힌다. 실제로 창 밖 또는 부모 밖 경고가 될 때만 `systemMenuBar`로 센다.
- 메뉴 팝업의 `outside-bounds`: 종류가 `Menu` 또는 `MenuItem`이고, 그 요소의 최상위 창이 메인 창이 아닐 때만 창 밖과 부모 밖 검사를 뺀다. 열린 팝업이 여기 해당한다. 메인 창의 메뉴 막대와 그 항목은 범위 검사에 남으므로, 150%에서 메뉴 막대가 넘치면 경고로 남는다. 뺀 경고는 `menuPopup`으로 센다.
- 메뉴 머리글의 말줄임: 부모가 `MenuItem`인 텍스트를 통째로 빼지 않는다. 이름에서 `(_X)`와 `_`를 지운 뒤 같은 잘림 검사를 다시 한다. 지운 뒤에도 잘리거나 말줄임이면 경고로 남긴다. 지우기 전에는 경고였고 지운 뒤에는 아니면 `menuHeader`로 센다. 내용 필터 머리글과 검색 결과의 말줄임은 부모가 메뉴 항목이 아니므로 접근 키를 지우지 않고 그대로 검사한다.

`Scenario04_BadHighlightRuleShowsPrepareFailureAndKeepsRunning`은 합성 로그를 연 뒤 올바른 문구를 강조하고, ‘강조 문구 관리’에서 깨진 UTF-16 강조 문구(짝 없는 서로게이트)를 하나 더 적용한다. 준비 단계가 그 규칙만 건너뛰면 상태 막대에 실패한 규칙을 가리키는 `강조 준비 실패`가 보이고, 올바른 문구는 계속 강조되며, 메인 창은 열린 채로 남는다. 일반 문구와 올바른 서로게이트 쌍은 그대로 준비된다. UTF-8 BOM의 잘린 마지막 문자는 아직 실패한다(fix 4). `LogFileReader.Decode`는 BOM을 확정 인코딩으로 보고 엄격한 UTF-8로 디코딩하며, BOM 없는 UTF-8만 끝의 미완성 문자를 버린다. `Scenario05_Utf8Bom_RefreshWhileAppending`은 그래서 건너뛰고 앱 동작은 바꾸지 않는다. BOM 없는 UTF-8과 CP949는 F5 20회를 실행한다. CP949는 2바이트 문자가 읽기 길이에 걸리면 실패할 수 있어, 도우미가 파일을 연 채 F5 사이에만 완전한 줄을 추가하고 F5 순간에는 쓰지 않는다.

## 실제 화면과 성능

`Scenario01`~`Scenario14`가 기능 표의 GUI와 작은 화면의 강조·도움말 창을 판정한다. 세 회차 모두 통과 14, 건너뜀 1, 전체 15, 실패 0이다. `Scenario14_HighlightAndHelpWindowsFitWorkArea`는 메인 창을 1024×768과 그 모니터 작업 영역 중 더 작은 쪽에 둔 뒤 강조 문구 관리와 단축키 도움말이 작업 영역 안에 있고, `적용`·`취소`·`확인`이 화면 안에 있으며 오프스크린이 아닌지 본다. 건너뛰는 테스트는 `Scenario05_Utf8Bom_RefreshWhileAppending`이며 이유는 수정 4다. `Scenario07_JumpToOriginalLine`은 원본 줄 이동, `Scenario08_PositionAndSelectionInfo`는 위치·선택 정보, `Scenario09_ValidHighlightRuleHighlightsPhrase`는 올바른 문구 강조, `Scenario10_SurroundingLogKeepsFilterOnReturn`은 주변 로그, `Scenario11_FilterKeepsOrMovesPosition`은 필터 후 위치, `Scenario12_SettingsPersistAfterRestart`는 설정 저장, `Scenario13_TimeDifferenceBetweenRecords`는 두 기록 시간 차이를 판정한다. 수동으로 남기는 것은 SmartScreen만 해당한다. 알려진 한계: CI의 끌어 놓기는 실제 Explorer 창이 아니라 테스트 프로세스의 OLE 드래그 소스를 쓴다(위 절). 실제 로그는 테스트·샘플·문서에 넣지 않으며 `local-data` 안에서만 관리한다.

빌드 경고·오류, 자동 테스트 수, 실제 GUI 조작, 측정 범위가 있는 성능, 배포 파일·버전·해시를 각각 기록한다. 자동 테스트 실행 시간은 앱의 마우스·스크롤 반응 속도로 해석하지 않는다. 성능을 측정한다면 새 합성 자료와 새 출력 경로를 사용하고 기존 파일을 보존한다.

Release 빌드 뒤 합성 작업 측정은 테스트 실행 파일의 별도 경로를 사용한다. 아래 결과 경로가 없을 때 실행하며 첫 실행의 OS 캐시·GC 상태 영향을 함께 기록한다.

```powershell
& .\.tools\dotnet\dotnet.exe .\tests\ThreadLogViewer.Tests\bin\Release\net10.0-windows\ThreadLogViewer.Tests.dll --workbench-benchmark TestResults\my-workbench-run 100
```

100 MiB 읽기/필터, 세 탭, 1 MiB 한 줄, 10만 검색 결과와 실제 reader 진행 후 취소를 실행한다. 긴 줄은 기본 구간 표시와 마지막 구간 검색을 측정한다. 같은 창에서 큰 탭을 닫고 복원 보관을 비운 뒤 원문·투영·문서 약한 참조 회수를 확인하는 경우와 전체 창 종료 후 검사를 구분한다. JSON에 시간·메모리·25ms dispatcher 대기·취소 확인을 구분하고 작은 창의 비표시 렌더를 생성한다. 높은 bitmap DPI는 실제 모니터 DPI 검증을 대신하지 않는다. 창을 닫은 뒤 GC 값과 약한 참조 회수 여부는 별도 지표이며, 미회수 원인 확인 없이 누수 여부를 결론내리지 않는다.
