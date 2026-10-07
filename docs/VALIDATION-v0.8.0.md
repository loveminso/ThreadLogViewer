# v0.8.0 동시 작업·반응형 화면·분석 저장 검증 — 2026-10-07

구현과 검증 완료. Release 빌드 경고 0/오류 0, 전체 자동 회귀 326/326 통과. 합성 100 MiB·다중 탭·1 MiB 한 줄·10만 검색 결과·실제 reader 취소를 측정했다. 비표시 WPF 레이아웃과 bitmap을 검사했으며 실제 GUI 입력·모니터 DPI 전환은 미실행이다.

## 범위

- F5 게시 직전 최신 검색·강조·북마크·스크롤·선택을 매핑하고, 매핑 대기 중 추가 변경을 재확인한다.
- 탭별 탐색 transaction/replay 상태, 설정 저장 실패 후 제한된 자동 재시도와 종료 시 재시도.
- 다중 파일 부분 실패·명시적 취소·미처리 요약, 원문 공유 소유권 변경으로 닫은 탭 보관을 정리한 결과 안내.
- 작업 영역/DPI에 맞는 창 크기, 설정 영역 스크롤과 목록/본문 공간 확보, 검색 오류 안내의 폭 제한, 입력 대상에 맞는 편집 메뉴.
- 긴 줄 구간 표시와 전체 표시 선택. 문서 원문·물리 줄·검색·복사·내보내기 매핑 유지.
- 줄별 필드 중복·임시 파싱 할당·행 목록 중복·전체 투영 offset 중복을 줄이고 디코딩/문서 생성 취소를 보강한다.
- 같은 창에서 큰 탭을 닫고 복원 보관을 비운 뒤 원문/투영/문서의 약한 참조와 GC 값을 검사한다. 창 참조 회수와 구분한다.
- 명시적 로컬 분석 상태와 필터·강조 프리셋. 기존 표시 설정 저장 형식은 유지한다. 원문 SHA-256 지문은 UTF-16 원문 바이트와 실제 개행을 기준으로 한다. 분석 파일은 앱 소유 marker·schema·kind·크기·범위를 검사한다.

## 저장·불러오기 정책

파일 → 분석 상태 저장/불러오기, 분석 → 필터·강조 프리셋 저장/불러오기를 제공한다. 새 파일만 생성하고 원본/기존 파일을 덮어쓰지 않는다. 파일 원문은 경로로 참조하며 같은 원본 종류·경로와 내용 지문인 열린 스냅샷만 재사용한다. 내용이 같은 다른 파일이나 파일/붙여넣기 종류를 서로 바꾸지 않는다. 붙여넣기는 원문을 포함하며 총 JSON 8 MiB 한도다. 원문이 달라지거나 파일이 손상·미지원이면 현재 분석을 유지한다. 자동 시작 복원이나 DB·온라인 저장은 없다.

붙여넣기 문자열을 작은 청크로 JSON 인코딩 크기와 UTF-16 유효성을 확인한 뒤 제한 stream에 직렬화한다. 큰 원문·escape 확장·전체 JSON 한도·중간 취소에서 destination과 임시 파일을 남기지 않는 회귀를 통과했다. 단독 surrogate는 치환된 원문을 저장하지 않고 명시적으로 거절한다. 기존 schema 1 화면 설정에 긴 줄 표시 bool만 추가하며 설정 저장 위치와 구조를 이동하지 않았다.

## 빌드·자동 회귀

프로젝트 내부 SDK와 기존 패키지 캐시만 사용했다. 외부 다운로드·설치 없이 `build tests/ThreadLogViewer.Tests/ThreadLogViewer.Tests.csproj -c Release --no-restore -m:1 -nr:false` 완료 후 `test --no-build --no-restore`를 실행했다. 테스트 호스트의 로컬 연결만 허용했다.

