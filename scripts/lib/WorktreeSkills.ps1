# Script-Governance:
#   Category: library
#   SideEffects:
#     - Runs the caller-injected skill install action in the main worktree
#     - Mirrors the main worktree's installed payload into .agents/skills when a worktree has none
#     - Recursively deletes and recreates .agents/skills/<name> for every directory under skills/
#     - Creates the agent link layer (.claude/skills/**) for a caller-provided worktree root
#   Writes:
#     - .agents/skills/**
#     - .claude/skills/**
#   Cleanup:
#     - None required; both layers are bounded by the skills/ sources and payload entries that drive them
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
$script:NervRepoSkillsRelative = 'skills'

function Get-NervSkillPayloadNames {
    <#
        .SYNOPSIS
        Names of the skills whose payload is present in a worktree.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $payloadRoot = Join-Path $RepoRoot $script:NervAgentSkillsRelative
    if (-not (Test-Path -LiteralPath $payloadRoot)) { return @() }

    # Directories only, and this is the single decision point for what counts as an installed
    # payload entry: both the agent link layer and, through Get-NervNonRepoPayloadNames, the
    # install/mirror gate read it. A stray file under .agents/skills is not a skill — linking it
    # would publish a broken entry to every agent runtime, and counting it would make the gate
    # report "installed" (#3465).
    return @(Get-ChildItem -LiteralPath $payloadRoot -Force -Directory | ForEach-Object { $_.Name })
}

function Get-NervRepoSkillNames {
    <#
        .SYNOPSIS
        Names of the skills whose source is tracked in this repository under skills/.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $sourceRoot = Join-Path $RepoRoot $script:NervRepoSkillsRelative
    if (-not (Test-Path -LiteralPath $sourceRoot)) { return @() }

    return @(Get-ChildItem -LiteralPath $sourceRoot -Force -Directory | ForEach-Object { $_.Name })
}

