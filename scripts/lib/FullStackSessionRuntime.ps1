Set-StrictMode -Version Latest

$runtimeLibraryRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $runtimeLibraryRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $runtimeLibraryRoot 'scripts/lib/FullStackSessionState.ps1')

function Test-NervDockerResourceOwnership {
    param(
        [Parameter(Mandatory)] [object] $InspectObject,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $RecordedIds
    )

    $id = "$($InspectObject.Id)"
    if ($RecordedIds -cnotcontains $id) {
        return $false
    }

    $labels = if ($null -ne $InspectObject.PSObject.Properties['Config'] -and
        $null -ne $InspectObject.Config -and
        $null -ne $InspectObject.Config.PSObject.Properties['Labels']) {
        $InspectObject.Config.Labels
    }
    elseif ($null -ne $InspectObject.PSObject.Properties['Labels']) {
        $InspectObject.Labels
    }
    else {
        $null
    }
    if ($null -eq $labels) {
        return $false
    }

    $sessionLabel = $labels.PSObject.Properties['com.nerv-iip.session']
    return $null -ne $sessionLabel -and "$($sessionLabel.Value)" -ceq $SessionId
}

function Test-NervDockerRecordedNameOwnership {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $RecordedNames
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        return $false
    }

    return ($RecordedNames -ccontains $Name) -and
        $Name.EndsWith("-$SessionId", [StringComparison]::Ordinal)
}

function Test-NervDockerOptionalSessionLabel {
    param(
        [AllowNull()] [object] $Labels,
        [Parameter(Mandatory)] [string] $SessionId
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        return $false
    }
    if ($null -eq $Labels) {
        return $true
    }

    $sessionLabel = $Labels.PSObject.Properties['com.nerv-iip.session']
    return $null -eq $sessionLabel -or "$($sessionLabel.Value)" -ceq $SessionId
}

function Read-NervAspireJson {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [ValidateRange(1, 4194304)] [int] $MaxCharacters = 1048576
    )

    if ($Text.Length -gt $MaxCharacters) {
        throw "Aspire output exceeded the $MaxCharacters character parsing limit."
    }

    $payloads = [System.Collections.Generic.List[object]]::new()
    for ($start = 0; $start -lt $Text.Length; $start++) {
        if ($Text[$start] -ne '{') { continue }

        $depth = 0
        $inString = $false
        $escaped = $false
        $end = -1
        for ($index = $start; $index -lt $Text.Length; $index++) {
            $character = $Text[$index]
            if ($inString) {
                if ($escaped) { $escaped = $false; continue }
                if ($character -eq '\') { $escaped = $true; continue }
                if ($character -eq '"') { $inString = $false }
                continue
            }
            if ($character -eq '"') { $inString = $true; continue }
            if ($character -eq '{') { $depth++ }
            elseif ($character -eq '}') {
                $depth--
                if ($depth -eq 0) { $end = $index; break }
            }
        }
        if ($end -lt $start) { continue }

        $candidate = $Text.Substring($start, $end - $start + 1)
        try { $payloads.Add(($candidate | ConvertFrom-Json -Depth 40)) } catch { }
        $start = $end
    }

    if ($payloads.Count -ne 1) {
        $safeText = Protect-ScriptAutomationText -Text $Text
        throw "Expected exactly one Aspire JSON object, found $($payloads.Count); redacted output length was $($safeText.Length)."
    }
    return $payloads[0]
}

function Get-NervAspireStartIdentity {
    param([Parameter(Mandatory)] [object] $StartObject)

    $appHostPid = [int] $StartObject.appHostPid
    $cliPid = [int] $StartObject.cliPid
    if ($appHostPid -le 0 -or $cliPid -le 0 -or [string]::IsNullOrWhiteSpace("$($StartObject.appHostPath)")) {
        throw 'Aspire detached-start JSON did not contain its AppHost path and process identities.'
    }

    return [pscustomobject]@{
        AppHostId = "pid:$appHostPid"
        AppHostPath = [System.IO.Path]::GetFullPath("$($StartObject.appHostPath)")
        AppHostPid = $appHostPid
        CliPid = $cliPid
        LogFile = "$($StartObject.logFile)"
    }
}

function Get-NervAspireResourceSnapshot {
    param(
        [Parameter(Mandatory)] [object] $DescribeObject,
        [Parameter(Mandatory)] [string] $ResourceName
    )

    $matches = @($DescribeObject.resources | Where-Object { "$($_.displayName)" -ceq $ResourceName })
    if ($matches.Count -ne 1) {
        throw "Expected one Aspire resource named '$ResourceName', found $($matches.Count)."
    }
    return $matches[0]
}

