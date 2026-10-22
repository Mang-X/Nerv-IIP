# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts short-lived synthetic Docker command probes
#   Writes:
#     - Temporary probe script and marker files under the system temporary directory
#   Cleanup:
#     - Removes the synthetic probe directory and restores process environment variables
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-worker-boundary-$([guid]::NewGuid().ToString('N'))"
$dockerPath = Join-Path $probeRoot 'docker'
$markerPath = Join-Path $probeRoot 'marker.log'
$sessionId = 'nerv-abcd-123456'
$volumeName = "probe-volume-$sessionId"
$dockerScript = @'
#!/bin/sh
set -eu
marker="${NERV_FULLSTACK_WORKER_PROBE_MARKER:?}"
if printenv 'NERV_IIP_LEADER_DEMO_WORKER_PASSWORD' >/dev/null 2>&1 || printenv 'Parameters__iam-seed-demo-worker-password' >/dev/null 2>&1; then
  printf '%s\n' 'worker-inherited' >> "$marker"
  exit 19
fi
printf 'child-ok:%s\n' "$*" >> "$marker"

if [ "${NERV_FULLSTACK_WORKER_PROBE_MAN:-}" = '1' ] && [ "${1:-}" = 'container' ] && [ "${2:-}" = 'inspect' ]; then
  cat <<'JSON'
[ {"Id":"probe-postgres","Name":"/postgres-probe","Config":{"Image":"postgres:18","Env":["POSTGRES_USER=postgres","POSTGRES_PASSWORD=synthetic-postgres"]},"NetworkSettings":{"Ports":{"5432/tcp":[{"HostPort":"15432"}]}},"Labels":{"com.nerv-iip.session":"nerv-abcd-123456"}},{"Id":"probe-redis","Name":"/redis-probe","Path":"redis-server","Args":["--requirepass","synthetic-redis"],"Config":{"Image":"redis:8","Env":["REDIS_PASSWORD=synthetic-redis"],"Entrypoint":[],"Cmd":["redis-server","--requirepass","synthetic-redis"]},"NetworkSettings":{"Ports":{"6379/tcp":[{"HostPort":"16379"}]}},"Labels":{"com.nerv-iip.session":"nerv-abcd-123456"}}]
JSON
  exit 0
fi

case "${1:-}:${2:-}" in
  container:ls) printf '%s\n' 'probe-container-id' ;;
  network:ls) printf '%s\n' 'probe-network-id' ;;
  volume:ls) printf '%s\n' 'probe-volume-nerv-abcd-123456' ;;
  container:inspect)
    cat <<'JSON'
[ {"Id":"probe-container-id","Name":"/postgres-probe","Config":{"Image":"postgres:18","Env":[]},"NetworkSettings":{"Networks":{"session":{"NetworkID":"probe-network-id"}}},"Labels":{"com.nerv-iip.session":"nerv-abcd-123456"}}]
JSON
    ;;
  network:inspect)
    cat <<'JSON'
[{"Id":"probe-network-id","Name":"aspire-session-network-probe","Containers":{"probe-container-id":{}}}]
JSON
    ;;
  volume:inspect)
    cat <<'JSON'
[{"Name":"probe-volume-nerv-abcd-123456","Labels":{"com.nerv-iip.session":"nerv-abcd-123456"}}]
JSON
    ;;
esac
exit 0
'@

