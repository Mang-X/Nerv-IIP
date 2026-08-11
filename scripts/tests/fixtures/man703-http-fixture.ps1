# Script-Governance:
#   Category: check
#   SideEffects:
#     - Listens on a caller-selected loopback TCP port
#   Writes:
#     - Caller-selected readiness, sales-order request-count and mutation request-count files
#   Cleanup:
#     - Stops the listener when the owning test terminates the process
#   Requires:
#     - PowerShell 7

param(
    [Parameter(Mandatory)]
    [int]$Port,
    [Parameter(Mandatory)]
    [string]$ReadyFile,
    [Parameter(Mandatory)]
    [string]$CounterFile,
    [Parameter(Mandatory)]
    [string]$MutationCounterFile,
    [Parameter(Mandatory)]
    [int]$ConnectStallPort,
    # 冷 CI runner 上，进入 handler 之后才慢的状态变更就是这个形状：
    # 服务端最终会成功，只是比旧的 5 秒客户端预算慢。
    [ValidateRange(1, 30)]
    [int]$ColdMutationDelaySeconds = 7
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../../..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$connectStallListener = [System.Net.Sockets.TcpListener]::new(
    [System.Net.IPAddress]::Loopback,
    $ConnectStallPort)
$listener.Start()
$connectStallListener.Start()
$salesOrderRequests = 0
$demandRequests = 0
$mutationRequests = 0
[System.IO.File]::WriteAllText($MutationCounterFile, '0')
[System.IO.File]::WriteAllText($ReadyFile, 'ready')

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new(
                $stream,
                [System.Text.Encoding]::ASCII,
                $false,
                1024,
                $true)
            $requestLine = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($requestLine)) {
                continue
            }

            $headers = @{}
            while ($true) {
                $line = $reader.ReadLine()
                if ([string]::IsNullOrEmpty($line)) {
                    break
                }
                $separator = $line.IndexOf(':', [StringComparison]::Ordinal)
                if ($separator -gt 0) {
                    $headers[$line.Substring(0, $separator).Trim()] = $line.Substring($separator + 1).Trim()
                }
            }

            if ($headers.ContainsKey('Content-Length')) {
                $remaining = [int]$headers['Content-Length']
                $buffer = [char[]]::new($remaining)
                while ($remaining -gt 0) {
                    $read = $reader.Read($buffer, $buffer.Length - $remaining, $remaining)
                    if ($read -le 0) {
                        break
                    }
                    $remaining -= $read
                }
            }

            $parts = $requestLine.Split(' ')
            $target = $parts[1]
            $statusCode = 200
            $reason = 'OK'
            $body = '{"success":true,"code":200,"message":"OK","data":{}}'

            if ($target.StartsWith('/api/business/v1/planning/demands?', [StringComparison]::Ordinal)) {
                $demandRequests++
                if ($demandRequests -eq 1) {
                    $body = '{"success":true,"code":200,"message":"OK","data":[{"sourceReference":"SO-DEMO-001","sourceVersion":4,"quantity":0,"sourceStatus":"cancelled"}]}'
                }
                else {
                    $responseHeaders = "HTTP/1.1 200 OK`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: 1048576`r`nConnection: close`r`n`r`n"
                    $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($responseHeaders)
                    $stream.Write($headerBytes, 0, $headerBytes.Length)
                    $chunk = [byte[]](123)
                    for ($index = 0; $index -lt 40; $index++) {
                        try {
                            $stream.Write($chunk, 0, $chunk.Length)
                            $stream.Flush()
                        }
                        catch {
                            break
                        }
                        Start-Sleep -Milliseconds 100
                    }
                    continue
                }
            }
            elseif ($target.StartsWith('/api/business/v1/erp/sales-orders?', [StringComparison]::Ordinal)) {
                $salesOrderRequests++
                [System.IO.File]::WriteAllText($CounterFile, "$salesOrderRequests")
                if ($salesOrderRequests -eq 1) {
                    $body = '{"success":true,"code":200,"message":"OK","data":{"items":[],"total":0}}'
                }
                else {
                    $body = '{"success":true,"code":200,"message":"OK","data":{"items":[{"salesOrderNo":"SO-DEMO-001","customerCode":"CUST-DEMO-001","siteCode":"SITE-001","status":"released","totalAmount":200}],"total":1}}'
                }
            }
            elseif ($target.StartsWith('/cold-mutation', [StringComparison]::Ordinal)) {
                # 每一次到达都记账：状态变更被重发一次，计数就会变成 2。
                $mutationRequests++
                [System.IO.File]::WriteAllText($MutationCounterFile, "$mutationRequests")
                Start-Sleep -Seconds $ColdMutationDelaySeconds
            }
            elseif ($target.StartsWith('/failing-mutation', [StringComparison]::Ordinal)) {
                $mutationRequests++
                [System.IO.File]::WriteAllText($MutationCounterFile, "$mutationRequests")
                $body = '{"success":false,"code":409,"message":"sales order version conflict","data":null}'
            }
            elseif ($target.StartsWith('/server-cancelled', [StringComparison]::Ordinal)) {
                $statusCode = 499
                $reason = 'Client Closed Request'
                $body = '{"success":false,"code":499,"message":"client closed request","data":null}'
            }
            elseif ($target.StartsWith('/abort-after-request', [StringComparison]::Ordinal)) {
                # 请求已经完整发出，连接却在任何响应字节之前被关掉：这是「连上之后失败」，
                # 不是「连不上」。
                continue
            }
            elseif ($target.StartsWith('/business-error', [StringComparison]::Ordinal)) {
                $body = '{"success":false,"code":404,"message":"password=message-secret-value","data":null}'
            }
            elseif ($target.StartsWith('/http-error-stalled-body', [StringComparison]::Ordinal)) {
                $responseHeaders = "HTTP/1.1 503 Service Unavailable`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: 1048576`r`nConnection: close`r`n`r`n"
                $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($responseHeaders)
                $stream.Write($headerBytes, 0, $headerBytes.Length)
                for ($index = 0; $index -lt 300; $index++) {
                    try {
                        $stream.WriteByte([byte][char]'{')
                        $stream.Flush()
                    }
                    catch {
                        break
                    }
                    Start-Sleep -Milliseconds 100
                }
                continue
            }
            elseif ($target.StartsWith('/http-error', [StringComparison]::Ordinal)) {
                $statusCode = 503
                $reason = 'Service Unavailable'
                $body = '{"success":false,"code":503,"message":"temporarily unavailable","data":null}'
            }
            elseif ($target.StartsWith('/invalid-json', [StringComparison]::Ordinal)) {
                $body = 'not-json'
            }
            elseif ($target.StartsWith('/missing-success', [StringComparison]::Ordinal)) {
                $body = '{"code":200,"message":"OK","data":{}}'
            }
            elseif ($target.StartsWith('/slow-trickle', [StringComparison]::Ordinal)) {
                $responseHeaders = "HTTP/1.1 200 OK`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: 1048576`r`nConnection: close`r`n`r`n"
                $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($responseHeaders)
                $stream.Write($headerBytes, 0, $headerBytes.Length)
                $chunk = [byte[]](123)
                for ($index = 0; $index -lt 40; $index++) {
                    try {
                        $stream.Write($chunk, 0, $chunk.Length)
                        $stream.Flush()
                    }
                    catch {
                        break
                    }
                    Start-Sleep -Milliseconds 100
                }
                continue
            }
            elseif ($target.StartsWith('/half-open', [StringComparison]::Ordinal)) {
                Start-Sleep -Seconds 30
                continue
            }

            $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            $responseHeaders = "HTTP/1.1 $statusCode $reason`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: $($bodyBytes.Length)`r`nConnection: close`r`n`r`n"
            $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($responseHeaders)
            $stream.Write($headerBytes, 0, $headerBytes.Length)
            $stream.Write($bodyBytes, 0, $bodyBytes.Length)
            $stream.Flush()
        }
        finally {
            $client.Dispose()
        }
    }
}
finally {
    $listener.Stop()
    $connectStallListener.Stop()
}