function Get-NervNonRepoPayloadNames {
    <#
        .SYNOPSIS
        Names of the installed payload entries this worktree's own skills/ does not provide —
        i.e. everything under .agents/skills that only the install or the mirror can supply.

        .DESCRIPTION
        The single place the "provided by this worktree / has to come from outside" split is
        expressed. The gate that decides whether to install and mirror, and the mirror that
        carries the payload, must move the same set: if the gate counts an entry the mirror has
        to carry it, and an entry the gate does not count must not be carried either.

        Deliberately NOT named after skills-lock.json: the lock does own these entries, but it
        also owns nerv-pr-review and nerv-task-delivery as sourceType "local" (41 entries, both
        present — implementation checked), and those two are exactly what this function
        subtracts. "Lock-owned" would therefore name the complement of what it returns.

        What counts as a payload entry is not decided here: it is Get-NervSkillPayloadNames'
        one rule (skill directories only), so the link layer and this gate cannot disagree about
        it. That matters because a stray file used to be counted, and macOS writes .DS_Store into
        any directory Finder has visited — one such file was enough to make the gate report
        "installed" and stop the install and mirror from ever firing again (#3465).
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $repoOwned = [System.Collections.Generic.HashSet[string]]::new(
        [string[]] @(Get-NervRepoSkillNames -RepoRoot $RepoRoot), [StringComparer]::Ordinal)
    return @(Get-NervSkillPayloadNames -RepoRoot $RepoRoot |
            Where-Object { -not $repoOwned.Contains($_) })
}

function Test-NervSkillsPayloadPresent {
    <#
        .SYNOPSIS
        True when a worktree holds at least one installed payload that this worktree's own
        skills/ does not provide — the payload the lock-driven install and mirror exist to bring in.

        .DESCRIPTION
        This is the gate for the lock-driven install and mirror, so it counts exactly what
        those two move. Repo-tracked skills land in the same directory unconditionally
        (Sync-NervRepoSkillPayload), so counting them would make the gate report "installed"
        the moment the repo publishes its own skills — after which a main worktree whose
        install failed once would never install or mirror again, and every third-party skill
        in skills-lock.json would stay silently missing.

        Guard on content, not existence: a mirror that fails midway leaves an empty
        .agents/skills behind, and an existence check would treat that as "installed" forever.

        Failure direction if skills-lock.json ever held nothing but repo-tracked skills: the
        gate stays false and the install re-runs each session — noisy, not silently missing.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    return @(Get-NervNonRepoPayloadNames -RepoRoot $RepoRoot).Count -gt 0
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

function Sync-NervRepoSkillPayload {
    <#
        .SYNOPSIS
        Republishes every repo-tracked skill so the installed payload equals its source.

        .DESCRIPTION
        `skills/<name>` is the tracked source of a project skill; `.agents/skills/<name>` is
        what an agent actually loads. Installing and mirroring both stop once a payload
        exists, so without this step an edit to the source never reaches a worktree that was
        already seeded, and the agent keeps loading the pre-edit text with nothing failing.
    #>
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $sourceRoot = Join-Path $RepoRoot $script:NervRepoSkillsRelative
    if (-not (Test-Path -LiteralPath $sourceRoot)) { return }

    $payloadRoot = Join-Path $RepoRoot $script:NervAgentSkillsRelative
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

    foreach ($name in Get-NervRepoSkillNames -RepoRoot $RepoRoot) {
        $source = Join-Path $sourceRoot $name
        $target = Join-Path $payloadRoot $name
        # 先删后拷：Copy-Item -Force 只覆盖同名文件，源里已删除的文件会永远留在安装层。
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
    }
}

function Initialize-NervWorktreeSkills {
    <#
        .SYNOPSIS
        Brings one worktree's agent skills to their required state.

        .DESCRIPTION
        Three steps in a fixed order, and the order is the contract:

        1. The lock-driven payload (skills-lock.json, third-party) is installed in the main
           worktree and mirrored into this one — but only while this worktree has none, because
           that install costs minutes and the payload is identical across worktrees.
        2. Every repo-tracked skill is republished from skills/ **unconditionally**. Step 1 stops
           at "a payload exists", so without this an edit to skills/ never reaches an
           already-seeded worktree and the agent keeps loading the pre-edit text with nothing
           failing. Running it inside step 1's else branch reintroduces exactly that drift.
        3. The link layer is rebuilt last, so a skill first published by step 2 is reachable.
           Rebuilding before step 2 leaves a newly added skill with no agent entry.

        MainRoot is the main worktree that owns the install; pass an empty string when it cannot
        be resolved. InstallAction receives the main worktree root and runs the skills CLI there;
        it is injected so the contract test can drive this whole flow without the network.
    #>
    param(
        [Parameter(Mandatory)] [string] $RepoRoot,
        [string] $MainRoot,
        [Parameter(Mandatory)] [scriptblock] $InstallAction
    )

    if ([string]::IsNullOrWhiteSpace($MainRoot)) {
        Write-Host '[setup] skills: skipped (main worktree root unknown)'
    }
    elseif (Test-NervSkillsPayloadPresent -RepoRoot $RepoRoot) {
        Write-Host '[setup] skills present - skipping'
    }
    else {
        if (-not (Test-NervSkillsPayloadPresent -RepoRoot $MainRoot)) {
            # Only ever install in the main worktree, so every future worktree copies from it.
            Write-Host '[setup] skills: npx skills experimental_install (main worktree)'
            try {
                & $InstallAction $MainRoot | Out-Null
            }
            catch {
                Write-Warning "[setup] skills install failed: $($_.Exception.Message)"
            }
        }

        if (Test-NervSkillsPayloadPresent -RepoRoot $MainRoot) {
            Write-Host '[setup] skills: mirroring .agents/skills from the main worktree'
            try {
                $mainPayload = Join-Path $MainRoot $script:NervAgentSkillsRelative
                $targetPayload = Join-Path $RepoRoot $script:NervAgentSkillsRelative
                New-Item -ItemType Directory -Path $targetPayload -Force | Out-Null
                # Carry exactly what the gate counted, read against the main worktree's own
                # skills/. Repo-tracked skills are republished from this worktree's source in
                # step 2, so mirroring main's copy of them would either be overwritten (same
                # name) or linger with no source here (name only main has).
                foreach ($name in Get-NervNonRepoPayloadNames -RepoRoot $MainRoot) {
                    Copy-Item -LiteralPath (Join-Path $mainPayload $name) -Destination (Join-Path $targetPayload $name) -Recurse -Force
                }
            }
            catch {
                Write-Warning "[setup] skills mirror failed: $($_.Exception.Message)"
            }
        }
        else {
            Write-Host '[setup] skills: unavailable in the main worktree - skipping'
        }
    }

    Sync-NervRepoSkillPayload -RepoRoot $RepoRoot
    New-NervSkillLinkLayer -RepoRoot $RepoRoot
}
