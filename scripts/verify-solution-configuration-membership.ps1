# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads every solution file in the repository and the project files reachable from it
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
    Fails when a solution would build a project under a configuration the solution never mapped.

.DESCRIPTION
    MSBuild resolves a project's Configuration through the solution's *configuration map* — the
    `GlobalSection(ProjectConfigurationPlatforms)` entries that become
    `CurrentSolutionConfigurationContents`. A project the map does not cover falls back to its own
    default (Debug) even when the solution is built with `--configuration Release`. Nothing in the
    build output fails: the Release assemblies simply link against a Debug dependency.

    A project escapes that map in two distinct ways, and this script checks both, because a gate
    that only checks one is the same class of half-covered rule as the bug it is chasing:

      1. **Not a member at all.** The solution reaches the project only as a transitive
         `ProjectReference`, so it has no `Project(...)` declaration and no map entries.
      2. **A member with a missing or wrong map entry.** The project has a `Project(...)`
         declaration — so any membership check based on those lines passes — but
         `ProjectConfigurationPlatforms` lacks its `.ActiveCfg` for some solution configuration, or
         points a `Release|*` solution configuration at a `Debug|*` project configuration.

    Form 1 has happened twice in this repository (MAN-669 PR-B and PR-C). Form 2 has not, but it is
    one hand-edit away and produces byte-for-byte the same symptom, which is why both are checked.

    The evidence for all of that — the leaked projects, the CI run ids showing the bin/Debug lines
    before and after, why fixing form 1 by hand is what makes form 2 reachable, and how PR-C's
    review reproduced form 2 with a two-project fixture — is written down ONCE, in
    docs/architecture/backend-ci-build-strategy.md ("PR-C 实际落地的改动"). Read it there rather
    than here: the same argument repeated in four files is an argument that will disagree with
    itself the first time any of it changes.

    Relationship to the *directory* rule in scripts/verify-backend-test-shards.ps1 (every csproj
    under backend/ must be a member of backend/Nerv.IIP.sln): that rule is per-solution by
    construction and reads only `Project(...)` lines, so it sees neither PR-C's form 1 nor form 2 at
    all. This script complements rather than replaces it — the directory rule still catches an
    orphan backend project that nothing references.

    Solutions are **discovered**, not listed, so a third solution added later is covered
    automatically rather than silently unchecked.

    There is deliberately no allowlist. A registered exception would be a project knowingly built
    under the wrong configuration, which is not a debt anyone can carry. If a future change
    genuinely needs one, the exemption path is to edit this script (with its contract test) and go
    through script governance; see docs/architecture/script-automation-governance.md.
#>

[CmdletBinding()]
param(
    # Repo-relative solution paths. Left empty, every *.sln in the repository is discovered and
    # checked — a new solution must not be able to join the repo unchecked. Supplied explicitly only
    # by the contract test, which points the script at throwaway fixture solutions.
    # The backend and Connector Host solutions stay separate by design (see
    # docs/architecture/repo-layout.md, 放置规则 5); this check reads both and never merges them.
    [string[]] $SolutionPath = @(),

    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    return ([System.IO.Path]::GetFullPath($Path) -replace '\\', '/')
}

