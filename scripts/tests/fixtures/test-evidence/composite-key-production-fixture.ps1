# Script-Governance:
#   Category: fixture
#   SideEffects:
#     - Exercises retained test-evidence artifact and baseline production paths
#   Writes:
#     - One owned temporary evidence directory under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary evidence directory in finally
#   Requires:
#     - PowerShell 7

param(
    [Parameter(Mandatory)] [string] $TestEvidenceLibraryPath,
    [Parameter(Mandatory)]
    [ValidateSet('artifact-record-sort', 'normalized-trx-group', 'normalized-trx-record-sort', 'baseline-aggregate', 'derived-instance-id')]
    [string] $Fixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path (Split-Path -Parent $TestEvidenceLibraryPath) 'ScriptAutomation.ps1')
. $TestEvidenceLibraryPath

function Assert-ProductionFixture([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "composite-key-fixture:${Fixture}: $Message" }
}

function New-ProductionFixtureRecord {
    param(
        [Parameter(Mandatory)] [int] $Sequence,
        [AllowNull()] [object] $Lane = 'backend-shard-1',
        [AllowNull()] [object] $Assembly = 'Fixture.Tests.dll',
        [AllowNull()] [object] $TestName = 'Fixture.Test',
        [AllowNull()] [object] $DisplayName = 'Fixture.Test',
        [AllowNull()] [object] $TestInstanceId
    )

    $definitionId = '10000000-0000-0000-0000-{0:D12}' -f $Sequence
    if (-not $PSBoundParameters.ContainsKey('TestInstanceId')) {
        $TestInstanceId = '20000000-0000-0000-0000-{0:D12}' -f $Sequence
    }
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        workflowRunId = 'fixture-run'
        runAttempt = 1
        headSha = '0123456789abcdef0123456789abcdef01234567'
        testedSha = '0123456789abcdef0123456789abcdef01234567'
        lane = $Lane
        project = 'Fixture.Tests.csproj'
        assembly = $Assembly
        testName = $TestName
        displayName = $DisplayName
        testClassName = 'Fixture.CompositeKeyTests'
        testMethodName = "case$Sequence"
        definitionId = $definitionId
        testInstanceId = $TestInstanceId
        durationTicks = 10000L * $Sequence
        durationMilliseconds = [double]$Sequence
        outcome = 'passed'
        skipReason = $null
        redactionCount = 0
    }
}