function Get-NervAspireResourceEndpoint {
    param(
        [Parameter(Mandatory)] [object] $DescribeObject,
        [Parameter(Mandatory)] [string] $ResourceName,
        [Parameter(Mandatory)] [string] $EndpointName
    )

    $resource = Get-NervAspireResourceSnapshot -DescribeObject $DescribeObject -ResourceName $ResourceName
    $matches = @($resource.urls | Where-Object { "$($_.name)" -ceq $EndpointName })
    if ($matches.Count -ne 1 -or [string]::IsNullOrWhiteSpace("$($matches[0].url)")) {
        throw "Expected one '$EndpointName' endpoint for Aspire resource '$ResourceName'."
    }
    return "$($matches[0].url)"
}

function Get-NervAspireDescribeObject {
    param(
        [Parameter(Mandatory)] [string] $AppHostProject,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $result = Invoke-AspireOutput `
        -Arguments @('describe', '--format', 'Json', '--apphost', $AppHostProject, '--non-interactive', '--nologo') `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 60 `
        -Name 'fullstack-aspire-describe'
    return (Read-NervAspireJson -Text "$($result.Stdout)")
}

function Wait-NervAspireResource {
    param(
        [Parameter(Mandatory)] [string] $AppHostProject,
        [Parameter(Mandatory)] [string] $ResourceName,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [ValidateRange(1, 1200)] [int] $TimeoutSeconds = 600
    )

    Invoke-AspireOutput `
        -Arguments @('wait', $ResourceName, '--status', 'healthy', '--timeout', "$TimeoutSeconds", '--apphost', $AppHostProject, '--non-interactive', '--nologo') `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds ($TimeoutSeconds + 30) `
        -Name "fullstack-aspire-wait-$ResourceName" | Out-Null
}

function Save-NervFullStackEndpoints {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $DescribeObject
    )

    $endpoints = [ordered]@{}
    foreach ($resourceName in @('gateway', 'business-gateway', 'console', 'business-console', 'screen')) {
        $endpoints[$resourceName] = Get-NervAspireResourceEndpoint `
            -DescribeObject $DescribeObject `
            -ResourceName $resourceName `
            -EndpointName 'http'
    }
    $Manifest.endpoints = $endpoints
    return $Manifest
}

function Get-NervFullStackEndpointValue {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ResourceName
    )

    $value = if ($Manifest.endpoints -is [System.Collections.IDictionary]) {
        $Manifest.endpoints[$ResourceName]
    }
    else {
        $property = $Manifest.endpoints.PSObject.Properties[$ResourceName]
        if ($null -ne $property) { $property.Value } else { $null }
    }
    if ([string]::IsNullOrWhiteSpace("$value")) {
        throw "Manifest '$($Manifest.sessionId)' has no endpoint named '$ResourceName'."
    }
    return "$value"
}

function Protect-NervFullStackDiagnosticText {
    param(
        [AllowNull()] [string] $Text,
        [string[]] $SensitiveValues = @()
    )

    $safe = Protect-ScriptAutomationText -Text $Text
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
            $safe = $safe.Replace($sensitiveValue, '<redacted>')
        }
    }
    return $safe
}

function Invoke-NervFullStackSmokeScenario {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $SessionAdminPassword,
        [scriptblock] $WaitAction,
        [scriptblock] $HttpCheckAction,
        [scriptblock] $AspireSnapshotAction,
        [scriptblock] $BrowserAction
    )

    if ($null -eq $WaitAction) {
        $WaitAction = {
            param($Name, $InputManifest)
            Wait-NervAspireResource `
                -AppHostProject "$($InputManifest.appHostProject)" `
                -ResourceName $Name `
                -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $HttpCheckAction) {
        $HttpCheckAction = {
            param($Name, $Url)
            try {
                Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 30 -UseBasicParsing | Out-Null
            }
            catch {
                $statusCode = if ($null -ne $_.Exception.Response) { [int] $_.Exception.Response.StatusCode } else { 0 }
                if ($statusCode -le 0 -or $statusCode -ge 500) { throw "HTTP check failed for '$Name' at '$Url': $($_.Exception.Message)" }
            }
        }
    }
    if ($null -eq $AspireSnapshotAction) {
        $AspireSnapshotAction = {
            param($InputManifest)
            Get-NervAspireDescribeObject -AppHostProject "$($InputManifest.appHostProject)" -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $BrowserAction) {
        $BrowserAction = {
            param($Environment, $InputManifest)
            try {
                foreach ($entry in $Environment.GetEnumerator()) { Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value }
                Invoke-Pnpm `
                    -Arguments @(
                        '-C', 'frontend', '--filter', '@nerv-iip/business-console', 'exec', 'playwright', 'test',
                        'e2e/fullstack-proxy.spec.ts', '--project=desktop', '--reporter=line',
                        '--output', (Join-Path "$($InputManifest.artifactPath)" 'test-results')
                    ) `
                    -WorkingDirectory "$($InputManifest.worktreeRoot)" `
                    -TimeoutSeconds 300 `
                    -Name "fullstack-$($InputManifest.sessionId)-playwright" | Out-Null
            }
            finally {
                foreach ($key in $Environment.Keys) { Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue }
            }
        }
    }

    $resourceNames = @('gateway', 'business-gateway', 'console', 'business-console', 'screen')
    foreach ($resourceName in $resourceNames) {
        & $WaitAction $resourceName $Manifest | Out-Null
        $url = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName $resourceName
        & $HttpCheckAction $resourceName $url | Out-Null
    }

    $snapshot = & $AspireSnapshotAction $Manifest
    $finishedProjects = @($snapshot.resources | Where-Object {
        "$($_.resourceType)" -like 'Project*' -and "$($_.state)" -eq 'Finished'
    })
    if ($finishedProjects.Count -gt 0) {
        throw "Aspire project resources finished unexpectedly: $($finishedProjects.displayName -join ', ')."
    }

    $childEnvironment = @{
        NERV_IIP_GATEWAY_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'gateway'
        NERV_IIP_BUSINESS_GATEWAY_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'business-gateway'
        PLAYWRIGHT_BASE_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'business-console'
        NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $SessionAdminPassword
    }
    & $BrowserAction $childEnvironment $Manifest | Out-Null
    return [pscustomobject]@{ ExitCode = 0; ChildEnvironment = $childEnvironment; CheckedResources = $resourceNames }
}

function Get-NervFullStackCollectTimeoutSeconds {
    if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS)) { return 120 }
    $seconds = 0
    if (-not [int]::TryParse($env:NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS, [ref] $seconds) -or $seconds -lt 1 -or $seconds -gt 600) {
        throw 'NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS must be an integer from 1 through 600.'
    }
    return $seconds
}

