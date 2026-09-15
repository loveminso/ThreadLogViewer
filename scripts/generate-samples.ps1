$ErrorActionPreference = 'Stop'
$sampleRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../samples'))
[IO.Directory]::CreateDirectory($sampleRoot) | Out-Null
$text = @'
[23:59:58] [T 3] 합성 SSD 테스트 시작 (testcase.cpp:103)
[23:59:58] [T7] Read submitted (testcase.cpp:208)
[23:59:59.125] [T 3] Write completed (testcase.cpp:117)
[23:59:59.125] [T 12] Flush submitted (flush.cpp:42)

DE AD BE EF 00 01 02 03 (스레드 정보 없는 합성 덤프)
malformed synthetic line / 형식 오류
[00:00:00] [T7] Read timeout (testcase.cpp:229)
[00:00:00] [T 3] 자정 이후 기록 (testcase.cpp:130)
[00:00:00] [T 12] 같은 시각의 다른 스레드 (flush.cpp:49)
[00:00:01] [T 3] 소스 위치 없는 합성 로그
[T7] 시각이 없는 로그 (testcase.cpp:235)
[00:00:02] 스레드 없는 공통 메시지 (runner.cpp:80)
[99:99:99] [T 12] 잘못된 시각도 보존 (flush.cpp:51)
[00:00:03.1234567] [T3] 선택적인 소수초 (testcase.cpp:150)
[00:00:04] [T7] Very long synthetic payload for horizontal scroll and word wrap: 000102030405060708090A0B0C0D0E0F 101112131415161718191A1B1C1D1E1F 202122232425262728292A2B2C2D2E2F 303132333435363738393A3B3C3D3E3F 404142434445464748494A4B4C4D4E4F (testcase.cpp:240)
last unclassified line without final newline
'@
$text = $text.Replace("`r`n", "`n")
[IO.File]::WriteAllText((Join-Path $sampleRoot 'mixed-utf8.log'), $text, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $sampleRoot 'mixed-utf8-bom.log'), $text, (New-Object Text.UTF8Encoding($true)))
[IO.File]::WriteAllText((Join-Path $sampleRoot 'mixed-utf16le.log'), $text, (New-Object Text.UnicodeEncoding($false, $true)))
[IO.File]::WriteAllText((Join-Path $sampleRoot 'mixed-utf16be.log'), $text, (New-Object Text.UnicodeEncoding($true, $true)))
[IO.File]::WriteAllText((Join-Path $sampleRoot 'mixed-cp949.log'), $text, [Text.Encoding]::GetEncoding(949))
[IO.File]::WriteAllBytes((Join-Path $sampleRoot 'empty.log'), [byte[]]@())
[IO.File]::WriteAllText((Join-Path $sampleRoot 'line-mapping.log'), ((1..110 | ForEach-Object { '[10:00:00] [T' + $(if ($_ -eq 100 -or $_ -eq 105) { '3' } else { '7' }) + '] synthetic original row ' + $_ + ' (testcase.cpp:900)' }) -join "`n"), (New-Object Text.UTF8Encoding($false)))
Write-Host 'Generated synthetic samples.'
