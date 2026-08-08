# Backend test shard timing cache (#1507).
#
# THE BOUNDARY THIS FILE EXISTS TO DRAW
#
# MAN-661 put two different kinds of thing into one governed file: a *policy* list (which skips are
# registered, which quarantines are legal, which determinism debts are owed) and a *measurement*
# (how long each assembly took). Policy is a governed asset — a human wrote it down and a gate must
# hold them to it. A measurement is not: nobody decides how fast a test suite is, they observe it.
#
# Governing the measurement produced the failure this file removes. The committed evidence snapshot
# keyed timing rows on `lane + assembly`, so a pure "how we run the tests" change — MAN-663 changing
# the shared host, MAN-669 re-homing assemblies between shards — invalidated keys that no test had
# touched. 17 of 64 backend assemblies lost their key that way and a human had to re-generate and
# re-commit a snapshot to clear it. That ceremony is the bug.
#
# Every mature implementation of this treats timings as a cache, not an asset: CircleCI
# `--split-by=timings`, Jest `--shard`, pytest-split and Knapsack all read the *last successful
# run's* artifact, and a stale or missing entry only makes the split slightly uneven — it never
# fails a build. Conversely, the lists that genuinely are governed (Chromium test expectations,
# Kubernetes flaky-test quarantine) key on test name or path, never on the runner topology, so a
# re-shard cannot lose a key.
#
# So, in this repository:
#
#   * Timing is a **cache**: aggregated automatically from recent successful `main` runs, stored
#     under the gitignored `artifacts/` tree, never committed, never hashed, never a gate. Missing
#     data degrades to a report-only warning plus a fallback estimate.
#   * Timing keys on **assembly only**. Lane is recorded for display and provenance and never
#     participates in a lookup, so a shard rearrangement can no longer lose a key.
#   * Policy stays governed and keeps its hard gates (`unregistered-skip`, `illegal-quarantine`,
#     `zero-execution`), keyed on test full name / source path — dimensions a re-shard cannot move.
#
# Narrative: docs/architecture/test-evidence-governance.md ("Timing data is a cache, not a governed
# asset"). Entry points: scripts/update-backend-test-shard-timings.ps1 (refresh the cache) and
# scripts/report-backend-test-shard-balance.ps1 (report-only balance).

Set-Variable -Name NervShardTimingCacheSchemaVersion -Value 1 -Scope Script -Force
Set-Variable -Name NervShardTimingDurationMetric -Value 'trx-elapsed' -Scope Script -Force
Set-Variable -Name NervShardTimingStatistic -Value 'median' -Scope Script -Force

# Why five runs: hosted-runner variance on the *same* commit is tens of percent and moves which
# shard tops the list (measured across runs 31114441118 / 31115903098 / 31116998822 — see
# docs/architecture/test-evidence-governance.md, "What the step budgets are"). One run is therefore
# not a measurement, it is a sample. Five independent samples let the median survive a single
# noisy-neighbour or mid-rollout runner image without needing outlier rules, and at the repository's
# `main` push rate five runs is normally well inside the 14-day artifact retention, so the window is
# recent as well as robust. It is a default, not a constant: callers may pass -RunCount.
Set-Variable -Name NervShardTimingDefaultRunCount -Value 5 -Scope Script -Force

# A cache older than this is refreshed opportunistically before it is read. Nothing fails when the
# refresh cannot happen (no gh, no token, no network) — the stale cache is used and the report says
# so. One day is short enough that a re-homed assembly gets a real measurement quickly and long
# enough that repeated local runs do not re-download the same artifacts.
Set-Variable -Name NervShardTimingDefaultMaxCacheAgeHours -Value 24 -Scope Script -Force

# Used only when *nothing* is known: no cache, no fallback snapshot, not one measured assembly.
# Deliberately a plausible mid-sized assembly rather than 0 (which would silently under-weight the
# shard that owns the unknown work) or the maximum (which would make the balancer panic).
Set-Variable -Name NervShardTimingLastResortEstimateMilliseconds -Value 5000.0 -Scope Script -Force

