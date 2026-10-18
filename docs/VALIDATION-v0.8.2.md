# v0.8.2 공유 읽기·예외·UI 자동화 검증 — 2026-10-18

다른 프로세스가 열거나 쓰는 로그의 공유 읽기, 전역 예외 처리·비동기 UI 보호, FlaUI UI 자동화(Windows CI 3회 연속)를 `main`에 병합한 뒤 확인했다. v0.8.2 버전 문자열·릴리스 노트는 이 문서와 README를 따른다. 새 성능·메모리 벤치마크는 실행하지 않았다.

## 변경 요약

- **공유 읽기**([PR #2](https://github.com/loveminso/ThreadLogViewer/pull/2)): 다른 프로그램이 읽기 공유를 허용한 채 열거나 쓰고 있는 로그도 열 수 있습니다(FileShare.ReadWrite·Delete). 다른 프로그램이 파일을 독점으로 열었으면 열 수 없습니다.
- **전역 예외 처리**([PR #3](https://github.com/loveminso/ThreadLogViewer/pull/3)): UI 작업과 비동기 작업에서 난 예외는 한국어 경고 창을 띄운 뒤 앱을 계속 실행합니다. 메모리 부족 같은 치명적 오류와 백그라운드 스레드 예외는 경고 후 종료됩니다. 예외 시각·종류·발생 경로만 `%LOCALAPPDATA%\ThreadLogViewer\unhandled-exceptions.log`에 남기고, 로그 내용과 파일 경로는 기록하지 않습니다. 잘못된 강조 규칙은 상태줄에 `강조 준비 실패`를 표시합니다.
- **UI 자동화**([PR #4](https://github.com/loveminso/ThreadLogViewer/pull/4), [PR #5](https://github.com/loveminso/ThreadLogViewer/pull/5)): `tests/ThreadLogViewer.UiTests`의 FlaUI 시나리오를 Windows CI에서 게시한 `ThreadLogViewer.exe`로 한 잡당 **3회** 연속 실행한다.

## 알려진 한계

- 다른 프로세스가 **쓰는 UTF-8 BOM** 로그에서 F5 새로 고침이 `파일 새로 고침 실패`로 끝날 수 있다. CI의 `Scenario05_Utf8Bom_RefreshWhileAppending`은 이 한계 때문에 건너뛴다. 수정은 후속 작업이다.
- CP949 로그는 F5를 누르는 순간 다른 프로그램이 쓰고 있는 경우가 검증되지 않았습니다.
- 강조 규칙 중 하나에 깨진 문자가 있으면 모든 강조가 표시되지 않습니다.
- 끌어다 놓기는 CI에서 테스트 프로그램으로 검증하며, 실제 Explorer 창에서 끄는 경우는 검증하지 않습니다.
- 원본 줄 이동, 위치·선택 정보, 주변 로그, 필터 후 위치 유지, 설정 저장, 두 기록 시간 차이(기능 1·2·8·9·10·11)는 UI 자동 테스트가 없습니다.

## Windows CI (병합 직전 PR #5)

`main`의 현재 기능은 PR 이벤트의 [PR CI](https://github.com/loveminso/ThreadLogViewer/actions/workflows/pr-ci.yml)로 검증한다. `main` push만으로는 [Windows release](https://github.com/loveminso/ThreadLogViewer/actions/workflows/windows-release.yml)가 실행되지만, 게시된 버전(`v0.8.1`)과 `Directory.Build.props`가 같으면 빌드·테스트·ZIP 생성을 **건너뛴다**. 따라서 v0.8.2 병합 전 기준은 PR #5의 성공 실행을 따른다.

| 검증 | 결과 | 기록 |
|---|---|---|
| PR CI (PR #5) | 성공 | [run 39129854369](https://github.com/loveminso/ThreadLogViewer/actions/runs/39129854369) |
| Release 빌드 | 경고 0/오류 0 | 동일 run |
| 단위 테스트 | 355/355 통과, 실패 0, 건너뜀 0 | `tests.trx` (동일 run job summary) |
| UI iteration 1 | 6 통과 / 1 건너뜀 / 0 실패 / 7 합계 | `ui-tests-1.trx` |
| UI iteration 2 | 6 통과 / 1 건너뜀 / 0 실패 / 7 합계 | `ui-tests-2.trx` |
| UI iteration 3 | 6 통과 / 1 건너뜀 / 0 실패 / 7 합계 | `ui-tests-3.trx` |

건너뛴 1건은 `Scenario05_Utf8Bom_RefreshWhileAppending`(UTF-8 BOM + 쓰기 중 F5 한계)이다. SmartScreen·실제 모니터 DPI·새 성능 측정은 이 CI에 포함하지 않았다.

## v0.8.2 릴리스 후 Windows release

`Directory.Build.props`의 `Version`을 `0.8.2`로 올린 커밋이 `main`에 병합되면 Windows release가 게시되지 않은 `v0.8.2`를 선택한다. Release 빌드, 전체 자동 테스트, `scripts/publish.ps1` ZIP, `scripts/verify-release.ps1` 검증, GitHub **draft → publish** 릴리스, `ThreadLogViewer-win-x64.zip`과 `.sha256` 업로드, 공개 다운로드 링크 대조를 수행한다. 수동 `workflow_dispatch`도 같은 `main` 조건을 따른다.

## 이전 버전

v0.8.1 북마크 제거 검증은 [v0.8.1 검증 기록](VALIDATION-v0.8.1.md)에 보존한다. 북마크는 v0.8.1부터 제공하지 않는다.
