param(
    [Parameter(Mandatory = $true)][string]$TestDll,
    [Parameter(Mandatory = $true)][string]$JsonDirectory,
    [string]$WorkDirectory = '',
    [switch]$IncludeOneGigabyte,
    [switch]$OneGigabyteOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
if ($IncludeOneGigabyte -and $OneGigabyteOnly) { throw 'Pass only one of -IncludeOneGigabyte and -OneGigabyteOnly.' }
$TestDll = [IO.Path]::GetFullPath($TestDll)
if (-not (Test-Path -LiteralPath $TestDll -PathType Leaf)) { throw "Test dll was not found: $TestDll" }
$JsonDirectory = [IO.Path]::GetFullPath($JsonDirectory)
New-Item -ItemType Directory -Force -Path $JsonDirectory | Out-Null
if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $WorkDirectory = Join-Path $tempRoot 'tlv-memory'
}
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null

$cases = New-Object System.Collections.Generic.List[object]
if ($OneGigabyteOnly) {
    $cases.Add([pscustomobject]@{ MiB = 1024; Refreshes = 3 })
} else {
    $cases.Add([pscustomobject]@{ MiB = 10; Refreshes = 10 })
    $cases.Add([pscustomobject]@{ MiB = 100; Refreshes = 10 })
    $cases.Add([pscustomobject]@{ MiB = 500; Refreshes = 10 })
    if ($IncludeOneGigabyte) { $cases.Add([pscustomobject]@{ MiB = 1024; Refreshes = 3 }) }
}