$workerProbeToken = [string]::Concat('worker', '-probe', '-sentinel')
$workerEnvironmentNames = @('NERV_IIP_LEADER_DEMO_WORKER_PASSWORD', 'Parameters__iam-seed-demo-worker-password')
$originalPath = [Environment]::GetEnvironmentVariable('PATH', 'Process')
$originalMarker = [Environment]::GetEnvironmentVariable('NERV_FULLSTACK_WORKER_PROBE_MARKER', 'Process')
$originalManMode = [Environment]::GetEnvironmentVariable('NERV_FULLSTACK_WORKER_PROBE_MAN', 'Process')
$originalMessagingProvider = [Environment]::GetEnvironmentVariable('Messaging__Provider', 'Process')
try {
    [IO.Directory]::CreateDirectory($probeRoot) | Out-Null
    [IO.File]::WriteAllText($dockerPath, $dockerScript, [Text.UTF8Encoding]::new($false))
    [IO.File]::SetUnixFileMode(
        $dockerPath,
        [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
    )
    Set-Item -LiteralPath 'Env:NERV_FULLSTACK_WORKER_PROBE_MARKER' -Value $markerPath
    Set-Item -LiteralPath 'Env:PATH' -Value "$probeRoot$([IO.Path]::PathSeparator)$originalPath"
    foreach ($name in $workerEnvironmentNames) { Set-Item -LiteralPath "Env:$name" -Value $workerProbeToken }

    $nonSeedEnvironment = Get-NervFullStackNonSeedEnvironment
    $manifest = [pscustomobject]@{
        sessionId = $sessionId
        worktreeRoot = $repoRoot
        appHostProject = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
        artifactPath = $probeRoot
        state = 'Running'
        runtime = [pscustomobject]@{
            containerIds = @('probe-container-id')
            networkIds = @('probe-network-id')
            volumeNames = @($volumeName)
            messagingProvider = 'Redis'
        }
        endpoints = [pscustomobject]@{}
    }

    $records = @(Get-NervFullStackContainerRecords -OwnedSessionId $sessionId -WorkingDirectory $repoRoot -Environment $nonSeedEnvironment)
    Assert-True ($records.Count -eq 1 -and [string]::Equals([string]$records[0].id, 'probe-container-id', [StringComparison]::Ordinal)) 'Public container discovery must execute a real child with the non-seed environment.'
    $resources = Get-NervSessionDockerResources -Manifest $manifest -WorkingDirectory $repoRoot -Environment $nonSeedEnvironment
    Assert-True ($resources.Unresolved.Count -eq 0) "Public Docker resource collection must complete through non-seed child probes; unresolved=$($resources.Unresolved -join ',')."
    $status = Get-NervFullStackStatusSummary -Manifest $manifest -WorkingDirectory $repoRoot -Environment $nonSeedEnvironment
    Assert-True ($status.ContainerCount -eq 1) 'Public status must use the non-seed Docker child boundary.'
    $remove = Remove-NervSessionDockerResources -Manifest $manifest -WorkingDirectory $repoRoot -TimeoutSeconds 10 -Environment $nonSeedEnvironment
    Assert-True ([bool]$remove.Complete) 'Public Docker cleanup must use the non-seed child boundary.'

    $stateRoot = Join-Path $probeRoot 'state'
    $storedManifest = New-NervFullStackManifest `
        -SessionId $sessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject $manifest.appHostProject `
        -ArtifactPath $probeRoot `
        -StateRoot $stateRoot
    $storedManifest.state = 'Running'
    $storedManifest.runtime.containerIds = @('probe-container-id')
    $storedManifest.runtime.networkIds = @('probe-network-id')
    $storedManifest.runtime.volumeNames = @($volumeName)
    Write-NervFullStackManifest -Manifest $storedManifest -StateRoot $stateRoot
    $stopped = Stop-NervFullStackSession `
        -SessionId $sessionId `
        -StateRoot $stateRoot `
        -AspireStopAction { param($InputManifest) } `
        -ProcessStopAction { param($InputManifest) } `
        -Environment $nonSeedEnvironment
    Assert-True ([bool]$stopped.Complete) 'Public stale/stop cleanup must use the non-seed Docker child boundary.'

    Set-Item -LiteralPath 'Env:NERV_FULLSTACK_WORKER_PROBE_MAN' -Value '1'
    Set-Item -LiteralPath 'Env:Messaging__Provider' -Value 'Redis'
    . (Join-Path $repoRoot 'scripts/fullstack-session.ps1') -Action help | Out-Null
    function Wait-NervAspireResource { param($AppHostProject, $ResourceName, $WorkingDirectory) }
    function Wait-NervFullStackPostgresRelations { param($ContainerId, $Database, $Relations) }
    function Invoke-DotNet { param($Arguments, $WorkingDirectory, $TimeoutSeconds, $Name) }
    $manManifest = [pscustomobject]@{
        sessionId = $sessionId
        appHostProject = $manifest.appHostProject
        worktreeRoot = $repoRoot
        runtime = [pscustomobject]@{ containerIds = @('probe-postgres', 'probe-redis'); messagingProvider = 'Redis' }
    }
    Invoke-NervMan528MesInventoryAcceptance -Manifest $manManifest
    Invoke-NervMan440RuntimeHoursAcceptance -Manifest $manManifest

    $probeText = Get-Content -LiteralPath $markerPath -Raw
    Assert-True (-not $probeText.Contains('worker-inherited', [StringComparison]::Ordinal)) 'Public cleanup, stale/stop, and MAN Docker children must not inherit either worker secret name.'
    Assert-True ($probeText.Contains('child-ok:container inspect', [StringComparison]::Ordinal)) 'MAN Docker acceptance must execute a real child probe through the explicit non-seed environment.'
}
finally {
    foreach ($name in $workerEnvironmentNames) {
        Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
    }
    if ($null -eq $originalPath) { Remove-Item -LiteralPath 'Env:PATH' -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath 'Env:PATH' -Value $originalPath }
    if ($null -eq $originalMarker) { Remove-Item -LiteralPath 'Env:NERV_FULLSTACK_WORKER_PROBE_MARKER' -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath 'Env:NERV_FULLSTACK_WORKER_PROBE_MARKER' -Value $originalMarker }
    if ($null -eq $originalManMode) { Remove-Item -LiteralPath 'Env:NERV_FULLSTACK_WORKER_PROBE_MAN' -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath 'Env:NERV_FULLSTACK_WORKER_PROBE_MAN' -Value $originalManMode }
    if ($null -eq $originalMessagingProvider) { Remove-Item -LiteralPath 'Env:Messaging__Provider' -ErrorAction SilentlyContinue } else { Set-Item -LiteralPath 'Env:Messaging__Provider' -Value $originalMessagingProvider }
    Remove-Item -LiteralPath $probeRoot -Recurse -Force -ErrorAction SilentlyContinue
}