# "Which files are part of this repository?" already has an answer, and it is .gitignore — not a
# second list maintained here. Agent tooling puts working copies of the whole repository under
# ignored roots (`/worktrees/`, `/.claude/worktrees/`, `/.agents/worktrees/`, `/.codex/worktrees/`
# are lines 5-8 of .gitignore), so a checker that discovers `*.sln` by walking the tree finds every
# one of those copies too and reports another agent's half-finished solution as this developer's
# failure. Enumerating agent directory names here instead would work, but it is a list that has to
# be extended for every future tool directory, and .gitignore is already that list.
#
# CI is unaffected either way — a fresh checkout has no worktrees — so this is a local-developer
# fix, and the gate's meaning must not change with it: everything git tracks is still discovered,
# including a third solution nobody registered anywhere. The measurements and the weighing of the
# two candidate fixes are in docs/architecture/backend-ci-build-strategy.md ("走查收尾" 第 4 条);
# they are machine- and moment-specific (how many working copies that checkout happened to have),
# so they are deliberately not repeated here. (MAN-669 PR-C follow-up, issue #1496.)
function Select-GitIgnoredPath {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $RelativePath
    )

    $candidates = @($RelativePath)
    if ($candidates.Count -eq 0) {
        return @()
    }

    $ignored = [System.Collections.Generic.List[string]]::new()
    for ($offset = 0; $offset -lt $candidates.Count; $offset += 100) {
        $batch = @($candidates | Select-Object -Skip $offset -First 100)
        try {
            $result = Invoke-NativeCommandOutput `
                -Command 'git' `
                -Arguments (@('check-ignore', '--') + $batch) `
                -WorkingDirectory $Root `
                -TimeoutSeconds 60 `
                -Name 'solution-discovery-check-ignore'
            foreach ($line in ($result.Stdout -split "`r?`n")) {
                $trimmed = $line.Trim()
                if (-not [string]::IsNullOrWhiteSpace($trimmed)) { $ignored.Add($trimmed) }
            }
        }
        catch {
            # `git check-ignore` exits 1 when nothing matched — a documented answer, not a failure.
            # Anything else (128 = not a git work tree, or git missing altogether) means the
            # question cannot be asked here at all; that is the normal case for the contract test's
            # throwaway fixture roots. Fall back to discovering everything rather than to
            # discovering nothing: over-discovery is a visible failure, under-discovery is silence.
            #
            # Note this discards whatever earlier batches had already collected, on purpose: a
            # partial ignore list would filter some copies and keep others, which is a third
            # behaviour nobody reasoned about. Only reachable past 100 discovered solutions, and it
            # fails in the safe direction (everything checked).
            $exitCode = $_.Exception.Data['ExitCode']
            if ($null -eq $exitCode -or (-not (([int] $exitCode) -eq (1)))) {
                return @()
            }
        }
    }

    return @($ignored)
}

function Get-DiscoveredSolutionPath {
    param(
        [Parameter(Mandatory)] [string] $Root
    )

    # Build output is excluded structurally as well: it is never a solution anyone edits, and this
    # keeps working when the root is not a git work tree.
    $discovered = @(
        Get-NervStringsSorted -Values @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.sln' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj|node_modules|artifacts|\.git)[/\\]' } |
            ForEach-Object { ([System.IO.Path]::GetRelativePath($Root, $_.FullName) -replace '\\', '/') }) -Comparer ([StringComparer]::Ordinal) -Unique
    )

    $ignored = @(Select-GitIgnoredPath -Root $Root -RelativePath $discovered)
    if ($ignored.Count -eq 0) {
        return $discovered
    }

    return @($discovered | Where-Object { $ignored -notcontains $_ })
}

# One pass over the solution text produces both halves of the picture: which projects the solution
# declares, and what configuration each of them is mapped to. Reading only the first half is the
# gap this script exists to close, so they are deliberately parsed together and returned together.
function Get-SolutionModel {
    param(
        [Parameter(Mandatory)] [string] $SolutionFullPath
    )

    $solutionDirectory = Split-Path -Parent $SolutionFullPath
    $projects = [System.Collections.Generic.List[object]]::new()
    $solutionConfigurations = [System.Collections.Generic.List[string]]::new()
    # key: "<project guid>|<solution configuration>" -> ActiveCfg value
    $activeConfiguration = @{}

    $section = 'none'
    foreach ($rawLine in (Get-Content -LiteralPath $SolutionFullPath)) {
        $line = $rawLine.Trim()

        if ($line -match '^GlobalSection\(SolutionConfigurationPlatforms\)') { $section = 'solution-configs'; continue }
        if ($line -match '^GlobalSection\(ProjectConfigurationPlatforms\)') { $section = 'project-configs'; continue }
        if ($line -match '^GlobalSection\(') { $section = 'other'; continue }
        if ($line -match '^EndGlobalSection') { $section = 'none'; continue }

        # Project("{type}") = "Name", "relative\path.csproj", "{project guid}"
        # Solution folders use the same shape but carry no project file, so the `.csproj` anchor
        # excludes them; they legitimately have no configuration map entries.
        if ($line -match '^Project\("\{[^}]+\}"\)\s*=\s*"[^"]*",\s*"(?<path>[^"]+\.csproj)",\s*"\{(?<guid>[^}]+)\}"') {
            $projects.Add([pscustomobject]@{
                Guid = $Matches.guid.ToUpperInvariant()
                FullPath = Get-NormalizedFullPath -Path (Join-Path $solutionDirectory ($Matches.path -replace '\\', '/'))
            })
            continue
        }

        if ([string]::Equals([string]($section), [string]('solution-configs'), [StringComparison]::OrdinalIgnoreCase) -and $line -match '^(?<name>[^=]+?)\s*=\s*\S') {
            $solutionConfigurations.Add($Matches.name.Trim())
            continue
        }

        # {guid}.<solution configuration>.ActiveCfg = <project configuration>
        # Build.0/Deploy.0 lines are ignored: ActiveCfg is what selects the configuration, Build.0
        # only decides whether the solution builds it.
        if ([string]::Equals([string]($section), [string]('project-configs'), [StringComparison]::OrdinalIgnoreCase) -and
            $line -match '^\{(?<guid>[^}]+)\}\.(?<solutionConfiguration>.+?)\.ActiveCfg\s*=\s*(?<projectConfiguration>.+)$') {
            $activeConfiguration["$($Matches.guid.ToUpperInvariant())|$($Matches.solutionConfiguration.Trim())"] =
                $Matches.projectConfiguration.Trim()
        }
    }

    return [pscustomobject]@{
        Projects = $projects
        SolutionConfigurations = @(Get-NervStringsSorted -Values @($solutionConfigurations) -Comparer ([StringComparer]::Ordinal) -Unique)
        ActiveConfiguration = $activeConfiguration
    }
}

# MSBuild lets a ProjectReference Include be a glob, and this repository uses one
# (backend/tests/Nerv.IIP.MigrationGovernance.Tests references ..\..\services\**\*.Infrastructure.csproj).
# Treating that literal string as a path would report a phantom missing project and hide every real
# finding behind it, so globs are expanded here the way MSBuild expands them: `**` crosses directory
# separators, `*` and `?` do not.
function Expand-ProjectReferenceGlob {
    param(
        [Parameter(Mandatory)] [string] $ProjectDirectory,
        [Parameter(Mandatory)] [string] $Include
    )

    $normalizedInclude = $Include -replace '\\', '/'
    if ($normalizedInclude -notmatch '[*?]') {
        return @(Get-NormalizedFullPath -Path (Join-Path $ProjectDirectory $normalizedInclude))
    }

    $segments = @($normalizedInclude -split '/')
    $fixedSegments = [System.Collections.Generic.List[string]]::new()
    foreach ($segment in $segments) {
        if ($segment -match '[*?]') { break }
        $fixedSegments.Add($segment)
    }

    $searchRoot = if ($fixedSegments.Count -gt 0) {
        Get-NormalizedFullPath -Path (Join-Path $ProjectDirectory ($fixedSegments -join '/'))
    } else {
        Get-NormalizedFullPath -Path $ProjectDirectory
    }
    if (-not (Test-Path -LiteralPath $searchRoot -PathType Container)) {
        return @()
    }

    $patternSegments = @($segments | Select-Object -Skip $fixedSegments.Count)
    $regexPattern = '^' + (
        @($patternSegments | ForEach-Object {
            if ([string]::Equals([string]($_), [string]('**'), [StringComparison]::Ordinal)) {
                '(?:.*/)?'
            } else {
                [regex]::Escape($_).Replace('\*', '[^/]*').Replace('\?', '[^/]') + '/'
            }
        }) -join ''
    )
    # Every segment above contributed a trailing separator; the last one is the file name, so drop it.
    $regexPattern = $regexPattern -replace '/$', ''
    $regexPattern += '$'

    return @(
        Get-NervStringsSorted -Values @(Get-ChildItem -LiteralPath $searchRoot -Recurse -File -Filter '*.csproj' |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
            ForEach-Object { Get-NormalizedFullPath -Path $_.FullName } |
            Where-Object { $_.Substring($searchRoot.Length).TrimStart('/') -match $regexPattern }) -Comparer ([StringComparer]::Ordinal) -Unique
    )
}

function Get-ProjectReferencePath {
    param(
        [Parameter(Mandatory)] [string] $ProjectFullPath
    )

    $projectDirectory = Split-Path -Parent $ProjectFullPath
    $projectText = Get-Content -LiteralPath $ProjectFullPath -Raw
    return @(
        Get-NervStringsSorted -Values @([regex]::Matches($projectText, '<ProjectReference\s[^>]*?Include\s*=\s*"(?<path>[^"]+)"') |
            ForEach-Object { $_.Groups['path'].Value -split ';' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Expand-ProjectReferenceGlob -ProjectDirectory $projectDirectory -Include $_.Trim() }) -Comparer ([StringComparer]::Ordinal) -Unique
    )
}

$errors = [System.Collections.Generic.List[string]]::new()
$normalizedRoot = Get-NormalizedFullPath -Path $RepositoryRoot
$summaryLines = [System.Collections.Generic.List[string]]::new()

$solutionPaths = @($SolutionPath)
if ($solutionPaths.Count -eq 0) {
    $solutionPaths = @(Get-DiscoveredSolutionPath -Root $normalizedRoot)
    # Narrowing discovery (as the .gitignore filter above does) must never be able to turn the gate
    # into a no-op, so an empty scan is a failure, reported in the same shape as every other one.
    if ($solutionPaths.Count -eq 0) {
        Write-Host 'Solution configuration membership failed:'
        Write-Host "  No solution files were discovered under $RepositoryRoot; the configuration-map check would pass vacuously."
        exit 1
    }
}

foreach ($relativeSolution in $solutionPaths) {
    $solutionFullPath = Get-NormalizedFullPath -Path (Join-Path $RepositoryRoot $relativeSolution)
    if (-not (Test-Path -LiteralPath $solutionFullPath -PathType Leaf)) {
        $errors.Add("Solution does not exist: $relativeSolution.")
        continue
    }

    $model = Get-SolutionModel -SolutionFullPath $solutionFullPath
    $members = @(Get-NervStringsSorted -Values @($model.Projects.FullPath) -Comparer ([StringComparer]::Ordinal) -Unique)
    if ($members.Count -eq 0) {
        $errors.Add("Solution declares no projects, which means it was parsed wrong: $relativeSolution.")
        continue
    }
    if ($model.SolutionConfigurations.Count -eq 0) {
        $errors.Add("Solution declares no SolutionConfigurationPlatforms, so its configuration map cannot be checked: $relativeSolution.")
        continue
    }

    # ---- Form 2: declared members whose configuration map is missing or points the wrong way. ----
    foreach ($project in $model.Projects) {
        $projectRelative = $project.FullPath.Replace("$normalizedRoot/", '')
        foreach ($solutionConfiguration in $model.SolutionConfigurations) {
            $key = "$($project.Guid)|$solutionConfiguration"
            if (-not $model.ActiveConfiguration.ContainsKey($key)) {
                $errors.Add(
                    "$relativeSolution declares $projectRelative but its ProjectConfigurationPlatforms has no " +
                    "'$solutionConfiguration.ActiveCfg' entry, so building that solution configuration resolves the " +
                    'project through its own default (Debug) and emits it into bin/Debug. ' +
                    "Re-add it with 'dotnet sln $relativeSolution add $projectRelative' to regenerate the full map.")
                continue
            }

            # `Release|Any CPU` mapped to `Debug|Any CPU` is a present-but-inverted map entry: it
            # produces exactly the bin/Debug symptom this script exists to catch, and no
            # Project()-line rule can see it.
            $mapped = [string] $model.ActiveConfiguration[$key]
            $solutionKind = ($solutionConfiguration -split '\|')[0].Trim()
            $mappedKind = ($mapped -split '\|')[0].Trim()
            if ($solutionKind -ne $mappedKind) {
                $errors.Add(
                    "$relativeSolution maps $projectRelative from solution configuration '$solutionConfiguration' to " +
                    "project configuration '$mapped'; a '$solutionKind' build would emit it into bin/$mappedKind.")
            }
        }
    }

    # ---- Form 1: projects reachable only as a transitive ProjectReference. ----
    $memberSet = [System.Collections.Generic.HashSet[string]]::new(
        [string[]] $members,
        [System.StringComparer]::OrdinalIgnoreCase)
    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pending = [System.Collections.Generic.Queue[string]]::new()
    foreach ($member in $members) { $pending.Enqueue($member) }

    # Non-members are reported with the member that pulled them in. Without that the failure text
    # names a project nobody put there on purpose and gives no way to find the reference.
    $introducedBy = @{}

    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        if (-not $visited.Add($current)) { continue }
        if (-not (Test-Path -LiteralPath $current -PathType Leaf)) {
            $errors.Add("$relativeSolution references a project file that does not exist: $($current.Replace("$normalizedRoot/", '')).")
            continue
        }

        foreach ($reference in Get-ProjectReferencePath -ProjectFullPath $current) {
            if (-not $introducedBy.ContainsKey($reference)) {
                $introducedBy[$reference] = $current
            }
            $pending.Enqueue($reference)
        }
    }

    $nonMembers = @(Get-NervStringsSorted -Values @($visited | Where-Object { -not $memberSet.Contains($_) }) -Comparer ([StringComparer]::Ordinal))
    foreach ($nonMember in $nonMembers) {
        $referencedBy = if ($introducedBy.ContainsKey($nonMember)) { $introducedBy[$nonMember] } else { '(unknown)' }
        $nonMemberRelative = $nonMember.Replace("$normalizedRoot/", '')
        $referencedByRelative = $referencedBy.Replace("$normalizedRoot/", '')
        $errors.Add(
            "$relativeSolution builds $nonMemberRelative through a transitive ProjectReference from " +
            "$referencedByRelative without listing it as a member, so a Release build resolves it through " +
            'its own default configuration and emits it into bin/Debug. Add it with ' +
            "'dotnet sln $relativeSolution add $nonMemberRelative'.")
    }

    $summaryLines.Add(
        "${relativeSolution}: $($members.Count) members cover all $($visited.Count) projects in their " +
        "ProjectReference closure, and each is mapped for all $($model.SolutionConfigurations.Count) solution configurations.")
}

# Findings are written to stdout and the script exits nonzero, the same shape as
# scripts/check-script-governance.ps1 — deliberately not `throw`, and callers must therefore check
# the exit code rather than share a `run:` block with another script. Why: see
# docs/architecture/backend-ci-build-strategy.md ("走查收尾" 第 3 条).
if ($errors.Count -gt 0) {
    Write-Host 'Solution configuration membership failed:'
    foreach ($failure in $errors) {
        Write-Host "  $failure"
    }

    exit 1
}

Write-Host 'Solution configuration membership passed:'
foreach ($line in $summaryLines) {
    Write-Host "  $line"
}

exit 0
