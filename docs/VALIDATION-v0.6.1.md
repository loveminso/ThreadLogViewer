# v0.6.1 줄 선택·우클릭 대상 표시 검증 — 2026-10-03

상태: 줄 번호 전체 선택과 우클릭 행 표시를 구현했다. Release 빌드 경고 0 / 오류 0, 최종 전체 자동 테스트 178/178 통과, 새 self-contained portable 폴더·EXE 파일 버전 0.6.1.0 및 ZIP 507개 파일 전체의 배포 폴더 대비 SHA-256 일치를 확인했다. 새 비표시 합성 WPF 회귀 8개와 두 테마의 콘텐츠 렌더링도 통과했다. 실제 프로그램 창·입력 장치·클립보드를 제어하지 않았다. 이번 변경의 성능은 측정하지 않았다. 이전 [v0.6.0 검증](VALIDATION-v0.6.0.md)은 당시 결과로 보존한다.

## 구현 범위

- 원본 줄 번호의 왼쪽 클릭은 표시 투영에 매핑된 물리적 한 줄 전체를 선택한다. 실제 CRLF/LF/CR 개행은 포함하고 개행 없는 마지막 줄에는 개행을 더하지 않는다.
- 선택 후 커서를 클릭한 줄 시작에 두어 북마크·줄 작업이 다음 줄에 적용되지 않게 한다. 필터·분리 범위의 원본 번호를 유지하며 빈 물리적 줄도 선택할 수 있다.
- 마지막 개행 뒤 편집기용 가상 행·본문 아래 빈 공간·여백 밖은 줄 선택 대상으로 받지 않는다.
- 우클릭 대상은 원본 한 줄의 감긴 화면 영역 전체에 색·윤곽선, 여백의 굵은 원본 번호로 표시한다. 메뉴 제목에도 작업 대상 원본 번호를 표시한다.
- 대상 표시와 텍스트 선택은 별개다. 선택 문구 안의 우클릭과 강조 관리 창의 문구 미리 채우기를 유지한다. 메뉴 닫기·캡처 취소·필터/투영 변경·탭 전환은 대상 표시를 해제한다.

본문과 원본 자료는 읽기 전용이다. 선택·우클릭 표시로 로그 파일을 수정하거나 저장하지 않는다. 원본/기존 파일 덮어쓰기 차단과 실행 중 로컬 오프라인 원칙을 유지한다.

## 빌드와 배포

| 항목 | 상태 | 결과 |
|---|---|---|
| Release 빌드 | 통과 | 프로젝트 내부 SDK·NuGet 캐시를 사용한 `--no-restore` 빌드. 경고 0 / 오류 0. |
| 새 portable 폴더 | 통과 | `artifacts/ThreadLogViewer-v0.6.1-win-x64`, self-contained Windows x64. 오프라인 publish, EXE 파일 버전 0.6.1.0, runtime·라이선스 파일 480개. |
| 새 배포 ZIP | 통과 | `artifacts/ThreadLogViewer-v0.6.1-win-x64.zip`. 507개 파일 모두 배포 폴더와 SHA-256 일치. 사용자 자료·개발 환경 제외 확인. 파일 버전과 해시 메타데이터는 [verification-v0.6.1.json](../artifacts/verification-v0.6.1.json)에 기록. |

기존 배포·원본 로그·사용자 파일을 보존한다. 배포 EXE는 실행하지 않는다. SDK 설치·네트워크·업로드·push를 수행하지 않는다.

배포에는 runtime·라이선스, 합성 샘플, README·문서를 포함한다. `user-data`, `local-data`, `TestResults`, Git 자료, 개발 SDK·패키지 캐시를 포함하지 않는다. ZIP 자체의 정확한 해시는 배포 바깥 검증 JSON에 기록하며 묶인 문서에는 넣지 않는다.

## 자동 테스트

