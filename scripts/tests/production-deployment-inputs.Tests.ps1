# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes release-install validation and captures the environment at an isolated Aspire command boundary
#     - Validates deployment input contracts as repository text
#   Writes:
#     - One owned temporary fixture root
#   Cleanup:
#     - Removes the fixture in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$startScript = Join-Path $repoRoot 'scripts/install/start-nerv-iip-apphost.ps1'
$appHostEnvironment = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/AppHostEnvironment.cs') -Raw
$appHostServices = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/AppHostServices.cs') -Raw
$dependenciesText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/compose/nerv-iip.dependencies.yml') -Raw
$platformText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/compose/nerv-iip.platform.yml') -Raw
$environmentExampleText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/compose/nerv-iip.production.env.example') -Raw

function Assert-ContainsOrdinal {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw $Message
    }
}

function Assert-StartFails {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $ExpectedMarker
    )

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $startScript @Arguments 2>&1
    $renderedOutput = $output | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $renderedOutput.Contains($ExpectedMarker, [StringComparison]::Ordinal)) {
        throw "Expected release-install validation to fail with marker '$ExpectedMarker'. Output: $renderedOutput"
    }
}

Assert-StartFails -Arguments @('-EnvironmentName', 'Production', '-MessagingProvider', 'InMemory') `
    -ExpectedMarker '-MessagingProvider InMemory'
Assert-StartFails -Arguments @('-EnvironmentName', 'Production', '-MessagingProvider', 'Redis') `
    -ExpectedMarker '-RedisPassword is required'
Assert-StartFails -Arguments @(
    '-EnvironmentName', 'Production',
    '-MessagingProvider', 'RabbitMQ',
    '-IamJwtSigningKeyId', 'kid',
    '-IamJwtPrivateKeyPem', 'private-key',
    '-IamJwtJwksJson', '{"keys":[]}',
    '-IamSecretsPepper', 'pepper',
    '-IamEnterpriseIdentityMfaCode', '654321'
) -ExpectedMarker '-RedisPassword is required'
Assert-StartFails -Arguments @(
    '-EnvironmentName', 'Production',
    '-MessagingProvider', 'RabbitMQ',
    '-RedisPassword', 'redis-password',
    '-IamJwtSigningKeyId', 'kid',
    '-IamJwtPrivateKeyPem', 'private-key',
    '-IamJwtJwksJson', '{"keys":[]}',
    '-IamSecretsPepper', 'pepper',
    '-IamEnterpriseIdentityMfaCode', '000000'
) -ExpectedMarker '-IamEnterpriseIdentityMfaCode must override'
$productionArguments = @(
    '-EnvironmentName', 'Production',
    '-MessagingProvider', 'Redis',
    '-RedisPassword', 'redis-password',
    '-IamJwtSigningKeyId', 'kid',
    '-IamJwtPrivateKeyPem', 'private-key',
    '-IamJwtJwksJson', '{"keys":[]}',
    '-IamSecretsPepper', 'pepper',
    '-IamEnterpriseIdentityMfaCode', '654321',
    '-InternalServiceBearerToken', 'internal-token',
    '-ConnectorHostSecret', 'connector-secret',
    '-ConnectorHostId', 'connector-host',
    '-ConnectorHostOrganizationId', 'organization',
    '-ConnectorHostEnvironmentId', 'environment',
    '-ConnectorIngestionTokenSigningKey', 'ingestion-key',
    '-MinioRootUser', 'minio-user',
    '-MinioRootPassword', 'minio-password',
    '-CorsAllowedOrigins', 'https://console.example.test'
)
Assert-StartFails -Arguments $productionArguments -ExpectedMarker '-BarcodeLabelPrinterId is required'

