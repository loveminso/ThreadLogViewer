# ThreadLog Viewer 검증 안내

현재 기능의 수동 확인 절차는 [기능 테스트 방법](FEATURE-TEST-GUIDE.md), 키 조작은 [단축키 표](SHORTCUTS.md), 실제 빌드·회귀·배포 결과는 [v0.8.0 검증 기록](VALIDATION-v0.8.0.md)을 따른다. 기대 결과와 실제 통과 기록을 구분한다.

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

## 실제 화면과 성능

사용자가 합성 샘플로 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)의 화면 조작을 직접 확인한다. 자동 테스트 통과나 숨긴 창의 PNG 생성은 실제 GUI 통과를 의미하지 않는다. 실제 로그는 테스트·샘플·문서에 넣지 않으며 `local-data` 안에서만 관리한다.

빌드 경고·오류, 자동 테스트 수, 실제 GUI 조작, 측정 범위가 있는 성능, 배포 파일·버전·해시를 각각 기록한다. 자동 테스트 실행 시간은 앱의 마우스·스크롤 반응 속도로 해석하지 않는다. 성능을 측정한다면 새 합성 자료와 새 출력 경로를 사용하고 기존 파일을 보존한다.

Release 빌드 뒤 합성 작업 측정은 테스트 실행 파일의 별도 경로를 사용한다. 아래 결과 경로가 없을 때 실행하며 첫 실행의 OS 캐시·GC 상태 영향을 함께 기록한다.

```powershell
& .\.tools\dotnet\dotnet.exe .\tests\ThreadLogViewer.Tests\bin\Release\net10.0-windows\ThreadLogViewer.Tests.dll --workbench-benchmark TestResults\my-workbench-run 100
```

100 MiB 읽기/필터, 세 탭, 1 MiB 한 줄, 10만 검색 결과와 실제 reader 진행 후 취소를 실행한다. 긴 줄은 기본 구간 표시와 마지막 구간 검색을 측정한다. 같은 창에서 큰 탭을 닫고 복원 보관을 비운 뒤 원문·투영·문서 약한 참조 회수를 확인하는 경우와 전체 창 종료 후 검사를 구분한다. JSON에 시간·메모리·25ms dispatcher 대기·취소 확인을 구분하고 작은 창의 비표시 렌더를 생성한다. 높은 bitmap DPI는 실제 모니터 DPI 검증을 대신하지 않는다. 창을 닫은 뒤 GC 값과 약한 참조 회수 여부는 별도 지표이며, 미회수 원인 확인 없이 누수 여부를 결론내리지 않는다.