| 항목 | 상태 | 결과 |
|---|---|---|
| 줄 선택·대상 표시 집중 회귀 | 통과 | 8개 통과 / 실패 0 / 건너뜀 0. [line-selection-focused.trx](../TestResults/v0.6.1/line-selection-focused.trx). |
| 표시 조정 후 두 테마 렌더링 재검증 | 통과 | 본문 표식 폭 조정 후 위 8개 중 두 테마의 2개만 재실행해 통과. [line-action-final-render.trx](../TestResults/v0.6.1/line-action-final-render.trx). 전체 건수에 더하지 않음. |
| 최종 전체 회귀 | 통과 | 기존 170개와 새 8개를 합쳐 178개 통과 / 실패 0 / 건너뜀 0. [final-v0.6.1.trx](../TestResults/v0.6.1/final-v0.6.1.trx). |

[LineSelectionUxTests.cs](../tests/ThreadLogViewer.Tests/LineSelectionUxTests.cs)는 혼합 개행·끝의 개행 여부·빈 물리적 줄·가상 행·필터/범위 매핑·읽기 전용 불변 원문, 전체 선택 후 커서와 북마크 위치, 실제 여백 `SelectLineAtPoint` → `LineClicked` → 창의 전체 줄 선택 구독 경로, 자동 줄바꿈된 영역의 좌표, 선택 문구 보존, 메뉴 닫기와 필터/탭 전환의 대상 해제, 두 테마의 감긴 전체 행 렌더 영역을 확인한다.

비표시 STA dispatcher에서 합성 텍스트만 사용했다. 실제 입력 장치·클립보드·팝업 창은 사용하지 않고 메뉴 열림/닫힘 처리를 합성 콜백으로 실행했다. 폴더와 샘플은 프로젝트 테스트 출력 안에서 만들고 정리한다. 기존 170개 테스트의 기대 동작을 변경하지 않는다.

### 합성 콘텐츠 렌더링

[다크 PNG](../TestResults/v0.6.1/line-action-dark.png) · [라이트 PNG](../TestResults/v0.6.1/line-action-light.png)는 1100×700의 숨긴 창 콘텐츠를 `RenderTargetBitmap`으로 그린 이미지다. 자동 줄바꿈된 원본 2줄의 전체 대상 영역과 선택 문구를 함께 표시한다. 이 렌더링 테스트는 위 8개 안에 포함되며 별도 테스트로 중복 계산하지 않는다.

두 PNG를 읽어 자동 줄바꿈된 원본 2줄의 모든 감긴 영역과 굵은 여백 번호가 표시되고 기존 선택 문구가 구별되는 것을 확인했다. 첫 글자와 겹치던 본문 표식 폭을 조정한 뒤 다시 렌더링해 각 감긴 줄의 첫 글자를 읽을 수 있는 것을 확인했다. 실제 창을 화면에 띄운 GUI 검증과 구분한다. 실제 메뉴 팝업·제목줄·다른 DPI·스크롤 입력·실제 우클릭 좌표는 이 이미지로 검증하지 않는다.

프로젝트 루트에서 실행한 집중 검증:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
& .\.tools\dotnet\dotnet.exe test tests\ThreadLogViewer.Tests\ThreadLogViewer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~LineSelectionUxTests --logger 'trx;LogFileName=line-selection-focused.trx' --results-directory TestResults\v0.6.1
```

## 실제 GUI 확인

**미실행.** 사용자의 프로그램 제어 금지 지시를 유지한다. 실제 여백 클릭·우클릭 메뉴·줄바꿈·스크롤·문구 선택·복사·DPI는 사용자가 [줄 번호 클릭·우클릭 대상 표시 확인](FEATURE-TEST-GUIDE.md#줄-번호-클릭우클릭-대상-표시-확인) 절차로 직접 확인한다. 가이드의 기대 결과를 실제 GUI 통과로 보고하지 않는다.

## 실측 성능

**미측정.** 이번 줄 선택·대상 표시·실제 마우스 반응·렌더링 시간과 메모리를 별도 측정하지 않았다. 자동 테스트의 실행 시간은 사용자 화면 반응 속도로 해석하지 않는다. 이전 버전의 디코딩·Core 측정은 [v0.6.0 기록의 성능 범위 안내](VALIDATION-v0.6.0.md#실측-성능)를 따르며 이번 기능의 성능을 대표하지 않는다.