# 在 Aspire 边界读取子进程可见的环境；使用真实的作用域设置和不启动服务的替身。
$fixture = Join-Path ([IO.Path]::GetTempPath()) "nerv-release-inputs-$([guid]::NewGuid().ToString('N'))"
try {
    $fixtureInstall = [IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts/install'))
    $fixtureLibrary = [IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts/lib'))
    $fixtureStart = Join-Path $fixtureInstall.FullName 'start-nerv-iip-apphost.ps1'
    Copy-Item -LiteralPath $startScript -Destination $fixtureStart
    $realLibrary = (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1').Replace("'", "''")
    $capture = @'
function Invoke-AspireInteractive {
    param($Name, $WorkingDirectory, $Arguments)
    $values = foreach ($key in @('Messaging__Provider', 'Parameters__redis-password', 'Parameters__iam-enterprise-identity-mfa-code', 'Parameters__barcode-label-printer-id', 'Parameters__barcode-label-printer-host')) {
        [Environment]::GetEnvironmentVariable($key)
    }
    [IO.File]::WriteAllLines((Join-Path $WorkingDirectory 'observed.txt'), [string[]] $values)
}
'@
    [IO.File]::WriteAllText((Join-Path $fixtureLibrary.FullName 'ScriptAutomation.ps1'), ". '$realLibrary'`n$capture")
    $printerArguments = @('-BarcodeLabelPrinterId', 'printer-id', '-BarcodeLabelPrinterHost', 'printer.example.test',
        '-BarcodeLabelPrinterPort', '9100', '-BarcodeLabelPrinterConnectTimeoutSeconds', '1',
        '-BarcodeLabelPrinterWriteTimeoutSeconds', '2', '-BarcodeLabelPrinterDpi', '203',
        '-BarcodeLabelPrinterCapabilities', 'zpl')
    & pwsh -NoProfile -File $fixtureStart @productionArguments @printerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Valid production input must reach Aspire without a seed administrator password.' }
    $observed = [IO.File]::ReadAllLines((Join-Path $fixture 'observed.txt'))
    if (-not [string]::Equals(($observed -join '|'), 'Redis|redis-password|654321|printer-id|printer.example.test', [StringComparison]::Ordinal)) {
        throw 'Release-install did not pass the requested messaging, Redis, MFA and printer inputs to Aspire.'
    }
}
finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force } }

foreach ($contract in @(
    @($appHostEnvironment, 'AddParameter("iam-enterprise-identity-mfa-code", secret: true)', 'AppHost MFA override must be a secret parameter'),
    @($appHostServices, 'Iam__EnterpriseIdentity__Mfa__DevelopmentCode', 'AppHost must deliver the MFA override to IAM'),
    @($dependenciesText, '/run/secrets/redis_password', 'legacy Redis must read the password from the mounted secret'),
    @($dependenciesText, 'user default on #%s', 'legacy Redis must require authentication'),
    @($dependenciesText, 'grep -qx PONG', 'legacy Redis health check must verify the authenticated PONG'),
    @($platformText, 'password=${NERV_IIP_REDIS_PASSWORD:?set NERV_IIP_REDIS_PASSWORD}', 'legacy services must authenticate to Redis'),
    @($platformText, 'Iam__Secrets__Pepper: ${NERV_IIP_IAM_SECRETS_PEPPER:?set NERV_IIP_IAM_SECRETS_PEPPER}', 'legacy IAM must receive its pepper'),
    @($platformText, 'Iam__EnterpriseIdentity__Mfa__DevelopmentCode: ${NERV_IIP_IAM_ENTERPRISE_IDENTITY_MFA_CODE:?set NERV_IIP_IAM_ENTERPRISE_IDENTITY_MFA_CODE}', 'legacy IAM must receive its MFA override'),
    @($platformText, 'LabelPrinter__Mode: ${NERV_IIP_BARCODE_LABEL_PRINTER_MODE:?set NERV_IIP_BARCODE_LABEL_PRINTER_MODE}', 'legacy BarcodeLabel must receive an explicit printer mode'),
    @($platformText, 'LabelPrinter__Printers__0__Host: ${NERV_IIP_BARCODE_LABEL_PRINTER_HOST:?set NERV_IIP_BARCODE_LABEL_PRINTER_HOST}', 'legacy BarcodeLabel must receive the site printer host'),
    @($environmentExampleText, 'NERV_IIP_REDIS_PASSWORD=change-me-strong-redis-password', 'production env example must declare the Redis password'),
    @($environmentExampleText, 'NERV_IIP_IAM_SECRETS_PEPPER=change-me-strong-iam-secrets-pepper', 'production env example must declare the IAM pepper'),
    @($environmentExampleText, 'NERV_IIP_IAM_ENTERPRISE_IDENTITY_MFA_CODE=change-me-non-development-mfa-code', 'production env example must declare the MFA override'),
    @($environmentExampleText, 'NERV_IIP_BARCODE_LABEL_PRINTER_MODE=zpl-tcp', 'production env example must select zpl-tcp explicitly'),
    @($environmentExampleText, 'NERV_IIP_BARCODE_LABEL_PRINTER_HOST=', 'production env example must expose the site printer host without committing a real address')
)) {
    Assert-ContainsOrdinal -Text $contract[0] -Expected $contract[1] -Message $contract[2]
}

# 校验各服务的独立块，避免其他服务上的同名键掩盖漏配。
$serviceBlocks = @{}
foreach ($match in [regex]::Matches($platformText, '(?ms)^  (?<name>[a-z0-9-]+):\r?\n(?<body>.*?)(?=^  [a-z0-9-]+:|^\S|\z)')) {
    $serviceBlocks[$match.Groups['name'].Value] = $match.Groups['body'].Value
}
$serviceEnvironments = @{}
foreach ($serviceEntry in $serviceBlocks.GetEnumerator()) {
    $envMap = [hashtable]::new([StringComparer]::Ordinal)
    $inEnv = $false
    foreach ($line in ($serviceEntry.Value -split '\r?\n')) {
        if ($line -match '^    environment:\s*$') { $inEnv = $true; continue }
        if ($inEnv -and $line -match '^      ') {
            if ($line -match '^      \s*(#.*)?$' -or $line -match '^      <<:\s*\*') { continue }
            if ($line -cmatch '^      (?:"(?<key>[^"]+)"|''(?<key>[^'']+)''|(?<key>[A-Za-z0-9_.-]+)):\s*(?:"(?<value>[^"]*)"|''(?<value>[^'']*)''|(?<value>[^\s#].*?))\s*$') {
                $envMap[$Matches['key']] = $Matches['value']
                continue
            }
            throw "无法解析服务 $($serviceEntry.Key) 的 environment 条目：$line"
        }
        if ($inEnv -and $line -notmatch '^\s*$') { $inEnv = $false }
    }
    $serviceEnvironments[$serviceEntry.Key] = $envMap
}
$fileStorageBindings = @(
    @('file-storage', 'FileStorage__Tus__VolumeIdentity', 'VOLUME_IDENTITY'),
    @('file-storage', 'FileStorage__DownloadCallers__platform-gateway__BearerToken', 'PLATFORM_GATEWAY_DOWNLOAD_TOKEN'),
    @('file-storage', 'FileStorage__DownloadCallers__business-gateway__BearerToken', 'BUSINESS_GATEWAY_DOWNLOAD_TOKEN'),
    @('gateway', 'FileStorage__DownloadBearerToken', 'PLATFORM_GATEWAY_DOWNLOAD_TOKEN'),
    @('business-gateway', 'FileStorage__DownloadBearerToken', 'BUSINESS_GATEWAY_DOWNLOAD_TOKEN')
)
$expectedReferences = @{}
foreach ($binding in $fileStorageBindings) {
    $service, $key, $suffix = $binding
    $variable = "NERV_IIP_FILE_STORAGE_$suffix"
    $reference = '${' + $variable
    $expectedValue = $reference + ':?set ' + $variable + '}'
    $actualValue = [string] $serviceEnvironments[$service][$key]
    if (-not [string]::Equals($actualValue, $expectedValue, [StringComparison]::Ordinal)) {
        throw "服务 $service 缺少有效的 $key 必填绑定（实际：'$actualValue'）。"
    }
    if (-not [regex]::IsMatch($environmentExampleText, "(?m)^$([regex]::Escape($variable))=")) {
        throw "生产环境样例缺少有效赋值行 $variable=。"
    }
    $expectedReferences[$reference]++
}
foreach ($reference in $expectedReferences.Keys) {
    $activeCount = 0
    foreach ($envMap in $serviceEnvironments.Values) {
        foreach ($val in $envMap.Values) {
            if (([string] $val).Contains($reference, [StringComparison]::Ordinal)) {
                $activeCount++
            }
        }
    }
    if ($activeCount -ne $expectedReferences[$reference]) {
        throw "部署输入 $reference 的有效引用数量不匹配（期望 $($expectedReferences[$reference])，实际 $activeCount），检查是否泄漏到其它服务。"
    }
}
foreach ($gateway in @('gateway', 'business-gateway')) {
    if (-not [string]::Equals([string] $serviceEnvironments[$gateway]['FileStorage__BaseUrl'], 'http://file-storage:8080', [StringComparison]::Ordinal)) {
        throw "网关 $gateway 缺少有效的容器内 FileStorage 路由。"
    }
}

foreach ($service in @('business-master-data', 'business-product-engineering', 'business-quality')) {
    Assert-ContainsOrdinal -Text $serviceBlocks[$service] -Expected 'Iam__Jwt__JwksJson: ${NERV_IIP_IAM_JWT_JWKS_JSON:?' -Message "服务 $service 缺少必填 JWKS。"
    if ($serviceBlocks[$service] -match 'PrivateKey') { throw "服务 $service 不得接收 IAM 私钥。" }
}
foreach ($gateway in @('gateway', 'business-gateway')) {
    Assert-ContainsOrdinal -Text $serviceBlocks[$gateway] -Expected 'Security__ForwardedHeaders__TrustedProxies: ${NERV_IIP_GATEWAY_TRUSTED_PROXIES:?' -Message "网关 $gateway 缺少必填可信代理配置。"
}
Assert-ContainsOrdinal -Text $environmentExampleText -Expected 'NERV_IIP_GATEWAY_TRUSTED_PROXIES=' -Message '生产环境样例缺少可信代理输入。'
$exposedBusinessServices = @('business-gateway', 'business-console')
$internalBusinessServices = @(
    $serviceBlocks.Keys | Where-Object {
        $candidate = [string] $_
        $candidate.StartsWith('business-', [StringComparison]::Ordinal) -and
            -not ($exposedBusinessServices | Where-Object { [string]::Equals($_, $candidate, [StringComparison]::Ordinal) })
    }
)
foreach ($service in @('iam') + $internalBusinessServices) {
    if ($serviceBlocks[$service] -match '(?m)^    ports:') { throw "服务 $service 不得发布宿主机端口。" }
}

Write-Host 'Production deployment input contracts passed.'

# GitHub 的 pwsh shell 包装会以残留 $LASTEXITCODE 收尾；Assert-StartFails 的预期失败子进程会留下 1，
# 此处显式归零。本脚本独占一个 CI step，任何断言 throw 仍会被包装器转为失败。
exit 0
