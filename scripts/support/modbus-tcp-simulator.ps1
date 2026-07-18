# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Opens one loopback TCP listener for acceptance verification
#   Writes:
#     - Ready JSON at the caller-provided path
#   Cleanup:
#     - Closes accepted sockets and the listener when the stop marker appears or the process exits
#   Requires:
#     - PowerShell 7
#   Assumption:
#     - Serves one Modbus TCP client at a time because collection and probing share one serialized adapter connection

[CmdletBinding()]
param(
    [ValidateRange(0, 65535)] [int] $Port = 0,
    [Parameter(Mandatory)] [string] $ReadyPath,
    [Parameter(Mandatory)] [string] $StopPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$client = $null
$stream = $null
$script:StopRequested = $false

function Read-Exactly([System.IO.Stream] $InputStream, [byte[]] $Buffer) {
    $offset = 0
    while ($offset -lt $Buffer.Length) {
        $read = $InputStream.Read($Buffer, $offset, $Buffer.Length - $offset)
        if ($read -eq 0) { return $false }
        $offset += $read
    }
    return $true
}

function Write-ModbusResponse([System.IO.Stream] $OutputStream, [byte[]] $Request) {
    $functionCode = $Request[7]
    if ($functionCode -notin @(3, 4)) {
        $exception = [byte[]] @(
            $Request[0], $Request[1], $Request[2], $Request[3],
            0, 3, $Request[6], ([byte] ($functionCode -bor 0x80)), 1)
        $OutputStream.Write($exception, 0, $exception.Length)
        $OutputStream.Flush()
        return
    }
    $registerCount = ([int] $Request[10] -shl 8) -bor [int] $Request[11]
    $byteCount = $registerCount * 2
    $response = [byte[]]::new(9 + $byteCount)
    [Array]::Copy($Request, 0, $response, 0, 4)
    $response[4] = [byte] ((3 + $byteCount) -shr 8)
    $response[5] = [byte] ((3 + $byteCount) -band 0xff)
    $response[6] = $Request[6]
    $response[7] = $functionCode
    $response[8] = $byteCount
    $startAddress = ([int] $Request[8] -shl 8) -bor [int] $Request[9]
    if ($startAddress -eq 1 -and $registerCount -eq 2) {
        $response[9] = 0x7f
        $response[10] = 0xc0
        $response[11] = 0x00
        $response[12] = 0x00
        $OutputStream.Write($response, 0, $response.Length)
        $OutputStream.Flush()
        return
    }
    for ($index = 0; $index -lt $registerCount; $index++) {
        $value = 100 + $startAddress + $index
        $response[9 + ($index * 2)] = [byte] ($value -shr 8)
        $response[10 + ($index * 2)] = [byte] ($value -band 0xff)
    }
    $OutputStream.Write($response, 0, $response.Length)
    $OutputStream.Flush()
}

try {
    Remove-Item -LiteralPath $ReadyPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $StopPath -Force -ErrorAction SilentlyContinue
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent ([System.IO.Path]::GetFullPath($ReadyPath)))) | Out-Null
    $listener.Start()
    $selectedPort = ([System.Net.IPEndPoint] $listener.LocalEndpoint).Port
    $ready = [ordered]@{
        state = 'ready'
        address = '127.0.0.1'
        port = $selectedPort
        processId = $PID
        readyAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $ready | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReadyPath -Encoding utf8

    while (-not $script:StopRequested) {
        if (Test-Path -LiteralPath $StopPath -PathType Leaf) {
            $script:StopRequested = $true
            break
        }
        if ($null -eq $client) {
            if (-not $listener.Pending()) {
                Start-Sleep -Milliseconds 20
                continue
            }
            $client = $listener.AcceptTcpClient()
            $client.NoDelay = $true
            $stream = $client.GetStream()
            $stream.ReadTimeout = 250
            continue
        }
        if (-not $stream.DataAvailable) {
            Start-Sleep -Milliseconds 20
            continue
        }
        $request = [byte[]]::new(12)
        if (-not (Read-Exactly -InputStream $stream -Buffer $request)) {
            $stream.Dispose()
            $client.Dispose()
            $stream = $null
            $client = $null
            continue
        }
        Write-ModbusResponse -OutputStream $stream -Request $request
    }
}
finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $client) { $client.Dispose() }
    $listener.Stop()
    Remove-Item -LiteralPath $ReadyPath -Force -ErrorAction SilentlyContinue
}
