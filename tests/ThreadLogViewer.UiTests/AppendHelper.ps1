param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$ReadyName,
    [Parameter(Mandatory = $true)][string]$StopName,
    [Parameter(Mandatory = $true)][string]$PauseName,
    [Parameter(Mandatory = $true)][string]$IdleName,
    [Parameter(Mandatory = $true)][string]$PrefixB64,
    [Parameter(Mandatory = $true)][string]$SuffixB64,
    [switch]$StartPaused
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell creates the events. The .NET test process opens them.
# Deleting a pause file while Test-Path runs throws "Access is denied" and,
# with ErrorAction Stop, kills this helper. Named events do not have that race.
function New-Signal([string]$name, [bool]$state) {
    return [System.Threading.EventWaitHandle]::new(
        $state,
        [System.Threading.EventResetMode]::ManualReset,
        $name)
}
$prefix = [Convert]::FromBase64String($PrefixB64)
$suffix = [Convert]::FromBase64String($SuffixB64)
$ascii = [Text.Encoding]::ASCII
$pause = $null
$idle = $null
$stop = $null
$ready = $null
$stream = $null
$pause = New-Signal $PauseName ([bool]$StartPaused)
$idle = New-Signal $IdleName $false
$stop = New-Signal $StopName $false
$ready = New-Signal $ReadyName $false
$stream = New-Object System.IO.FileStream(
    $Path,
    [System.IO.FileMode]::Append,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::ReadWrite,
    1,
    [System.IO.FileOptions]::WriteThrough)
try {
    [void]$ready.Set()
    $i = 1
    while (-not $stop.WaitOne(0)) {
        if ($pause.WaitOne(0)) {
            [void]$idle.Set()
            Start-Sleep -Milliseconds 30
            continue
        }
        [void]$idle.Reset()
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
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $pause) { $pause.Dispose() }
    if ($null -ne $idle) { $idle.Dispose() }
    if ($null -ne $stop) { $stop.Dispose() }
    if ($null -ne $ready) { $ready.Dispose() }
}
