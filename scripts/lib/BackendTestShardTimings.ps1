# Script-Governance:
#   Category: library, generate
#   SideEffects:
#     - Runs `gh` (via Invoke-NativeCommandOutput) to list and download main-run evidence artifacts
#     - Expands downloaded artifact archives under a caller-supplied working directory
#   Writes:
#     - The -OutputPath that Update-NervShardTimingCache is given (callers use the gitignored
#       artifacts/ tree); no repository-tracked file
#   Cleanup:
#     - Owns no long-lived process; temporary extraction directories are removed by the caller
#   Requires:
#     - PowerShell 7
#     - GitHub CLI only for a refresh; every read path degrades to report-only without it
#
# Header note: scripts/check-script-governance.ps1 skips scripts/lib/* in its default sweep, so this
# block is not what makes the gate pass. It is here because this is the highest-side-effect library
# in the directory — it starts a child process and writes files — and its governance-adjacent peers
# (TestEvidence.ps1, CiWorkflowBudgets.ps1) carry the same block. Running the checker with an
# explicit -Path on this file therefore reports only MissingHelper, which every library here shares:
# a library is dot-sourced *by* an entry point that has already loaded ScriptAutomation.ps1.
#
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

. (Join-Path $PSScriptRoot 'OrdinalString.ps1')

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

    $leaf = $trimmed.Substring($trimmed.LastIndexOf('/', [StringComparison]::Ordinal) + 1)
    foreach ($extension in @('.csproj', '.dll')) {
        if ($leaf.EndsWith($extension, [StringComparison]::OrdinalIgnoreCase)) {
            $leaf = $leaf.Substring(0, $leaf.Length - $extension.Length)
            break
        }
    }

    return ($leaf.ToLowerInvariant() + '.dll')
}

