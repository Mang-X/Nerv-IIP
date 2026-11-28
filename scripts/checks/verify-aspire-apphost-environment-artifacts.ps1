# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Builds and publishes temporary Aspire Docker Compose artifacts and probes rejected Production ephemeral publish
#     - Validates environment-gated BarcodeLabel printer and retirement proof configuration and its FileStorage endpoint
#     - Validates Production Compose internal service port isolation
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

function Assert-PurchaseReceivingArtifact {
    param([hashtable] $Services, [hashtable] $Inputs)

    foreach ($service in $Services.GetEnumerator()) {
        $expected = [hashtable]::new([StringComparer]::Ordinal)
        foreach ($entry in $Inputs.GetEnumerator()) {
            if (([string]::Equals($service.Key, 'business-erp', [StringComparison]::Ordinal) -and $entry.Key.StartsWith('PurchaseOrderReceiving__WmsScopes__', [StringComparison]::Ordinal)) -or
                ([string]::Equals($service.Key, 'business-wms', [StringComparison]::Ordinal) -and $entry.Key.StartsWith('PurchaseOrderReceiving__Routes__', [StringComparison]::Ordinal))) {
                $expected[$entry.Key] = $entry.Value
            }
        }
        $actual = @($service.Value.Keys | Where-Object { $_.StartsWith('PurchaseOrderReceiving__', [StringComparison]::Ordinal) })
        if (-not (Test-OrdinalSetEquals -Actual $actual -Expected @($expected.Keys))) {
            throw "Unexpected purchase receiving settings for $($service.Key) in published Compose."
        }
        foreach ($entry in $expected.GetEnumerator()) {
            if (-not [string]::Equals($service.Value[$entry.Key], $entry.Value, [StringComparison]::Ordinal)) {
                throw "Published Compose changed $($service.Key) purchase receiving setting $($entry.Key)."
            }
        }
    }
}

function Assert-ApprovalDepartmentArtifact {
    param([hashtable] $Services, [bool] $ConfigureSender)

    $expectedRecipients = if ($ConfigureSender) { @('business-master-data', 'business-approval') } else { @('business-master-data') }
    $masterData = $Services['business-master-data']
    if (-not [string]::Equals($masterData['Iam__BaseUrl'], 'http://iam:${IAM_PORT}', [StringComparison]::Ordinal)) {
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
        $targets = @($Services.GetEnumerator() | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@($_.Value.Values), [StringComparer]::Ordinal).Contains($reference) } | ForEach-Object Key)
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
            if (-not [string]::Equals($masterData[$receiver + $entry.Key], $entry.Value, [StringComparison]::Ordinal)) {
                throw 'MasterData must receive the scoped Approval department caller profile.'
            }
        }
        if ($ConfigureSender) {
            $approval = $Services['business-approval']
            $expectedSender = $scopes[$index] + @{ BearerToken = $reference }
            foreach ($entry in $expectedSender.GetEnumerator()) {
                if (-not [string]::Equals($approval[$sender + $entry.Key], $entry.Value, [StringComparison]::Ordinal)) {
                    throw 'Approval must receive the scoped MasterData department credential.'
                }
            }
        }
    }
    $approval = $Services['business-approval']
    if ($ConfigureSender) {
        if (-not [string]::Equals($approval['Approval__MasterDataDepartment__UseScopedCaller'], 'true', [StringComparison]::Ordinal)) {
            throw 'Approval must explicitly enable the configured department caller.'
        }
    }
    elseif (-not [string]::Equals($approval['Approval__MasterDataDepartment__UseScopedCaller'], 'false', [StringComparison]::Ordinal) -or
        @($approval.Keys | Where-Object { $_.StartsWith('Approval__MasterDataDepartment__OutboundCredentials__', [StringComparison]::Ordinal) }).Count -ne 0) {
        throw 'Receiver-first deployment must explicitly keep the compatibility sender and omit scoped credentials.'
    }
}

$approvalCallers = [ordered]@{
    CreateChain = @('business-erp', 'business-inventory', 'business-gateway')
    GetChain = @('business-mes', 'business-quality', 'business-product-engineering', 'business-gateway')
    ReadCollections = @('business-gateway')
    ReadTemplates = @('business-gateway')
    WriteTemplates = @('business-gateway')
    ReadDelegations = @('business-gateway')
    OverdueCheck = @('approval-overdue-operator')
}

