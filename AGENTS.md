# ThreadLog Viewer

- Preserve source logs and existing user files. Viewing is read-only; export must protect the source path.
- Runtime is local and offline: no network, telemetry, login, API, or server.
- Use only synthetic data in samples, tests, and performance reports. Keep real logs in ignored `local-data/`.
- Keep UI, core logic, and tests separate and small. No database, web framework, or plugin architecture.
- Test parsing, original line mapping, filtering, encoding, and source-safe export. Report build, automated tests, GUI checks, and measured performance separately.
- Install development tools or change anything outside the project only with user approval. Never upload or push without instruction.
- Automatically create coherent local Git commits when a requested change is complete and the relevant build and checks pass. Review the diff and include the task's code, tests, and documentation without asking again.
- Keep real logs, user settings, build artifacts, development runtimes, caches, credentials, and unrelated changes out of commits. Do not upload or push unless the user explicitly instructs it.

## 프로젝트 구조와 담당 범위

- 개발 언어는 C#이며, .NET 10 + WPF + AvalonEdit를 사용한다. 기존 기술 스택과 디자인을 따른다.
- UI는 `src/ThreadLogViewer.App/`, UI와 독립적인 핵심 로직은 `src/ThreadLogViewer.Core/`, 자동 테스트는 `tests/ThreadLogViewer.Tests/`에 있다.
- 화면 설정 저장은 현재 `src/ThreadLogViewer.App/SettingsStore.cs`와 `UiSettings.cs`가 담당한다. 저장 로직이라는 이유로 임의로 이동하거나 형식을 변경하지 않는다. 현재 DB는 사용하지 않으며 위의 DB 금지 지침을 유지한다.
- 사용자 정의 하위 에이전트는 `.codex/agents/desktop_ui.toml`, `.codex/agents/core_logic.toml`, `.codex/agents/reviewer.toml`에 정의한다. 모델과 추론 수준은 지정하지 않고 부모 설정을 상속한다.

## 총괄 운영 규칙

- 메인 에이전트가 총괄을 맡는다. 사용자의 기능 요청을 분석하고 완료 조건을 정한다.
- 작은 수정은 총괄이 직접 처리한다. 여러 영역에 걸친 작업은 필요한 하위 에이전트에 실제로 위임한다.
- 위임 전에 공통 데이터 구조와 인터페이스, 의존 관계를 정한다. 무거운 작업의 실행 방식, UI 스레드 복귀, 진행 상태와 취소 처리도 필요한 범위에서 합의한다.
- 각 담당에게 목표, 수정 가능한 파일 범위, 완료 조건을 전달한다. `desktop_ui`는 화면과 상호작용, `core_logic`은 파일 처리와 핵심 로직, `reviewer`는 통합 결과의 독립 검증을 담당한다.
- 독립적인 UI와 핵심 로직 작업은 병렬로 진행하고, 의존하는 작업은 순서대로 진행한다.
- 같은 파일을 여러 에이전트가 동시에 수정하지 않게 한다. 특히 `MainWindow`의 partial 파일, 설정 저장 파일, 테스트도 파일 단위로 담당을 정한다.
- 공통 파일과 의존성 변경은 총괄이 조율한다. 담당 범위 밖의 변경이 필요하면 먼저 총괄에게 보고한다.
- 결과를 통합한 뒤 `reviewer`로 검증하고, 발견된 문제의 수정을 총괄이 배정한다. 수정 후 필요한 항목을 다시 검증한다.
- 매번 모든 에이전트를 호출하지 않는다. 작업 범위와 의존 관계에 따라 필요한 담당만 호출한다.
- 백엔드 서버나 운영체제별 전담 에이전트는 필요한 기능이 생길 때만 추가한다. 추가 전에 로컬·오프라인 실행과 기존 아키텍처 지침에 맞는지 확인한다.
- 앞으로 사용자는 원하는 기능만 요청하면 되고, 총괄이 작업 분할부터 구현·통합·검증까지 처리한다.
- 하위 에이전트의 보고에는 수정 파일, 구현 결과, 실행한 검증과 결과, 확인하지 못한 항목을 포함한다. 빌드, 자동 테스트, GUI 확인, 측정 성능을 구분하고 실제로 확인하지 않은 항목을 통과로 보고하지 않는다.
- 사용자 정의 에이전트 선택 기능이 현재 세션에 없으면 역할을 흉내 내거나 호출 성공을 주장하지 않는다. 설정 파일 검증과 실제 역할 호출을 구분해 보고하고, 해당 설정을 읽는 새 프로젝트 대화에서 호출을 확인한다.
