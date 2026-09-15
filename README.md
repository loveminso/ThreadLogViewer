# ThreadLog Viewer v0.1

Windows x64용 SSD 멀티스레드 **읽기 전용** 로그 뷰어. C# + .NET 10 + WPF + AvalonEdit.
실행 중 인터넷, 로그인, API, 서버, DB, 텔레메트리를 사용하지 않습니다.
개발 시 .NET SDK와 NuGet 패키지 다운로드에는 인터넷이 필요합니다. 회사 PC에는 ZIP 전체를 풀어서 복사합니다.

## 개발도구

- Windows x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (런타임만으로 빌드 불가), Git.
- SDK 공식 설치 방법: https://learn.microsoft.com/dotnet/core/install/windows
- 관리자 설치 대신 SDK Windows x64 **Binaries ZIP**을 `.tools/dotnet/`에 풀어도 됩니다. 스크립트는 이 경로를 우선 사용하며 시스템 PATH를 변경하지 않습니다.
- 승인 후 프로젝트 내부 설치 스크립트: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-local-sdk.ps1`. Microsoft 공식 ZIP의 SHA512를 확인한 뒤 `.tools/dotnet/`에 압축 해제합니다.
- `global.json`은 정식 .NET 10 SDK를 선택합니다. `Directory.Build.props`의 NuGet 캐시와 CLI 설정은 프로젝트의 ignored 폴더를 사용합니다.
- 도구 설치는 저장소 사용자의 승인 후 진행합니다. `run.cmd` 등은 SDK를 자동 설치하지 않습니다.

## 실행 / 빌드 / 테스트

프로젝트 폴더에서:

```bat
run.cmd
run.cmd samples\mixed-utf8.log
build.cmd
test.cmd
publish.cmd
benchmark.cmd 10
benchmark.cmd 100
```

시스템 SDK가 설치된 경우 개별 명령:

```bat
dotnet build ThreadLogViewer.slnx -c Release
dotnet test ThreadLogViewer.slnx -c Release
```

프로젝트 내부 SDK는 `.tools\dotnet\dotnet.exe`로 `dotnet`을 대체합니다.
CMD 창에서 스크립트를 실행하면 오류와 종료 코드를 확인할 수 있습니다. 실패한 빌드·테스트·배포는 0이 아닌 코드로 종료합니다.

## 회사 PC 배포

GitHub 저장소의 **Releases**에서 `ThreadLogViewer-win-x64.zip`을 받습니다. GitHub가 자동 생성하는 `Source code (zip)`은 소스 코드이며 실행용 배포물이 아닙니다.
공개 릴리스의 ZIP은 GitHub 로그인 없이 다운로드할 수 있고, 내려받은 앱의 실행에는 인터넷이 필요하지 않습니다.

`publish.cmd` 결과:

- 폴더: `artifacts/ThreadLogViewer-win-x64/`
- ZIP: `artifacts/ThreadLogViewer-win-x64.zip`

ZIP을 **모두 압축 해제한 뒤 `ThreadLogViewer.exe` 실행**. DLL과 `licenses/` 등 나머지 파일도 함께 유지합니다.
Release / Windows x64 / self-contained / 폴더형 배포이며 회사 PC의 별도 .NET SDK 설치가 필요하지 않습니다.
코드 서명은 하지 않습니다. 배포물에는 의존성 라이선스와 합성 샘플을 포함합니다.

## 사용법

1. ‘파일 열기’ 또는 Ctrl+O로 `.log`, `.txt`를 선택합니다. **파일 하나를 앱 창에 끌어 놓거나, 탐색기에서 파일을 복사한 뒤 앱에서 Ctrl+V**로 열 수도 있습니다. 여러 파일·폴더는 한 번에 열지 않습니다. 실패하면 기존 화면을 유지합니다.
2. BOM이 있으면 UTF-8 또는 UTF-16 LE/BE로 읽습니다. BOM 없는 유효 UTF-8은 **추정**이라고 표시합니다. 해석 실패 시 ‘CP949로 다시 읽기’를 누릅니다. CP949는 사용자 지정이며 확정 판별이 아닙니다.
3. 스레드 체크박스·전체 선택·전체 해제·‘단독’으로 필터링합니다. ‘미분류’에는 빈 줄과 스레드 없는 덤프도 포함됩니다.
4. 왼쪽의 최초/마지막 기록은 **파일 순서에서 처음/마지막으로 관측된 유효 시각**입니다. 스레드 생성 시각이 아닙니다. 날짜 없는 자정 통과를 임의로 날짜에 배정하지 않습니다.
5. 중앙 여백은 **원본 로그 파일의 줄 번호**입니다. `(testcase.cpp:103)`의 103은 소스 코드 줄 번호입니다. 여백은 복사·내보내기에 섞이지 않습니다.
6. 드래그/Shift+화살표로 다중 행 선택, Ctrl+A, Ctrl+C. 가로·세로 스크롤, 자동 줄바꿈, 글자 크기를 지원합니다. 편집·저장은 비활성화되어 있습니다.
7. Ctrl+F로 **현재 표시 중인 로그만** 대소문자 구분 없이 일반 텍스트 검색. Enter/F3 다음, Shift+Enter/Shift+F3 이전, Esc 닫기. 노란색 검색 강조, 짙은 파란색 선택 영역.
8. ‘필터 결과 내보내기’는 **UTF-8 (BOM 없음)** 새 파일을 만듭니다. 원본 경로와 기존 파일의 덮어쓰기를 모두 차단합니다. 기존 파일을 허용하지 않아 원본의 하드 링크·별칭을 통한 덮어쓰기도 방지합니다.

### 파일 없이 로그 텍스트 붙여넣기

- 다른 프로그램에서 로그 텍스트를 복사한 뒤 로그 화면에서 **Ctrl+V**, **Shift+Insert**, 상단 ‘붙여넣기’, 또는 우클릭 ‘붙여넣은 로그 열기’를 사용합니다.
- 붙여넣기는 현재 보기를 **새 읽기 전용 로그로 대체**합니다. 문서에 삽입하거나 원본 파일을 수정하지 않습니다. 이어 붙이기는 지원하지 않습니다.
- 붙여넣은 내용에도 스레드 색상·필터·검색·내보내기가 적용됩니다. 줄 번호는 **붙여넣은 텍스트의 첫 줄부터 1**이며 복사해 온 원본 파일의 줄 번호를 추정하지 않습니다.
- 클립보드는 이미 해석된 Unicode 텍스트이므로 원본 파일 인코딩은 알 수 없다고 표시합니다. ‘CP949로 다시 읽기’는 비활성화됩니다. 필요하면 원본 파일을 직접 여세요.
- 검색창의 Ctrl+V는 검색어만 붙여넣습니다. 검색창에 포커스가 있어도 **Ctrl+Shift+V**를 누르면 클립보드를 새 로그로 엽니다.
- 빈 클립보드·텍스트가 아닌 내용·여러 파일을 붙여넣으면 안내하고 현재 화면을 유지합니다. 붙여넣은 로그의 내보내기도 기존 파일을 덮어쓰지 않습니다.

## 데이터 / 파싱

- `[HH:mm:ss[.소수초]] [T3] 메시지 (소스파일:줄번호)` 또는 `[T 3]`. 소수초는 1~7자리까지 파싱하며 표시는 입력 그대로입니다.
- 누락되거나 잘못된 필드가 있어도 원문과 원본 줄을 보존합니다. 상태줄의 ‘완전’은 시각·스레드·소스 위치가 모두 있는 줄, ‘부분’은 일부만 해석한 줄, ‘미인식’은 필드를 해석하지 못한 줄입니다.
- 메시지 안쪽의 `[T3]`를 스레드 접두어로 해석하지 않습니다. 여러 줄 덤프를 이전 스레드에 귀속시키지 않습니다.
- CRLF/LF/CR와 마지막 개행 여부를 원문·내보내기에 보존합니다. 끝 개행 뒤의 편집기 커서용 빈 행은 원본 행 수에 포함하지 않아 여백 번호가 없습니다. 0바이트 파일은 0줄입니다.
- 스레드 색은 ID에 대한 고정 팔레트입니다. 12색 팔레트는 반복될 수 있으므로 번호와 함께 식별합니다.

## 샘플 / 성능

[samples/README.md](samples/README.md)에 합성 샘플을 설명했습니다. `line-mapping.log`에서 T 3만 표시하면 100, 105가 남습니다.

```bat
dotnet run --project tests\ThreadLogViewer.Tests -c Release -- --benchmark local-data 10
dotnet run --project tests\ThreadLogViewer.Tests -c Release -- --benchmark local-data 100
```

벤치마크는 파일을 생성한 뒤 읽기·디코딩·파싱, 필터 투영, AvalonEdit 문서 생성 시간과 해당 콘솔 프로세스 메모리를 JSON으로 저장합니다. UI 배치/렌더 시간은 포함하지 않습니다. 합성 10/100 **MiB**(1 MiB=1,048,576바이트)를 사용합니다.

실제 빌드·자동 테스트·GUI 확인 및 10/100 MiB 측정 결과는 [검증 기록](docs/VALIDATION.md)에 있습니다. 100 MiB 콘솔 측정의 최대 작업 집합은 약 960 MiB였습니다.

## 구조와 현재 한계

- `src/ThreadLogViewer.Core`: UI와 독립적인 읽기, 인코딩, 파싱, 줄 매핑, 필터, 검색, 원본 보호 내보내기.
- `src/ThreadLogViewer.App`: WPF 화면과 AvalonEdit 렌더링. 화면에 보이는 줄만 그립니다.
- `tests/ThreadLogViewer.Tests`: xUnit 자동 테스트와 재현 가능한 벤치마크.
- 메모리에 디코딩 원문·행 정보·표시 문서를 유지합니다. 2GB 이상 파일은 차단하며 그 미만도 RAM과 줄 길이에 따라 한계가 있습니다. 극단적으로 긴 한 줄은 표시가 느려질 수 있습니다.
- 전체 디코딩 및 AvalonEdit 문서 생성의 내부 단계는 즉시 취소되지 않을 수 있습니다. 백그라운드에서 끝난 뒤 취소를 확인하며 UI는 계속 조작할 수 있습니다.
- 검색은 첫 100,000개까지 강조·이동하며 초과 여부를 표시합니다. 닫기로 검색을 취소할 수 있습니다. 필터 변경은 검색을 다시 수행합니다.
- 글꼴 폴백의 한글 폭은 설치 글꼴에 영향을 받습니다.
- 실시간 파일 추적·편집·타임라인·북마크는 후속 기능입니다. [계획](docs/PLAN.md) 참고.
- 공개 저장소에는 소스·문서·합성 샘플만 포함합니다. 실제 로그는 버전 관리에서 제외한 `local-data/`에 보관하고 공개 저장소나 릴리스에 올리지 마세요.

의존성 고지: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