function Assert-ApprovalCallerArtifact {
    param([hashtable] $Services, [bool] $AllowLegacy)

    if ($Services['iam'].ContainsKey('InternalService__BearerToken')) {
        throw 'IAM must receive no shared internal credential.'
    }
    $approval = $Services['business-approval']
    $tokens = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($operation in $approvalCallers.Keys) {
        $settings = if ([string]::Equals($operation, 'OverdueCheck', [StringComparison]::Ordinal)) { 'OverdueCheck__HttpAuthorization' } else { $operation }
        if (-not [string]::Equals($approval["Approval__${settings}__AllowLegacyBearer"], $AllowLegacy.ToString().ToLowerInvariant(), [StringComparison]::Ordinal)) {
            throw "Unexpected legacy authentication policy for Approval $operation."
        }
        $index = 0
        foreach ($caller in $approvalCallers[$operation]) {
            $profile = "Approval__${settings}__ScopedCallers__Profiles__${index}__"
            $token = [string] $approval[$profile + 'BearerToken']
            if ($token -cnotmatch '^\$\{APPROVAL_[A-Z0-9_]+\}$' -or -not $tokens.Add($token)) {
                throw "Approval $operation must receive a unique secret for $caller."
            }
            $expectedRecipients = @('business-approval')
            if (-not $AllowLegacy -and $Services.ContainsKey($caller)) {
                $expectedRecipients += $caller
                $outbound = "Approval__${operation}__OutboundCredentials__0__"
                foreach ($field in @('OrganizationId', 'EnvironmentId', 'BearerToken')) {
                    if ([string]::IsNullOrEmpty($approval[$profile + $field]) -or -not [string]::Equals($approval[$profile + $field], $Services[$caller][$outbound + $field], [StringComparison]::Ordinal)) {
                        throw "Approval $operation and $caller must receive matching $field."
                    }
                }
            }
            $recipients = @($Services.GetEnumerator() | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@($_.Value.Values), [StringComparer]::Ordinal).Contains($token) } | ForEach-Object Key)
            if (-not (Test-OrdinalSetEquals -Actual $recipients -Expected $expectedRecipients)) {
                throw "Approval $operation has an invalid recipient set for $caller."
            }
            $index++
        }
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-apphost-environment-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
    foreach ($profile in @('Development', 'ProductionScoped', 'ProductionCompatibility')) {
        $environmentName = if ([string]::Equals($profile, 'Development', [StringComparison]::Ordinal)) { 'Development' } else { 'Production' }
        $outputPath = Join-Path $temporaryRoot $profile.ToLowerInvariant()
        $receivingInputs = @{}
        if ([string]::Equals($profile, 'ProductionScoped', [StringComparison]::Ordinal)) {
            $receivingInputs = @{
                PurchaseOrderReceiving__WmsScopes__0__OrganizationId = 'verify-receiving-org'
                PurchaseOrderReceiving__WmsScopes__0__EnvironmentId = 'verify-receiving-env'
                PurchaseOrderReceiving__WmsScopes__0__SiteCode = 'verify-site'
                PurchaseOrderReceiving__Routes__0__OrganizationId = 'verify-receiving-org'
                PurchaseOrderReceiving__Routes__0__EnvironmentId = 'verify-receiving-env'
                PurchaseOrderReceiving__Routes__0__SiteCode = 'verify-site'
                PurchaseOrderReceiving__Routes__0__StagingLocationCode = 'verify-staging'
                PurchaseOrderReceiving__Routes__0__QualityStatus = 'unrestricted'
                PurchaseOrderReceiving__Routes__0__OwnerType = 'company'
            }
        }
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
        $allowLegacyApproval = [string]::Equals($environmentName, 'Development', [StringComparison]::Ordinal)
        foreach ($operation in $approvalCallers.Keys) {
            $publishEnvironment["Approval${operation}__Scopes__0__OrganizationId"] = 'verify-approval-org-a'
            $publishEnvironment["Approval${operation}__Scopes__0__EnvironmentId"] = 'verify-approval-env-a'
            if ($allowLegacyApproval) {
                $publishEnvironment["Approval${operation}__Enabled"] = 'true'
                $publishEnvironment["Approval${operation}__AllowLegacyBearer"] = 'true'
                $publishEnvironment["Approval${operation}__ConfigureSenders"] = 'false'
            }
        }
        $configureApprovalDepartmentSender = -not [string]::Equals($profile, 'ProductionCompatibility', [StringComparison]::Ordinal)
        $publishEnvironment += @{
            ApprovalMasterDataDepartment__Scopes__0__OrganizationId = 'verify-approval-org-a'
            ApprovalMasterDataDepartment__Scopes__0__EnvironmentId = 'verify-approval-env-a'
            ApprovalMasterDataDepartment__Scopes__1__OrganizationId = 'verify-approval-org-b'
            ApprovalMasterDataDepartment__Scopes__1__EnvironmentId = 'verify-approval-env-b'
        }
        if (-not [string]::Equals($profile, 'ProductionScoped', [StringComparison]::Ordinal)) {
            $publishEnvironment['ApprovalMasterDataDepartment__Enabled'] = 'true'
            $publishEnvironment['ApprovalMasterDataDepartment__ConfigureSender'] = $configureApprovalDepartmentSender.ToString()
        }
        $publishEnvironment += $receivingInputs
        Invoke-WithScopedEnvironment -Variables $publishEnvironment -ScriptBlock {
            Invoke-Aspire -Arguments @('publish', '--output-path', $outputPath, '--environment', $environmentName, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds $PublishTimeoutSeconds -Name "verify-apphost-$($profile.ToLowerInvariant())-publish" | Out-Null
        }
        $composePath = Join-Path $outputPath 'docker-compose.yaml'
        if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) { throw "Aspire publish did not produce $composePath." }
        $services = Get-ComposeProjectEnvironments -ComposePath $composePath
        Assert-EnvironmentArtifact -Services $services -EnvironmentName $environmentName
        Assert-ApprovalCallerArtifact -Services $services -AllowLegacy $allowLegacyApproval
        Assert-PurchaseReceivingArtifact -Services $services -Inputs $receivingInputs
        Assert-ApprovalDepartmentArtifact -Services $services -ConfigureSender $configureApprovalDepartmentSender
        $composeText = [IO.File]::ReadAllText($composePath)
        if ([string]::Equals($environmentName, 'Production', [StringComparison]::Ordinal)) {
            Assert-ProductionComposeInternalPorts -ServiceBlocks (Get-ComposeServiceBlocks -ComposeText $composeText)
        }
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
    Write-Diagnostic 'Aspire Development and Production Compose artifacts preserve environment, migration, seed, BarcodeLabel printer, retirement proof, and FileStorage endpoint profiles. Production Compose keeps IAM and internal business services off host ports.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