function Collect-NervFullStackDiagnostics {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string[]] $SensitiveValues = @(),
        [scriptblock] $LogAction,
        [int] $TimeoutSeconds = (Get-NervFullStackCollectTimeoutSeconds)
    )

    if ($null -eq $LogAction) {
        $LogAction = {
            param($ResourceName, $InputManifest, $BoundedTimeoutSeconds)
            $result = Invoke-AspireOutput `
                -Arguments @('logs', $ResourceName, '--tail', '500', '--format', 'Json', '--apphost', "$($InputManifest.appHostProject)", '--non-interactive', '--nologo') `
                -WorkingDirectory "$($InputManifest.worktreeRoot)" `
                -TimeoutSeconds $BoundedTimeoutSeconds `
                -Name "fullstack-$($InputManifest.sessionId)-collect-$ResourceName"
            return "$($result.Stdout)"
        }
    }

    $artifactPath = [System.IO.Path]::GetFullPath("$($Manifest.artifactPath)")
    $logDirectory = Join-Path $artifactPath 'aspire-logs'
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $collectionErrors = [System.Collections.Generic.List[string]]::new()
    foreach ($resourceName in @('gateway', 'business-gateway', 'console', 'business-console', 'screen', 'postgres', 'redis', 'minio')) {
        try {
            $raw = (& $LogAction $resourceName $Manifest $TimeoutSeconds) -join "`n"
            $safe = Protect-NervFullStackDiagnosticText -Text $raw -SensitiveValues $SensitiveValues
            [System.IO.File]::WriteAllText(
                (Join-Path $logDirectory "$resourceName.ndjson"),
                $safe,
                [System.Text.UTF8Encoding]::new($false)
            )
        }
        catch {
            $collectionErrors.Add((Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues $SensitiveValues))
        }
    }

    $existingCleanupErrors = if ($null -ne $Manifest.cleanup) { @($Manifest.cleanup.errors) } else { @() }
    $summary = [ordered]@{
        schemaVersion = 1
        sessionId = "$($Manifest.sessionId)"
        state = "$($Manifest.state)"
        collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        endpoints = $Manifest.endpoints
        cleanupErrors = @($existingCleanupErrors | ForEach-Object {
            Protect-NervFullStackDiagnosticText -Text "$_" -SensitiveValues $SensitiveValues
        })
        collectionErrors = @($collectionErrors)
    }
    $summaryJson = Protect-NervFullStackDiagnosticText `
        -Text ($summary | ConvertTo-Json -Depth 20) `
        -SensitiveValues $SensitiveValues
    [System.IO.File]::WriteAllText(
        (Join-Path $artifactPath 'summary.json'),
        $summaryJson,
        [System.Text.UTF8Encoding]::new($false)
    )
    return [pscustomobject]@{ Complete = $collectionErrors.Count -eq 0; Errors = @($collectionErrors) }
}

