# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Builds and publishes two temporary Aspire Docker Compose artifacts and probes rejected Production ephemeral publish
#     - Validates environment-gated BarcodeLabel printer and retirement proof configuration and its FileStorage endpoint
#   Writes:
#     - A uniquely owned temporary directory under the system temporary directory
#     - .ryugi/artifacts/script-logs/**
#   Cleanup:
#     - Deletes only the temporary directory created by this invocation
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Aspire CLI 17.0.0

[CmdletBinding()]
param(
    [ValidateRange(60, 1800)]
    [int] $PublishTimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Get-ComposeProjectEnvironments {
    param([Parameter(Mandatory)][string] $ComposePath)

    $services = [hashtable]::new([StringComparer]::Ordinal)
    $currentService = $null
    $inEnvironment = $false
    $inServices = $false
    foreach ($line in [IO.File]::ReadLines($ComposePath)) {
        if ($line -cmatch '^[^ #]') {
            $inServices = [string]::Equals($line, 'services:', [StringComparison]::Ordinal)
        }
        if (-not $inServices) { continue }
        if ($line -match '^  (?<name>[a-z0-9-]+):$') {
            $currentService = $Matches['name']
            $services[$currentService] = [hashtable]::new([StringComparer]::Ordinal)
            $inEnvironment = $false
            continue
        }
        if ($null -ne $currentService -and $line -match '^    environment:$') {
            $inEnvironment = $true
            continue
        }
        if ($inEnvironment -and $line -match '^      ') {
            if ($line -match '^      \s*(#.*)?$') { continue }
            if ($line -cmatch '^      (?:"(?<key>[^"]+)"|''(?<key>[^'']+)''|(?<key>[A-Za-z0-9_.-]+)):\s*(?:"(?<value>[^"]*)"|''(?<value>[^'']*)''|(?<value>[^\s#].*?))\s*$') {
                $services[$currentService][$Matches['key']] = $Matches['value']
                continue
            }
            throw "Unable to parse environment entry in service '$currentService': $line"
        }
        if ($line -notmatch '^      ' -and $line -notmatch '^    environment:$') {
            $inEnvironment = $false
        }
    }
    return $services
}

function Test-OrdinalSetEquals {
    param([string[]] $Actual, [string[]] $Expected)
    $actualSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Actual) { [void] $actualSet.Add($value) }
    foreach ($value in $Expected) { [void] $expectedSet.Add($value) }
    return $actualSet.Count -eq $Expected.Count -and $actualSet.SetEquals($expectedSet)
}

