# v0.8.3 메모리 측정

버전은 `Directory.Build.props`의 `0.8.3`이다. 로딩 구현은 바꾸지 않았다. 측정은 Windows PR CI에서 기존 콘솔 `--benchmark`에 `--memory-refreshes`를 붙인 경로다. 절차와 경고 기준은 [검증 안내](TESTING.md)의 `합성 로그 메모리` 절과 같다.

## 측정하는 것

크기마다 `tests/ThreadLogViewer.Tests` 프로세스를 새로 띄운다. FlaUI와 게시한 `ThreadLogViewer.exe`는 쓰지 않는다.

- 열기: `LogFileReader.ReadAsync`, `LogData`, 모든 스레드에 대한 `LogProjection.Create`, `CancellableDocumentFactory.Create`.
- 필터: 스레드 3, 7, 미분류. 내용 필터는 비어 있다. 기존 시간 벤치마크의 `LogProjection.Create(log, [3, 7, null])`과 같다. 일부만 보이므로 `LogProjection.Text`는 `LogData.Text`와 다른 문자열이다.
- 강조: `status=OK`를 `LogSearch.Find`로 찾고 `HighlightIndex`에 넣는다. 상한은 `LogSearch.MaxHighlights`다. `RefreshKeywordsAsync`가 쓰는 검색이다.
- F5: 같은 파일을 `LogFileReader.ReadAsync`로 다시 읽고, `SourceLineRemap.Create`, `LogProjection.CreateFiltered`, 문서 복사, 강조를 다시 만든다. 이전 원문·투영·문서·강조 참조는 새 결과가 생긴 뒤에 놓는다.
- 빼는 것: `MainWindow`, WPF 배치·렌더, dispatcher, 화면 입력.

GC는 `GC.Collect`, `WaitForPendingFinalizers`, `GC.Collect`를 스레드 풀에서 실행한 뒤 `GC.GetTotalMemory(false)`를 읽는다. 작업 집합과 최대 작업 집합은 그 직후의 프로세스 값이다. private bytes는 `PrivateMemorySize64`가 되면 적고, 아니면 없다고 적는다. 최대 작업 집합은 프로세스 누적 `PeakWorkingSet64`라서 단계마다 초기화되지 않는다.

10/100/500 MiB는 F5 10회다. 1024 MiB는 한 번만 돌리고 F5는 3회다. 1 MiB는 1,048,576바이트다. 합성 파일은 CI의 임시 폴더에 만들고 그 크기가 끝나면 지운다. 원본 로그 자리에는 쓰지 않는다.

## 증가 확인

F5 표본만 비교한다. 열기 표본과 필터 표본은 표에는 있지만 기울기에는 넣지 않는다. 표본이 3개보다 적으면 경고하지 않는다. 관리 힙 마진은 열기 직후 GC 뒤 관리 힙의 `값/50`과 32 MiB 중 큰 값이다. 작업 집합 마진은 열기 직후 GC 뒤 작업 집합의 `값/25`와 64 MiB 중 큰 값이다. 지표가 F5마다 직전보다 크고 마지막이 첫 F5보다 마진을 초과하면 `누수 의심, 원인 미확인`이다. 이 문장은 누수가 있다는 뜻이 아니다. 경고가 없어도 누수가 없다는 뜻이 아니다. 경고는 CI를 실패시키지 않는다.

## 결과

이 파일의 숫자 표는 아직 없다. 측정 단계가 끝난 CI 실행의 `memory-results.json`을 받은 뒤에, 그 실행의 run id와 head sha와 같이 채운다. 그 전에 시간이나 메모리를 적지 않는다.
