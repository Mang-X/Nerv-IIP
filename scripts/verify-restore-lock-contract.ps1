# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the BusinessGateway restore manifest, the per-project packages.lock.json files it
#       registers, and the .csproj files in the seed project's ProjectReference closure
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
    Fails when the repository's restore lock contract has drifted from the files it claims to pin.

.DESCRIPTION
    Two artifacts in this repository declare, in machine-readable form, that a set of restore inputs
    is pinned:

      * docs/reference/api/business-gateway-surface-restore.manifest.json — records a SHA-256 for
        every restore input and enumerates the per-project lock fixture set.
      * the packages.lock.json files themselves — record, per package, the requested version range
        and the version NuGet actually resolved.

    Until this checker existed, nothing read either of them. That is not a theoretical gap: issue
    #3136 found `"requested": "[14.0.0, )", "resolved": "12.5.0"` written verbatim in the
    BusinessGateway lock — a MediatR fork that makes in-process WebApplicationFactory hosting throw
    TypeLoadException — and it had survived in the repository, in machine-readable form, because no
    gate read the file. The manifest had drifted the same way: the seed .csproj gained a
    ProjectReference in 33c792c04 and nobody updated the recorded hash.

    WHY THIS IS A STATIC CHECK AND NOT `dotnet restore --locked-mode`.

    `--locked-mode` was measured against this exact class of defect and has zero discrimination for
    it (issue #3145, all runs with obj/ removed first):

      | mutation                                                   | obj cleared | EXIT |
      | ---------------------------------------------------------- | ----------- | ---- |
      | lock `resolved` 1.15.3 -> 1.15.2                            | no          | 0    |
      | lock `resolved` 1.15.3 -> 1.15.2                            | yes         | 0    |
      | same, plus -p:RestorePackagesWithLockFile=true              |             |      |
      |   -p:RestoreLockedMode=true                                 | yes         | 0    |
      | same, plus --force                                          | yes         | 0    |
      | delete one Direct dependency entry from the lock            | yes         | 1 (NU1004) |
      | change Directory.Packages.props version, lock untouched     | yes         | 1 (NU1004) |

    What `--locked-mode` compares is the set of Direct `requested` ranges recorded in the lock
    against the project's currently declared PackageReference set. It never validates that
    `resolved` is what a real resolution would produce. A CentralTransitive fork is not a change to
    the package reference set, so `--locked-mode` is green on it by construction — with or without a
    clean obj/. Adding a `--locked-mode` step would therefore have installed a permanently green
    gate for the defect this check exists for.

    FOUR CLASSES CHECKED.

      1. Fork inside a lock — a `resolved` version below its own `requested` lower bound. This is
         the #3136 shape. Known live instances are registered in the exemption table (below) and
         nothing else is tolerated.
      2. A tampered lock — every registered lock's SHA-256 must equal the manifest's record. The
         manifest is, structurally, a hash ledger for the lock files.
      3. A project in the closure with no lock — the seed project's transitive ProjectReference
         closure is recomputed from the .csproj files and must equal the registered lock set exactly,
         in both directions, and every registered lock must exist on disk.
      4. Manifest drift — every `inputs` entry must exist and hash-match.

    NOT CHECKED, deliberately: whether a lock's `contentHash` is the package's true hash. Verifying
    that requires downloading every package, so it is out of scope and stated here rather than left
    to be assumed.

    THE EXEMPTION TABLE.

    Class 1 has live instances on main that predate this gate and are not this ticket's to fix
    (they change dependency versions, which needs its own evidence surface). They are registered in
    scripts/restore-lock-drift-exemptions.json under rules that keep the table from becoming an
    escape hatch:

      * a registration matches on the full tuple — lock path, package id, requested range and
        resolved version, all compared ordinally. No wildcards, no `skip` verb, no per-file opt-out.
        Any one of the four moving means the registration stops matching and the violation is
        reported.
      * every registration must carry an issue reference.
      * a registration that matches nothing is itself a failure. Without that reverse check an
        exemption outlives the defect it excused and then silently covers the next real fork that
        lands on the same tuple.
#>

[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    # Overridden only by the contract test, which points the checker at throwaway fixtures.
    [string] $ManifestPath = 'docs/reference/api/business-gateway-surface-restore.manifest.json',

    [string] $ExemptionPath = 'scripts/restore-lock-drift-exemptions.json'
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
$errors = [System.Collections.Generic.List[string]]::new()

# ScriptAutomation.ps1 turns on `Set-StrictMode -Version Latest`, under which reading a property that
# does not exist throws instead of yielding $null. Lock entries are heterogeneous — a `"type":
# "Project"` entry carries no `requested`/`resolved` — so every read of parsed JSON goes through
# here. Returning $null for an absent property is the point: callers decide whether absence is
# legal, and none of them can be silently skipped by a throw.
function Get-NervJsonProperty {
    param(
        [Parameter(Mandatory)] [AllowNull()] $Object,
        [Parameter(Mandatory)] [string] $Name)

    if ($null -eq $Object) { return $null }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }

    return $property.Value
}

function Get-RepositoryRelativeLockPath {
    param([Parameter(Mandatory)] [string] $ProjectPath)

    $directory = [System.IO.Path]::GetDirectoryName($ProjectPath.Replace('\', '/'))
    if ([string]::IsNullOrEmpty($directory)) {
        return 'packages.lock.json'
    }

    return ($directory.Replace('\', '/') + '/packages.lock.json')
}

# Normalizes 'a/b/../c' to 'a/c' without touching the filesystem: the closure is computed from
# declared ProjectReference text, and resolving through the disk would turn a missing project into a
# path problem instead of the contract violation it is.
function Resolve-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $BaseDirectory,
        [Parameter(Mandatory)] [string] $RelativePath)

    $combined = if ([string]::IsNullOrEmpty($BaseDirectory)) { $RelativePath.Replace('\', '/') }
                else { $BaseDirectory.Replace('\', '/') + '/' + $RelativePath.Replace('\', '/') }

    $segments = [System.Collections.Generic.List[string]]::new()
    foreach ($segment in $combined.Split('/')) {
        if ([string]::Equals($segment, '', [StringComparison]::Ordinal) -or
            [string]::Equals($segment, '.', [StringComparison]::Ordinal)) {
            continue
        }

        if ([string]::Equals($segment, '..', [StringComparison]::Ordinal)) {
            if ($segments.Count -gt 0) { $segments.RemoveAt($segments.Count - 1) }
            continue
        }

        $segments.Add($segment)
    }

    return ($segments -join '/')
}

function ConvertTo-NervVersionParts {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }

    $core = $Value.Trim()
    $plus = $core.IndexOf('+', [StringComparison]::Ordinal)
    if ($plus -ge 0) { $core = $core.Substring(0, $plus) }

    $prerelease = ''
    $dash = $core.IndexOf('-', [StringComparison]::Ordinal)
    if ($dash -ge 0) {
        $prerelease = $core.Substring($dash + 1)
        $core = $core.Substring(0, $dash)
    }

    $numbers = [System.Collections.Generic.List[int]]::new()
    foreach ($part in $core.Split('.')) {
        $parsed = 0
        if (-not [int]::TryParse($part, [ref] $parsed)) { return $null }
        $numbers.Add($parsed)
    }

    if ($numbers.Count -eq 0) { return $null }

    return [pscustomobject]@{ Numbers = $numbers; Prerelease = $prerelease }
}

# Compares two NuGet release versions. A prerelease-labelled version sorts below the same release
# version, matching SemVer; build metadata is ignored. Returns $null when either side cannot be
# parsed, and every caller treats $null as a failure rather than as "no violation" — a version this
# function cannot read must not be able to buy silence.
function Compare-NervPackageVersion {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Left,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Right)

    $leftParts = ConvertTo-NervVersionParts -Value $Left
    $rightParts = ConvertTo-NervVersionParts -Value $Right
    if ($null -eq $leftParts -or $null -eq $rightParts) { return $null }

    $width = [Math]::Max($leftParts.Numbers.Count, $rightParts.Numbers.Count)
    for ($i = 0; $i -lt $width; $i++) {
        $l = if ($i -lt $leftParts.Numbers.Count) { $leftParts.Numbers[$i] } else { 0 }
        $r = if ($i -lt $rightParts.Numbers.Count) { $rightParts.Numbers[$i] } else { 0 }
        if ($l -ne $r) { return [Math]::Sign($l - $r) }
    }

    $leftHasPrerelease = -not [string]::IsNullOrEmpty($leftParts.Prerelease)
    $rightHasPrerelease = -not [string]::IsNullOrEmpty($rightParts.Prerelease)
    if (-not $leftHasPrerelease -and $rightHasPrerelease) { return 1 }
    if ($leftHasPrerelease -and -not $rightHasPrerelease) { return -1 }
    if (-not $leftHasPrerelease -and -not $rightHasPrerelease) { return 0 }

    return [Math]::Sign([string]::CompareOrdinal($leftParts.Prerelease, $rightParts.Prerelease))
}

# Parses the interval notation NuGet writes into lock files. Only '[lower, )', '[lower, upper)' and
# '[lower, upper]' occur there; anything else returns $null and is reported as unreadable rather than
# passed over.
function ConvertFrom-NervVersionRange {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Range)

    if ([string]::IsNullOrWhiteSpace($Range)) { return $null }

    $match = [regex]::Match($Range.Trim(), '^\[(?<lower>[^,\]\)\s]+)\s*,\s*(?<upper>[^,\]\)\s]*)(?<close>[\]\)])$')
    if (-not $match.Success) { return $null }

    return [pscustomobject]@{
        Lower = $match.Groups['lower'].Value
        Upper = $match.Groups['upper'].Value
        UpperInclusive = [string]::Equals($match.Groups['close'].Value, ']', [StringComparison]::Ordinal)
    }
}

$manifestFullPath = Join-Path $RepositoryRoot $ManifestPath
$exemptionFullPath = Join-Path $RepositoryRoot $ExemptionPath

$manifest = $null
if (-not (Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    $errors.Add("Restore manifest does not exist: $ManifestPath.")
}
else {
    try {
        $manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
    }
    catch {
        $errors.Add("Restore manifest $ManifestPath is not valid JSON: $($_.Exception.Message)")
    }
}

$exemptions = @()
if (-not (Test-Path -LiteralPath $exemptionFullPath -PathType Leaf)) {
    $errors.Add("Exemption table does not exist: $ExemptionPath. It must exist even when it registers " +
        'nothing, so that deleting it cannot quietly turn a registered fork into an unreported one.')
}
else {
    try {
        $exemptionDocument = Get-Content -LiteralPath $exemptionFullPath -Raw | ConvertFrom-Json
        $exemptions = @(Get-NervJsonProperty -Object $exemptionDocument -Name 'exemptions')
    }
    catch {
        $errors.Add("Exemption table $ExemptionPath is not valid JSON: $($_.Exception.Message)")
    }
}

foreach ($exemption in $exemptions) {
    foreach ($field in @('lockPath', 'package', 'requested', 'resolved', 'issue')) {
        if ([string]::IsNullOrWhiteSpace([string] (Get-NervJsonProperty -Object $exemption -Name $field))) {
            $errors.Add("An entry in $ExemptionPath is missing the required '$field' field. Every " +
                'registration must pin the full tuple and name the issue tracking its removal.')
        }
    }

    $issue = [string] (Get-NervJsonProperty -Object $exemption -Name 'issue')
    if (-not [string]::IsNullOrWhiteSpace($issue) -and -not [regex]::IsMatch($issue, '^#[0-9]+$')) {
        $package = [string] (Get-NervJsonProperty -Object $exemption -Name 'package')
        $errors.Add("Exemption for '$package' in $ExemptionPath has issue '$issue', which is not a " +
            "'#<number>' reference.")
    }
}

$matchedExemptionKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$closureProjects = [System.Collections.Generic.List[string]]::new()
$inspectedDependencyCount = 0
$manifestInputs = @()

if ($null -ne $manifest) {
    $seedProject = [string] (Get-NervJsonProperty -Object $manifest -Name 'project')
    if ([string]::IsNullOrWhiteSpace($seedProject)) {
        $errors.Add("$ManifestPath declares no seed 'project', so the ProjectReference closure cannot be computed.")
    }

    $manifestInputs = @(Get-NervJsonProperty -Object $manifest -Name 'inputs')
    if ($manifestInputs.Count -eq 0) {
        $errors.Add("$ManifestPath declares no 'inputs'. An empty ledger hash-matches vacuously.")
    }

    $declaredLockPaths = @(Get-NervJsonProperty -Object (Get-NervJsonProperty -Object $manifest -Name 'lock') -Name 'paths')
    if ($declaredLockPaths.Count -eq 0) {
        $errors.Add("$ManifestPath declares no 'lock.paths'. An empty lock set makes the closure comparison vacuous.")
    }

    # --- Classes 4 and 2: every recorded input must exist and hash-match. Because the manifest
    # records a SHA-256 for each lock file, it is structurally a hash ledger for them, and a tampered
    # lock is reported here without any restore having to run. ---
    $inputPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($inputEntry in $manifestInputs) {
        $path = [string] (Get-NervJsonProperty -Object $inputEntry -Name 'path')
        if ([string]::IsNullOrWhiteSpace($path)) {
            $errors.Add("$ManifestPath contains an input entry with no 'path'.")
            continue
        }

        if (-not $inputPaths.Add($path)) {
            $errors.Add("$ManifestPath records '$path' more than once; a duplicated entry lets one copy be " +
                'updated while a stale copy keeps passing.')
        }

        $recorded = [string] (Get-NervJsonProperty -Object $inputEntry -Name 'sha256')
        if ([string]::IsNullOrWhiteSpace($recorded)) {
            $errors.Add("$ManifestPath records no sha256 for '$path'.")
            continue
        }

        $full = Join-Path $RepositoryRoot $path
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            $errors.Add("$ManifestPath pins '$path' but that file does not exist.")
            continue
        }

        $actual = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [string]::Equals($actual, $recorded.ToLowerInvariant(), [StringComparison]::Ordinal)) {
            $errors.Add("Restore input '$path' has drifted from the manifest: recorded $recorded, actual " +
                "$actual. Either the change was never registered, or the manifest was updated without the " +
                'file. Establish which before touching the recorded hash; re-baselining it is approving a ' +
                'contract change on someone else behalf.')
        }
    }

    # --- Class 3: the ProjectReference closure must equal the registered lock set, both directions. ---
    if (-not [string]::IsNullOrWhiteSpace($seedProject)) {
        $pending = [System.Collections.Generic.Queue[string]]::new()
        $pending.Enqueue($seedProject.Replace('\', '/'))
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

        while ($pending.Count -gt 0) {
            $projectPath = $pending.Dequeue()
            if (-not $seen.Add($projectPath)) { continue }

            $closureProjects.Add($projectPath)

            $projectFull = Join-Path $RepositoryRoot $projectPath
            if (-not (Test-Path -LiteralPath $projectFull -PathType Leaf)) {
                $errors.Add("Project '$projectPath' is in the ProjectReference closure but does not exist on disk.")
                continue
            }

            $projectText = Get-Content -LiteralPath $projectFull -Raw
            $baseDirectory = [string] [System.IO.Path]::GetDirectoryName($projectPath)
            foreach ($reference in [regex]::Matches($projectText, '<ProjectReference\s[^>]*Include\s*=\s*"(?<include>[^"]+)"')) {
                $pending.Enqueue((Resolve-RepositoryRelativePath -BaseDirectory $baseDirectory.Replace('\', '/') -RelativePath $reference.Groups['include'].Value))
            }
        }
    }

    if ($closureProjects.Count -eq 0) {
        $errors.Add('The ProjectReference closure came out empty; an empty closure equals an empty lock set vacuously.')
    }

    $expectedLockPaths = @()
    if ($closureProjects.Count -gt 0) {
        $expectedLockPaths = @(Get-NervStringsSorted `
            -Values @($closureProjects | ForEach-Object { Get-RepositoryRelativeLockPath -ProjectPath $_ }) `
            -Comparer ([StringComparer]::Ordinal) -Unique)
    }

    $declaredLockSet = [System.Collections.Generic.HashSet[string]]::new([string[]] @($declaredLockPaths | ForEach-Object { [string] $_ }), [System.StringComparer]::Ordinal)
    $expectedLockSet = [System.Collections.Generic.HashSet[string]]::new([string[]] $expectedLockPaths, [System.StringComparer]::Ordinal)

    foreach ($expected in $expectedLockPaths) {
        if (-not $declaredLockSet.Contains($expected)) {
            $errors.Add("'$expected' is required by the ProjectReference closure but is not registered in " +
                "$ManifestPath under 'lock.paths'. A project entered the closure without its restore contract " +
                'being registered with it.')
        }

        if (-not $inputPaths.Contains($expected)) {
            $errors.Add("'$expected' is required by the closure but is not listed in the manifest 'inputs', so " +
                'nothing pins its hash and it could be edited freely.')
        }
    }

    foreach ($project in $closureProjects) {
        if (-not $inputPaths.Contains($project)) {
            $errors.Add("Project file '$project' is in the ProjectReference closure but is not listed in the " +
                "manifest 'inputs'. Its PackageReference declarations could then change without the " +
                'corresponding lock being updated, and nothing would report it.')
        }
    }

    foreach ($declaredEntry in @($declaredLockPaths)) {
        $declared = [string] $declaredEntry
        if (-not $expectedLockSet.Contains($declared)) {
            $errors.Add("$ManifestPath registers lock '$declared', which no project in the ProjectReference " +
                'closure corresponds to. A stale registration keeps a file under contract after it left the closure.')
        }

        $declaredFull = Join-Path $RepositoryRoot $declared
        if (-not (Test-Path -LiteralPath $declaredFull -PathType Leaf)) {
            $errors.Add("$ManifestPath registers lock '$declared' but that file does not exist.")
        }
    }

    # --- Class 1: a resolved version below its own requested lower bound (the #3136 shape). ---
    foreach ($lockPath in $expectedLockPaths) {
        $lockFull = Join-Path $RepositoryRoot $lockPath
        if (-not (Test-Path -LiteralPath $lockFull -PathType Leaf)) { continue }

        $lockDocument = $null
        try {
            $lockDocument = Get-Content -LiteralPath $lockFull -Raw | ConvertFrom-Json
        }
        catch {
            $errors.Add("Lock '$lockPath' is not valid JSON: $($_.Exception.Message)")
            continue
        }

        $dependencies = Get-NervJsonProperty -Object $lockDocument -Name 'dependencies'
        if ($null -eq $dependencies) {
            $errors.Add("Lock '$lockPath' has no 'dependencies' object, so no fork could be detected in it.")
            continue
        }

        foreach ($targetProperty in $dependencies.PSObject.Properties) {
            foreach ($packageProperty in $targetProperty.Value.PSObject.Properties) {
                $entry = $packageProperty.Value
                $requested = [string] (Get-NervJsonProperty -Object $entry -Name 'requested')
                $resolved = [string] (Get-NervJsonProperty -Object $entry -Name 'resolved')

                # A `"type": "Project"` entry carries neither, and that is legal: a project reference
                # has no NuGet version to fork.
                if ([string]::IsNullOrWhiteSpace($requested) -or [string]::IsNullOrWhiteSpace($resolved)) {
                    continue
                }

                $inspectedDependencyCount++

                $range = ConvertFrom-NervVersionRange -Range $requested
                if ($null -eq $range) {
                    $errors.Add("Lock '$lockPath' records requested range '$requested' for " +
                        "'$($packageProperty.Name)', which this checker cannot parse. An unreadable range is " +
                        'reported rather than skipped.')
                    continue
                }

                $comparison = Compare-NervPackageVersion -Left $resolved -Right $range.Lower
                if ($null -eq $comparison) {
                    $errors.Add("Lock '$lockPath' records versions for '$($packageProperty.Name)' that this " +
                        "checker cannot compare (requested '$requested', resolved '$resolved').")
                    continue
                }

                if ($comparison -ge 0) { continue }

                $key = "$lockPath|$($packageProperty.Name)|$requested|$resolved"
                $registered = @($exemptions | Where-Object {
                    [string]::Equals([string] (Get-NervJsonProperty -Object $_ -Name 'lockPath'), $lockPath, [StringComparison]::Ordinal) -and
                    [string]::Equals([string] (Get-NervJsonProperty -Object $_ -Name 'package'), [string] $packageProperty.Name, [StringComparison]::Ordinal) -and
                    [string]::Equals([string] (Get-NervJsonProperty -Object $_ -Name 'requested'), $requested, [StringComparison]::Ordinal) -and
                    [string]::Equals([string] (Get-NervJsonProperty -Object $_ -Name 'resolved'), $resolved, [StringComparison]::Ordinal)
                })

                if ($registered.Count -gt 0) {
                    $matchedExemptionKeys.Add($key) | Out-Null
                    continue
                }

                $entryType = [string] (Get-NervJsonProperty -Object $entry -Name 'type')
                $errors.Add("Lock '$lockPath' resolves '$($packageProperty.Name)' to $resolved, below the " +
                    "requested lower bound of $requested (entry type '$entryType'). This is the shape that made " +
                    'MediatR compile against 12.5.0 while 14.0.0 loaded at runtime (#3136). Fix the version, or ' +
                    "register the tuple in $ExemptionPath with a tracking issue.")
            }
        }
    }

    if ($inspectedDependencyCount -eq 0) {
        $errors.Add('No lock entry carried both a requested range and a resolved version, so the fork check ' +
            'inspected nothing. A comparison over zero entries passes vacuously.')
    }
}

# --- The reverse check: a registration that no longer matches anything must fail. Without it an
# exemption outlives the defect it excused, and then quietly covers the next real fork that lands on
# the same tuple. ---
foreach ($exemption in $exemptions) {
    $lockPathValue = [string] (Get-NervJsonProperty -Object $exemption -Name 'lockPath')
    $packageValue = [string] (Get-NervJsonProperty -Object $exemption -Name 'package')
    $requestedValue = [string] (Get-NervJsonProperty -Object $exemption -Name 'requested')
    $resolvedValue = [string] (Get-NervJsonProperty -Object $exemption -Name 'resolved')
    $key = "$lockPathValue|$packageValue|$requestedValue|$resolvedValue"
    if (-not $matchedExemptionKeys.Contains($key)) {
        $errors.Add("$ExemptionPath registers an exemption for '$packageValue' in '$lockPathValue' " +
            "(requested $requestedValue, resolved $resolvedValue) that matches nothing. Either the fork was " +
            'fixed and the registration must be deleted, or the tuple moved and the registration is now ' +
            'covering a different fork than the one it was approved for.')
    }
}

if ($errors.Count -gt 0) {
    Write-Host 'Restore lock contract check failed:'
    foreach ($failure in $errors) {
        Write-Host "  $failure"
    }

    exit 1
}

Write-Host 'Restore lock contract check passed:'
Write-Host "  $ManifestPath pins $($manifestInputs.Count) restore inputs, all hash-matched."
Write-Host "  The ProjectReference closure has $($closureProjects.Count) projects, each with a registered lock."
Write-Host "  $inspectedDependencyCount versioned lock entries checked for requested/resolved forks."
Write-Host "  $($matchedExemptionKeys.Count) of $($exemptions.Count) registered exemptions matched a live fork."

exit 0
