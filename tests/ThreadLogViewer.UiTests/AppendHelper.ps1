param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$ReadyFile,
    [Parameter(Mandatory = $true)][string]$StopFile,
    [Parameter(Mandatory = $true)][string]$PauseFile,
    [Parameter(Mandatory = $true)][string]$IdleFile,
    [Parameter(Mandatory = $true)][string]$PrefixB64,
    [Parameter(Mandatory = $true)][string]$SuffixB64
)
$ErrorActionPreference = 'Stop'
$prefix = [Convert]::FromBase64String($PrefixB64)
$suffix = [Convert]::FromBase64String($SuffixB64)
$ascii = [Text.Encoding]::ASCII
$stream = New-Object System.IO.FileStream(
    $Path,
    [System.IO.FileMode]::Append,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::ReadWrite,
    1,
    [System.IO.FileOptions]::WriteThrough)
try {
    [IO.File]::WriteAllText($ReadyFile, 'ready')
    $i = 1
    while (-not (Test-Path -LiteralPath $StopFile)) {
        if (Test-Path -LiteralPath $PauseFile) {
            if (-not (Test-Path -LiteralPath $IdleFile)) {
                Set-Content -LiteralPath $IdleFile -Value 'idle' -Encoding ascii
            }
            Start-Sleep -Milliseconds 30
            continue
        }
        if (Test-Path -LiteralPath $IdleFile) {
            Remove-Item -LiteralPath $IdleFile -Force
        }
        $number = $ascii.GetBytes(('{0:D4}' -f $i))
        $stream.Write($prefix, 0, $prefix.Length)
        $stream.Write($number, 0, $number.Length)
        $stream.Write($suffix, 0, $suffix.Length)
        $stream.Flush($true)
        $i++
        Start-Sleep -Milliseconds 80
    }
}
finally {
    $stream.Dispose()
}
