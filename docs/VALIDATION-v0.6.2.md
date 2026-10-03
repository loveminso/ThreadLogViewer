# v0.6.2 여러 줄 선택·즉시 문구 강조 검증 — 2026-10-03

상태: 줄 번호 드래그·Ctrl 불연속 행 선택·선택 행 복사와 Shift+F8 즉시 문구 강조를 구현했다. 최종 Release 빌드 경고 0 / 오류 0, 전체 자동 회귀 212/212 통과와 집중 회귀 42/42 통과, 새 self-contained portable 폴더·EXE 파일 버전 0.6.2.0 및 ZIP 509개 파일 전체의 배포 폴더 대비 SHA-256 일치를 확인했다. 실제 프로그램 창·마우스·클립보드는 제어하지 않았다. 이전 [v0.6.1 검증](VALIDATION-v0.6.1.md)의 178개 통과 기록을 보존한다.

## 구현 범위

- 줄 번호에서 왼쪽 버튼을 누르고 드래그해 표시한 물리적 여러 줄을 선택한다. 위/아래 방향과 자동 줄바꿈된 원본 한 줄을 같은 원본 줄 번호로 처리한다.
- Ctrl+줄 번호 클릭으로 떨어진 줄의 선택을 추가하거나 해제한다. Ctrl+C는 선택한 표시 줄만 원본 순서로 복사하며 실제 CRLF/LF/CR 개행과 마지막 줄의 개행 유무를 유지한다. 숨긴 중간 원본 줄과 여백 번호를 끼워 넣지 않는다.
- 각 탭의 행 선택과 기존 텍스트 선택을 구분한다. 일반 문구 선택·복사와 읽기 전용 원문을 유지한다. 새 표시 투영을 게시하면 오래된 행 선택과 진행 중인 드래그를 해제한다.
- Shift+F8은 선택 문구 또는 커서 단어를 즉시 강조하며 대소문자 구분 없이 동일 문구를 다시 실행하면 같은 규칙을 해제한다. 새 문구는 미사용 색을 우선 배정하고 규칙은 현재 탭에서 최대 8개다. 불연속 여러 조각·공백만인 문구·4,096자 초과 선택을 거절하고 관리 창은 메뉴로 제공한다.

원본/기존 파일을 덮어쓰지 않고 실행 중 로컬 오프라인 원칙을 유지한다. 합성 자료만 테스트·렌더링·보고서에 사용한다. 실제 로그를 기록하거나 업로드하지 않는다.

## 빌드와 배포

| 항목 | 상태 | 결과 |
|---|---|---|
| Release 빌드 | 통과 | 기존 SDK·NuGet 캐시 사용, 최종 경고 0 / 오류 0. 새 설치·패키지 복원·네트워크 없이 검증. |
| 새 portable 폴더 | 통과 | `artifacts/ThreadLogViewer-v0.6.2-win-x64`, self-contained Windows x64. 오프라인 publish, EXE 파일 버전 0.6.2.0, runtime·라이선스 파일 480개. |
| 새 배포 ZIP | 통과 | `artifacts/ThreadLogViewer-v0.6.2-win-x64.zip`. 509개 파일 모두 배포 폴더와 SHA-256 일치. 중복·누락·예상 밖 경로 없음, 사용자 자료·개발 환경 제외 확인. 파일 버전과 정확한 해시는 [verification-v0.6.2.json](../artifacts/verification-v0.6.2.json)에 기록. |

기존 배포·원본·사용자 파일을 보존한다. 배포 EXE는 실행하지 않는다. SDK 설치·네트워크·업로드·push를 수행하지 않는다. ZIP 자체의 해시는 배포 밖 검증 JSON에 기록하며 묶인 문서에는 넣지 않는다.

배포에는 runtime·라이선스 480개, 합성 샘플 11개, 문서 17개와 README를 포함한다. `user-data`, `local-data`, `TestResults`, Git 자료, 개발 SDK·패키지 캐시를 포함하지 않는다.

## 자동 테스트

| 항목 | 상태 | 결과 |
|---|---|---|
| 초기 선택·강조 집중 회귀 | 통과 | 빠른 강조 22개 + 다중 줄 선택 8개 + 기존 단일 줄 선택 8개 = 38개 통과 / 실패 0 / 건너뜀 0. [selection-highlight-focused.trx](../TestResults/v0.6.2/selection-highlight-focused.trx). |
| 최종 선택·강조 집중 회귀 | 통과 | 빠른 강조 22개 + 다중 줄 선택 12개 + 기존 단일 줄 선택 8개 = 42개 통과 / 실패 0 / 건너뜀 0. [selection-highlight-final-focused.trx](../TestResults/v0.6.2/selection-highlight-final-focused.trx). |
| 최종 전체 회귀 | 통과 | 기존 178개 + 새 다중 줄 선택 12개 + 새 빠른 강조 22개 = 212개 통과 / 실패 0 / 건너뜀 0. [final-v0.6.2.trx](../TestResults/v0.6.2/final-v0.6.2.trx). |