function Get-NervShardTimingAssemblyKey {
    <#
        Normalizes a project path, assembly file name, or bare assembly name to the single timing
        key. `ToLowerInvariant` rather than a case-insensitive comparison at every call site: VSTest
        writes the `storage` attribute lower-cased while the file on disk is Pascal-cased, so the two
        sources must be folded once, here, and every downstream comparison stays ordinal.
    #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Name)

    $trimmed = ([string] $Name).Trim() -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($trimmed)) { return '' }

    $leaf = $trimmed.Substring($trimmed.LastIndexOf('/') + 1)
    foreach ($extension in @('.csproj', '.dll')) {
        if ($leaf.EndsWith($extension, [StringComparison]::OrdinalIgnoreCase)) {
            $leaf = $leaf.Substring(0, $leaf.Length - $extension.Length)
            break
        }
    }

    return ($leaf.ToLowerInvariant() + '.dll')
}

function Get-NervShardTimingMedian {
    <#
        Median, not mean: runner-image rollouts and noisy neighbours produce one-sided outliers, and
        a mean lets a single 3x run move a shard's whole budget. Even counts average the two middle
        values so two samples still produce a usable number.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [double[]] $Values)

    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double] $sorted[$middle] }
    return [double] (([double] $sorted[$middle - 1] + [double] $sorted[$middle]) / 2.0)
}

function Get-NervShardTimingRowsFromEvidenceSummary {
    <#
        Reads one MAN-661 `summary.json` (or one committed evidence snapshot) into keyed timing rows.
        `lane` is carried through for provenance only and is never part of the key.
    #>
    param([Parameter(Mandatory)] [AllowNull()] [object] $Summary)

    if ($null -eq $Summary -or -not ($Summary.PSObject.Properties.Name -contains 'assemblies')) { return @() }

    return @(
        foreach ($assembly in @($Summary.assemblies)) {
            $key = Get-NervShardTimingAssemblyKey -Name ([string] $assembly.assembly)
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $elapsed = 0.0
            if (-not [double]::TryParse([string] $assembly.elapsedMilliseconds, [ref] $elapsed)) { continue }
            # A non-positive duration is not an observation. Keeping it would let a run that failed
            # to record elapsed time pull an assembly's median toward zero, which is exactly the
            # direction that hides a slow shard.
            if ($elapsed -le 0) { continue }
            [pscustomobject][ordered]@{
                assembly = $key
                lane = [string] $assembly.lane
                elapsedMilliseconds = $elapsed
            }
        }
    )
}

function Merge-NervShardTimingObservations {
    <#
        Aggregation contract, stated once so the scripts do not each invent one:

          * an observation is (runId, assembly, elapsedMilliseconds) taken from that run's retained
            TRX evidence, using the same `trx-elapsed` metric the collector reports;
          * observations of one assembly inside one run are summed first (an assembly classified into
            two lanes would otherwise be counted as two independent samples of half the work);
          * the per-assembly statistic across runs is the median.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Observations)

    $perRun = @{}
    $lanes = @{}
    foreach ($observation in @($Observations)) {
        $key = [string] $observation.assembly
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        $runId = [string] $observation.runId
        if (-not $perRun.ContainsKey($key)) { $perRun[$key] = @{} }
        if (-not $perRun[$key].ContainsKey($runId)) { $perRun[$key][$runId] = 0.0 }
        $perRun[$key][$runId] = [double] $perRun[$key][$runId] + [double] $observation.elapsedMilliseconds
        if (-not [string]::IsNullOrWhiteSpace([string] $observation.lane)) { $lanes[$key] = [string] $observation.lane }
    }

    return @(
        foreach ($key in @($perRun.Keys | Sort-Object)) {
            $values = [double[]] @($perRun[$key].Values | ForEach-Object { [double] $_ })
            [pscustomobject][ordered]@{
                assembly = $key
                elapsedMilliseconds = [Math]::Round((Get-NervShardTimingMedian -Values $values), 4)
                observationCount = $values.Count
                lastObservedLane = if ($lanes.ContainsKey($key)) { [string] $lanes[$key] } else { '' }
            }
        }
    )
}