function New-ProductionFixtureSummary([object[]] $Records) {
    $metadata = @{
        workflowRunId = 'fixture-run'
        runAttempt = 1
        headSha = '0123456789abcdef0123456789abcdef01234567'
        testedSha = '0123456789abcdef0123456789abcdef01234567'
        lane = 'backend-shard-1'
        selectedLanes = @('backend-shard-1')
        jobName = 'Backend Tests - BusinessGateway'
        currentTestOutcome = 'success'
        runnerOs = 'Linux'
        runnerImage = 'ubuntu24@20260720.247.2'
        dotnetSdk = '10.0.302'
        artifactName = 'composite-key-production-fixture'
        retentionDays = 1
        retentionLocation = 'fixture://composite-key-production/'
    }
    return New-NervTestEvidenceSummary -Records $Records -RunMetadata $metadata -Violations @() -Baseline $null -PriorAttemptOutcome $null -TopCount 5
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-composite-key-production-$Fixture-$([Guid]::NewGuid().ToString('N'))"
$fixtureRootRepeat = "$fixtureRoot-repeat"
try {
    if ([string]::Equals($Fixture, 'derived-instance-id', [StringComparison]::Ordinal)) {
        $targetOnlyPath = Join-Path $fixtureRoot 'target-only.trx'
        $withUnrelatedPath = Join-Path $fixtureRoot 'with-unrelated.trx'
        [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
        $targetResult = '<UnitTestResult testId="11111111-1111-1111-1111-111111111111" testName="Display|Value" duration="00:00:00.0010000" outcome="Passed" />'
        $targetDefinition = '<UnitTest id="11111111-1111-1111-1111-111111111111" name="Target" storage="Fixture.Tests.dll"><TestMethod className="Fixture" name="Target" /></UnitTest>'
        $delimiterCollisionResult = '<UnitTestResult testId="22222222-2222-2222-2222-222222222222" testName="Value" duration="00:00:00.0010000" outcome="Passed" />'
        $delimiterCollisionDefinition = '<UnitTest id="22222222-2222-2222-2222-222222222222" name="Target|Display" storage="Fixture.Tests.dll"><TestMethod className="Fixture" name="Target|Display" /></UnitTest>'
        $caseDistinctResult = '<UnitTestResult testId="33333333-3333-3333-3333-333333333333" testName="Display|Value" duration="00:00:00.0010000" outcome="Passed" />'
        $caseDistinctDefinition = '<UnitTest id="33333333-3333-3333-3333-333333333333" name="Target" storage="Fixture.Tests.dll"><TestMethod className="fixture" name="Target" /></UnitTest>'
        $targetOnlyXml = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times start="2026-08-09T00:00:00Z" finish="2026-08-09T00:00:00.001Z" /><Results>$targetResult</Results><TestDefinitions>$targetDefinition</TestDefinitions><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>
"@
        $withUnrelatedXml = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times start="2026-08-09T00:00:00Z" finish="2026-08-09T00:00:00.003Z" /><Results>$delimiterCollisionResult$caseDistinctResult$targetResult</Results><TestDefinitions>$delimiterCollisionDefinition$caseDistinctDefinition$targetDefinition</TestDefinitions><ResultSummary outcome="Completed"><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0" /></ResultSummary></TestRun>
"@
        [IO.File]::WriteAllText($targetOnlyPath, $targetOnlyXml, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($withUnrelatedPath, $withUnrelatedXml, [Text.UTF8Encoding]::new($false))
        $runMetadata = @{ lane = 'backend-shard-1'; workflowRunId = 'fixture-run'; runAttempt = 1; headSha = '0123456789abcdef0123456789abcdef01234567'; testedSha = '0123456789abcdef0123456789abcdef01234567' }
        $targetOnly = @(Read-NervTrxResults -Path @($targetOnlyPath) -RunMetadata $runMetadata.Clone())[0]
        $targetWithUnrelated = @(Read-NervTrxResults -Path @($withUnrelatedPath) -RunMetadata $runMetadata.Clone() | Where-Object {
            [string]::Equals([string]$_.testName, 'Fixture.Target', [StringComparison]::Ordinal)
        })[0]
        Assert-ProductionFixture (-not [string]::IsNullOrWhiteSpace([string]$targetOnly.testInstanceId)) `
            'a TRX result without executionId must receive a derived test instance ID.'
        Assert-ProductionFixture ([string]::Equals([string]$targetOnly.testInstanceId, [string]$targetWithUnrelated.testInstanceId, [StringComparison]::Ordinal)) `
            'delimiter-colliding or case-distinct unrelated definitions changed the target derived instance ID.'
        Write-Host "Composite-key production fixture '$Fixture' passed."
        exit 0
    }

    if ([string]::Equals($Fixture, 'baseline-aggregate', [StringComparison]::Ordinal)) {
        $records = @(
            New-ProductionFixtureRecord -Sequence 1 -Assembly $null
            New-ProductionFixtureRecord -Sequence 2 -Assembly ''
        )
        $summary = New-ProductionFixtureSummary -Records $records
        Assert-ProductionFixture (@($summary.assemblies).Count -eq 2) 'summary merged null and empty assembly identities.'
        Assert-ProductionFixture (@($summary.assemblies | Where-Object { $null -eq $_.assembly }).Count -eq 1) 'summary did not retain the null assembly identity.'
        Assert-ProductionFixture (@($summary.assemblies | Where-Object { $_.assembly -is [string] -and $_.assembly.Length -eq 0 }).Count -eq 1) 'summary did not retain the empty assembly identity.'

        # Exercise the same retained JSON boundary and wrapper shape used by
        # generate-test-evidence-baseline.ps1, instead of constructing a summary by hand.
        $retainedSummary = ($summary | ConvertTo-Json -Depth 100) | ConvertFrom-Json
        $summaries = @([pscustomobject]@{
            schemaVersion = 1
            granularity = 'test'
            durationMetric = 'trx-elapsed'
            lane = $retainedSummary.lane
            assemblies = @($retainedSummary.assemblies)
        })
        $source = @{
            sourceKind = 'trx-evidence'
            repository = 'Mang-X/Nerv-IIP'
            workflowRunId = 'fixture-run'
            runAttempt = 1
            jobId = 'fixture-job'
            headSha = '0123456789abcdef0123456789abcdef01234567'
            testedSha = '0123456789abcdef0123456789abcdef01234567'
            sourceUrl = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/fixture-run'
            event = 'push'
            headBranch = 'main'
            conclusion = 'success'
            jobConclusion = 'success'
            selectedLanes = @('backend-shard-1')
            generatorCommand = 'composite-key-production-fixture'
            laneProvenance = @([pscustomobject]@{
                lane = 'backend-shard-1'
                jobName = 'Backend Tests - BusinessGateway'
                runnerOs = 'Linux'
                runnerImage = 'ubuntu24@20260720.247.2'
                dotnetSdk = '10.0.302'
            })
        }
        $baseline = New-NervTestEvidenceBaseline -Summaries $summaries -SourceMetadata $source -GeneratedAtUtc ([DateTimeOffset]'2026-08-09T00:00:00Z')
        Assert-ProductionFixture (@($baseline.assemblies).Count -eq 2) 'baseline aggregate merged null and empty assembly identities.'
        Assert-ProductionFixture (@($baseline.assemblies | Where-Object { [int]$_.total -eq 1 }).Count -eq 2) 'baseline aggregate did not preserve one measurement in each null/empty group.'
        Assert-ProductionFixture (@($baseline.assemblies | Where-Object { $null -eq $_.assembly }).Count -eq 1) 'baseline did not retain the null assembly identity.'
        Assert-ProductionFixture (@($baseline.assemblies | Where-Object { $_.assembly -is [string] -and $_.assembly.Length -eq 0 }).Count -eq 1) 'baseline did not retain the empty assembly identity.'
        Write-Host "Composite-key production fixture '$Fixture' passed."
        exit 0
    }

    $records = if ([string]::Equals($Fixture, 'artifact-record-sort', [StringComparison]::Ordinal)) {
        @(
            New-ProductionFixtureRecord -Sequence 1 -TestName $null
            New-ProductionFixtureRecord -Sequence 2 -TestName ''
        )
    }
    elseif ([string]::Equals($Fixture, 'normalized-trx-group', [StringComparison]::Ordinal)) {
        $longAssembly = ('Long' + ('A' * 260) + '.Tests.dll')
        $slashIdentity = Get-NervOrdinalCompositeKey -Components @('backend-shard-1', 'A/B.dll')
        $slashIdentityDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($slashIdentity))).ToLowerInvariant()
        @(
            New-ProductionFixtureRecord -Sequence 1 -Assembly $null
            New-ProductionFixtureRecord -Sequence 2 -Assembly ''
            New-ProductionFixtureRecord -Sequence 3 -Assembly 'A/B.dll'
            New-ProductionFixtureRecord -Sequence 4 -Assembly 'A_B.dll'
            New-ProductionFixtureRecord -Sequence 5 -Assembly 'Case.Tests.dll'
            New-ProductionFixtureRecord -Sequence 6 -Assembly 'case.Tests.dll'
            New-ProductionFixtureRecord -Sequence 7 -Assembly 'Unique.Tests.dll'
            New-ProductionFixtureRecord -Sequence 8 -Assembly $longAssembly
            New-ProductionFixtureRecord -Sequence 9 -Assembly 'Bad:Name?.Tests.dll'
            # This third identity's compatible legacy name exactly preoccupies the first identity's
            # hashed candidate. Allocation must keep valid inputs and deterministically move the
            # hash, rather than throwing or overwriting either identity.
            New-ProductionFixtureRecord -Sequence 10 -Assembly "A_B.dll-id-$slashIdentityDigest"
        )
    }
    else {
        @(
            New-ProductionFixtureRecord -Sequence 1 -TestInstanceId $null
            New-ProductionFixtureRecord -Sequence 2 -TestInstanceId ''
        )
    }
    $summary = New-ProductionFixtureSummary -Records $records
    Write-NervTestEvidenceArtifacts -Records $records -Summary $summary -OutputDirectory $fixtureRoot

    if ([string]::Equals($Fixture, 'artifact-record-sort', [StringComparison]::Ordinal)) {
        $jsonRows = @(Get-Content (Join-Path $fixtureRoot 'tests.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
        Assert-ProductionFixture ([string]::Equals([string]$jsonRows[0].definitionId, '10000000-0000-0000-0000-000000000002', [StringComparison]::Ordinal)) `
            'tests.jsonl did not ordinal-sort empty before null; the artifact record selector collapsed them.'
    }
    elseif ([string]::Equals($Fixture, 'normalized-trx-group', [StringComparison]::Ordinal)) {
        $retainedJsonRows = @(Get-Content (Join-Path $fixtureRoot 'tests.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
        Assert-ProductionFixture (@($retainedJsonRows | Where-Object { $null -eq $_.assembly }).Count -eq 1) `
            'tests.jsonl projection did not retain the null assembly identity.'
        Assert-ProductionFixture (@($retainedJsonRows | Where-Object { $_.assembly -is [string] -and $_.assembly.Length -eq 0 }).Count -eq 1) `
            'tests.jsonl projection did not retain the empty assembly identity.'
        $caseInsensitiveNames = New-NervNormalizedTrxFileNameSet
        Add-NervNormalizedTrxFileName -ResolvedFileNames $caseInsensitiveNames -FileName 'CaseOnly.trx'
        $caseOnlyCollisionFailed = $false
        try { Add-NervNormalizedTrxFileName -ResolvedFileNames $caseInsensitiveNames -FileName 'caseonly.trx' }
        catch { $caseOnlyCollisionFailed = $true }
        Assert-ProductionFixture $caseOnlyCollisionFailed `
            'the final normalized TRX filename uniqueness guard must reject case-only duplicates.'

        $trxFiles = @(Get-ChildItem (Join-Path $fixtureRoot 'trx') -File -Filter '*.trx')
        Assert-ProductionFixture ($trxFiles.Count -eq 10) 'normalized TRX groups with distinct identities must produce ten unique artifact paths.'
        Assert-ProductionFixture (@(Get-NervStringsSorted -Values @($trxFiles.Name) -Comparer ([StringComparer]::OrdinalIgnoreCase) -Unique).Count -eq 10) `
            'normalized TRX paths must remain unique on case-insensitive filesystems.'
        Assert-ProductionFixture (@($trxFiles | Where-Object { $_.Name.Length -gt 240 }).Count -eq 0) `
            'normalized TRX paths must stay within the 240-character cross-platform budget.'
        Assert-ProductionFixture (@($trxFiles | Where-Object { $_.Name -notmatch '^[A-Za-z0-9_.-]+$' }).Count -eq 0) `
            'normalized TRX paths must contain only the governed safe filename alphabet.'
        $hashedFiles = @($trxFiles | Where-Object { $_.Name -match '-id-[0-9a-f]+-' })
        Assert-ProductionFixture ($hashedFiles.Count -eq 8) `
            'null, empty, sanitization-collision, case-only, overlength, and the hash-lookalike legacy identity must retain distinct filenames.'
        Assert-ProductionFixture (@($hashedFiles | Where-Object { $_.Name -notmatch '-id-[0-9a-f]{64}(?:-collision-[1-9][0-9]*)?-01234567-attempt-1\.trx$' }).Count -eq 0) `
            'every hashed normalized TRX path must retain the complete lowercase SHA-256 identity digest and any deterministic collision ordinal.'
        Assert-ProductionFixture (@($trxFiles | Where-Object { $_.Name -match '-id-[0-9a-f]{64}-collision-1-01234567-attempt-1\.trx$' }).Count -eq 1) `
            'the three-identity legacy-versus-hash attack must deterministically allocate one collision-suffixed path instead of failing valid input.'
        Assert-ProductionFixture (@($trxFiles | Where-Object { [string]::Equals($_.Name, 'backend-shard-1-Unique.Tests.dll-01234567-attempt-1.trx', [StringComparison]::Ordinal) }).Count -eq 1) `
            'a normal non-colliding assembly must retain its existing normalized TRX filename.'
        Assert-ProductionFixture (@($trxFiles | Where-Object { [string]::Equals($_.Name, 'backend-shard-1-Bad_Name_.Tests.dll-01234567-attempt-1.trx', [StringComparison]::Ordinal) }).Count -eq 1) `
            'a non-colliding assembly with illegal filename characters must retain its compatible sanitized filename.'
        $trxDocuments = @($trxFiles | ForEach-Object {
            [xml]$trx = Get-Content $_.FullName -Raw
            $trx
        })
        Assert-ProductionFixture (@(Get-NervStringsSorted -Values @($trxDocuments.TestRun.id) -Comparer ([StringComparer]::Ordinal) -Unique).Count -eq 10) `
            'normalized TRX groups with distinct identities must retain distinct TestRun ids.'
        $normalDefinition = @($trxDocuments | ForEach-Object { @($_.TestRun.TestDefinitions.UnitTest) } | Where-Object {
            [string]::Equals([string]$_.storage, 'Unique.Tests.dll', [StringComparison]::Ordinal)
        })
        Assert-ProductionFixture ($normalDefinition.Count -eq 1 -and -not $normalDefinition[0].HasAttribute('assemblyIdentity', 'urn:nerv-iip:test-evidence:assembly-identity:v1')) `
            'a normal assembly must keep its existing standard TRX storage without a custom identity marker.'
        $retainedResults = @($trxDocuments | ForEach-Object { @($_.TestRun.Results.UnitTestResult) })
        Assert-ProductionFixture ($retainedResults.Count -eq 10) `
            'normalized TRX output must retain every input result without overwrite or identity merge.'

        $roundTripMetadata = @{
            lane = 'backend-shard-1'
            workflowRunId = 'fixture-run'
            runAttempt = 1
            headSha = '0123456789abcdef0123456789abcdef01234567'
            testedSha = '0123456789abcdef0123456789abcdef01234567'
        }
        $roundTrip = @(Read-NervTrxResults -Path @($trxFiles.FullName) -RunMetadata $roundTripMetadata)
        Assert-ProductionFixture ($roundTrip.Count -eq $records.Count) `
            'Write-NervTestEvidenceArtifacts to Read-NervTrxResults round-trip changed the total record count.'
        $expectedAssemblyKeys = @(Get-NervStringsSorted -Values @($records | ForEach-Object { Get-NervOrdinalCompositeKey -Components @($_.assembly) }) -Comparer ([StringComparer]::Ordinal))
        $actualAssemblyKeys = @(Get-NervStringsSorted -Values @($roundTrip | ForEach-Object { Get-NervOrdinalCompositeKey -Components @($_.assembly) }) -Comparer ([StringComparer]::Ordinal))
        Assert-ProductionFixture ([string]::Equals(($expectedAssemblyKeys -join "`n"), ($actualAssemblyKeys -join "`n"), [StringComparison]::Ordinal)) `
            "normalized TRX round-trip did not preserve assembly identities exactly; expected=$($expectedAssemblyKeys -join ',') actual=$($actualAssemblyKeys -join ',')."
        Assert-ProductionFixture (@($roundTrip | Where-Object { $null -eq $_.assembly }).Count -eq 1) `
            'normalized TRX round-trip did not restore the null assembly identity.'
        Assert-ProductionFixture (@($roundTrip | Where-Object { $_.assembly -is [string] -and $_.assembly.Length -eq 0 }).Count -eq 1) `
            'normalized TRX round-trip did not restore the empty assembly identity.'
        Assert-ProductionFixture (@($roundTripMetadata.trxRuns | Where-Object { $null -eq $_.assembly }).Count -eq 1) `
            'normalized TRX RunMetadata timing projection did not retain the null assembly identity.'
        Assert-ProductionFixture (@($roundTripMetadata.trxRuns | Where-Object { $_.assembly -is [string] -and $_.assembly.Length -eq 0 }).Count -eq 1) `
            'normalized TRX RunMetadata timing projection did not retain the empty assembly identity.'

        $markerProbeRoot = Join-Path $fixtureRoot 'marker-probes'
        [IO.Directory]::CreateDirectory($markerProbeRoot) | Out-Null
        $nullMarkerFile = @($trxFiles | Where-Object {
            [xml]$candidate = Get-Content $_.FullName -Raw
            @($candidate.TestRun.TestDefinitions.UnitTest | Where-Object {
                [string]::Equals($_.GetAttribute('assemblyIdentity', 'urn:nerv-iip:test-evidence:assembly-identity:v1'), 'null', [StringComparison]::Ordinal)
            }).Count -gt 0
        })[0]
        $nullMarkerXml = Get-Content $nullMarkerFile.FullName -Raw
        $prefixAliasPath = Join-Path $markerProbeRoot 'prefix-alias.trx'
        Set-Content -LiteralPath $prefixAliasPath -NoNewline -Value ($nullMarkerXml.Replace('xmlns:nerv=', 'xmlns:identity=').Replace('nerv:assemblyIdentity=', 'identity:assemblyIdentity='))
        $prefixAliasAccepted = $true
        try { $prefixAliasRecords = @(Read-NervTrxResults -Path @($prefixAliasPath) -RunMetadata $roundTripMetadata.Clone()) }
        catch { $prefixAliasAccepted = $false }
        Assert-ProductionFixture ($prefixAliasAccepted -and $prefixAliasRecords.Count -eq 1 -and $null -eq $prefixAliasRecords[0].assembly) `
            'assembly identity markers must bind by namespace URI rather than one literal XML prefix.'

        $markerGateCases = @(
            [pscustomobject]@{ Name = 'missing-provenance'; Xml = ($nullMarkerXml -replace ' headSha="[0-9a-f]{40}" testedSha="[0-9a-f]{40}"', '') },
            [pscustomobject]@{ Name = 'tested-sha-mismatch'; Xml = $nullMarkerXml.Replace('testedSha="0123456789abcdef0123456789abcdef01234567"', 'testedSha="1123456789abcdef0123456789abcdef01234567"') },
            [pscustomobject]@{ Name = 'wrong-namespace'; Xml = $nullMarkerXml.Replace('urn:nerv-iip:test-evidence:assembly-identity:v1', 'urn:nerv-iip:test-evidence:assembly-identity:v2') },
            [pscustomobject]@{ Name = 'empty-verbatim-storage'; Xml = $nullMarkerXml.Replace('assemblyIdentity="null"', 'assemblyIdentity="verbatim"') }
        )
        foreach ($markerGateCase in $markerGateCases) {
            $markerGatePath = Join-Path $markerProbeRoot "$($markerGateCase.Name).trx"
            Set-Content -LiteralPath $markerGatePath -NoNewline -Value $markerGateCase.Xml
            $markerGateRejected = $false
            try { Read-NervTrxResults -Path @($markerGatePath) -RunMetadata $roundTripMetadata.Clone() | Out-Null }
            catch { $markerGateRejected = $_.Exception.Message.Contains('assembly identity marker', [StringComparison]::OrdinalIgnoreCase) }
            Assert-ProductionFixture $markerGateRejected `
                "assembly identity marker gate case '$($markerGateCase.Name)' must fail closed with its trust-boundary diagnostic."
        }

        $reversedRecords = @($records)
        [array]::Reverse($reversedRecords)
        Write-NervTestEvidenceArtifacts -Records $reversedRecords -Summary $summary -OutputDirectory $fixtureRootRepeat
        $repeatFiles = @(Get-ChildItem (Join-Path $fixtureRootRepeat 'trx') -File -Filter '*.trx')
        Assert-ProductionFixture ([string]::Equals((@(Get-NervStringsSorted -Values @($trxFiles.Name) -Comparer ([StringComparer]::Ordinal)) -join "`n"), (@(Get-NervStringsSorted -Values @($repeatFiles.Name) -Comparer ([StringComparer]::Ordinal)) -join "`n"), [StringComparison]::Ordinal)) `
            'two normalized TRX generations from the same records produced different filenames.'
        foreach ($trxFile in $trxFiles) {
            $repeatFile = Join-Path (Join-Path $fixtureRootRepeat 'trx') $trxFile.Name
            [xml]$firstDocument = Get-Content $trxFile.FullName -Raw
            [xml]$repeatDocument = Get-Content $repeatFile -Raw
            Assert-ProductionFixture ([string]::Equals([string]$firstDocument.TestRun.id, [string]$repeatDocument.TestRun.id, [StringComparison]::Ordinal)) `
                "two normalized TRX generations produced different TestRun ids for '$($trxFile.Name)'."
            Assert-ProductionFixture ([string]::Equals([Convert]::ToBase64String([IO.File]::ReadAllBytes($trxFile.FullName)), [Convert]::ToBase64String([IO.File]::ReadAllBytes($repeatFile)), [StringComparison]::Ordinal)) `
                "two normalized TRX generations produced different bytes for '$($trxFile.Name)'."
        }

        # Force the otherwise cryptographic hash-vs-hash branch through the real allocator. The
        # production hash builder is covered separately by the complete-SHA mutation; this probe
        # replaces only candidate generation so two distinct identities present the same first
        # candidate, then proves stable ordinal collision allocation under reversed input.
        $productionHashedNameBuilder = (Get-Command Get-NervNormalizedTrxHashedFileName -CommandType Function).ScriptBlock
        try {
            Set-Item -LiteralPath Function:\Get-NervNormalizedTrxHashedFileName -Value {
                param($Group, $Summary, $Sha8, $CollisionOrdinal = 0)
                if ($CollisionOrdinal -eq 0) { return 'forced-hash-collision.trx' }
                return "forced-hash-collision-$CollisionOrdinal-$([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]$Group.Identity))).Substring(0, 8).ToLowerInvariant()).trx"
            }
            $forcedGroups = @(
                [pscustomobject]@{ Identity = 'identity-b'; AssemblyName = 'B.dll'; LegacyFileName = 'same.trx' },
                [pscustomobject]@{ Identity = 'identity-a'; AssemblyName = 'A.dll'; LegacyFileName = 'same.trx' }
            )
            $forcedForward = Resolve-NervNormalizedTrxFileNames -Groups $forcedGroups -Summary $summary -Sha8 '01234567'
            [array]::Reverse($forcedGroups)
            $forcedReverse = Resolve-NervNormalizedTrxFileNames -Groups $forcedGroups -Summary $summary -Sha8 '01234567'
            Assert-ProductionFixture ([string]::Equals([string]$forcedForward['identity-a'], 'forced-hash-collision.trx', [StringComparison]::Ordinal)) `
                'hash-vs-hash allocation must assign the first candidate to the ordinal-first identity.'
            Assert-ProductionFixture ([string]::Equals([string]$forcedForward['identity-b'], [string]$forcedReverse['identity-b'], [StringComparison]::Ordinal) -and
                [string]::Equals([string]$forcedForward['identity-a'], [string]$forcedReverse['identity-a'], [StringComparison]::Ordinal)) `
                'hash-vs-hash allocation must be independent of input order.'
            Assert-ProductionFixture (-not [string]::Equals([string]$forcedForward['identity-a'], [string]$forcedForward['identity-b'], [StringComparison]::OrdinalIgnoreCase)) `
                'hash-vs-hash allocation must retain both valid identities instead of failing or overwriting.'
        }
        finally {
            Set-Item -LiteralPath Function:\Get-NervNormalizedTrxHashedFileName -Value $productionHashedNameBuilder
        }
    }
    elseif ([string]::Equals($Fixture, 'normalized-trx-record-sort', [StringComparison]::Ordinal)) {
        $trxFile = @(Get-ChildItem (Join-Path $fixtureRoot 'trx') -File -Filter '*.trx')[0]
        [xml]$trx = Get-Content $trxFile.FullName -Raw
        $firstResult = @($trx.TestRun.Results.UnitTestResult)[0]
        Assert-ProductionFixture ([string]::Equals([string]$firstResult.testId, '10000000-0000-0000-0000-000000000002', [StringComparison]::Ordinal)) `
            'normalized TRX record selector did not ordinal-sort empty before null; the record identities collapsed.'
    }

    Write-Host "Composite-key production fixture '$Fixture' passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    if (Test-Path -LiteralPath $fixtureRootRepeat) { Remove-Item -LiteralPath $fixtureRootRepeat -Recurse -Force }
}
