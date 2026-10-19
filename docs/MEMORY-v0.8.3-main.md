# v0.8.3 메모리 측정

버전은 `Directory.Build.props`의 `0.8.3`이다. 로딩 구현은 바꾸지 않았다. 측정은 Windows PR CI에서 기존 콘솔 `--benchmark`에 `--memory-refreshes`를 붙인 경로다. 절차와 경고 기준은 [검증 안내](TESTING.md)의 `합성 로그 메모리` 절과 같다.

## 측정하는 것

크기마다 `tests/ThreadLogViewer.Tests` 프로세스를 새로 띄운다. FlaUI와 게시한 `ThreadLogViewer.exe`는 쓰지 않는다.

- 열기: `LogFileReader.ReadAsync`, `LogData`, 모든 스레드에 대한 `LogProjection.Create`, `CancellableDocumentFactory.Create`.
- 필터: 스레드 3, 7, 미분류. 내용 필터는 비어 있다. 기존 시간 벤치마크의 `LogProjection.Create(log, [3, 7, null])`과 같다. 일부만 보이므로 `LogProjection.Text`는 `LogData.Text`와 다른 문자열이다.
- 강조: `status=OK`를 `LogSearch.Find`로 찾고 `HighlightIndex`에 넣는다. 상한은 `LogSearch.MaxHighlights`다. `RefreshKeywordsAsync`가 쓰는 검색이다.
- F5: 같은 파일을 `LogFileReader.ReadAsync`로 다시 읽고, `SourceLineRemap.Create`, `LogProjection.CreateFiltered`, 문서 복사, 강조를 다시 만든다. 이전 원문·투영·문서·강조 참조는 새 결과가 생긴 뒤에 놓는다.
- 빼는 것: `MainWindow`, WPF 배치·렌더, dispatcher, 화면 입력.

이 숫자는 ThreadLogViewer.exe의 메모리가 아니다. 화면·배치·렌더와 앱이 따로 붙잡는 상태가 빠져 있어 실제 앱은 이보다 클 수 있다.

GC는 `GC.Collect`, `WaitForPendingFinalizers`, `GC.Collect`를 스레드 풀에서 실행한 뒤 `GC.GetTotalMemory(false)`와 `GC.GetGCMemoryInfo`를 읽는다. 커밋은 `TotalCommittedBytes`, 조각화는 `FragmentedBytes`, LOH는 `GenerationInfo` 인덱스 3의 `SizeAfterBytes`다. 작업 집합과 최대 작업 집합은 그 직후의 프로세스 값이다. private bytes는 `PrivateMemorySize64`가 되면 적고, 아니면 없다고 적는다. 최대 작업 집합은 프로세스 누적 `PeakWorkingSet64`라서 단계마다 초기화되지 않는다.

10/100/500 MiB는 매 PR의 `build-test-publish`에서 F5 10회다. 1024 MiB는 그 잡에 넣지 않는다. 최대 작업 집합이 16 GB 러너에서 약 14 GB까지 올라 메모리 부족이 날 수 있어서다. 1024 MiB는 같은 워크플로 파일의 `measure-one-gigabyte` 잡이고, `workflow_dispatch`로 그 ref를 지정했을 때만 돈다. F5는 3회다. 1 MiB는 1,048,576바이트다. 합성 파일은 CI의 임시 폴더에 만들고 그 크기가 끝나면 지운다. 원본 로그 자리에는 쓰지 않는다.

## 증가 확인

F5 표본만 기울기에 넣는다. 열기 표본과 필터 표본은 표에 남지만 기울기에는 넣지 않는다. F5 1회는 워밍업이고, 최소제곱 기울기는 F5 2회부터 마지막까지다. x는 1부터 세는 F5 번호다. 판정 지표는 GC 뒤 관리 힙과 private bytes뿐이다. 그 기울기에 `(횟수-1)`을 곱한 값이 마진보다 크면 그 지표는 `누수 의심, 원인 미확인`이다. F5가 4개보다 적으면 그 지표는 `판정 불가(표본 부족)`이고, `지속 증가 경고 없음`으로 적지 않는다. 1024 MiB는 F5가 3회라서 이 경우다. GC 커밋과 private bytes에서 GC 커밋을 뺀 값은 JSON, 잡 요약, 이 표에 바이트로 남긴다. 그 두 칸의 판정은 `기록용(판정 안 함)`이고 `::warning`을 내지 않는다.

판정하는 지표는 GC 뒤 관리 힙과 private bytes다. GC 커밋, private bytes에서 GC 커밋을 뺀 값, 작업 집합, LOH(`GenerationInfo` 3번의 `SizeAfterBytes`), `FragmentedBytes`는 적기만 한다. 관리 힙의 마진은 열기 직후 그 값의 `값/50`과 32 MiB 중 큰 값이다. private bytes의 마진은 열기 직후 그 값의 절댓값을 25로 나눈 값과 64 MiB 중 큰 값이다. `누수 의심, 원인 미확인`은 누수가 있다는 뜻이 아니다. 경고가 없어도 누수가 없다는 뜻이 아니다. 경고는 CI를 실패시키지 않는다.

## 결과