function New-NervShardTimingCache {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Observations,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Runs,
        [DateTimeOffset] $GeneratedAtUtc = [DateTimeOffset]::UtcNow
    )

    return [pscustomobject][ordered]@{
        schemaVersion = $script:NervShardTimingCacheSchemaVersion
        kind = 'backend-test-shard-timing-cache'
        # Stated in the file itself so a reader who finds a stale copy on disk knows what it is
        # allowed to do with it. There is no hash, no owner and no expiry date by design.
        enforcement = 'report-only'
        governed = $false
        key = 'assembly'
        durationMetric = $script:NervShardTimingDurationMetric
        statistic = $script:NervShardTimingStatistic
        generatedAtUtc = $GeneratedAtUtc.UtcDateTime.ToString('o')
        runs = @($Runs)
        assemblies = @(Merge-NervShardTimingObservations -Observations $Observations)
    }
}

function Import-NervShardTimingCache {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $cache = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        # A corrupt cache is a cache miss, never a failure: it is regenerated on the next refresh.
        Write-Diagnostic -Level 'WARN' -Message "Shard timing cache is unreadable and will be ignored: $(Protect-ScriptAutomationText $_.Exception.Message)"
        return $null
    }

    $schemaVersion = 0
    if (-not ($cache.PSObject.Properties.Name -contains 'schemaVersion') -or
        -not [int]::TryParse([string] $cache.schemaVersion, [ref] $schemaVersion) -or
        $schemaVersion -ne $script:NervShardTimingCacheSchemaVersion) {
        Write-Diagnostic -Level 'WARN' -Message 'Shard timing cache has an unsupported schemaVersion and will be ignored.'
        return $null
    }

    return $cache
}

function ConvertTo-NervShardTimingTimestampText {
    <#
        `ConvertFrom-Json` turns an ISO-8601 string into a `DateTime`, and `[string]` on a `DateTime`
        renders it in the *current culture* — the same class of bug the evidence governance document
        spends a section on. Round-trip format under the invariant culture keeps the reported
        provenance stable no matter which machine reads the cache.
    #>
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [DateTime]) { return ([DateTime] $Value).ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture) }
    if ($Value -is [DateTimeOffset]) { return ([DateTimeOffset] $Value).UtcDateTime.ToString('o', [Globalization.CultureInfo]::InvariantCulture) }
    return [string] $Value
}

function Get-NervShardTimingLookup {
    <#
        Builds the assembly -> milliseconds lookup that the balance report consumes, preferring the
        auto-refreshed cache and falling back to the last committed evidence snapshot. Both sources
        are keyed by assembly alone, so neither can lose a key to a shard rearrangement.
    #>
    param(
        [string] $CachePath,
        [string] $FallbackEvidencePath
    )

    $rows = @{}
    $source = 'none'
    $sourceDetail = ''
    $generatedAtUtc = $null

    $cache = if ([string]::IsNullOrWhiteSpace($CachePath)) { $null } else { Import-NervShardTimingCache -Path $CachePath }
    if ($null -ne $cache -and @($cache.assemblies).Count -gt 0) {
        foreach ($row in @($cache.assemblies)) {
            $key = Get-NervShardTimingAssemblyKey -Name ([string] $row.assembly)
            if (-not [string]::IsNullOrWhiteSpace($key)) { $rows[$key] = [double] $row.elapsedMilliseconds }
        }
        $source = 'main-run-evidence-cache'
        $sourceDetail = "$(@($cache.runs).Count) successful main run(s), statistic=$([string] $cache.statistic)"
        $generatedAtUtc = ConvertTo-NervShardTimingTimestampText -Value $cache.generatedAtUtc
    }
    elseif (-not [string]::IsNullOrWhiteSpace($FallbackEvidencePath) -and (Test-Path -LiteralPath $FallbackEvidencePath -PathType Leaf)) {
        # Offline / no-token degradation path. The committed snapshot is the *fallback*, never the
        # authority, and it is deliberately not required to be fresh or complete: whatever it does
        # not cover is estimated and reported, and nothing fails.
        try {
            $snapshot = Get-Content -LiteralPath $FallbackEvidencePath -Raw | ConvertFrom-Json
            foreach ($row in @(Get-NervShardTimingRowsFromEvidenceSummary -Summary $snapshot)) {
                if ($rows.ContainsKey([string] $row.assembly)) {
                    $rows[[string] $row.assembly] = [double] $rows[[string] $row.assembly] + [double] $row.elapsedMilliseconds
                }
                else {
                    $rows[[string] $row.assembly] = [double] $row.elapsedMilliseconds
                }
            }
            if ($rows.Count -gt 0) {
                $source = 'committed-evidence-snapshot'
                $sourceDetail = ([System.IO.Path]::GetFileName($FallbackEvidencePath))
                if ($snapshot.PSObject.Properties.Name -contains 'generatedAtUtc') { $generatedAtUtc = ConvertTo-NervShardTimingTimestampText -Value $snapshot.generatedAtUtc }
            }
        }
        catch {
            Write-Diagnostic -Level 'WARN' -Message "Fallback evidence snapshot is unreadable and will be ignored: $(Protect-ScriptAutomationText $_.Exception.Message)"
        }
    }

    return [pscustomobject][ordered]@{
        source = $source
        sourceDetail = $sourceDetail
        generatedAtUtc = $generatedAtUtc
        rows = $rows
    }
}

