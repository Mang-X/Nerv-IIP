# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads Git base content and the current working tree
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - Git

param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $BaseCommit,

    [ValidateRange(1, 2147483647)]
    [int] $MaximumLines = 1000,

    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    [ValidateNotNullOrEmpty()]
    [string[]] $GovernedExtension = @('.cs', '.ps1', '.psm1', '.js', '.jsx', '.ts', '.tsx', '.vue')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/SourceSizeGovernance.ps1')

function Invoke-SourceSizeGit {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    return Invoke-NativeCommandOutput `
        -Command 'git' `
        -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 60 `
        -Name $Name
}

function ConvertFrom-SourceSizeGitDiff {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $tokens = @($Text.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))
    $changes = [Collections.Generic.List[object]]::new()
    $seenHeadPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $index = 0
    while ($index -lt $tokens.Count) {
        $statusToken = [string]$tokens[$index]
        $index++
        if ($statusToken.StartsWith('R', [StringComparison]::Ordinal)) {
            if ($index + 1 -ge $tokens.Count) { throw "Rename status '$statusToken' is missing a path." }
            $basePath = [string]$tokens[$index]
            $headPath = [string]$tokens[$index + 1]
            $index += 2
            $status = 'R'
        }
        elseif (@('A', 'M', 'D') -contains $statusToken) {
            if ($index -ge $tokens.Count) { throw "Status '$statusToken' is missing a path." }
            $headPath = [string]$tokens[$index]
            $basePath = $headPath
            $index++
            $status = $statusToken
        }
        else {
            throw "Unsupported Git change status '$statusToken'."
        }

        if ([string]::Equals($status, 'D', [StringComparison]::Ordinal)) { continue }
        if ([string]::IsNullOrWhiteSpace($headPath) -or [string]::IsNullOrWhiteSpace($basePath)) {
            throw "Git status '$statusToken' contains an empty path."
        }
        if (-not $seenHeadPaths.Add($headPath)) { throw "Git reported duplicate head path '$headPath'." }
        $changes.Add([pscustomobject]@{ Status = $status; BasePath = $basePath; HeadPath = $headPath })
    }

    return @($changes)
}

try {
    if ($GovernedExtension.Count -eq 0) { throw 'At least one governed extension is required.' }
    foreach ($extension in $GovernedExtension) {
        if ([string]::IsNullOrWhiteSpace($extension)) { throw 'Governed extensions must not contain empty values.' }
    }

    $resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
    $reportedGitRoot = (Invoke-SourceSizeGit -Arguments @('rev-parse', '--show-toplevel') -Name 'source-size-git-root' -WorkingDirectory $resolvedRepositoryRoot).Stdout.Trim()
    $gitPrefix = (Invoke-SourceSizeGit -Arguments @('rev-parse', '--show-prefix') -Name 'source-size-git-prefix' -WorkingDirectory $resolvedRepositoryRoot).Stdout.Trim()
    if (-not [string]::IsNullOrEmpty($gitPrefix)) {
        throw "RepositoryRoot must be the Git root; received prefix '$gitPrefix'."
    }
    $resolvedGitRoot = (Resolve-Path -LiteralPath $reportedGitRoot -ErrorAction Stop).Path

    Invoke-SourceSizeGit -Arguments @('cat-file', '-e', "$BaseCommit^{commit}") -Name 'source-size-base-commit' -WorkingDirectory $resolvedGitRoot | Out-Null
    $diffText = (Invoke-SourceSizeGit -Arguments @('diff', '--name-status', '--find-renames=50%', '-z', $BaseCommit, '--') -Name 'source-size-git-diff' -WorkingDirectory $resolvedGitRoot).Stdout
    $changes = [Collections.Generic.List[object]]::new()
    foreach ($change in @(ConvertFrom-SourceSizeGitDiff -Text $diffText)) { $changes.Add($change) }
    $seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($change in $changes) { [void]$seenPaths.Add([string]$change.HeadPath) }

    $untrackedText = (Invoke-SourceSizeGit -Arguments @('ls-files', '--others', '--exclude-standard', '-z') -Name 'source-size-git-untracked' -WorkingDirectory $resolvedGitRoot).Stdout
    foreach ($untrackedPath in @($untrackedText.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))) {
        if (-not $seenPaths.Add($untrackedPath)) { throw "Git reported duplicate head path '$untrackedPath'." }
        $changes.Add([pscustomobject]@{ Status = 'A'; BasePath = $null; HeadPath = $untrackedPath })
    }

    $violations = [Collections.Generic.List[object]]::new()
    $governedFileCount = 0
    foreach ($change in $changes) {
        if (-not (Test-NervGovernedSourcePath -Path $change.HeadPath -GovernedExtension $GovernedExtension)) { continue }
        $governedFileCount++
        $headPath = Join-Path $resolvedGitRoot $change.HeadPath
        if (-not (Test-Path -LiteralPath $headPath -PathType Leaf)) { throw "Changed source path cannot be read: '$($change.HeadPath)'." }
        $headText = [IO.File]::ReadAllText($headPath)
        $headLineCount = Get-NervSourcePhysicalLineCount -Text $headText
        $baseLineCount = $null
        if (-not [string]::Equals($change.Status, 'A', [StringComparison]::Ordinal)) {
            $baseText = (Invoke-SourceSizeGit -Arguments @('show', "$BaseCommit`:$($change.BasePath)") -Name 'source-size-git-base-content' -WorkingDirectory $resolvedGitRoot).Stdout
            $baseLineCount = Get-NervSourcePhysicalLineCount -Text $baseText
        }
        $violation = Get-NervSourceSizeViolation `
            -Status $change.Status `
            -Path $change.HeadPath `
            -BaseLineCount $baseLineCount `
            -HeadLineCount $headLineCount `
            -MaximumLines $MaximumLines
        if ($null -ne $violation) { $violations.Add($violation) }
    }

    if ($violations.Count -gt 0) {
        $orderedViolations = $violations.ToArray()
        [Array]::Sort(
            $orderedViolations,
            [Collections.Generic.Comparer[object]]::Create({
                param([object] $left, [object] $right)
                return [StringComparer]::Ordinal.Compare([string]$left.Path, [string]$right.Path)
            }))
        foreach ($violation in $orderedViolations) {
            $baseDisplay = if ($null -eq $violation.BaseLineCount) { '-' } else { [string]$violation.BaseLineCount }
            Write-Output "SOURCE_SIZE_VIOLATION rule=$($violation.Rule) status=$($violation.Status) path=$($violation.Path) base=$baseDisplay head=$($violation.HeadLineCount) maximum=$($violation.MaximumLines)"
        }
        exit 1
    }

    Write-Output "Source size governance check passed. Governed changed files: $governedFileCount."
}
catch {
    $safeMessage = Protect-ScriptAutomationText ([string]$_.Exception.Message)
    Write-Error "Source size governance check failed closed: $safeMessage"
    exit 1
}
