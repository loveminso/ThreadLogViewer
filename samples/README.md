# 합성 샘플

모두 직접 만든 가상 SSD 로그이며 실제 회사 데이터는 포함하지 않습니다.

- `mixed-utf8.log`: 기본 샘플. 3개 스레드, 한글, 빈 줄, 미분류 덤프, 잘못된 시각/형식, 소스 또는 시각 누락, 동일 시각, 자정 통과, 긴 줄.
- `mixed-utf8-bom.log`, `mixed-utf16le.log`, `mixed-utf16be.log`: 같은 내용, BOM 인코딩 확인용.
- `mixed-cp949.log`: 열기 후 ‘CP949로 다시 읽기’로 해석.
- `empty.log`: 0바이트 파일.
- `line-mapping.log`: T 3만 선택하면 원본 줄 번호 **100, 105**, 소스 줄 번호는 **900**.

재생성: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-samples.ps1`.
성능용 10/100 MiB 로그는 테스트 프로젝트의 `--benchmark`로 `local-data/` 안에 생성하며 Git에 포함하지 않습니다.