function Assert-EnvironmentArtifact {
    param(
        [Parameter(Mandatory)][hashtable] $Services,
        [Parameter(Mandatory)][string] $EnvironmentName
    )

    $production = [string]::Equals($EnvironmentName, 'Production', [StringComparison]::Ordinal)
    $retirementProofServices = @(if (-not $production) { 'business-barcode-label', 'business-gateway' })
    $retirementProofSettings = @{
        'TemplateAssetRetirementProof__Issuer' = 'nerv-local-business-gateway'
        'TemplateAssetRetirementProof__Audience' = 'nerv-barcodelabel-template-asset-retirement'
        'TemplateAssetRetirementProof__SecretBase64' = '${TEMPLATE_ASSET_RETIREMENT_PROOF_SECRET_BASE64}'
    }
    foreach ($setting in $retirementProofSettings.GetEnumerator()) {
        $recipients = @($Services.GetEnumerator() | Where-Object { $_.Value.ContainsKey($setting.Key) })
        if (-not (Test-OrdinalSetEquals -Actual @($recipients | ForEach-Object Key) -Expected $retirementProofServices)) {
            throw "Published $EnvironmentName artifact has an invalid service set for $($setting.Key)."
        }
        foreach ($recipient in $recipients) {
            if (-not [string]::Equals($recipient.Value[$setting.Key], $setting.Value, [StringComparison]::Ordinal)) {
                throw "Published $EnvironmentName $($recipient.Key) has an unexpected value or parameter reference for $($setting.Key)."
            }
        }
    }

    $fileStorageBindings = @(
        @('file-storage', 'FileStorage__Tus__VolumeIdentity', '${FILE_STORAGE_VOLUME_IDENTITY}'),
        @('file-storage', 'FileStorage__DownloadCallers__platform-gateway__BearerToken', '${FILE_STORAGE_PLATFORM_GATEWAY_DOWNLOAD_TOKEN}'),
        @('file-storage', 'FileStorage__DownloadCallers__business-gateway__BearerToken', '${FILE_STORAGE_BUSINESS_GATEWAY_DOWNLOAD_TOKEN}'),
        @('gateway', 'FileStorage__DownloadBearerToken', '${FILE_STORAGE_PLATFORM_GATEWAY_DOWNLOAD_TOKEN}'),
        @('business-gateway', 'FileStorage__DownloadBearerToken', '${FILE_STORAGE_BUSINESS_GATEWAY_DOWNLOAD_TOKEN}')
    )
    foreach ($binding in $fileStorageBindings) {
        $service, $key, $placeholder = $binding
        if ($production -and -not [string]::Equals($Services[$service][$key], $placeholder, [StringComparison]::Ordinal)) {
            throw "Production $service 缺少正确的 $key 参数引用。"
        }
    }
    foreach ($service in $Services.GetEnumerator()) {
        foreach ($setting in $service.Value.GetEnumerator()) {
            $isFileStorageInput = [string]::Equals($setting.Key, 'FileStorage__Tus__VolumeIdentity', [StringComparison]::Ordinal) -or
                [string]::Equals($setting.Key, 'FileStorage__DownloadBearerToken', [StringComparison]::Ordinal) -or
                $setting.Key.StartsWith('FileStorage__DownloadCallers__', [StringComparison]::Ordinal)
            $referencesFileStorageInput = $fileStorageBindings | Where-Object {
                $setting.Value.Contains($_[2], [StringComparison]::Ordinal)
            }
            if ($isFileStorageInput -or $referencesFileStorageInput) {
                $allowed = $fileStorageBindings | Where-Object {
                    [string]::Equals($service.Key, $_[0], [StringComparison]::Ordinal) -and
                    [string]::Equals($setting.Key, $_[1], [StringComparison]::Ordinal) -and
                    [string]::Equals($setting.Value, $_[2], [StringComparison]::Ordinal)
                }
                if (-not $production -or -not $allowed) {
                    throw "$EnvironmentName $($service.Key) 意外接收 FileStorage 部署输入 $($setting.Key)。"
                }
            }
        }
    }

    $expectedEnabled = if ([string]::Equals($EnvironmentName, 'Development', [StringComparison]::Ordinal)) { 'true' } else { 'false' }
    $expectedProjects = @('apphub', 'iam', 'ops', 'file-storage', 'notification', 'business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling', 'gateway', 'business-gateway', 'connector-host')
    $projectServices = @($Services.GetEnumerator() | Where-Object { $_.Value.ContainsKey('ASPNETCORE_ENVIRONMENT') })
    if (-not (Test-OrdinalSetEquals -Actual @($projectServices | ForEach-Object Key) -Expected $expectedProjects)) { throw 'Published project resource identity set differs from the #2031 contract.' }
    foreach ($service in $projectServices) {
        if (-not [string]::Equals($service.Value['ASPNETCORE_ENVIRONMENT'], $EnvironmentName, [StringComparison]::Ordinal) -or
            -not [string]::Equals($service.Value['DOTNET_ENVIRONMENT'], $EnvironmentName, [StringComparison]::Ordinal)) {
            throw "Service '$($service.Key)' did not inherit $EnvironmentName for both .NET environment variables."
        }
    }

    $expectedPersistent = @('apphub', 'iam', 'ops', 'file-storage', 'notification', 'business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling')
    $persistentServices = @($projectServices | Where-Object { $_.Value.ContainsKey('Persistence__AutoMigrate') })
    if (-not (Test-OrdinalSetEquals -Actual @($persistentServices | ForEach-Object Key) -Expected $expectedPersistent)) { throw 'Published AutoMigrate resource identity set differs from the #2031 contract.' }
    foreach ($service in $persistentServices) {
        if (-not [string]::Equals($service.Value['Persistence__AutoMigrate'], $expectedEnabled, [StringComparison]::Ordinal)) {
            throw "Service '$($service.Key)' has Persistence__AutoMigrate='$($service.Value['Persistence__AutoMigrate'])', expected '$expectedEnabled'."
        }
    }

    $expectedProfiles = @{
        'Iam__Seed__Enabled' = @{ Services = @('iam'); Value = $expectedEnabled }
        'Erp__Seed__SalesOrderDemandDemo__Enabled' = @{ Services = @('business-erp'); Value = $expectedEnabled }
        'Walkthrough__Seed__Enabled' = @{ Services = @('business-master-data', 'business-product-engineering', 'business-erp'); Value = $expectedEnabled }
    }
    foreach ($profile in $expectedProfiles.GetEnumerator()) {
        $matching = @($projectServices | Where-Object { $_.Value.ContainsKey($profile.Key) })
        if (-not (Test-OrdinalSetEquals -Actual @($matching | ForEach-Object Key) -Expected $profile.Value.Services)) { throw "Published $EnvironmentName artifact has an invalid service set for $($profile.Key)." }
        foreach ($service in $matching) {
            if (-not [string]::Equals($service.Value[$profile.Key], $profile.Value.Value, [StringComparison]::Ordinal)) { throw "Service '$($service.Key)' has $($profile.Key)='$($service.Value[$profile.Key])', expected '$($profile.Value.Value)'." }
        }
    }

    if ([string]::Equals($EnvironmentName, 'Production', [StringComparison]::Ordinal)) {
        $allowedJwtKeys = @('Iam__Jwt__JwksJson', 'Iam__Jwt__Issuer', 'Iam__Jwt__Audience')
        foreach ($service in @('business-master-data', 'business-product-engineering', 'business-quality', 'gateway', 'business-gateway')) {
            $jwtKeys = @($Services[$service].Keys | Where-Object { ([string] $_).StartsWith('Iam__Jwt__', [StringComparison]::Ordinal) })
            $unexpectedJwtKeys = @($jwtKeys | Where-Object {
                $candidate = [string] $_
                -not ($allowedJwtKeys | Where-Object { [string]::Equals($_, $candidate, [StringComparison]::Ordinal) })
            })
            if (-not [string]::Equals([string] $Services[$service]['Iam__Jwt__JwksJson'], '${IAM_JWT_JWKS_JSON}', [StringComparison]::Ordinal) -or
                $unexpectedJwtKeys.Count -gt 0) {
                throw "Production $service 必须使用 IAM 公钥 JWKS 占位符，且不得注入签名材料。"
            }
        }
        foreach ($gateway in @('gateway', 'business-gateway')) {
            if (-not [string]::Equals([string] $Services[$gateway]['Security__ForwardedHeaders__TrustedProxies'], '192.0.2.10,192.0.2.16/28', [StringComparison]::Ordinal)) {
                throw "Production $gateway 必须接收显式的可信代理配置。"
            }
        }
    }

    # #3097：tus 盘承载已 complete 文件的字节，生成产物必须给 FileStorage 一个容器内绝对路径并挂命名卷；
    # 系统 temp 或容器可写层不是持久落点（ADR 0024 §5）。
    $fileStorage = $Services['file-storage']
    if (-not [string]::Equals([string] $fileStorage['FileStorage__UploadProvider'], 'tus', [StringComparison]::Ordinal)) {
        throw "Published FileStorage must select the tus upload provider; observed '$($fileStorage['FileStorage__UploadProvider'])'."
    }
    $tusRootPath = [string] $fileStorage['FileStorage__Tus__RootPath']
    if (-not $tusRootPath.StartsWith('/home/app/', [StringComparison]::Ordinal)) {
        throw "Published FileStorage must place FileStorage__Tus__RootPath under the app-owned volume mount; observed '$tusRootPath'."
    }

    if (-not $Services.ContainsKey('business-barcode-label')) {
        throw 'Published artifact is missing the business-barcode-label resource.'
    }

    $barcodeLabel = $Services['business-barcode-label']
    if (-not [string]::Equals([string] $barcodeLabel['FileStorage__BaseUrl'], 'http://file-storage:${FILE_STORAGE_PORT}', [StringComparison]::Ordinal)) {
        throw "BusinessBarcodeLabel must consume the generated FileStorage endpoint; observed '$($barcodeLabel['FileStorage__BaseUrl'])'."
    }

    if ([string]::Equals($EnvironmentName, 'Development', [StringComparison]::Ordinal)) {
        if (-not [string]::Equals([string] $barcodeLabel['LabelPrinter__Mode'], 'simulated', [StringComparison]::Ordinal)) {
            throw "Development BusinessBarcodeLabel must select simulated printing explicitly; observed '$($barcodeLabel['LabelPrinter__Mode'])'."
        }

        if ($barcodeLabel.ContainsKey('LabelPrinter__Printers__0__Host')) {
            throw 'Development BusinessBarcodeLabel must not publish a real printer route.'
        }
    }
    else {
        $expectedPrinterRoute = @{
            'LabelPrinter__Mode' = 'zpl-tcp'
            'LabelPrinter__Printers__0__Id' = '${BARCODE_LABEL_PRINTER_ID}'
            'LabelPrinter__Printers__0__Host' = '${BARCODE_LABEL_PRINTER_HOST}'
            'LabelPrinter__Printers__0__Port' = '${BARCODE_LABEL_PRINTER_PORT}'
            'LabelPrinter__Printers__0__ConnectTimeoutSeconds' = '${BARCODE_LABEL_PRINTER_CONNECT_TIMEOUT_SECONDS}'
            'LabelPrinter__Printers__0__WriteTimeoutSeconds' = '${BARCODE_LABEL_PRINTER_WRITE_TIMEOUT_SECONDS}'
            'LabelPrinter__Printers__0__Dpi' = '${BARCODE_LABEL_PRINTER_DPI}'
            'LabelPrinter__Printers__0__Language' = 'zpl'
            'LabelPrinter__Printers__0__Capabilities' = '${BARCODE_LABEL_PRINTER_CAPABILITIES}'
            'LabelPrinter__Printers__0__Enabled' = 'true'
        }
        foreach ($entry in $expectedPrinterRoute.GetEnumerator()) {
            if (-not [string]::Equals([string] $barcodeLabel[$entry.Key], [string] $entry.Value, [StringComparison]::Ordinal)) {
                throw "Production BusinessBarcodeLabel has $($entry.Key)='$($barcodeLabel[$entry.Key])', expected '$($entry.Value)'."
            }
        }
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-apphost-environment-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
    foreach ($environmentName in @('Development', 'Production')) {
        $outputPath = Join-Path $temporaryRoot $environmentName.ToLowerInvariant()
        $publishEnvironment = @{ NERV_IIP_EPHEMERAL = 'false' }
        if ([string]::Equals($environmentName, 'Production', [StringComparison]::Ordinal)) {
            $publishEnvironment += @{
                Messaging__Provider = 'Redis'
                Security__ForwardedHeaders__TrustedProxies = '192.0.2.10,192.0.2.16/28'
                Security__Cors__AllowedOrigins = 'https://console.example.test,https://business.example.test'
                ConnectorHost__ConnectorHostId = 'verify-connector-host'
                ConnectorHost__OrganizationId = 'verify-organization'
                ConnectorHost__EnvironmentId = 'verify-environment'
            }
        }
        Invoke-WithScopedEnvironment -Variables $publishEnvironment -ScriptBlock {
            Invoke-Aspire -Arguments @('publish', '--output-path', $outputPath, '--environment', $environmentName, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds $PublishTimeoutSeconds -Name "verify-apphost-$($environmentName.ToLowerInvariant())-publish" | Out-Null
        }
        $composePath = Join-Path $outputPath 'docker-compose.yaml'
        if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) { throw "Aspire publish did not produce $composePath." }
        Assert-EnvironmentArtifact -Services (Get-ComposeProjectEnvironments -ComposePath $composePath) -EnvironmentName $environmentName
        $composeText = [IO.File]::ReadAllText($composePath)
        $postgresService = [regex]::Match($composeText, '(?m)^  postgres:\s*\r?\n(?:^    .*\r?\n)*')
        if (-not $postgresService.Success) { throw 'Published Compose artifact must define postgres.' }
        $hasDevelopmentConnectionLimit = $postgresService.Value.Contains('max_connections=300', [StringComparison]::Ordinal)
        $isDevelopmentEnvironment = [string]::Equals($environmentName, 'Development', [StringComparison]::Ordinal)
        if ($hasDevelopmentConnectionLimit -ne $isDevelopmentEnvironment) {
            throw "Published $environmentName PostgreSQL command has incorrect Development connection limit."
        }
        if ($composeText -notmatch '(?m)^\s+- type: "volume"\s*\n\s+target: "/home/app"\s*\n\s+source: "nerv-iip-file-storage"' -and
            $composeText -notmatch '(?m)^\s+- "nerv-iip-file-storage:/home/app"') {
            throw 'Published FileStorage service must mount the nerv-iip-file-storage volume at /home/app.'
        }
        if ($composeText -notmatch '(?m)^volumes:(?:.*\n)+?\s+nerv-iip-file-storage:') {
            throw 'Published Compose artifact must declare the nerv-iip-file-storage volume.'
        }
    }
    $rejectedOutputPath = Join-Path $temporaryRoot 'rejected-production-ephemeral'
    $rejected = $false
    try {
        Invoke-AspireOutput -Arguments @('publish', '--output-path', $rejectedOutputPath, '--environment', 'Production', '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds $PublishTimeoutSeconds -Name 'verify-apphost-production-ephemeral-rejection' -Environment @{
            NERV_IIP_EPHEMERAL = 'true'
            NERV_IIP_SESSION_ID = 'nerv-abcd-123456'
        } | Out-Null
    }
    catch {
        $hasExitCode = $false
        foreach ($key in $_.Exception.Data.Keys) {
            if ([string]::Equals([string]$key, 'ExitCode', [StringComparison]::Ordinal)) {
                $hasExitCode = $true
                break
            }
        }
        if (-not $hasExitCode -or
            -not $_.Exception.Message.Contains('System.InvalidOperationException: NERV_IIP_EPHEMERAL=true is only supported in Development.', [StringComparison]::Ordinal)) {
            throw
        }
        $rejected = $true
    }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $rejectedOutputPath 'docker-compose.yaml'))) {
        throw 'Production ephemeral publish must fail before generating a Compose artifact.'
    }
    Write-Diagnostic 'Production rejects ephemeral publish before generating Compose.'
    Write-Diagnostic 'Aspire Development and Production Compose artifacts preserve environment, migration, seed, BarcodeLabel printer, retirement proof, and FileStorage endpoint profiles.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