function Get-NervShardBalanceReport {
    <#
        Report-only shard balance. Never throws on missing timing data and never returns a failure
        signal: an assembly with no observation gets an estimate and a warning.

        Fallback estimate, in order:
          1. the median of the *same shard's* measured assemblies — a new project usually resembles
             its neighbours more than it resembles the repository average;
          2. the median of every measured assembly, when the shard has no measured assembly at all;
          3. a fixed last-resort value, when nothing anywhere has been measured.
    #>
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $Timings
    )

    $rows = $Timings.rows
    $allMeasured = [double[]] @($rows.Values | ForEach-Object { [double] $_ })
    $globalMedian = Get-NervShardTimingMedian -Values $allMeasured
    $warnings = [System.Collections.Generic.List[object]]::new()

    if ($rows.Count -eq 0) {
        $warnings.Add([pscustomobject][ordered]@{
            code = 'timing-source-unavailable'
            assembly = ''
            shard = ''
            message = 'No shard timing observations are available; every shard total below is an estimate. Run scripts/update-backend-test-shard-timings.ps1 with GitHub access to refresh the cache.'
        })
    }

    $shards = @(
        foreach ($shard in @($Manifest.fastShards)) {
            $assemblies = @(
                foreach ($project in @($shard.projects)) {
                    Get-NervShardTimingAssemblyKey -Name ([string] $project)
                }
            )
            $measured = [double[]] @(
                foreach ($assembly in $assemblies) {
                    if ($rows.ContainsKey($assembly)) { [double] $rows[$assembly] }
                }
            )
            $shardMedian = Get-NervShardTimingMedian -Values $measured

            $total = 0.0
            $estimatedAssemblies = [System.Collections.Generic.List[string]]::new()
            foreach ($assembly in $assemblies) {
                if ($rows.ContainsKey($assembly)) {
                    $total += [double] $rows[$assembly]
                    continue
                }

                $estimate = if ($null -ne $shardMedian) { [double] $shardMedian }
                    elseif ($null -ne $globalMedian) { [double] $globalMedian }
                    else { $script:NervShardTimingLastResortEstimateMilliseconds }
                $total += $estimate
                [void] $estimatedAssemblies.Add($assembly)
                # When *nothing* is measured, `timing-source-unavailable` above already says every
                # row is an estimate; repeating it per assembly would bury that one line under one
                # warning per test project and train the reader to skip the warnings entirely. The
                # per-assembly warning exists for the case it can act on: a source that works, with
                # a gap in it.
                if ($rows.Count -gt 0) {
                    $warnings.Add([pscustomobject][ordered]@{
                        code = 'timing-assembly-missing'
                        assembly = $assembly
                        shard = [string] $shard.id
                        message = "No timing observation for '$assembly' in shard '$($shard.id)'; balancing with an estimate of $([Math]::Round($estimate, 1))ms. This is report-only."
                    })
                }
            }

            [pscustomobject][ordered]@{
                id = [string] $shard.id
                evidenceLane = [string] $shard.evidenceLane
                jobName = [string] $shard.jobName
                assemblyCount = @($assemblies).Count
                measuredAssemblyCount = @($assemblies).Count - $estimatedAssemblies.Count
                estimatedAssemblies = @($estimatedAssemblies)
                totalMilliseconds = [Math]::Round($total, 1)
            }
        }
    )

    $totals = [double[]] @($shards | ForEach-Object { [double] $_.totalMilliseconds })
    $mean = if ($totals.Count -gt 0) { ($totals | Measure-Object -Sum).Sum / $totals.Count } else { 0.0 }
    $spreadPercent = if ($mean -gt 0) {
        [Math]::Round(((($totals | Measure-Object -Maximum).Maximum - ($totals | Measure-Object -Minimum).Minimum) / $mean) * 100, 1)
    }
    else { 0.0 }

    return [pscustomobject][ordered]@{
        enforcement = 'report-only'
        timingSource = [string] $Timings.source
        timingSourceDetail = [string] $Timings.sourceDetail
        timingGeneratedAtUtc = $Timings.generatedAtUtc
        shards = $shards
        spreadPercent = $spreadPercent
        warnings = @($warnings)
    }
}

