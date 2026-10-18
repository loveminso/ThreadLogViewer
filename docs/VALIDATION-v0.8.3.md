# v0.8.3 강조·설정·작은 화면·접근성 검증 — 2026-10-18

깨진 강조 규칙 건너뛰기, 화면 설정 LocalAppData 대체 저장, 1024×768 레이아웃·도움말 창, 접근성 이름, FlaUI `Scenario07`~`Scenario13`, PR CI 화면 갤러리·UI 린트를 `main`에 병합한 뒤 v0.8.3 릴리스 준비 PR로 확인한다. v0.8.3 버전 문자열·릴리스 노트는 이 문서와 README를 따른다. 새 성능·메모리 벤치마크는 실행하지 않았다.

## 변경 요약

- **강조**([PR #8](https://github.com/loveminso/ThreadLogViewer/pull/8), [PR #11](https://github.com/loveminso/ThreadLogViewer/pull/11)): 깨진 문자가 있는 강조 규칙 하나는 건너뛰고, 나머지 올바른 규칙은 계속 강조합니다. 상태줄 안내에는 목록 번호·색 이름·문구를 붙입니다.
- **설정**([PR #12](https://github.com/loveminso/ThreadLogViewer/pull/12)): 실행 폴더에 설정 파일이 없고 그 위치에 쓸 수 없으면 `%LOCALAPPDATA%\ThreadLogViewer\ui-settings.json`에 같은 형식으로 저장합니다. 설정을 읽지 못하면 경로를 적지 않은 안내를 표시합니다.
- **작은 화면**([PR #13](https://github.com/loveminso/ThreadLogViewer/pull/13)): 강조 문구 관리와 단축키 도움말 창이 메인 창 가운데에 열리고 1024×768 화면 안에 맞춥니다. 창이 좁으면 스레드 패널이 줄어들어 로그 본문이 넓어지며, 저장한 패널 너비는 유지합니다.
- 강조 문구 관리에서 새 규칙을 추가하면 아직 쓰지 않은 색을 받습니다. 도움말 창의 텍스트를 복사할 수 있습니다.
- 로그 본문, 보여줄 기록·숨길 기록 입력, 스레드 패널 너비 조절 분할선에 접근성 이름을 붙였습니다.
- **FlaUI UI 테스트**([PR #7](https://github.com/loveminso/ThreadLogViewer/pull/7)): `Scenario07`~`Scenario13`을 추가했습니다. 회차마다 13 통과 / 1 건너뜀 / 14 합계입니다.
- **CI**([PR #10](https://github.com/loveminso/ThreadLogViewer/pull/10)): 합성 화면 갤러리와 경고만 UI 린트를 PR CI에 포함합니다.
- **문서**([PR #9](https://github.com/loveminso/ThreadLogViewer/pull/9)): [DESIGN.md](DESIGN.md)를 추가했습니다.

## 알려진 한계

- 다른 프로세스가 **쓰는 UTF-8 BOM** 로그에서 F5 새로 고침이 `파일 새로 고침 실패`로 끝날 수 있다. CI의 `Scenario05_Utf8Bom_RefreshWhileAppending`은 이 한계 때문에 건너뛴다.
- CP949 로그는 F5를 누르는 순간 다른 프로그램이 쓰고 있는 경우가 검증되지 않았습니다.
- 끌어다 놓기는 CI에서 테스트 프로그램으로 검증하며, 실제 Explorer 창에서 끌어 오는 경우는 검증하지 않습니다.
- 설정 읽기·저장 실패 경로, 필터 작업 중 위치 이동, 검색 옵션 일부는 UI 자동 테스트가 없습니다.

## Windows CI (v0.8.3 릴리스 준비 PR)

`main` 병합 전 v0.8.3 릴리스 준비는 PR 이벤트의 [PR CI](https://github.com/loveminso/ThreadLogViewer/actions/workflows/pr-ci.yml)로 검증한다. 성공 실행과 TRX 건수는 해당 PR 설명에 기록한다.

## v0.8.3 릴리스 후 Windows release

`Directory.Build.props`의 `Version`을 `0.8.3`으로 올린 커밋이 `main`에 병합되면 Windows release가 게시되지 않은 `v0.8.3`을 선택한다. Release 빌드, 전체 자동 테스트, `scripts/publish.ps1` ZIP, `scripts/verify-release.ps1` 검증, GitHub **draft → publish** 릴리스, `ThreadLogViewer-win-x64.zip`과 `.sha256` 업로드, 공개 다운로드 링크 대조를 수행한다. 수동 `workflow_dispatch`도 같은 `main` 조건을 따른다.

## 이전 버전

v0.8.2 공유 읽기·예외·UI 자동화 검증은 [v0.8.2 검증 기록](VALIDATION-v0.8.2.md)에 보존한다.
