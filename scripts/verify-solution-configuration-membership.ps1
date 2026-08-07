# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads every tracked solution file and the project files reachable from it
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
    Fails when a solution builds a project that is not one of its own members.

.DESCRIPTION
    MSBuild resolves a project's Configuration through the *solution's* configuration map. A project
    that a solution only reaches as a transitive ProjectReference has no entry in that map, so it
    falls back to its own default (Debug) even when the solution is built with
    `--configuration Release`. Nothing in the build output fails: the Release assemblies simply link
    against a Debug dependency.

    This has now happened twice in this repository:

      * backend/common/Contracts/Nerv.IIP.Contracts.Mes was missing from backend/Nerv.IIP.sln, so all
        four Release shards emitted it into bin/Debug (MAN-669 PR-B, run 31136085020).
      * backend/common/Sdk/Nerv.IIP.Sdk.Ops, backend/common/Contracts/Nerv.IIP.Contracts.Ops and
        backend/common/Contracts/Nerv.IIP.Contracts.IntegrationEvents were missing from
        connector-hosts/Nerv.IIP.ConnectorHost.sln, so `dotnet test connector-hosts/… -c Release`
        emitted those three into bin/Debug (MAN-669 PR-C, runs 31143773140 and 31138913408).

    PR-B's fix was a *directory* rule inside scripts/verify-backend-test-shards.ps1: every csproj
    under backend/ must be a member of backend/Nerv.IIP.sln. That rule is per-solution by
    construction and could not see the connector-host solution, whose leak came from projects that
    live under backend/ and are perfectly good members of the *backend* solution.

    This script enforces the underlying invariant instead, once per solution and independent of
    where the projects live: **the transitive ProjectReference closure of a solution's members must
    contain no non-members**. It complements rather than replaces the directory rule, which also
    catches an orphan backend project that nothing references at all.

    There is deliberately no allowlist. A registered exception would be a project knowingly built
    under the wrong configuration, which is not a debt anyone can carry. If a future change
    genuinely needs one, the exemption path is to edit this script (with its contract test) and go
    through script governance; see docs/architecture/script-automation-governance.md.
#>

[CmdletBinding()]
param(
    # Repo-relative solution paths. Defaults to every solution this repository builds in CI. The two
    # solutions stay separate by design (AGENTS.md "Do NOT" #2) — this check reads both, it never
    # merges them.
    [string[]] $SolutionPath = @(
        'backend/Nerv.IIP.sln',
        'connector-hosts/Nerv.IIP.ConnectorHost.sln'
    ),

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

function Get-SolutionMemberProject {
    param(
        [Parameter(Mandatory)] [string] $SolutionFullPath
    )

    $solutionDirectory = Split-Path -Parent $SolutionFullPath
    return @(
        Get-Content -LiteralPath $SolutionFullPath |
            ForEach-Object {
                if ($_ -match '"(?<path>[^"]*\.csproj)"') {
                    Get-NormalizedFullPath -Path (Join-Path $solutionDirectory ($Matches.path -replace '\\', '/'))
                }
            } |
            Sort-Object -Unique
    )
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
            if ($_ -ceq '**') {
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
        Get-ChildItem -LiteralPath $searchRoot -Recurse -File -Filter '*.csproj' |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
            ForEach-Object { Get-NormalizedFullPath -Path $_.FullName } |
            Where-Object { $_.Substring($searchRoot.Length).TrimStart('/') -match $regexPattern } |
            Sort-Object -Unique
    )
}

function Get-ProjectReferencePath {
    param(
        [Parameter(Mandatory)] [string] $ProjectFullPath
    )

    $projectDirectory = Split-Path -Parent $ProjectFullPath
    $projectText = Get-Content -LiteralPath $ProjectFullPath -Raw
    return @(
        [regex]::Matches($projectText, '<ProjectReference\s[^>]*?Include\s*=\s*"(?<path>[^"]+)"') |
            ForEach-Object { $_.Groups['path'].Value -split ';' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Expand-ProjectReferenceGlob -ProjectDirectory $projectDirectory -Include $_.Trim() } |
            Sort-Object -Unique
    )
}

$errors = [System.Collections.Generic.List[string]]::new()
$normalizedRoot = Get-NormalizedFullPath -Path $RepositoryRoot
$summaryLines = [System.Collections.Generic.List[string]]::new()

foreach ($relativeSolution in $SolutionPath) {
    $solutionFullPath = Get-NormalizedFullPath -Path (Join-Path $RepositoryRoot $relativeSolution)
    if (-not (Test-Path -LiteralPath $solutionFullPath -PathType Leaf)) {
        $errors.Add("Solution does not exist: $relativeSolution.")
        continue
    }

    $members = @(Get-SolutionMemberProject -SolutionFullPath $solutionFullPath)
    if ($members.Count -eq 0) {
        $errors.Add("Solution declares no projects, which means it was parsed wrong: $relativeSolution.")
        continue
    }

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

    $nonMembers = @($visited | Where-Object { -not $memberSet.Contains($_) } | Sort-Object)
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

    $summaryLines.Add("${relativeSolution}: $($members.Count) members cover all $($visited.Count) projects in their ProjectReference closure.")
}

if ($errors.Count -gt 0) {
    throw ("Solution configuration membership failed:`n  " + ($errors -join "`n  "))
}

Write-Output 'Solution configuration membership passed:'
foreach ($line in $summaryLines) {
    Write-Output "  $line"
}