function Get-NervShardTimingProperty {
    <#
        Reads one property off a `ConvertFrom-Json` object without asserting that it exists.
        `Set-StrictMode -Version Latest` (scripts/lib/ScriptAutomation.ps1) makes `$obj.missing` a
        *terminating* error, so every field this file reads out of a file on disk has to go through
        an existence check first — otherwise a structurally wrong cache stops being a cache miss and
        becomes a nonzero exit in whatever script happened to read it.
    #>
    param(
        [AllowNull()] [object] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-NervShardTimingArray {
    <#
        `@($null).Count` is 1, so the usual `@($x).Count -gt 0` idiom reports "one element" for an
        absent JSON array and then indexes into `$null`. Every array read out of a file goes through
        here so an absent property is an empty array rather than a one-element array of nothing.
    #>
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return @() }
    return @($Value)
}

function ConvertTo-NervShardTimingDouble {
    <#
        Invariant numeric parse, and the *only* one this file uses for external text.

        `[double]::TryParse($s, [ref]$x)` binds the culture-aware overload, while PowerShell renders
        `[string]$double` under the invariant culture — so writing the cache and reading it back is
        not a round-trip on a machine whose culture is not en-US. Measured on this repository's
        .NET: `87643.2` reads back as `876432` under `de-DE` (a silent 10x, because `.` is a group
        separator there) and fails to parse at all under `fr-FR`, which drops every observation and
        leaves `Update-NervShardTimingCache` with nothing to write, so the cache is never refreshed
        again. `ConvertTo-NervShardTimingTimestampText` below already had this treatment for dates;
        the numbers did not.

        `NumberStyles::Float` deliberately excludes `AllowThousands`: this text is machine-written
        invariant, so a group separator in it is corruption rather than formatting.
    #>
    param([AllowNull()] [object] $Value)

    $parsed = 0.0
    if ([double]::TryParse([string] $Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed)) {
        return $parsed
    }
    return $null
}

function ConvertTo-NervShardTimingInteger {
    <#
        Integer counterpart of ConvertTo-NervShardTimingDouble, for the same reason: the schema
        version arrives as external text and its verdict must not depend on who is reading it.
    #>
    param([AllowNull()] [object] $Value)

    $parsed = 0
    if ([int]::TryParse([string] $Value, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed)) {
        return $parsed
    }
    return $null
}

function Get-NervShardTimingMedian {
    <#
        Median, not mean: runner-image rollouts and noisy neighbours produce one-sided outliers, and
        a mean lets a single 3x run move a shard's whole budget. Even counts average the two middle
        values so two samples still produce a usable number.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [double[]] $Values)

    $sorted = @(Get-NervItemsSorted -Items @($Values) -Comparison { param($left, $right) if ([double]$left -lt [double]$right) { -1 } elseif ([double]$left -gt [double]$right) { 1 } else { 0 } })
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

    return @(
        foreach ($assembly in @(ConvertTo-NervShardTimingArray -Value (Get-NervShardTimingProperty -Object $Summary -Name 'assemblies'))) {
            $key = Get-NervShardTimingAssemblyKey -Name ([string] (Get-NervShardTimingProperty -Object $assembly -Name 'assembly'))
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $elapsed = ConvertTo-NervShardTimingDouble -Value (Get-NervShardTimingProperty -Object $assembly -Name 'elapsedMilliseconds')
            # A non-positive (or unparseable) duration is not an observation. Keeping it would let a
            # run that failed to record elapsed time pull an assembly's median toward zero, which is
            # exactly the direction that hides a slow shard.
            if ($null -eq $elapsed -or $elapsed -le 0) { continue }
            [pscustomobject][ordered]@{
                assembly = $key
                lane = [string] (Get-NervShardTimingProperty -Object $assembly -Name 'lane')
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

        The lane an observation carries is deliberately dropped here rather than recorded on the
        row. The cache is a *budget*: what a shard costs is the sum of the work in it, so an assembly
        split across two lanes contributes one summed number and there is no single lane to name.
        `New-NervTestEvidenceSummary` in scripts/lib/TestEvidence.ps1 does the opposite with the same
        input — it keeps lane on the row and uses it to disambiguate, reporting
        `ambiguous-assembly-in-baseline` when it cannot — because that comparison is per lane and
        needs the row's *identity*, not a total. Both rules are correct for their own job; neither is
        a fallback for the other.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Observations)

    $perRun = @{}
    foreach ($observation in @($Observations)) {
        $key = [string] $observation.assembly
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        $runId = [string] $observation.runId
        if (-not $perRun.ContainsKey($key)) { $perRun[$key] = @{} }
        if (-not $perRun[$key].ContainsKey($runId)) { $perRun[$key][$runId] = 0.0 }
        $perRun[$key][$runId] = [double] $perRun[$key][$runId] + [double] $observation.elapsedMilliseconds
    }

    return @(
        foreach ($key in @(Get-NervStringsSorted -Values @($perRun.Keys) -Comparer ([StringComparer]::Ordinal))) {
            $values = [double[]] @($perRun[$key].Values | ForEach-Object { [double] $_ })
            [pscustomobject][ordered]@{
                assembly = $key
                elapsedMilliseconds = [Math]::Round((Get-NervShardTimingMedian -Values $values), 4)
                observationCount = $values.Count
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
    <#
        Reads the cache file into a normalized lookup, or returns `$null` for "no usable cache".

        The whole read — file, JSON parse, every field access, every numeric conversion — is inside
        one try/catch on purpose, and that is a fix rather than a style choice. Under
        `Set-StrictMode -Version Latest` a missing property and a bad type conversion are both
        *terminating* errors, so a cache that is valid JSON but structurally wrong escaped the older
        wrapping (which covered only `ConvertFrom-Json`) and propagated out of a report whose
        contract says its only nonzero exit is an unusable manifest. Two shapes actually did it: a
        cache object with no `assemblies` property, and an `elapsedMilliseconds` holding a string.
        A malformed cache is a cache miss by definition — it is regenerated on the next refresh —
        and a cache can never make a caller red.
    #>
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }

    try {
        $cache = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json

        $schemaVersion = ConvertTo-NervShardTimingInteger -Value (Get-NervShardTimingProperty -Object $cache -Name 'schemaVersion')
        if ($null -eq $schemaVersion -or $schemaVersion -ne $script:NervShardTimingCacheSchemaVersion) {
            Write-Diagnostic -Level 'WARN' -Message 'Shard timing cache has an unsupported schemaVersion and will be ignored.'
            return $null
        }

        $rows = @{}
        foreach ($row in @(ConvertTo-NervShardTimingArray -Value (Get-NervShardTimingProperty -Object $cache -Name 'assemblies'))) {
            $key = Get-NervShardTimingAssemblyKey -Name ([string] (Get-NervShardTimingProperty -Object $row -Name 'assembly'))
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $elapsed = ConvertTo-NervShardTimingDouble -Value (Get-NervShardTimingProperty -Object $row -Name 'elapsedMilliseconds')
            if ($null -eq $elapsed -or $elapsed -le 0) { continue }
            $rows[$key] = $elapsed
        }

        return [pscustomobject][ordered]@{
            runCount = @(ConvertTo-NervShardTimingArray -Value (Get-NervShardTimingProperty -Object $cache -Name 'runs')).Count
            statistic = [string] (Get-NervShardTimingProperty -Object $cache -Name 'statistic')
            generatedAtUtc = ConvertTo-NervShardTimingTimestampText -Value (Get-NervShardTimingProperty -Object $cache -Name 'generatedAtUtc')
            rows = $rows
        }
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Shard timing cache is unreadable and will be ignored: $(Protect-ScriptAutomationText $_.Exception.Message)"
        return $null
    }
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

    # Import-NervShardTimingCache has already normalized and validated every row, so nothing here can
    # throw on a malformed cache; an unusable cache arrives as `$null` and the fallback below runs.
    $cache = if ([string]::IsNullOrWhiteSpace($CachePath)) { $null } else { Import-NervShardTimingCache -Path $CachePath }
    if ($null -ne $cache -and $cache.rows.Count -gt 0) {
        $rows = $cache.rows
        $source = 'main-run-evidence-cache'
        $sourceDetail = "$($cache.runCount) successful main run(s), statistic=$([string] $cache.statistic)"
        $generatedAtUtc = $cache.generatedAtUtc
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
                $generatedAtUtc = ConvertTo-NervShardTimingTimestampText -Value (Get-NervShardTimingProperty -Object $snapshot -Name 'generatedAtUtc')
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
        foreach ($summaryFile in @(Get-NervItemsSortedByString -Items @(Get-ChildItem -LiteralPath $Path -Filter 'summary.json' -File -Recurse) -KeySelector { param($row) [string]$row.FullName } -Comparer ([StringComparer]::Ordinal))) {
            $rows = @()
            try {
                $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
                $status = Get-NervShardTimingProperty -Object $summary -Name 'collectionStatus'
                # Ordinal, not `-cne`. PowerShell's `c` prefix does not make a comparison ordinal —
                # `-ceq`/`-cne` still consult the collation table, which folds away ignorable
                # characters, so a status of "succee<U+00AD>ded" compares *equal* to 'succeeded' and
                # a failed bundle's diagnostics would be admitted as measurements. Same rule as the
                # rest of the repository (docs/architecture/script-automation-governance.md).
                if ($null -ne $status -and -not [string]::Equals([string] $status, 'succeeded', [StringComparison]::Ordinal)) { continue }
                $rows = @(Get-NervShardTimingRowsFromEvidenceSummary -Summary $summary)
            }
            catch { continue }
            foreach ($row in $rows) {
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
    # One catch over the whole collect-and-write block, not only over the per-run download. Directory
    # enumeration, unpacking a bundle and writing the cache file are all IO, and IO on a runner
    # fails for reasons that have nothing to do with this repository — a read-only or full
    # `artifacts/` tree, a partially extracted archive, a permission change. Before this catch
    # existed those escaped into the caller, whose `$ErrorActionPreference = 'Stop'` turned a failed
    # *cache refresh* into a nonzero exit: exactly the ceremony #1507 removed, rebuilt by accident.
    try {
        [IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
        foreach ($run in @($runs)) {
            $runId = [string] (Get-NervShardTimingProperty -Object $run -Name 'databaseId')
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
                headSha = [string] (Get-NervShardTimingProperty -Object $run -Name 'headSha')
                completedAtUtc = [string] (Get-NervShardTimingProperty -Object $run -Name 'updatedAt')
                assemblyObservationCount = $runObservations.Count
            })
        }

        if ($observations.Count -eq 0) {
            Write-Diagnostic -Level 'WARN' -Message 'Shard timing refresh produced no observations; the existing cache (if any) is left untouched.'
            return $null
        }

        $cache = New-NervShardTimingCache -Observations @($observations) -Runs @($usedRuns) -GeneratedAtUtc $GeneratedAtUtc
        # Temp-then-replace rather than writing the destination in place. A direct write that fails
        # part-way (full disk, killed process) leaves a truncated file where a *complete previous
        # cache* used to be, and a truncated cache reads as a cache miss — so a failed refresh would
        # silently destroy good data instead of leaving it alone. Everything else in this file is
        # built on "a refresh that cannot happen changes nothing"; this keeps that true for a refresh
        # that starts and then dies. `File.Move -Force` is atomic within a volume, and the temp file
        # is a sibling so it always is one.
        $resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
        $outputParent = Split-Path -Parent $resolvedOutputPath
        [IO.Directory]::CreateDirectory($outputParent) | Out-Null
        $stagingPath = "$resolvedOutputPath.$([Guid]::NewGuid().ToString('N')).tmp"
        try {
            [IO.File]::WriteAllText($stagingPath, (($cache | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
            [IO.File]::Move($stagingPath, $resolvedOutputPath, $true)
        }
        finally {
            if ([IO.File]::Exists($stagingPath)) { Remove-Item -LiteralPath $stagingPath -Force -ErrorAction SilentlyContinue }
        }
        return $cache
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Shard timing refresh failed; the existing cache (if any) is left untouched: $(Protect-ScriptAutomationText $_.Exception.Message)"
        return $null
    }
    finally {
        Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