$watch = [Diagnostics.Stopwatch]::StartNew()
$failures = New-Object System.Collections.Generic.List[object]
$raws = New-Object System.Collections.Generic.List[string]
function Format-Byte($value) {
    if ($null -eq $value) { return 'n/a' }
    return ([int64]$value).ToString([Globalization.CultureInfo]::InvariantCulture)
}
function Remove-Tree([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -eq 5) { Write-Host "::warning::Synthetic log directory was not deleted after 5 attempts." }
            else { Start-Sleep -Seconds 2 }
        }
    }
}
try {
    foreach ($case in $cases) {
        $mib = [int]$case.MiB
        $refreshes = [int]$case.Refreshes
        $caseDir = Join-Path $WorkDirectory ("mib-" + $mib)
        New-Item -ItemType Directory -Force -Path $caseDir | Out-Null
        Write-Host "memory benchmark $mib MiB, F5 x $refreshes"
        & dotnet $TestDll --benchmark $caseDir $mib --memory-refreshes $refreshes
        $exit = $LASTEXITCODE
        $jsonName = "memory-${mib}MiB.json"
        $jsonPath = Join-Path $caseDir $jsonName
        $errorPath = Join-Path $caseDir "memory-${mib}MiB.error.txt"
        if (Test-Path -LiteralPath $jsonPath) {
            Copy-Item -LiteralPath $jsonPath -Destination (Join-Path $JsonDirectory $jsonName) -Force
            $raws.Add((Get-Content -LiteralPath $jsonPath -Raw -Encoding utf8))
        }
        if (Test-Path -LiteralPath $errorPath) {
            Copy-Item -LiteralPath $errorPath -Destination (Join-Path $JsonDirectory "memory-${mib}MiB.error.txt") -Force
        }
        Remove-Tree $caseDir
        $copied = Test-Path -LiteralPath (Join-Path $JsonDirectory $jsonName)
        if ($exit -ne 0 -or -not $copied) {
            $failures.Add([pscustomobject]@{ MiB = $mib; ExitCode = $exit; Json = [bool]$copied })
            Write-Host "memory benchmark $mib MiB failed with exit $exit"
        }
    }
} finally {
    $watch.Stop()
    $elapsed = [int64]$watch.ElapsedMilliseconds
    $sha = [string]$env:GITHUB_SHA
    $runId = [string]$env:GITHUB_RUN_ID
    $sizeLines = New-Object System.Collections.Generic.List[string]
    foreach ($raw in $raws) {
        $trimmed = $raw.Trim()
        if ($sizeLines.Count -gt 0) { $sizeLines[$sizeLines.Count - 1] = $sizeLines[$sizeLines.Count - 1] + ',' }
        $sizeLines.Add($trimmed)
    }
    $failureLines = New-Object System.Collections.Generic.List[string]
    foreach ($failure in $failures) {
        if ($failureLines.Count -gt 0) { $failureLines[$failureLines.Count - 1] = $failureLines[$failureLines.Count - 1] + ',' }
        $failureLines.Add(('    {{ "MiB": {0}, "ExitCode": {1}, "Json": {2} }}' -f $failure.MiB, $failure.ExitCode, $(if ($failure.Json) { 'true' } else { 'false' })))
    }
    $combined = New-Object System.Text.StringBuilder
    [void]$combined.AppendLine('{')
    [void]$combined.AppendLine('  "Schema": 1,')
    [void]$combined.AppendLine(('  "GithubSha": "{0}",' -f $sha))
    [void]$combined.AppendLine(('  "GithubRunId": "{0}",' -f $runId))
    [void]$combined.AppendLine(('  "StepElapsedMs": {0},' -f $elapsed))
    [void]$combined.AppendLine('  "Sizes": [')
    foreach ($line in $sizeLines) { [void]$combined.AppendLine($line) }
    [void]$combined.AppendLine('  ],')
    [void]$combined.AppendLine('  "Failures": [')
    foreach ($line in $failureLines) { [void]$combined.AppendLine($line) }
    [void]$combined.AppendLine('  ]')
    [void]$combined.AppendLine('}')
    $combinedPath = Join-Path $JsonDirectory 'memory-results.json'
    [IO.File]::WriteAllText($combinedPath, $combined.ToString())

    $lines = New-Object System.Collections.Generic.List[string]
    $compact = New-Object System.Collections.Generic.List[string]
    $notes = New-Object System.Collections.Generic.List[string]
    $detail = New-Object System.Collections.Generic.List[string]
    $lines.Add('')
    $lines.Add('## Memory')
    $lines.Add('')
    $lines.Add('Console process `ThreadLogViewer.Tests`. Not FlaUI and not the published exe. Each size is a new process. Synthetic files were under the runner temp directory and deleted after the size finished.')
    $lines.Add('Included: LogFileReader, LogData, LogProjection, SourceLineRemap on F5, CancellableDocumentFactory (AvalonEdit document copy), LogSearch + HighlightIndex. Excluded: WPF layout and render.')
    $lines.Add('A growth warning does not fail this job and does not mean a leak was confirmed.')
    $lines.Add('')
    $lines.Add("- Step elapsed: $elapsed ms")
    $lines.Add('')
    $compact.Add('| MiB | File bytes | Refreshes | After open working set | After open peak | After open private | After open managed | Last F5 working set | Last F5 peak | Last F5 private | Last F5 managed | F5 managed delta | F5 working set delta | Warning |')
    $compact.Add('| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |')
    $detail.Add('| MiB | Phase | Refresh | Working set | Peak working set | Private bytes | Managed heap after GC | Elapsed ms |')
    $detail.Add('| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($raw in $raws) {
        $report = $raw | ConvertFrom-Json
        $samples = @($report.Samples)
        $open = @($samples | Where-Object { $_.Phase -eq 'afterOpen' }) | Select-Object -First 1
        $refreshSamples = @($samples | Where-Object { $_.Phase -eq 'afterRefresh' })
        $first = $refreshSamples | Select-Object -First 1
        $last = $refreshSamples | Select-Object -Last 1
        $heapDelta = if ($null -eq $first -or $null -eq $last) { 'n/a' } else { Format-Byte ([int64]$last.ManagedHeapBytes - [int64]$first.ManagedHeapBytes) }
        $wsDelta = if ($null -eq $first -or $null -eq $last) { 'n/a' } else { Format-Byte ([int64]$last.WorkingSetBytes - [int64]$first.WorkingSetBytes) }
        $warningCell = if ($report.Warning) { [string]$report.WarningText } else { 'no' }
        $lastWs = if ($null -eq $last) { 'n/a' } else { Format-Byte $last.WorkingSetBytes }
        $lastPeak = if ($null -eq $last) { 'n/a' } else { Format-Byte $last.PeakWorkingSetBytes }
        $lastPrivate = if ($null -eq $last) { 'n/a' } else { Format-Byte $last.PrivateBytes }
        $lastHeap = if ($null -eq $last) { 'n/a' } else { Format-Byte $last.ManagedHeapBytes }
        $compact.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |' -f `
            $report.RequestedMiB, (Format-Byte $report.FileBytes), $report.RefreshCount, `
            (Format-Byte $open.WorkingSetBytes), (Format-Byte $open.PeakWorkingSetBytes), (Format-Byte $open.PrivateBytes), (Format-Byte $open.ManagedHeapBytes), `
            $lastWs, $lastPeak, $lastPrivate, $lastHeap, $heapDelta, $wsDelta, $warningCell))
        foreach ($sample in $samples) {
            $refreshCell = if ($null -eq $sample.Refresh) { '' } else { [string]$sample.Refresh }
            $detail.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f `
                $report.RequestedMiB, $sample.Phase, $refreshCell, `
                (Format-Byte $sample.WorkingSetBytes), (Format-Byte $sample.PeakWorkingSetBytes), (Format-Byte $sample.PrivateBytes), `
                (Format-Byte $sample.ManagedHeapBytes), (Format-Byte $sample.ElapsedMs)))
        }
        $notes.Add("- $($report.RequestedMiB) MiB lines $($report.Lines), filtered lines $($report.FilteredLines), highlight spans $($report.HighlightSpans), limited $($report.HighlightLimited), elapsed $($report.ElapsedMs) ms, generate $($report.GenerateMs) ms")
        $notes.Add("- $($report.RequestedMiB) MiB verdict: $($report.Verdict)")
        $notes.Add("- $($report.RequestedMiB) MiB margins: managed $($report.ManagedHeapMarginBytes) bytes ($($report.ManagedHeapMarginRule)); working set $($report.WorkingSetMarginBytes) bytes ($($report.WorkingSetMarginRule))")
        if ($report.Warning) {
            $text = ([string]$report.WarningText) -replace "`r|`n", ' '
            Write-Host "::warning title=Memory growth::$text"
        }
    }
    foreach ($failure in $failures) {
        $notes.Add("- $($failure.MiB) MiB process failed with exit $($failure.ExitCode).")
    }
    foreach ($row in $compact) { $lines.Add([string]$row) }
    $lines.Add('')
    foreach ($row in $notes) { $lines.Add([string]$row) }
    $lines.Add('')
    foreach ($row in $detail) { $lines.Add([string]$row) }
    $markdown = [string]::Join("`n", $lines.ToArray())
    [IO.File]::WriteAllText((Join-Path $JsonDirectory 'memory-summary.md'), $markdown + "`n")
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $markdown -Encoding utf8NoBOM
    }
    Remove-Tree $WorkDirectory
}
if ($failures.Count -gt 0) {
    throw ("Memory measurement failed for: " + (($failures | ForEach-Object { $_.MiB }) -join ', '))
}