function Invoke-NervManagedFullStackRun {
    param(
        [Parameter(Mandatory)] [scriptblock] $StartAction,
        [Parameter(Mandatory)] [scriptblock] $ScenarioAction,
        [Parameter(Mandatory)] [scriptblock] $CollectAction,
        [Parameter(Mandatory)] [scriptblock] $StopAction,
        [scriptblock] $ResolveFailedManifestAction,
        [scriptblock] $FailureAction,
        [scriptblock] $CollectionFailureAction
    )

    $manifest = $null
    $scenarioFailure = $null
    $cleanupFailure = $null
    $collectionFailures = [System.Collections.Generic.List[string]]::new()
    try {
        $manifest = & $StartAction
        & $ScenarioAction $manifest | Out-Null
    }
    catch {
        $scenarioFailure = $_
        if ($null -eq $manifest -and $null -ne $ResolveFailedManifestAction) {
            try { $manifest = & $ResolveFailedManifestAction } catch { }
        }
        if ($null -ne $manifest -and $null -ne $FailureAction) {
            try { & $FailureAction $manifest $scenarioFailure | Out-Null } catch { }
        }
    }
    finally {
        if ($null -ne $manifest) {
            try { & $CollectAction $manifest | Out-Null }
            catch {
                $collectionFailures.Add("$($_.Exception.Message)")
                if ($null -ne $CollectionFailureAction) {
                    try { & $CollectionFailureAction $manifest $_ | Out-Null } catch { }
                }
            }
            try {
                $stopResult = & $StopAction $manifest
                if ($null -eq $stopResult -or -not [bool] $stopResult.Complete) {
                    throw 'Managed full-stack stop did not report complete cleanup.'
                }
                if ($null -ne $stopResult.Manifest) { $manifest = $stopResult.Manifest }
            }
            catch { $cleanupFailure = $_ }
        }
    }
    if ($cleanupFailure) { throw $cleanupFailure }
    if ($scenarioFailure) { throw $scenarioFailure }
    return [pscustomobject]@{ Manifest = $manifest; CollectionFailures = @($collectionFailures) }
}

function Get-NervFullStackEnvironment {
    param(
        [Parameter(Mandatory)]
        [string] $SessionId
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$SessionId'."
    }

    return @{
        NERV_IIP_EPHEMERAL = 'true'
        NERV_IIP_SESSION_ID = $SessionId
        ASPNETCORE_ENVIRONMENT = 'Development'
        DOTNET_ENVIRONMENT = 'Development'
        NERV_IIP_POSTGRES_VOLUME = "nerv-iip-postgres-18-$SessionId"
        NERV_IIP_REDIS_VOLUME = "nerv-iip-redis-$SessionId"
        NERV_IIP_MINIO_VOLUME = "nerv-iip-minio-$SessionId"
        NERV_IIP_VICTORIA_LOGS_VOLUME = "nerv-iip-victoria-logs-$SessionId"
    }
}

function New-NervFullStackSecretValue {
    param(
        [ValidateRange(16, 256)]
        [int] $Bytes = 32
    )

    $buffer = [Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes)
    try {
        return [Convert]::ToBase64String($buffer)
    }
    finally {
        [Array]::Clear($buffer, 0, $buffer.Length)
    }
}