function Format-NervShardBalanceReport {
    param([Parameter(Mandatory)] [object] $Report)

    $lines = [System.Collections.Generic.List[string]]::new()
    [void] $lines.Add("Backend fast shard balance (report-only; timing is a cache, not a gate).")
    [void] $lines.Add("  timing source: $($Report.timingSource) $($Report.timingSourceDetail)".TrimEnd())
    if (-not [string]::IsNullOrWhiteSpace([string] $Report.timingGeneratedAtUtc)) {
        [void] $lines.Add("  timing generated: $($Report.timingGeneratedAtUtc)")
    }
    foreach ($shard in @($Report.shards)) {
        # Invariant culture on purpose: a grouped number rendered under the current culture makes the
        # same report read differently per machine, and this text is compared in contract tests.
        [void] $lines.Add([string]::Format(
            [Globalization.CultureInfo]::InvariantCulture,
            "  {0,-18} {1,10:N0} ms over {2} assemblies ({3} measured, {4} estimated) [{5}]",
            $shard.id, $shard.totalMilliseconds, $shard.assemblyCount, $shard.measuredAssemblyCount, @($shard.estimatedAssemblies).Count, $shard.evidenceLane))
    }
    [void] $lines.Add("  spread (max-min)/mean: $($Report.spreadPercent)%")
    foreach ($warning in @($Report.warnings)) {
        [void] $lines.Add("  WARN [$($warning.code)] $($warning.message)")
    }
    return @($lines)
}

function Get-NervShardTimingObservationsFromEvidenceDirectory {
    <#
        Collects observations from one downloaded run's extracted evidence artifacts. Only summaries
        that actually completed collection are used; a `collectionStatus: failed` bundle carries
        diagnostics, not measurements.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RunId
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @() }

    return @(
        foreach ($summaryFile in @(Get-ChildItem -LiteralPath $Path -Filter 'summary.json' -File -Recurse | Sort-Object FullName)) {
            $summary = $null
            try { $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json }
            catch { continue }
            if (($summary.PSObject.Properties.Name -contains 'collectionStatus') -and [string] $summary.collectionStatus -cne 'succeeded') { continue }
            foreach ($row in @(Get-NervShardTimingRowsFromEvidenceSummary -Summary $summary)) {
                [pscustomobject][ordered]@{
                    runId = $RunId
                    assembly = [string] $row.assembly
                    lane = [string] $row.lane
                    elapsedMilliseconds = [double] $row.elapsedMilliseconds
                }
            }
        }
    )
}