[MultiLineSelectionUxTests.cs](../tests/ThreadLogViewer.Tests/MultiLineSelectionUxTests.cs)는 원본 순서·혼합 개행·끝 행·빈 물리적 줄·가상 행·필터/분리 범위·탭 상태·일반 텍스트 선택, `SelectionSourceRanges.Map`과 `SelectionMetrics` 및 고정 선택 검색의 불연속 간격, Ctrl 드래그의 원래 집합·방향 반전/축소, 물리적 자동 스크롤과 양 끝 정지, 우클릭 안쪽의 불연속 선택 복원, 같은 문서 새 투영 게시 시 무효화를 검사한다. `WholeLineSelection.ReplaceSelectionWithText`가 편집기와 불변 원문을 수정하지 않는 것도 확인한다.

복사는 `WholeLineSelection.CreateDataObject`가 만드는 UnicodeText를 합성 자료로 검사한다. 기본 Ctrl+C·`Editor.Copy` 복사 라우팅은 앱 코드와 로컬 AvalonEdit API에서 확인했다. 실제 Ctrl+C와 클립보드는 실행하지 않았다. 숨긴 STA dispatcher에서 여백 좌표와 이벤트 연결을 검사하고 원본 텍스트·읽기 전용 상태를 별도로 확인한다.

[QuickHighlightTests.cs](../tests/ThreadLogViewer.Tests/QuickHighlightTests.cs)는 창을 열지 않는 토글, 정확한 선택 문구·커서 단어, 같은 문구의 중복/비활성 규칙 삭제, 미사용 색과 재사용, 8개 상한·기존 해제, 긴/공백 선택 거절·오래된 투영·Unicode 단어 경계를 검사한다.

다중 줄 선택 12개와 빠른 강조 22개는 최종 전체 212개에 포함된다. 초기 38개 및 최종 집중 42개는 반복 실행 기록이며 전체 통과 수에 더하지 않는다. 기존 178개 테스트를 이번 동작에 맞춰 느슨하게 변경하지 않았다.

### 합성 콘텐츠 렌더링

[다크 PNG](../TestResults/v0.6.2/multi-line-dark.png) · [라이트 PNG](../TestResults/v0.6.2/multi-line-light.png)는 1160×780의 숨긴 창 콘텐츠를 `RenderTargetBitmap`으로 그린 합성 이미지다. 원본 2·5·9줄을 선택하고 긴 물리적 2줄을 자동 줄바꿈했다. 두 이미지를 읽어 떨어진 선택 줄·감긴 2줄 전체 영역·굵은 원본 번호가 두 테마에서 읽히는 것을 확인했다.

렌더링은 다중 줄 선택 회귀 건수에 포함되며 별도 테스트로 중복 계산하지 않는다. 실제 GUI·메뉴·제목줄·다른 DPI·마우스 움직임·자동 스크롤 타이머는 검증하지 않았다. 선택 정보는 비동기 계산 중일 수 있어 PNG의 상태줄을 집계 정확성 증거로 삼지 않는다. 집계는 별도 합성 `SelectionMetrics` 회귀로 확인한다.

## 실제 GUI 확인

**미실행.** 사용자의 프로그램 제어 금지 지시를 유지한다. 사용자가 [기능 테스트 방법](FEATURE-TEST-GUIDE.md)에서 줄 번호 드래그·Ctrl 선택·Ctrl+C·Shift+F8·탭 전환을 합성 샘플로 직접 확인한다. 기대 결과를 실제 GUI 통과로 보고하지 않는다.

## 실측 성능

**미측정.** 다중 줄 선택·불연속 복사·강조 토글·실제 스크롤·렌더링 시간과 메모리를 별도 측정하지 않았다. 자동 테스트의 실행 시간을 사용자 화면 반응 속도로 해석하지 않는다. 이전 버전의 디코딩·Core 측정은 [v0.6.0 기록의 성능 범위](VALIDATION-v0.6.0.md#실측-성능)를 따르며 이번 변경의 성능을 대표하지 않는다.