| 검증 | 결과 | 기록 |
|---|---|---|
| 최종 Release 빌드 | 경고 0/오류 0 | .NET 10, App/Core/Tests |
| 전체 자동 회귀 | 기존 271개 + 새 55개 = 326/326 통과, 건너뜀 0 | [final-r2-v0.8.0.trx](../TestResults/v0.8.0/final-r2-v0.8.0.trx) |
| 작은 창 레이아웃·편집 메뉴 | 반응형 4개, 정책 5개, 편집 2개와 기존 빈 결과 화면 4개 통과 | 위 TRX의 ResponsiveEditingTests/EmptyRecoveryLayoutTests |
| 상태 파일·프리셋·한도 | 원본 보호·새 파일만 생성·중간 취소·지문·범위·종류/경로·최신 입력 보존 통과 | AnalysisFilesTests/AnalysisFileLimitsTests |

새 회귀는 F5 읽기/매핑 대기 중 입력 변경, 취소·실패·다른 작업 차단, 탭별 탐색, 설정 저장 실패 후 종료 재시도, 일괄 열기 요약과 보관 정리, 긴 줄 원문/검색/선택/실제 marker handler와 surrogate 경계, 청크 디코딩과 문서 생성 취소를 포함한다. 기존 파싱·개행·원본 줄 매핑·필터·인코딩·원본 안전 내보내기도 통과했다.

첫 통합 실행은 324개 중 11개 실패했다. 분석 파일의 InvalidDataException 처리 누락과 일괄 실패의 이전 화면 안내를 수정했다. 합성 테스트의 params 배열 전달·MouseDown routed event·AvalonEdit 템플릿 준비도 고쳤다. 결과 높이는 실제 슬롯 예산과 본문 최소 공간을 검사하고 창을 넓히면 선호 높이로 복귀하는 검증으로 보강했다. 실패 기록은 보존하며 최종 통과 결과에 섞지 않는다.

## 합성 화면·실제 GUI

작업 영역의 물리 픽셀→DIP 계산, 작은 창에서 펼친 필터·북마크·검색 오류·넓힌 사이드바·본문 120 DIP 확보·스레드 목록 가상화·행 버튼·설정 스크롤·결과 높이 복귀를 자동 검사했다. [944×484 다크](../TestResults/v0.8.0/responsive-layout/responsive-944x484-dark.png), [1040×600 내용 0건 라이트](../TestResults/v0.8.0/empty-recovery-layout/empty-content-light-1040x600.png)를 직접 읽었다.

합성 1040×600 DIP 화면을 다크/라이트 × 96/120/144/192 DPI로 raster 생성했다. [다크 125%](../TestResults/v0.8.0/workbench-100MiB-final/workbench-small-dark-raster-125.png), [라이트 200%](../TestResults/v0.8.0/workbench-100MiB-final/workbench-small-light-raster-200.png)를 직접 확인했다. 본문 120 DIP/결과 177 DIP이며 작은 화면의 실제 예산 때문에 선호 180보다 제한한다. visual tree는 100%다. 실제 모니터 배율 전환·창 조작·포커스·팝업·클립보드·입력 장치 확인은 미실행이다.

## 측정 성능·메모리

새 프로세스에서 `--workbench-benchmark TestResults/v0.8.0/workbench-100MiB-final 100`을 실행했다. [최종 JSON](../TestResults/v0.8.0/workbench-100MiB-final/workbench-performance.json)의 Completed/Successful/ActualCancellationConfirmed/LiveTabResourcesCollected는 모두 true이며 14개 시나리오 Error는 null이다. 런타임 10.0.12, 한 프로세스의 단회 측정으로 OS 캐시 영향을 포함한다. 1 MiB smoke도 별도 새 경로에서 성공했다.

25ms worker의 Input 우선순위 dispatcher 대기이며 실제 입력 지연이나 FPS가 아니다. p95는 단회 작업 안의 callback 표본이다.

