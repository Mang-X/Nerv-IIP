# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Builds and publishes two temporary Aspire Docker Compose artifacts
#     - Validates Production Compose internal service port isolation
#     - Validates environment-gated BarcodeLabel printer configuration and its FileStorage endpoint
#     - Validates synthetic Approval department caller wiring and rollout in generated Compose
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
. (Join-Path $root 'scripts/lib/ProductionComposePorts.ps1')

function Get-ComposeProjectEnvironments {
    param([Parameter(Mandatory)][string] $ComposePath)

    $services = @{}
    $currentService = $null
    $inEnvironment = $false
    foreach ($line in [IO.File]::ReadLines($ComposePath)) {
        if ($line -match '^  (?<name>[a-z0-9-]+):$') {
            $currentService = $Matches['name']
            $services[$currentService] = @{}
            $inEnvironment = $false
            continue
        }
        if ($null -ne $currentService -and $line -match '^    environment:$') {
            $inEnvironment = $true
            continue
        }
        if ($inEnvironment -and $line -match '^      (?<key>[A-Za-z0-9_]+): "(?<value>[^"]*)"$') {
            $services[$currentService][$Matches['key']] = $Matches['value']
            continue
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

    $expectedEnabled = if ([string]::Equals($EnvironmentName, 'Development', [StringComparison]::Ordinal)) { 'true' } else { 'false' }
    $expectedProjects = @('apphub', 'iam', 'ops', 'file-storage', 'notification', 'business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling', 'gateway', 'business-gateway', 'connector-host')
    $projectServices = @($Services.GetEnumerator() | Where-Object { $_.Value.ContainsKey('ASPNETCORE_ENVIRONMENT') })
    if (-not (Test-OrdinalSetEquals -Actual @($projectServices | ForEach-Object Key) -Expected $expectedProjects)) { throw 'Published project resource identity set differs from the #2031 contract.' }
    foreach ($service in $projectServices) {
        if ($service.Value['ASPNETCORE_ENVIRONMENT'] -ne $EnvironmentName -or $service.Value['DOTNET_ENVIRONMENT'] -ne $EnvironmentName) {
            throw "Service '$($service.Key)' did not inherit $EnvironmentName for both .NET environment variables."
        }
    }

    $expectedPersistent = @('apphub', 'iam', 'ops', 'file-storage', 'notification', 'business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling')
    $persistentServices = @($projectServices | Where-Object { $_.Value.ContainsKey('Persistence__AutoMigrate') })
    if (-not (Test-OrdinalSetEquals -Actual @($persistentServices | ForEach-Object Key) -Expected $expectedPersistent)) { throw 'Published AutoMigrate resource identity set differs from the #2031 contract.' }
    foreach ($service in $persistentServices) {
        if ($service.Value['Persistence__AutoMigrate'] -ne $expectedEnabled) {
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
            if ($service.Value[$profile.Key] -ne $profile.Value.Value) { throw "Service '$($service.Key)' has $($profile.Key)='$($service.Value[$profile.Key])', expected '$($profile.Value.Value)'." }
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

function Assert-ApprovalDepartmentArtifact {
    param([hashtable] $Services, [bool] $ConfigureSender)

    $expectedRecipients = if ($ConfigureSender) { @('business-master-data', 'business-approval') } else { @('business-master-data') }
    $masterData = $Services['business-master-data']
    if ($masterData['Iam__BaseUrl'] -cne 'http://iam:${IAM_PORT}') {
        throw 'MasterData must receive the generated IAM endpoint for the Approval department receiver.'
    }

    $scopes = @(
        @{ OrganizationId = 'verify-approval-org-a'; EnvironmentId = 'verify-approval-env-a' }
        @{ OrganizationId = 'verify-approval-org-b'; EnvironmentId = 'verify-approval-env-b' }
    )
    for ($index = 0; $index -lt $scopes.Count; $index++) {
        $parameter = 'APPROVAL_MASTER_DATA_DEPARTMENT_' + $index
        $reference = '${' + $parameter + '}'
        $receiver = "MasterData__ApprovalDepartment__ScopedCallers__Profiles__${index}__"
        $sender = "Approval__MasterDataDepartment__OutboundCredentials__${index}__"
        $targets = @($Services.GetEnumerator() | Where-Object { $_.Value.Values -ccontains $reference } | ForEach-Object Key)
        if (-not (Test-OrdinalSetEquals -Actual $targets -Expected $expectedRecipients)) {
            throw 'The Approval department credential has an invalid Compose service set.'
        }
        $expectedReceiver = $scopes[$index] + @{
            Name = "approval-master-data-department-$index"
            Subject = 'business-approval'
            Permissions__0 = 'internal.masterdata.approval-department.read'
            BearerToken = $reference
        }
        foreach ($entry in $expectedReceiver.GetEnumerator()) {
            if ($masterData[$receiver + $entry.Key] -cne $entry.Value) {
                throw 'MasterData must receive the scoped Approval department caller profile.'
            }
        }
        if ($ConfigureSender) {
            $approval = $Services['business-approval']
            $expectedSender = $scopes[$index] + @{ BearerToken = $reference }
            foreach ($entry in $expectedSender.GetEnumerator()) {
                if ($approval[$sender + $entry.Key] -cne $entry.Value) {
                    throw 'Approval must receive the scoped MasterData department credential.'
                }
            }
        }
    }
    $approval = $Services['business-approval']
    if ($ConfigureSender) {
        if ($approval['Approval__MasterDataDepartment__UseScopedCaller'] -cne 'true') {
            throw 'Approval must explicitly enable the configured department caller.'
        }
    }
    elseif (@($approval.Keys | Where-Object { $_ -clike 'Approval__MasterDataDepartment__*' }).Count -ne 0) {
        throw 'Receiver-first deployment must leave the Approval sender configuration absent.'
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-apphost-environment-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
    foreach ($environmentName in @('Development', 'Production')) {
        $outputPath = Join-Path $temporaryRoot $environmentName.ToLowerInvariant()
        $publishEnvironment = @{}
        if ([string]::Equals($environmentName, 'Production', [StringComparison]::Ordinal)) {
            $publishEnvironment = @{
                Messaging__Provider = 'Redis'
                Security__ForwardedHeaders__TrustedProxies = '192.0.2.10,192.0.2.16/28'
                Security__Cors__AllowedOrigins = 'https://console.example.test,https://business.example.test'
                ConnectorHost__ConnectorHostId = 'verify-connector-host'
                ConnectorHost__OrganizationId = 'verify-organization'
                ConnectorHost__EnvironmentId = 'verify-environment'
            }
        }
        $configureApprovalDepartmentSender = $environmentName -ceq 'Development'
        $publishEnvironment += @{
            ApprovalMasterDataDepartment__Enabled = 'true'
            ApprovalMasterDataDepartment__ConfigureSender = $configureApprovalDepartmentSender.ToString()
            ApprovalMasterDataDepartment__Scopes__0__OrganizationId = 'verify-approval-org-a'
            ApprovalMasterDataDepartment__Scopes__0__EnvironmentId = 'verify-approval-env-a'
            ApprovalMasterDataDepartment__Scopes__1__OrganizationId = 'verify-approval-org-b'
            ApprovalMasterDataDepartment__Scopes__1__EnvironmentId = 'verify-approval-env-b'
        }
        Invoke-WithScopedEnvironment -Variables $publishEnvironment -ScriptBlock {
            Invoke-Aspire -Arguments @('publish', '--output-path', $outputPath, '--environment', $environmentName, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds $PublishTimeoutSeconds -Name "verify-apphost-$($environmentName.ToLowerInvariant())-publish" | Out-Null
        }
        $composePath = Join-Path $outputPath 'docker-compose.yaml'
        if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) { throw "Aspire publish did not produce $composePath." }
        $services = Get-ComposeProjectEnvironments -ComposePath $composePath
        Assert-EnvironmentArtifact -Services $services -EnvironmentName $environmentName
        Assert-ApprovalDepartmentArtifact -Services $services -ConfigureSender $configureApprovalDepartmentSender
        $composeText = [IO.File]::ReadAllText($composePath)
        if ($environmentName -ceq 'Production') {
            Assert-ProductionComposeInternalPorts -ServiceBlocks (Get-ComposeServiceBlocks -ComposeText $composeText)
        }
        if ($composeText -notmatch '(?m)^\s+- type: "volume"\s*\n\s+target: "/home/app"\s*\n\s+source: "nerv-iip-file-storage"' -and
            $composeText -notmatch '(?m)^\s+- "nerv-iip-file-storage:/home/app"') {
            throw 'Published FileStorage service must mount the nerv-iip-file-storage volume at /home/app.'
        }
        if ($composeText -notmatch '(?m)^volumes:(?:.*\n)+?\s+nerv-iip-file-storage:') {
            throw 'Published Compose artifact must declare the nerv-iip-file-storage volume.'
        }
    }
    Write-Diagnostic 'Aspire Development and Production Compose artifacts preserve environment, migration, seed, BarcodeLabel printer, and FileStorage endpoint profiles. Production Compose keeps IAM and internal business services off host ports.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