10/100/500 MiB는 PR CI 실행 [39212578254](https://github.com/loveminso/ThreadLogViewer/actions/runs/39212578254)에서 왔다. 그 실행의 head는 `55ee84df4d7b993786c5d07a5d452ad1db918b85`다. `memory-results.json`의 `GithubSha`는 `90e0bb2bc79857a7a89795c4b528285f70edad41`이고, 이것은 그 head를 `f86af78da6ca16523a5c83889bd8af1e87e4c43c`에 합친 pull_request 병합 커밋이다. 아티팩트 `memory-results`는 [12169076901](https://github.com/loveminso/ThreadLogViewer/actions/runs/39212578254/artifacts/12169076901)이다.

1024 MiB 행의 숫자는 그대로다. 그 행은 예전에 10/100/500 MiB와 같은 PR CI 단계에서 잰 값이다. 실행은 [39208016760](https://github.com/loveminso/ThreadLogViewer/actions/runs/39208016760)이고, head는 `3487b8dbbf286c79b29ac48508482788dec12d0c`다. `workflow_dispatch`로 잰 값이 아니다. 이 문서의 head에서 잰 값도 아니고, 10/100/500 MiB를 잰 head `55ee84df4d7b993786c5d07a5d452ad1db918b85`에서 잰 값도 아니다.

10/100/500 MiB를 잰 환경은 Windows NT 10.0.26100.0, .NET 10.0.13, 논리 프로세서 4개다. 그 세 크기의 private bytes는 `PrivateMemorySize64`로 읽혔다.

`Measure memory on synthetic logs`는 2026-10-19T07:12:10Z에 시작해 2026-10-19T07:13:46Z에 끝났다. 스크립트가 기록한 `StepElapsedMs`는 94932이다. 같은 잡 `build-test-publish`는 2026-10-19T06:53:54Z부터 2026-10-19T07:13:51Z까지다. `measure-one-gigabyte`는 pull_request라서 건너뛰었다.

아래 10/100/500 MiB 바이트는 실행 39212578254의 마지막 F5 직후, GC 뒤 값이다. 100 MiB와 500 MiB의 강조 범위는 `LogSearch.MaxHighlights`(100000)에서 잘렸고, 10 MiB는 잘리지 않았다. 1024 MiB 행은 실행 39208016760의 마지막 F5 값이다. 단계별 표본은 각 실행의 아티팩트 JSON에 있다.

| MiB | 파일 바이트 | 줄 | F5 | 작업 집합 | private | 관리 힙 | 커밋 | LOH | 조각화 | 관리 힙 판정 | 커밋 판정 | private 판정 | private−커밋 판정 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- | --- |
| 10 | 10485800 | 121881 | 10 | 188866560 | 164814848 | 78866880 | 149946368 | 98825240 | 24695800 | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. |
| 100 | 104857664 | 1218807 | 10 | 1447772160 | 1437188096 | 779401384 | 1075818496 | 1009034992 | 270217640 | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. |
| 500 | 524288072 | 6065749 | 10 | 6776107008 | 6824415232 | 3874290624 | 5070139392 | 4745728408 | 1072359704 | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. | 누수 의심, 원인 미확인 | 누수 의심, 원인 미확인 | 지속 증가 경고 없음. 누수 없음으로 결론내지 않는다. |
| 1024 | 1073741839 | 12379869 | 3 | 12016812032 | 12619100160 | 7918287848 | 8813813760 | 8051054432 | 543507800 | 판정 불가(표본 부족) | 판정 불가(표본 부족) | 판정 불가(표본 부족) | 판정 불가(표본 부족) |

세 번의 PR CI에서 경고가 같은 곳에 나오지 않았다. 3487b8d(실행 39208016760)와 d8880b4(실행 39214963904)는 100 MiB의 private bytes − GC 커밋에서만, 55ee84d(실행 39212578254)는 500 MiB의 GC 커밋과 private bytes에서만 경고했다. 같은 100 MiB 입력에서도 마지막 F5의 GC 커밋은 873697280~1075818496바이트로 달랐고, 관리 힙은 세 실행 모두 크기마다 거의 같았다. 그래서 이 경고들은 GC가 빈 영역을 붙잡고 돌려주는 시점에 따라 달라지는 것으로 보고, 누수 결론으로 쓰지 않는다. 원인은 확인하지 않았다. 실제 회사 로그는 최대 약 10 MB이고, 이번 작업에서 메모리 최적화는 하지 않는다.

이에 따라 판정 지표는 관리 힙·private bytes로 한정하고, GC 커밋과 private bytes − GC 커밋은 기록만 한다.

10 MiB와 100 MiB의 네 지표는 `지속 증가 경고 없음. 누수 없음으로 결론내지 않는다.`다. 500 MiB는 관리 힙과 private bytes에서 GC 커밋을 뺀 값이 경고 없음이고, GC 커밋과 private bytes는 `누수 의심, 원인 미확인`이다. GC 커밋의 기울기×9는 253800038.4바이트이고 마진은 84674805바이트다. private bytes의 기울기×9는 219774566.4바이트이고 마진은 171732828바이트다. 잡은 `500 MiB GC committed and private bytes: 누수 의심, 원인 미확인`으로 `::warning`을 냈고 실패하지 않았다. 이 경고는 누수가 있다는 결론이 아니다. 경고가 없는 지표에 대해서도 누수가 없다고 결론내지 않는다. 1024 MiB의 네 지표는 실행 39208016760에서 F5가 3개라 `판정 불가(표본 부족)`으로 남아 있다. 그 숫자는 그 실행의 head `3487b8dbbf286c79b29ac48508482788dec12d0c`에서 온 것이고, dispatch도 이 문서의 head도 아니다.