function Update-NervShardTimingCache {
    <#
        Refreshes the timing cache from the most recent successful `main` push CI runs.

        Degradation is the contract, not an afterthought. Every reachable failure — `gh` absent, no
        token, no network, expired artifacts, a run whose bundle carries no usable summary — is
        reported as a warning and returns `$null`. Callers keep whatever they already had. Nothing
        here is allowed to make a caller exit nonzero, because a timing refresh failing is not a
        defect in the repository.
    #>
    param(
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [ValidateRange(1, 20)] [int] $RunCount = $script:NervShardTimingDefaultRunCount,
        [DateTimeOffset] $GeneratedAtUtc = [DateTimeOffset]::UtcNow
    )

    $runs = @()
    try {
        $listed = Invoke-NativeCommandOutput -Command 'gh' -Arguments @(
            'run', 'list',
            '--repo', $Repository,
            '--workflow', 'CI',
            '--branch', 'main',
            '--event', 'push',
            '--status', 'success',
            '--limit', "$RunCount",
            '--json', 'databaseId,headSha,conclusion,updatedAt'
        ) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 120 -Name 'shard-timings-run-list'
        $runs = @((Protect-ScriptAutomationText $listed.Stdout) | ConvertFrom-Json)
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Shard timing refresh skipped; GitHub run listing unavailable: $(Protect-ScriptAutomationText $_.Exception.Message)"
        return $null
    }

    if (@($runs).Count -eq 0) {
        Write-Diagnostic -Level 'WARN' -Message 'Shard timing refresh skipped; no successful main push runs were listed.'
        return $null
    }

    $downloadRoot = Join-Path ([IO.Path]::GetTempPath()) ("nerv-iip-shard-timings-{0}" -f [Guid]::NewGuid().ToString('N'))
    $observations = [System.Collections.Generic.List[object]]::new()
    $usedRuns = [System.Collections.Generic.List[object]]::new()
    try {
        [IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
        foreach ($run in @($runs)) {
            $runId = [string] $run.databaseId
            if ([string]::IsNullOrWhiteSpace($runId)) { continue }
            $runDirectory = Join-Path $downloadRoot $runId
            try {
                [IO.Directory]::CreateDirectory($runDirectory) | Out-Null
                Invoke-NativeCommandOutput -Command 'gh' -Arguments @(
                    'run', 'download', $runId,
                    '--repo', $Repository,
                    '--dir', $runDirectory,
                    '--pattern', 'test-evidence-*'
                ) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 300 -Name "shard-timings-download-$runId" | Out-Null
            }
            catch {
                # Retention is 14 days and reruns delete nothing, so an older run in the window can
                # legitimately have no artifacts left. That thins the sample; it does not break it.
                Write-Diagnostic -Level 'WARN' -Message "Shard timing evidence unavailable for run ${runId}: $(Protect-ScriptAutomationText $_.Exception.Message)"
                continue
            }

            $runObservations = @(Get-NervShardTimingObservationsFromEvidenceDirectory -Path $runDirectory -RunId $runId)
            if ($runObservations.Count -eq 0) {
                Write-Diagnostic -Level 'WARN' -Message "Run $runId carried no usable shard timing evidence."
                continue
            }
            foreach ($observation in $runObservations) { [void] $observations.Add($observation) }
            [void] $usedRuns.Add([pscustomobject][ordered]@{
                workflowRunId = $runId
                headSha = [string] $run.headSha
                completedAtUtc = [string] $run.updatedAt
                assemblyObservationCount = $runObservations.Count
            })
        }
    }
    finally {
        Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($observations.Count -eq 0) {
        Write-Diagnostic -Level 'WARN' -Message 'Shard timing refresh produced no observations; the existing cache (if any) is left untouched.'
        return $null
    }

    $cache = New-NervShardTimingCache -Observations @($observations) -Runs @($usedRuns) -GeneratedAtUtc $GeneratedAtUtc
    $outputParent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
    [IO.Directory]::CreateDirectory($outputParent) | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), (($cache | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    return $cache
}