| 시나리오 | 총 시간 | dispatcher p95 / 최대 |
|---|---:|---:|
| 100 MiB, 1,603,941줄 열기+배치 | 4,059ms | 491 / 679ms |
| 두 스레드 필터+배치, debounce 100ms 포함 | 364ms | 37 / 37ms |
| 10 MiB 세 파일 개별 열기+배치 | 297 / 317 / 240ms | 88 / 82 / 83ms (각 최대) |
| 세 탭 전환+배치 합계 | 194ms | 61 / 61ms |
| 1 MiB 한 줄, 구간 표시·줄바꿈 꺼짐 | 42ms | 13 / 13ms |
| 같은 줄 구간 표시·줄바꿈 켜짐 | 18ms | 17 / 17ms |
| 긴 줄 마지막 target 검색·이동·배치, debounce 포함 | 226ms | 48 / 48ms |
| 첫 100,000개 검색·게시·배치, debounce 180ms 포함 | 262ms | 27 / 27ms |
| 검색 결과 목록 API 스크롤·마지막 결과 이동 | 17ms | 5 / 5ms |

[v0.7.0](VALIDATION-v0.7.0.md)의 긴 줄 전체 표시 1,926/2,212ms에서 기본 구간 표시 42/18ms로 줄었다. 표시 방식을 바꾼 비교이며 전체 원문 렌더 비용이 없어졌다는 뜻은 아니다. 원문·물리 줄·검색·선택·복사·내보내기는 유지하며 전체 표시를 선택하면 긴 줄 렌더 비용이 다시 발생한다. 실제 reader 진행 확인 후 취소 요청→handler 23.53ms, handler→완료 2.94ms, 합계 26.47ms였다. token 취소·이전 원문 유지·로드 실패 반환으로 취소를 확인했으며 모든 단계의 최악 지연을 뜻하지 않는다.

100 MiB 열기 직후 working set 1,262,006,272 bytes(약 1.18 GiB), managed heap 추정 902,990,936 bytes(약 861 MiB)다. 이전 1.28 GiB/905 MiB보다 감소했지만 여전히 큰 파일의 메모리와 초기 게시 679ms dispatcher 점유는 개선 여지가 있다. 구간 로딩이나 전체 앱 메모리 상한은 이번 범위에 추가하지 않았다.

같은 창에서 작은 탭 하나를 남기고 큰 탭을 닫은 뒤 복원 보관을 명시적으로 비웠다. idle+강제 GC 후 원문·투영·문서 약한 참조가 모두 회수됐다. managed 추정은 약 995 MiB에서 4.75 MiB로 줄었으나 working set은 약 1.12 GiB로 남았다. 런타임이 예약한 메모리와 살아 있는 로그 객체를 구분한다. 전체 창 종료 후 별도 5개 시나리오의 창 약한 참조도 모두 회수됐다. 이는 합성 수명 검사의 통과이며 모든 실제 사용에서 누수가 없다는 증명은 아니다. 전체 sampled peak는 약 1.22 GiB이고 이전 측정과 달리 중간에 수명 검사/강제 GC가 추가돼 전체 peak 감소를 최적화 효과로 직접 비교하지 않는다.

## 독립 검토·로컬 배포

reviewer가 통합 소스를 읽고 발견한 긴 줄 이전 marker의 surrogate 경계, 저장 한도 이전 전체 JSON 할당, 스레드 선택의 제곱 비교, 동일 내용의 다른 원본 경로 재사용을 수정했다. 재검토에서 추가 확정 차단 문제는 확인하지 못했다. 최종 실행 결과 독립 확인과 배포 대조는 아래 기록을 따른다.

새 self-contained 배포 경로는 `artifacts/ThreadLogViewer-v0.8.0-win-x64/ThreadLogViewer.exe`다. publish 성공, EXE/앱 DLL FileVersion 0.8.0.0과 .NET/WPF 10.0.12를 확인한다. ZIP과 폴더 내용의 최종 대조 결과는 `artifacts/verification-v0.8.0.json`에 기록한다. 이전 실행본/ZIP·원본·사용자 설정을 보존하며 실행본을 직접 실행하거나 업로드/push하지 않는다. 개발 도구·캐시·실제 로그·사용자 설정·TestResults는 패키지와 Git 커밋에서 제외한다.