function ConvertTo-NervBase64Url {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-NervFullStackSecretEnvironment {
    param(
        [Parameter(Mandatory)]
        [string] $SessionId
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$SessionId'."
    }

    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try {
        $parameters = $rsa.ExportParameters($false)
        $kid = "$SessionId-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
        $jwks = [ordered]@{
            keys = @([ordered]@{
                kty = 'RSA'
                use = 'sig'
                kid = $kid
                alg = 'RS256'
                n = ConvertTo-NervBase64Url -Bytes $parameters.Modulus
                e = ConvertTo-NervBase64Url -Bytes $parameters.Exponent
            })
        } | ConvertTo-Json -Compress -Depth 5
        $adminPassword = New-NervFullStackSecretValue -Bytes 24
        $environment = @{
            'Parameters__iam-jwt-signing-key-id' = $kid
            'Parameters__iam-jwt-private-key-pem' = $rsa.ExportPkcs8PrivateKeyPem()
            'Parameters__iam-jwt-jwks-json' = $jwks
            'Parameters__iam-secrets-pepper' = New-NervFullStackSecretValue -Bytes 48
            'Parameters__internal-service-bearer-token' = New-NervFullStackSecretValue -Bytes 48
            'Parameters__redis-password' = New-NervFullStackSecretValue -Bytes 24
            'Parameters__minio-root-user' = "nerv-$SessionId"
            'Parameters__minio-root-password' = New-NervFullStackSecretValue -Bytes 24
            'Parameters__iam-seed-admin-password' = $adminPassword
            'Parameters__iam-seed-connector-host-secret' = New-NervFullStackSecretValue -Bytes 32
            'Parameters__connector-ingestion-token-signing-key' = New-NervFullStackSecretValue -Bytes 48
        }

        return [pscustomobject]@{
            Environment = $environment
            AdminPassword = $adminPassword
        }
    }
    finally {
        $rsa.Dispose()
    }
}

function Get-NervDockerListedValues {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $Name
    )

    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 30 `
        -Name $Name

    return @($result.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-NervDockerInspectObjects {
    param(
        [Parameter(Mandatory)] [ValidateSet('container', 'network', 'volume')] [string] $Kind,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Identifiers,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($Identifiers.Count -eq 0) {
        return @()
    }

    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments (@($Kind, 'inspect') + $Identifiers) `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 30 `
        -Name $Name
    return @($result.Stdout | ConvertFrom-Json -Depth 30)
}

function Get-NervFullStackContainerRecords {
    param(
        [Parameter(Mandatory)] [string] $OwnedSessionId,
        [string] $WorkingDirectory = (Get-Location).Path
    )

    $ids = @(Get-NervDockerListedValues `
        -Arguments @('container', 'ls', '-a', '--no-trunc', '--filter', "label=com.nerv-iip.session=$OwnedSessionId", '--format', '{{.ID}}') `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$OwnedSessionId-container-discovery")
    $objects = @(Get-NervDockerInspectObjects `
        -Kind container `
        -Identifiers $ids `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$OwnedSessionId-container-discovery-inspect")
    return @($objects | ForEach-Object {
        $containerName = "$($_.Name)".TrimStart('/')
        $resourceName = @('postgres', 'redis', 'minio', 'victoria-logs') |
            Where-Object { $containerName.StartsWith("$_-", [StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($resourceName)) { $resourceName = $containerName }
        [ordered]@{
            resourceName = $resourceName
            id = "$($_.Id)"
            name = $containerName
        }
    })
}

function Merge-NervSessionContainerIds {
    param(
        [object[]] $RecordedIds = @(),
        [object[]] $DiscoveredRecords = @()
    )

    return @(
        @($RecordedIds | ForEach-Object { "$_" }) +
            @($DiscoveredRecords | ForEach-Object { "$($_.id)" }) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )
}

function Get-NervContainerNetworkIds {
    param([object[]] $Containers = @())

    return @(
        foreach ($container in $Containers) {
            $networks = $container.NetworkSettings.Networks
            if ($null -eq $networks) { continue }
            foreach ($property in $networks.PSObject.Properties) {
                $networkId = "$($property.Value.NetworkID)"
                if (-not [string]::IsNullOrWhiteSpace($networkId)) { $networkId }
            }
        }
    ) | Select-Object -Unique
}

function Get-NervSessionDockerResources {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = "$($Manifest.worktreeRoot)"
    )

    $sessionId = "$($Manifest.sessionId)"
    if ($sessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$sessionId'."
    }

    $recordedContainerIds = @($Manifest.runtime.containerIds | ForEach-Object { "$_" })
    $recordedNetworkIds = @($Manifest.runtime.networkIds | ForEach-Object { "$_" })
    $recordedVolumeNames = @($Manifest.runtime.volumeNames | ForEach-Object { "$_" })
    $unresolved = [System.Collections.Generic.List[string]]::new()
    $discoveredNetworkIds = @()

    try {
        $discoveredContainers = @(Get-NervFullStackContainerRecords -OwnedSessionId $sessionId -WorkingDirectory $WorkingDirectory)
        $candidateContainerIds = @(Merge-NervSessionContainerIds -RecordedIds $recordedContainerIds -DiscoveredRecords $discoveredContainers)
        $listedContainerIds = Get-NervDockerListedValues -Arguments @('container', 'ls', '-a', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-list"
        $presentContainerIds = @($candidateContainerIds | Where-Object { $listedContainerIds -ccontains $_ })
        $containerInspect = if ($presentContainerIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind container -Identifiers $presentContainerIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-inspect")
        }
        else { @() }
        $containers = @($containerInspect | Where-Object {
            Test-NervDockerResourceOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $candidateContainerIds
        })
        $discoveredNetworkIds = @(Get-NervContainerNetworkIds -Containers $containers)
        $ownedContainerIds = @($containers | ForEach-Object { "$($_.Id)" })
        foreach ($id in $presentContainerIds) {
            if ($ownedContainerIds -cnotcontains $id) { $unresolved.Add("container:$id") }
        }
    }
    catch {
        $containers = @()
        foreach ($id in $recordedContainerIds) { $unresolved.Add("container:$id") }
    }

    try {
        $candidateNetworkIds = @($recordedNetworkIds + $discoveredNetworkIds | Select-Object -Unique)
        $listedNetworkIds = Get-NervDockerListedValues -Arguments @('network', 'ls', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-list"
        $presentNetworkIds = @($candidateNetworkIds | Where-Object { $listedNetworkIds -ccontains $_ })
        $networkInspect = if ($presentNetworkIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind network -Identifiers $presentNetworkIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-inspect")
        }
        else { @() }
        $networks = @($networkInspect | Where-Object {
            $candidateNetworkIds -ccontains "$($_.Id)" -and
                (Test-NervDockerOptionalSessionLabel -Labels $_.Labels -SessionId $sessionId)
        })
        $ownedNetworkIds = @($networks | ForEach-Object { "$($_.Id)" })
        foreach ($id in $presentNetworkIds) {
            if ($ownedNetworkIds -cnotcontains $id) { $unresolved.Add("network:$id") }
        }
    }
    catch {
        $networks = @()
        foreach ($id in $recordedNetworkIds) { $unresolved.Add("network:$id") }
    }

    try {
        $listedVolumeNames = Get-NervDockerListedValues -Arguments @('volume', 'ls', '--format', '{{.Name}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-volume-list"
        $presentVolumeNames = @($recordedVolumeNames | Where-Object { $listedVolumeNames -ccontains $_ })
        $volumeInspect = if ($presentVolumeNames.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind volume -Identifiers $presentVolumeNames -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-volume-inspect")
        }
        else { @() }
        $volumes = @($volumeInspect | Where-Object {
            $name = "$($_.Name)"
            (Test-NervDockerRecordedNameOwnership -Name $name -SessionId $sessionId -RecordedNames $recordedVolumeNames) -and
                (Test-NervDockerOptionalSessionLabel -Labels $_.Labels -SessionId $sessionId)
        })
        $ownedVolumeNames = @($volumes | ForEach-Object { "$($_.Name)" })
        foreach ($name in $presentVolumeNames) {
            if ($ownedVolumeNames -cnotcontains $name) { $unresolved.Add("volume:$name") }
        }
    }
    catch {
        $volumes = @()
        foreach ($name in $recordedVolumeNames) { $unresolved.Add("volume:$name") }
    }

    return [pscustomobject]@{
        Containers = @($containers)
        Networks = @($networks)
        Volumes = @($volumes)
        Unresolved = @($unresolved | Select-Object -Unique)
    }
}

function Remove-NervSessionDockerResources {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = "$($Manifest.worktreeRoot)",
        [ValidateRange(1, 600)] [int] $TimeoutSeconds = 120
    )

    $sessionId = "$($Manifest.sessionId)"
    $resources = Get-NervSessionDockerResources -Manifest $Manifest -WorkingDirectory $WorkingDirectory
    $unresolved = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $resources.Unresolved) { $unresolved.Add("$item") }

    $removals = @(
        [pscustomobject]@{ Kind = 'container'; Values = @($resources.Containers | ForEach-Object { "$($_.Id)" }); Arguments = @('container', 'rm', '-f') },
        [pscustomobject]@{ Kind = 'network'; Values = @($resources.Networks | ForEach-Object { "$($_.Id)" }); Arguments = @('network', 'rm') },
        [pscustomobject]@{ Kind = 'volume'; Values = @($resources.Volumes | ForEach-Object { "$($_.Name)" }); Arguments = @('volume', 'rm') }
    )
    foreach ($removal in $removals) {
        if ($removal.Values.Count -eq 0) { continue }
        try {
            Invoke-NativeCommandWithTimeout `
                -Command 'docker' `
                -Arguments ($removal.Arguments + $removal.Values) `
                -WorkingDirectory $WorkingDirectory `
                -TimeoutSeconds $TimeoutSeconds `
                -Name "fullstack-$sessionId-$($removal.Kind)-remove" | Out-Null
        }
        catch {
            foreach ($value in $removal.Values) { $unresolved.Add("$($removal.Kind):$value") }
        }
    }

    return [pscustomobject]@{
        Complete = $unresolved.Count -eq 0
        Remaining = @($unresolved | Select-Object -Unique)
    }
}

function Invoke-NervDockerCleanupWithRetry {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [scriptblock] $RemoveAction,
        [ValidateRange(1, 10)] [int] $MaximumAttempts = 5,
        [scriptblock] $DelayAction
    )

    if ($null -eq $DelayAction) {
        $DelayAction = { param($Attempt) Start-Sleep -Seconds 3 }
    }

    $result = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $result = & $RemoveAction $Manifest
        if ($null -eq $result) { throw 'Docker cleanup action returned no result.' }
        $remaining = @($result.Remaining)
        if ([bool] $result.Complete -and $remaining.Count -eq 0) { return $result }
        if ($attempt -lt $MaximumAttempts) { & $DelayAction $attempt }
    }
    return $result
}

function Invoke-NervAspireStartWithRetry {
    param(
        [Parameter(Mandatory)] [scriptblock] $StartAction,
        [Parameter(Mandatory)] [scriptblock] $CleanupAction,
        [ValidateRange(1, 3)] [int] $MaximumAttempts = 2,
        [scriptblock] $DelayAction
    )

    if ($null -eq $DelayAction) {
        $DelayAction = { param($Attempt) Start-Sleep -Seconds 5 }
    }

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try { return (& $StartAction) }
        catch {
            $message = "$($_.Exception.Message)"
            $isTransientBuildResourceFailure = $message -match 'MSB4166|system resource|系统资源不足'
            if (-not $isTransientBuildResourceFailure -or $attempt -ge $MaximumAttempts) { throw }
            & $CleanupAction
            & $DelayAction $attempt
        }
    }
}

function Stop-NervWorktreeProcesses {
    param(
        [Parameter(Mandatory)] [string] $WorktreeRoot,
        [int[]] $ExcludedProcessIds = @(),
        [scriptblock] $ProcessQueryAction,
        [scriptblock] $StopAction
    )

    if ($null -eq $ProcessQueryAction) {
        $ProcessQueryAction = {
            if (-not $IsWindows) { return @() }
            return @(Get-CimInstance Win32_Process | Select-Object ProcessId, Name, CommandLine)
        }
    }
    if ($null -eq $StopAction) {
        $StopAction = {
            param($ProcessId, $Reason)
            Stop-ProcessTree -ProcessId $ProcessId -Reason $Reason | Out-Null
        }
    }

    $normalizedRoot = [System.IO.Path]::GetFullPath($WorktreeRoot).Replace('/', '\').TrimEnd('\') + '\'
    $excluded = @($ExcludedProcessIds | ForEach-Object { [int] $_ })
    $allowedProcessNames = @('dotnet', 'dotnet.exe', 'node', 'node.exe', 'aspire', 'aspire.exe', 'dcp', 'dcp.exe')
    $stopped = [System.Collections.Generic.List[int]]::new()
    foreach ($process in @(& $ProcessQueryAction)) {
        $processId = [int] $process.ProcessId
        if ($processId -le 0 -or $excluded -contains $processId) { continue }
        if ($allowedProcessNames -cnotcontains "$($process.Name)".ToLowerInvariant()) { continue }
        $commandLine = "$($process.CommandLine)".Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($commandLine) -or -not $commandLine.Contains($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
        & $StopAction $processId "Exact worktree process cleanup for $normalizedRoot"
        $stopped.Add($processId)
    }

    return [pscustomobject]@{ StoppedProcessIds = @($stopped) }
}

function Stop-NervFullStackSession {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [scriptblock] $AspireStopAction,
        [scriptblock] $ProcessStopAction,
        [scriptblock] $DockerRemoveAction
    )

    if ($null -eq $AspireStopAction) {
        $AspireStopAction = {
            param($Manifest)
            Invoke-AspireOutput `
                -Arguments @('stop', '--apphost', "$($Manifest.appHostProject)", '--non-interactive', '--nologo') `
                -WorkingDirectory "$($Manifest.worktreeRoot)" `
                -TimeoutSeconds 150 `
                -Name "fullstack-$($Manifest.sessionId)-aspire-stop" | Out-Null
        }
    }
    if ($null -eq $ProcessStopAction) {
        $ProcessStopAction = {
            param($Manifest)
            $guardianPid = if ($null -ne $Manifest.guardian) { $Manifest.guardian.pid } else { $null }
            $guardianStarted = if ($null -ne $Manifest.guardian) { $Manifest.guardian.processStartTimeUtc } else { $null }
            $identities = @(
                [pscustomobject]@{ Pid = $Manifest.aspire.appHostPid; Started = $Manifest.aspire.appHostProcessStartTimeUtc },
                [pscustomobject]@{ Pid = $Manifest.aspire.cliPid; Started = $Manifest.aspire.cliProcessStartTimeUtc },
                [pscustomobject]@{ Pid = $guardianPid; Started = $guardianStarted }
            )
            $callerGuardianPid = 0
            [void] [int]::TryParse($env:NERV_IIP_FULLSTACK_CALLER_GUARDIAN_PID, [ref] $callerGuardianPid)
            foreach ($identity in $identities) {
                if ($null -eq $identity.Pid -or [string]::IsNullOrWhiteSpace("$($identity.Started)")) { continue }
                $processId = [int] $identity.Pid
                if ($processId -eq $PID -or $processId -eq $callerGuardianPid) { continue }
                if (Test-NervProcessIdentity -ProcessId $processId -ProcessStartTimeUtc $identity.Started) {
                    Stop-ProcessTree -ProcessId $processId -Reason "Exact full-stack session stop for $($Manifest.sessionId)" | Out-Null
                }
            }
            Stop-NervWorktreeProcesses `
                -WorktreeRoot "$($Manifest.worktreeRoot)" `
                -ExcludedProcessIds @($PID, $callerGuardianPid) | Out-Null
        }
    }
    if ($null -eq $DockerRemoveAction) {
        $DockerRemoveAction = {
            param($Manifest)
            Remove-NervSessionDockerResources -Manifest $Manifest -WorkingDirectory "$($Manifest.worktreeRoot)"
        }
    }

    $manifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
    $wasStopped = "$($manifest.state)" -eq 'Stopped'
    if (-not $wasStopped) {
        $manifest = Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
            $lockedManifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
            if ("$($lockedManifest.state)" -ne 'Stopped' -and "$($lockedManifest.state)" -ne 'Stopping') {
                $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State Stopping
                Write-NervFullStackManifest -Manifest $lockedManifest -StateRoot $StateRoot
            }
            return $lockedManifest
        }
        $wasStopped = "$($manifest.state)" -eq 'Stopped'
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $cleanupFailures = [System.Collections.Generic.List[string]]::new()
    if (-not $wasStopped) {
        try { & $AspireStopAction $manifest } catch {
            $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
            $cleanupFailures.Add('aspire:stop-failed')
        }
        try { & $ProcessStopAction $manifest } catch {
            $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
            $cleanupFailures.Add('process:stop-failed')
        }
    }

    try {
        $dockerResult = Invoke-NervDockerCleanupWithRetry -Manifest $manifest -RemoveAction $DockerRemoveAction
    }
    catch {
        $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
        $dockerResult = [pscustomobject]@{ Complete = $false; Remaining = @('docker:inspection-failed') }
    }
    $remaining = @(
        @($cleanupFailures) + @($dockerResult.Remaining | ForEach-Object { "$_" }) |
            Select-Object -Unique
    )
    $complete = $errors.Count -eq 0 -and [bool] $dockerResult.Complete -and $remaining.Count -eq 0

    $manifest = Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
        $lockedManifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
        $lockedManifest.cleanup.remaining = @($remaining)
        $lockedManifest.cleanup.errors = @($lockedManifest.cleanup.errors) + @($errors)
        if ($complete) {
            $lockedManifest.cleanup.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            if ("$($lockedManifest.state)" -eq 'Stopping') {
                $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State Stopped
            }
        }
        elseif ("$($lockedManifest.state)" -eq 'Stopping') {
            $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State CleanupFailed
        }
        Write-NervFullStackManifest -Manifest $lockedManifest -StateRoot $StateRoot
        return $lockedManifest
    }

    return [pscustomobject]@{
        Complete = $complete
        Remaining = @($remaining)
        Errors = @($errors)
        Manifest = $manifest
    }
}
