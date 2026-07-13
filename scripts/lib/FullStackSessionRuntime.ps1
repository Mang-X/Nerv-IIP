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

    try {
        $listedContainerIds = Get-NervDockerListedValues -Arguments @('container', 'ls', '-a', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-list"
        $presentContainerIds = @($recordedContainerIds | Where-Object { $listedContainerIds -ccontains $_ })
        $containerInspect = if ($presentContainerIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind container -Identifiers $presentContainerIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-inspect")
        }
        else { @() }
        $containers = @($containerInspect | Where-Object {
            Test-NervDockerResourceOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $recordedContainerIds
        })
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
        $listedNetworkIds = Get-NervDockerListedValues -Arguments @('network', 'ls', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-list"
        $presentNetworkIds = @($recordedNetworkIds | Where-Object { $listedNetworkIds -ccontains $_ })
        $networkInspect = if ($presentNetworkIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind network -Identifiers $presentNetworkIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-inspect")
        }
        else { @() }
        $networks = @($networkInspect | Where-Object {
            Test-NervDockerResourceOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $recordedNetworkIds
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
    if (-not $wasStopped) {
        try { & $AspireStopAction $manifest } catch { $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)")) }
        try { & $ProcessStopAction $manifest } catch { $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)")) }
    }

    try {
        $dockerResult = & $DockerRemoveAction $manifest
        if ($null -eq $dockerResult) { throw 'Docker cleanup action returned no result.' }
    }
    catch {
        $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
        $dockerResult = [pscustomobject]@{ Complete = $false; Remaining = @('docker:inspection-failed') }
    }
    $remaining = @($dockerResult.Remaining | ForEach-Object { "$_" } | Select-Object -Unique)
    $complete = [bool] $dockerResult.Complete -and $remaining.Count -eq 0

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
