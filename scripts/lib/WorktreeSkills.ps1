# Script-Governance:
#   Category: library
#   SideEffects:
#     - Creates the agent link layer (.claude/skills/**) for a caller-provided worktree root
#   Writes:
#     - .claude/skills/**
#   Cleanup:
#     - None required; entries are bounded by the payload directory that drives them
#   Requires:
#     - PowerShell 7

# `npx skills` keeps the real skill payload in .agents/skills/ and exposes it to each
# agent runtime through a directory of relative symlinks
# (.claude/skills/<name> -> ../../.agents/skills/<name>).
#
# `npx skills experimental_install` restores the payload from skills-lock.json but
# creates no agent links, so the link layer has to be derived locally. Deriving it from
# another worktree's .claude/skills only works on a machine where that worktree was
# seeded by hand; the payload is the authoritative set.

$script:NervAgentSkillsRelative = '.agents/skills'
$script:NervClaudeSkillsRelative = '.claude/skills'

function Get-NervSkillPayloadNames {
    <#
        .SYNOPSIS
        Names of the skills whose payload is present in a worktree.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $payloadRoot = Join-Path $RepoRoot $script:NervAgentSkillsRelative
    if (-not (Test-Path -LiteralPath $payloadRoot)) { return @() }

    # Directories only: a stray file under .agents/skills is not a skill, and linking it
    # would publish a broken entry to every agent runtime.
    return @(Get-ChildItem -LiteralPath $payloadRoot -Force -Directory | ForEach-Object { $_.Name })
}

function Test-NervSkillsPayloadPresent {
    <#
        .SYNOPSIS
        True when a worktree holds at least one installed skill payload.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    # Guard on content, not existence: a mirror that fails midway leaves an empty
    # .agents/skills behind, and an existence check would treat that as "installed"
    # forever after.
    $payloadRoot = Join-Path $RepoRoot $script:NervAgentSkillsRelative
    if (-not (Test-Path -LiteralPath $payloadRoot)) { return $false }
    return @(Get-ChildItem -LiteralPath $payloadRoot -Force).Count -gt 0
}

function New-NervSkillLinkLayer {
    <#
        .SYNOPSIS
        Rebuilds .claude/skills so every installed payload is reachable by the agent.

        .DESCRIPTION
        Idempotent: an entry that already exists is left alone, so a worktree seeded by
        `npx skills add` keeps whatever that produced.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    # @() 包裹：PowerShell 在 return 时会把空数组解包成 $null，直接取 .Count 会抛。
    $payloadNames = @(Get-NervSkillPayloadNames -RepoRoot $RepoRoot)
    if ($payloadNames.Count -eq 0) { return }

    $linkDir = Join-Path $RepoRoot $script:NervClaudeSkillsRelative
    New-Item -ItemType Directory -Path $linkDir -Force | Out-Null

    foreach ($name in $payloadNames) {
        $entry = Join-Path $linkDir $name
        if (Test-Path -LiteralPath $entry) { continue }

        $relativeTarget = Join-Path '..' (Join-Path '..' (Join-Path $script:NervAgentSkillsRelative $name))
        try {
            New-Item -ItemType SymbolicLink -Path $entry -Target $relativeTarget -Force | Out-Null
        }
        catch {
            # Windows without developer mode cannot create symlinks; a real copy still works.
            $payload = Join-Path (Join-Path $RepoRoot $script:NervAgentSkillsRelative) $name
            Copy-Item -LiteralPath $payload -Destination $entry -Recurse -Force
        }
    }
}
