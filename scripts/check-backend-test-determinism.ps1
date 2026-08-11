# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads C# test sources belonging to projects in backend/Nerv.IIP.sln
#     - Reads and validates the backend test-determinism baseline
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

[CmdletBinding()]
param(
    [string] $SourceRoot = (Join-Path $PSScriptRoot '../backend'),

    [string] $BaselinePath = (Join-Path $PSScriptRoot '../backend/test-determinism-baseline.json'),

    # Where a flagged construct IS the audited primitive itself — its implementation or its own
    # self-test — so the finding can never be removed and dating it would be theatre. Each entry is
    # `<repo-relative-path>=<pattern>=<maxRows>`: a path alone is not enough, because the justification
    # is always about one specific construct in that file, and the positive row capacity prevents a
    # previously reviewed pair from growing through baseline-only edits. The cap counts valid
    # permanent baseline rows, not source occurrences and not occurrenceCount.
    #
    # The list is deliberately hard-coded rather than read from the baseline: a `permanent` row must
    # be countersigned by the checker, otherwise the classification degrades into a self-served
    # exemption. The parameter exists so the checker's own fixture harness can exercise both sides of
    # the rule; CI invokes the script with no arguments.
    [string[]] $PermanentAllowlist = @(
        'backend/tests/Nerv.IIP.Testing.Tests/GlobalTestStateScopeTests.cs=StaticSetter=12',
        'backend/common/Testing/Nerv.IIP.Testing/GlobalTestStateScope.cs=StaticSetter=9',
        'backend/common/Testing/Nerv.IIP.Testing/BoundedObservationWindow.cs=Task.Delay=1'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$backendRoot = (Resolve-Path (Join-Path $repoRoot 'backend')).Path
$solutionPath = Join-Path $backendRoot 'Nerv.IIP.sln'
$allowedPatterns = @('Task.Delay', 'Thread.Sleep', 'ShortLease', 'UnreachableAddress', 'StaticSetter')

# Two classifications, deliberately disjoint metadata:
#   expiring-debt — an admitted debt. Carries distinct registering and owning issues, the offline
#                   registration date, an exit condition, and an expiry no more than 45 days after
#                   registration. This is the default and the only classification that may grow.
#   permanent     — the flagged construct is the audited primitive itself: its implementation, or its
#                   own self-test. There is nothing to expire towards, so a date would be a lie;
#                   instead it carries a rationale and is only legal for an allow-listed
#                   path + pattern pair within its checker-owned row capacity (see
#                   $PermanentAllowlist).
$expiringClassification = 'expiring-debt'
$permanentClassification = 'permanent'
$allowedClassifications = @($expiringClassification, $permanentClassification)
$commonBaselineFields = @('path', 'pattern', 'lineTextSha256', 'occurrenceCount', 'classification', 'reason')
$commonStringBaselineFields = @('path', 'pattern', 'lineTextSha256', 'classification', 'reason')
$expiringOnlyFields = @('ownerIssue', 'registeredByIssue', 'exitCondition', 'registeredOn', 'expiresOn')
$permanentOnlyFields = @('rationale')

function Resolve-RepoInputPath {
    param(
        [Parameter(Mandatory)]
        [string] $InputPath
    )

    $candidate = if ([System.IO.Path]::IsPathRooted($InputPath)) {
        $InputPath
    }
    else {
        Join-Path $repoRoot $InputPath
    }

    return (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path
}

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return ([System.IO.Path]::GetRelativePath($repoRoot, $Path) -replace '\\', '/')
}

function Get-LineTextSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $LineText
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($LineText.Trim())
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [System.Convert]::ToHexString($hash).ToLowerInvariant()
}

# Single source of the finding identity: baseline rows and source findings are matched, deduped and
# marked as admitted through this key, so the separator and the field order must never be spelled
# out at a call site.
function Get-FindingIdentity {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $LineTextSha256
    )

    return "$Path|$Pattern|$LineTextSha256"
}

# Issue keys keep their namespace but normalize the numeric identity before comparison. GitHub and
# Linear render canonical keys without leading zeroes, yet the documented `\d+` input shape accepts
# them; comparing raw strings would let `#1487` self-guarantee through `#01487`.
function Get-CanonicalIssueIdentity {
    param(
        [Parameter(Mandatory)]
        [string] $Issue
    )

    $issueMatch = [regex]::Match(
        $Issue,
        '^(?<namespace>MAN-|#)(?<number>\d+)$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $issueMatch.Success) {
        return $null
    }

    $canonicalNumber = $issueMatch.Groups['number'].Value -replace '^0+(?=\d)', ''
    return "$($issueMatch.Groups['namespace'].Value)$canonicalNumber"
}

function Test-GeneratedSource {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo] $File
    )

    $relativePath = Get-RepoRelativePath -Path $File.FullName
    if ($relativePath -match '(?i)(^|/)(obj|bin|migrations)(/|$)') {
        return $true
    }

    if ($File.Name -match '(?i)(\.g|\.generated|\.designer|\.AssemblyInfo|\.GlobalUsings)\.cs$') {
        return $true
    }

    $head = @(Get-Content -LiteralPath $File.FullName -TotalCount 5)
    return ($head -join "`n") -match '(?i)<auto-generated'
}

function Get-DirectSourceFiles {
    param(
        [Parameter(Mandatory)]
        [string] $ResolvedSourceRoot
    )

    if (Test-Path -LiteralPath $ResolvedSourceRoot -PathType Leaf) {
        if (-not [string]::Equals([System.IO.Path]::GetExtension($ResolvedSourceRoot), '.cs', [StringComparison]::Ordinal)) {
            return @()
        }

        return @((Get-Item -LiteralPath $ResolvedSourceRoot))
    }

    return @(Get-ChildItem -LiteralPath $ResolvedSourceRoot -Recurse -File -Filter '*.cs')
}

function Test-IsTestProject {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    if ([System.IO.Path]::GetFileNameWithoutExtension($ProjectPath) -match '(?i)Tests$') {
        return $true
    }

    $projectContent = Get-Content -LiteralPath $ProjectPath -Raw
    return $projectContent -match '(?is)<IsTestProject>\s*true\s*</IsTestProject>'
}

# The shared test-infrastructure projects (`Nerv.IIP.Testing`, `.Xunit`, `.PostgreSql`) satisfy
# neither Test-IsTestProject condition — no `Tests` suffix, no `<IsTestProject>`. They used to fall
# out of the scan as a side effect of that. #1471 made this directory the place process-global writes
# are supposed to live, which turns "not scanned" from a harmless fact into an unguarded hole, so the
# scan includes it explicitly and the permanent allowlist countersigns each construct inside it.
function Test-IsSharedTestingProject {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    $relativePath = Get-RepoRelativePath -Path $ProjectPath
    return $relativePath -clike 'backend/common/Testing/*'
}

function Get-SolutionTestSourceFiles {
    $solutionList = Invoke-DotNetOutput `
        -Arguments @('sln', $solutionPath, 'list') `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 60 `
        -Name 'backend-test-determinism-solution-list'

    $projectPaths = @(
        Get-NervStringsSorted -Values @($solutionList.Stdout -split "`r?`n" |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -match '(?i)\.csproj$' } |
            ForEach-Object { Join-Path $backendRoot ($_ -replace '\\', [System.IO.Path]::DirectorySeparatorChar) } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Where-Object { (Test-IsTestProject -ProjectPath $_) -or (Test-IsSharedTestingProject -ProjectPath $_) }) -Comparer ([StringComparer]::Ordinal) -Unique
    )

    if ($projectPaths.Count -eq 0) {
        throw "No C# test projects were returned for solution '$solutionPath'."
    }

    # If the shared test infrastructure is ever moved or dropped from the solution, the scan would
    # quietly shrink back to the old blind spot. Fail loudly instead.
    $sharedTestingProjects = @($projectPaths | Where-Object { Test-IsSharedTestingProject -ProjectPath $_ })
    if ($sharedTestingProjects.Count -eq 0) {
        throw "No shared test-infrastructure projects were found under backend/common/Testing in solution '$solutionPath'."
    }

    $files = foreach ($projectPath in $projectPaths) {
        $projectDirectory = Split-Path -Parent $projectPath
        Get-ChildItem -LiteralPath $projectDirectory -Recurse -File -Filter '*.cs'
    }

    return @(Get-NervItemsUniqueSortedByString -Items @($files) -KeySelector { param($row) [string]$row.FullName } -Comparer ([StringComparer]::Ordinal))
}

function Get-CSharpSanitizedText {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Text,

        [switch] $PreserveStringContent
    )

    $characters = $Text.ToCharArray()
    $length = $characters.Length
    $index = 0

    while ($index -lt $length) {
        $character = $characters[$index]
        $next = if ($index + 1 -lt $length) { $characters[$index + 1] } else { [char] 0 }

        if ([string]::Equals([string]($character), [string]('/'), [StringComparison]::Ordinal) -and [string]::Equals([string]($next), [string]('/'), [StringComparison]::Ordinal)) {
            while ($index -lt $length -and (-not [string]::Equals([string]($characters[$index]), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($characters[$index]), [string]("`n"), [StringComparison]::Ordinal))) {
                $characters[$index] = ' '
                $index++
            }
            continue
        }

        if ([string]::Equals([string]($character), [string]('/'), [StringComparison]::Ordinal) -and [string]::Equals([string]($next), [string]('*'), [StringComparison]::Ordinal)) {
            $characters[$index] = ' '
            $characters[$index + 1] = ' '
            $index += 2
            while ($index -lt $length) {
                if ($index + 1 -lt $length -and [string]::Equals([string]($characters[$index]), [string]('*'), [StringComparison]::Ordinal) -and [string]::Equals([string]($characters[$index + 1]), [string]('/'), [StringComparison]::Ordinal)) {
                    $characters[$index] = ' '
                    $characters[$index + 1] = ' '
                    $index += 2
                    break
                }
                if ((-not [string]::Equals([string]($characters[$index]), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($characters[$index]), [string]("`n"), [StringComparison]::Ordinal))) {
                    $characters[$index] = ' '
                }
                $index++
            }
            continue
        }

        if ([string]::Equals([string]($character), [string]("'"), [StringComparison]::Ordinal)) {
            $characters[$index] = ' '
            $index++
            while ($index -lt $length) {
                $current = $characters[$index]
                if ((-not [string]::Equals([string]($current), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($current), [string]("`n"), [StringComparison]::Ordinal))) {
                    $characters[$index] = ' '
                }
                if ([string]::Equals([string]($current), [string]('\'), [StringComparison]::Ordinal) -and $index + 1 -lt $length) {
                    $index++
                    if ((-not [string]::Equals([string]($characters[$index]), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($characters[$index]), [string]("`n"), [StringComparison]::Ordinal))) {
                        $characters[$index] = ' '
                    }
                }
                elseif ([string]::Equals([string]($current), [string]("'"), [StringComparison]::Ordinal)) {
                    $index++
                    break
                }
                $index++
            }
            continue
        }

        if ([string]::Equals([string]($character), [string]('"'), [StringComparison]::Ordinal)) {
            $quoteRunLength = 1
            while ($index + $quoteRunLength -lt $length -and [string]::Equals([string]($Text[$index + $quoteRunLength]), [string]('"'), [StringComparison]::Ordinal)) {
                $quoteRunLength++
            }
            $isRawString = $quoteRunLength -ge 3

            if ($isRawString) {
                $quoteCount = $quoteRunLength
                $rawClosingIndex = -1
                $rawSearchIndex = $index + $quoteCount
                while ($rawSearchIndex -lt $length) {
                    if ((-not [string]::Equals([string]($Text[$rawSearchIndex]), [string]('"'), [StringComparison]::Ordinal))) {
                        $rawSearchIndex++
                        continue
                    }

                    $closingQuoteRun = 1
                    while ($rawSearchIndex + $closingQuoteRun -lt $length -and
                        [string]::Equals([string]($Text[$rawSearchIndex + $closingQuoteRun]), [string]('"'), [StringComparison]::Ordinal)) {
                        $closingQuoteRun++
                    }
                    if ($closingQuoteRun -ge $quoteCount) {
                        $rawClosingIndex = $rawSearchIndex
                        break
                    }
                    $rawSearchIndex += $closingQuoteRun
                }

                if ($rawClosingIndex -lt 0) {
                    $rawClosingIndex = $length
                }

                $rawDollarCount = 0
                $dollarIndex = $index - 1
                while ($dollarIndex -ge 0 -and [string]::Equals([string]($Text[$dollarIndex]), [string]('$'), [StringComparison]::Ordinal)) {
                    $rawDollarCount++
                    $dollarIndex--
                }

                if (-not $PreserveStringContent) {
                    for ($offset = 1; $offset -le $rawDollarCount; $offset++) {
                        $characters[$index - $offset] = ' '
                    }
                    for ($offset = 0; $offset -lt $quoteCount; $offset++) {
                        $characters[$index + $offset] = ' '
                    }
                }

                $rawContentIndex = $index + $quoteCount
                while ($rawContentIndex -lt $rawClosingIndex) {
                    if ($rawDollarCount -gt 0 -and [string]::Equals([string]($Text[$rawContentIndex]), [string]('{'), [StringComparison]::Ordinal)) {
                        $openingBraceRun = 1
                        while ($rawContentIndex + $openingBraceRun -lt $rawClosingIndex -and
                            [string]::Equals([string]($Text[$rawContentIndex + $openingBraceRun]), [string]('{'), [StringComparison]::Ordinal)) {
                            $openingBraceRun++
                        }

                        if ($openingBraceRun -ge $rawDollarCount -and $openingBraceRun -lt 2 * $rawDollarCount) {
                            $expressionStart = $rawContentIndex + $openingBraceRun
                            $expressionTail = $Text.Substring($expressionStart, $rawClosingIndex - $expressionStart)
                            $sanitizedExpressionTail = if ($PreserveStringContent) {
                                Get-CSharpSanitizedText -Text $expressionTail -PreserveStringContent
                            }
                            else {
                                Get-CSharpSanitizedText -Text $expressionTail
                            }

                            $expressionCloseOffset = -1
                            $closingBraceRun = 0
                            $nestedBraceDepth = 0
                            $expressionCursor = 0
                            while ($expressionCursor -lt $sanitizedExpressionTail.Length) {
                                if ([string]::Equals([string]($sanitizedExpressionTail[$expressionCursor]), [string]('{'), [StringComparison]::Ordinal)) {
                                    $nestedBraceDepth++
                                    $expressionCursor++
                                    continue
                                }
                                if ((-not [string]::Equals([string]($sanitizedExpressionTail[$expressionCursor]), [string]('}'), [StringComparison]::Ordinal))) {
                                    $expressionCursor++
                                    continue
                                }

                                $candidateClosingRun = 1
                                while ($expressionCursor + $candidateClosingRun -lt $sanitizedExpressionTail.Length -and
                                    [string]::Equals([string]($sanitizedExpressionTail[$expressionCursor + $candidateClosingRun]), [string]('}'), [StringComparison]::Ordinal)) {
                                    $candidateClosingRun++
                                }
                                $nestedClosingBraces = [Math]::Min($nestedBraceDepth, $candidateClosingRun)
                                $nestedBraceDepth -= $nestedClosingBraces
                                $remainingClosingBraces = $candidateClosingRun - $nestedClosingBraces
                                if ($nestedBraceDepth -eq 0 -and
                                    $remainingClosingBraces -ge $rawDollarCount -and
                                    $remainingClosingBraces -lt 2 * $rawDollarCount) {
                                    $expressionCloseOffset = $expressionCursor + $nestedClosingBraces
                                    $closingBraceRun = $remainingClosingBraces
                                    break
                                }
                                $expressionCursor += $candidateClosingRun
                            }

                            if ($expressionCloseOffset -ge 0) {
                                if (-not $PreserveStringContent) {
                                    for ($offset = 0; $offset -lt $openingBraceRun; $offset++) {
                                        $characters[$rawContentIndex + $offset] = ' '
                                    }
                                }

                                $expressionLength = $expressionCloseOffset
                                $expressionText = $Text.Substring($expressionStart, $expressionLength)
                                $sanitizedExpression = if ($PreserveStringContent) {
                                    Get-CSharpSanitizedText -Text $expressionText -PreserveStringContent
                                }
                                else {
                                    Get-CSharpSanitizedText -Text $expressionText
                                }
                                for ($offset = 0; $offset -lt $expressionLength; $offset++) {
                                    $characters[$expressionStart + $offset] = $sanitizedExpression[$offset]
                                }

                                $expressionClosingIndex = $expressionStart + $expressionCloseOffset
                                if (-not $PreserveStringContent) {
                                    for ($offset = 0; $offset -lt $closingBraceRun; $offset++) {
                                        $characters[$expressionClosingIndex + $offset] = ' '
                                    }
                                }
                                $rawContentIndex = $expressionClosingIndex + $closingBraceRun
                                continue
                            }
                        }
                    }

                    if (-not $PreserveStringContent -and
                        (-not [string]::Equals([string]($Text[$rawContentIndex]), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($Text[$rawContentIndex]), [string]("`n"), [StringComparison]::Ordinal))) {
                        $characters[$rawContentIndex] = ' '
                    }
                    $rawContentIndex++
                }

                if (-not $PreserveStringContent -and $rawClosingIndex -lt $length) {
                    for ($offset = 0; $offset -lt $quoteCount; $offset++) {
                        $characters[$rawClosingIndex + $offset] = ' '
                    }
                }
                $index = [Math]::Min($rawClosingIndex + $quoteCount, $length)
                continue
            }

            $quoteCount = 1
            $isVerbatimString = ($index -gt 0 -and [string]::Equals([string]($characters[$index - 1]), [string]('@'), [StringComparison]::Ordinal)) -or
                ($index -gt 1 -and [string]::Equals([string]($characters[$index - 1]), [string]('$'), [StringComparison]::Ordinal) -and [string]::Equals([string]($characters[$index - 2]), [string]('@'), [StringComparison]::Ordinal))
            $prefixIndex = $index - 1
            if ($prefixIndex -ge 0 -and [string]::Equals([string]($characters[$prefixIndex]), [string]('@'), [StringComparison]::Ordinal)) {
                $prefixIndex--
            }
            $isInterpolatedString = $prefixIndex -ge 0 -and [string]::Equals([string]($characters[$prefixIndex]), [string]('$'), [StringComparison]::Ordinal)

            for ($offset = 0; $offset -lt $quoteCount; $offset++) {
                if (-not $PreserveStringContent) {
                    $characters[$index + $offset] = ' '
                }
            }
            $index += $quoteCount

            while ($index -lt $length) {
                $current = $characters[$index]
                if ($isInterpolatedString -and [string]::Equals([string]($current), [string]('{'), [StringComparison]::Ordinal) -and
                    $index + 1 -lt $length -and [string]::Equals([string]($characters[$index + 1]), [string]('{'), [StringComparison]::Ordinal)) {
                    if (-not $PreserveStringContent) {
                        $characters[$index] = ' '
                        $characters[$index + 1] = ' '
                    }
                    $index += 2
                    continue
                }
                elseif ($isInterpolatedString -and [string]::Equals([string]($current), [string]('{'), [StringComparison]::Ordinal)) {
                    $expressionStart = $index + 1
                    $expressionCloseOffset = -1
                    $candidateSearchOffset = $expressionStart
                    while ($candidateSearchOffset -lt $length) {
                        $candidateClosingIndex = $Text.IndexOf('}', $candidateSearchOffset, [StringComparison]::Ordinal)
                        if ($candidateClosingIndex -lt 0) {
                            break
                        }

                        $candidateLength = $candidateClosingIndex - $expressionStart + 1
                        $candidateText = $Text.Substring($expressionStart, $candidateLength)
                        $sanitizedCandidate = if ($PreserveStringContent) {
                            Get-CSharpSanitizedText -Text $candidateText -PreserveStringContent
                        }
                        else {
                            Get-CSharpSanitizedText -Text $candidateText
                        }

                        $nestedBraceDepth = 0
                        for ($expressionCursor = 0; $expressionCursor -lt $sanitizedCandidate.Length; $expressionCursor++) {
                            if ([string]::Equals([string]($sanitizedCandidate[$expressionCursor]), [string]('{'), [StringComparison]::Ordinal)) {
                                $nestedBraceDepth++
                                continue
                            }
                            if ((-not [string]::Equals([string]($sanitizedCandidate[$expressionCursor]), [string]('}'), [StringComparison]::Ordinal))) {
                                continue
                            }
                            if ($nestedBraceDepth -gt 0) {
                                $nestedBraceDepth--
                                continue
                            }

                            $expressionCloseOffset = $expressionCursor
                            break
                        }
                        if ($expressionCloseOffset -ge 0) {
                            break
                        }
                        $candidateSearchOffset = $candidateClosingIndex + 1
                    }

                    if ($expressionCloseOffset -ge 0) {
                        if (-not $PreserveStringContent) {
                            $characters[$index] = ' '
                        }

                        $expressionText = $Text.Substring($expressionStart, $expressionCloseOffset)
                        $sanitizedExpression = if ($PreserveStringContent) {
                            Get-CSharpSanitizedText -Text $expressionText -PreserveStringContent
                        }
                        else {
                            Get-CSharpSanitizedText -Text $expressionText
                        }
                        for ($offset = 0; $offset -lt $expressionCloseOffset; $offset++) {
                            $characters[$expressionStart + $offset] = $sanitizedExpression[$offset]
                        }

                        $expressionClosingIndex = $expressionStart + $expressionCloseOffset
                        if (-not $PreserveStringContent) {
                            $characters[$expressionClosingIndex] = ' '
                        }
                        $index = $expressionClosingIndex + 1
                        continue
                    }
                }
                elseif ($isVerbatimString -and [string]::Equals([string]($current), [string]('"'), [StringComparison]::Ordinal) -and $index + 1 -lt $length -and [string]::Equals([string]($characters[$index + 1]), [string]('"'), [StringComparison]::Ordinal)) {
                    if (-not $PreserveStringContent) {
                        $characters[$index] = ' '
                        $characters[$index + 1] = ' '
                    }
                    $index += 2
                    continue
                }
                elseif ([string]::Equals([string]($current), [string]('"'), [StringComparison]::Ordinal)) {
                    if (-not $PreserveStringContent) {
                        $characters[$index] = ' '
                    }
                    $index++
                    break
                }
                elseif (-not $isVerbatimString -and [string]::Equals([string]($current), [string]('\'), [StringComparison]::Ordinal) -and $index + 1 -lt $length) {
                    if (-not $PreserveStringContent) {
                        $characters[$index] = ' '
                    }
                    $index++
                    if (-not $PreserveStringContent -and (-not [string]::Equals([string]($characters[$index]), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($characters[$index]), [string]("`n"), [StringComparison]::Ordinal))) {
                        $characters[$index] = ' '
                    }
                    $index++
                    continue
                }

                if (-not $PreserveStringContent -and (-not [string]::Equals([string]($current), [string]("`r"), [StringComparison]::Ordinal)) -and (-not [string]::Equals([string]($current), [string]("`n"), [StringComparison]::Ordinal))) {
                    $characters[$index] = ' '
                }
                $index++
            }
            continue
        }

        $index++
    }

    return -join $characters
}

function Get-LineFindingAtOffset {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Source,

        [Parameter(Mandatory)]
        [int[]] $LineStarts,

        [Parameter(Mandatory)]
        [int] $Offset,

        [Parameter(Mandatory)]
        [string] $Pattern
    )

    $lineIndex = [Array]::BinarySearch($LineStarts, $Offset)
    if ($lineIndex -lt 0) {
        $lineIndex = -$lineIndex - 2
    }
    $lineStart = $LineStarts[$lineIndex]
    $lineEnd = $lineStart
    while ($lineEnd -lt $Source.Length -and (-not [string]::Equals([string]($Source[$lineEnd]), [string]("`r"), [StringComparison]::OrdinalIgnoreCase)) -and (-not [string]::Equals([string]($Source[$lineEnd]), [string]("`n"), [StringComparison]::OrdinalIgnoreCase))) {
        $lineEnd++
    }
    $lineText = $Source.Substring($lineStart, $lineEnd - $lineStart).Trim()

    return [pscustomobject]@{
        Path = $Path
        Pattern = $Pattern
        Line = $lineIndex + 1
        LineText = $lineText
        LineTextSha256 = Get-LineTextSha256 -LineText $lineText
    }
}

function Get-SourceFindings {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo[]] $Files
    )

    $findings = New-Object System.Collections.Generic.List[object]

    foreach ($file in $Files) {
        if (Test-GeneratedSource -File $file) {
            continue
        }

        $relativePath = Get-RepoRelativePath -Path $file.FullName
        $source = [System.IO.File]::ReadAllText($file.FullName)
        $sanitizedCode = Get-CSharpSanitizedText -Text $source
        $commentSanitizedSource = Get-CSharpSanitizedText -Text $source -PreserveStringContent
        $lineStarts = New-Object System.Collections.Generic.List[int]
        $lineStarts.Add(0)
        for ($index = 0; $index -lt $source.Length; $index++) {
            if ([string]::Equals([string]($source[$index]), [string]("`n"), [StringComparison]::OrdinalIgnoreCase)) {
                $lineStarts.Add($index + 1)
            }
        }

        $patternExpressions = @(
            [pscustomobject]@{ Pattern = 'Task.Delay'; Expression = '(?<![\w.])(?:(?:global\s*::\s*)?System\s*\.\s*Threading\s*\.\s*Tasks\s*\.\s*)?Task\s*\.\s*Delay\s*\(' },
            [pscustomobject]@{ Pattern = 'Thread.Sleep'; Expression = '(?<![\w.])(?:(?:global\s*::\s*)?System\s*\.\s*Threading\s*\.\s*)?Thread\s*\.\s*Sleep\s*\(' },
            [pscustomobject]@{ Pattern = 'StaticSetter'; Expression = '(?<![\w.])(?:(?:global\s*::\s*)?System\s*\.\s*)?Environment\s*\.\s*SetEnvironmentVariable\s*\(' },
            [pscustomobject]@{ Pattern = 'StaticSetter'; Expression = '\bValidatorOptions\s*\.\s*Global\b(?:\s*\.\s*[A-Za-z_]\w*)+\s*(?<![=!<>])=(?!=|>)' },
            [pscustomobject]@{ Pattern = 'StaticSetter'; Expression = '\bCultureInfo\s*\.\s*(?:CurrentCulture|CurrentUICulture|DefaultThreadCurrentCulture|DefaultThreadCurrentUICulture)\b\s*(?<![=!<>])=(?!=|>)' },
            [pscustomobject]@{ Pattern = 'StaticSetter'; Expression = '\bThread\s*\.\s*CurrentThread\s*\.\s*(?:CurrentCulture|CurrentUICulture)\b\s*(?<![=!<>])=(?!=|>)' }
        )
        foreach ($patternExpression in $patternExpressions) {
            foreach ($match in [regex]::Matches($sanitizedCode, $patternExpression.Expression)) {
                $findings.Add((Get-LineFindingAtOffset -Path $relativePath -Source $source -LineStarts $lineStarts.ToArray() -Offset $match.Index -Pattern $patternExpression.Pattern))
            }
        }

        $numberExpression = '\d(?:_?\d)*(?:\.\d(?:_?\d)*)?'
        $shortLeaseExpressions = @(
            [pscustomobject]@{ Expression = "(?i)\b\w*(?:Lease|Renew)\w*\b\s*(?:=|:|\()\s*TimeSpan\s*\.\s*FromMilliseconds\s*\(\s*(?<value>$numberExpression)\s*\)"; Limit = [decimal] 1000 },
            [pscustomobject]@{ Expression = "(?i)\b\w*(?:Lease|Renew)\w*\b\s*(?:=|:|\()\s*TimeSpan\s*\.\s*FromSeconds\s*\(\s*(?<value>$numberExpression)\s*\)"; Limit = [decimal] 1 },
            [pscustomobject]@{ Expression = "(?i)\b(?:\w*Lease\w*|\w*Renew\w*)(?:Milliseconds|Ms)\s*=\s*(?<value>$numberExpression)"; Limit = [decimal] 1000 },
            [pscustomobject]@{ Expression = "(?i)\b(?:\w*Lease\w*|\w*Renew\w*)Seconds\s*=\s*(?<value>$numberExpression)"; Limit = [decimal] 1 }
        )
        foreach ($shortLeaseExpression in $shortLeaseExpressions) {
            foreach ($match in [regex]::Matches($sanitizedCode, $shortLeaseExpression.Expression)) {
                $numericValue = [decimal]::Parse(
                    $match.Groups['value'].Value.Replace('_', ''),
                    [Globalization.CultureInfo]::InvariantCulture
                )
                if ($numericValue -lt $shortLeaseExpression.Limit) {
                    $findings.Add((Get-LineFindingAtOffset -Path $relativePath -Source $source -LineStarts $lineStarts.ToArray() -Offset $match.Index -Pattern 'ShortLease'))
                }
            }
        }

        $unreachableExpressions = @(
            '(?i)127\.0\.0\.1\s*:\s*1(?!\d)',
            '(?i)\bPort\s*=\s*1(?=\s*[,;"'']|\s*$)'
        )
        foreach ($expression in $unreachableExpressions) {
            foreach ($match in [regex]::Matches($commentSanitizedSource, $expression)) {
                $findings.Add((Get-LineFindingAtOffset -Path $relativePath -Source $source -LineStarts $lineStarts.ToArray() -Offset $match.Index -Pattern 'UnreachableAddress'))
            }
        }
    }

    return @(Get-NervItemsSortedByString -Items $findings.ToArray() -KeySelector {
        param($row)
        Get-NervStringCompositeKey -Components @([string]$row.Path, ('{0:D10}' -f [int]$row.Line), [string]$row.Pattern)
    } -Comparer ([StringComparer]::Ordinal))
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)]
        [object] $Object,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return ,$property.Value
}

function Read-Baseline {
    param(
        [Parameter(Mandatory)]
        [string] $ResolvedBaselinePath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]] $Errors
    )

    try {
        $baseline = Get-Content -LiteralPath $ResolvedBaselinePath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        $Errors.Add("could not parse JSON: $($_.Exception.Message)")
        return @()
    }

    $schema = Get-PropertyValue -Object $baseline -Name 'schema'
    if ($schema -isnot [long] -or $schema -ne 3) {
        $Errors.Add('schema must equal 3 as a JSON number.')
    }

    $exceptionsValue = Get-PropertyValue -Object $baseline -Name 'exceptions'
    if ($null -eq $exceptionsValue -or $exceptionsValue -isnot [System.Array]) {
        $Errors.Add('exceptions must be a JSON array.')
        return @()
    }

    $rows = New-Object System.Collections.Generic.List[object]
    $seen = [System.Collections.Hashtable]::new([StringComparer]::Ordinal)
    $today = [DateOnly]::FromDateTime([DateTime]::UtcNow)
    # Ordinal dictionaries make both path and pattern membership exact, matching the explicit ordinal
    # finding comparisons below. The nested value is the checker-owned maximum number of valid rows.
    $permanentPathPatternCapacities = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $PermanentAllowlist) {
        $normalizedEntry = ($entry -replace '\\', '/').Trim()
        $capacitySeparatorIndex = $normalizedEntry.LastIndexOf('=', [StringComparison]::Ordinal)
        if ($capacitySeparatorIndex -lt 0) {
            $Errors.Add("permanent allowlist entry '$entry' must use '<path>=<pattern>=<maxRows>'.")
            continue
        }

        $pathAndPattern = $normalizedEntry.Substring(0, $capacitySeparatorIndex)
        $patternSeparatorIndex = $pathAndPattern.LastIndexOf('=', [StringComparison]::Ordinal)
        if ($patternSeparatorIndex -lt 0) {
            $Errors.Add("permanent allowlist entry '$entry' must use '<path>=<pattern>=<maxRows>'.")
            continue
        }

        $entryPath = $pathAndPattern.Substring(0, $patternSeparatorIndex).Trim()
        $entryPattern = $pathAndPattern.Substring($patternSeparatorIndex + 1).Trim()
        $entryMaxRows = $normalizedEntry.Substring($capacitySeparatorIndex + 1).Trim()
        if ([string]::IsNullOrWhiteSpace($entryPath)) {
            $Errors.Add("permanent allowlist entry '$entry' path must be non-empty.")
            continue
        }
        if ([string]::IsNullOrWhiteSpace($entryPattern)) {
            $Errors.Add("permanent allowlist entry '$entry' pattern must be non-empty.")
            continue
        }

        $maxRows = 0
        $maxRowsValid = [int]::TryParse(
            $entryMaxRows,
            [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref] $maxRows)
        if (-not $maxRowsValid -or $maxRows -le 0) {
            $Errors.Add("permanent allowlist entry '$entry' maxRows must be a positive integer.")
            continue
        }
        if ((-not [Collections.Generic.HashSet[string]]::new([string[]]@($allowedPatterns), [StringComparer]::Ordinal).Contains([string]($entryPattern)))) {
            $Errors.Add("permanent allowlist entry '$entry' names unsupported pattern '$entryPattern'.")
            continue
        }

        if (-not $permanentPathPatternCapacities.ContainsKey($entryPath)) {
            $permanentPathPatternCapacities[$entryPath] = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
        }
        $patternCapacities = $permanentPathPatternCapacities[$entryPath]
        if ($patternCapacities.ContainsKey($entryPattern)) {
            $Errors.Add("duplicate permanent allowlist entry for path '$entryPath' and pattern '$entryPattern'.")
            continue
        }
        $patternCapacities[$entryPattern] = $maxRows
    }

    for ($index = 0; $index -lt $exceptionsValue.Count; $index++) {
        $exception = $exceptionsValue[$index]

        # The classification decides which metadata the row owes, so it is resolved before the
        # required-field check rather than alongside it.
        $classification = Get-PropertyValue -Object $exception -Name 'classification'
        if ($classification -isnot [string] -or (-not [Collections.Generic.HashSet[string]]::new([string[]]@($allowedClassifications), [StringComparer]::Ordinal).Contains([string]($classification)))) {
            $Errors.Add("exception[$index] classification must be one of: $($allowedClassifications -join ', ').")
            continue
        }

        $isPermanent = [string]::Equals($classification, $permanentClassification, [StringComparison]::Ordinal)
        $classificationFields = if ($isPermanent) { $permanentOnlyFields } else { $expiringOnlyFields }
        $forbiddenFields = if ($isPermanent) { $expiringOnlyFields } else { $permanentOnlyFields }
        $requiredBaselineFields = @($commonBaselineFields + $classificationFields)
        $requiredStringBaselineFields = @($commonStringBaselineFields + $classificationFields)

        $missing = @(
            $requiredBaselineFields |
                Where-Object { $null -eq $exception.PSObject.Properties[$_] }
        )
        if ($missing.Count -gt 0) {
            $Errors.Add("exception[$index] classification '$classification' is missing required field(s): $($missing -join ', ').")
            continue
        }

        # A permanent row carrying an expiry (or a debt row carrying a rationale) reads as if it had
        # been reviewed under the other set of rules. Reject the mixture rather than pick a winner.
        $forbidden = @(
            $forbiddenFields |
                Where-Object { $null -ne $exception.PSObject.Properties[$_] }
        )
        if ($forbidden.Count -gt 0) {
            $Errors.Add("exception[$index] classification '$classification' must not carry field(s): $($forbidden -join ', ').")
            continue
        }

        $rowValid = $true
        $stringValues = [System.Collections.Hashtable]::new([StringComparer]::Ordinal)
        foreach ($field in $requiredStringBaselineFields) {
            $value = Get-PropertyValue -Object $exception -Name $field
            if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
                $Errors.Add("exception[$index] $field must be a non-empty string.")
                $rowValid = $false
                continue
            }
            $stringValues[$field] = $value.Trim()
        }

        $occurrenceCount = Get-PropertyValue -Object $exception -Name 'occurrenceCount'
        if ($occurrenceCount -isnot [long] -or $occurrenceCount -le 0) {
            $Errors.Add("exception[$index] occurrenceCount must be a positive integer.")
            $rowValid = $false
        }

        if (-not $rowValid) {
            continue
        }

        $path = $stringValues['path'] -replace '\\', '/'
        $pattern = $stringValues['pattern']
        $hash = $stringValues['lineTextSha256']
        $ownerIssue = $stringValues['ownerIssue']
        $registeredByIssue = $stringValues['registeredByIssue']
        $reason = $stringValues['reason']
        $exitCondition = $stringValues['exitCondition']
        $registeredOn = $stringValues['registeredOn']
        $expiresOn = $stringValues['expiresOn']
        $rationale = $stringValues['rationale']

        if ([System.IO.Path]::IsPathRooted($path) -or $path -match '(^|/)\.\.(/|$)') {
            $Errors.Add("exception[$index] path must be repo-relative: '$path'.")
            $rowValid = $false
        }
        if ($allowedPatterns -notcontains $pattern) {
            $Errors.Add("exception[$index] pattern '$pattern' is not supported.")
            $rowValid = $false
        }
        if ($hash -cnotmatch '^[0-9a-f]{64}$') {
            $Errors.Add("exception[$index] lineTextSha256 must be a lowercase SHA-256 hash.")
            $rowValid = $false
        }

        if ($isPermanent) {
            # Only the checker decides which files may hold a permanent row, and for which pattern;
            # the baseline cannot nominate itself into the allowlist, nor widen an entry written for
            # one construct to cover a different one.
            if (-not $permanentPathPatternCapacities.ContainsKey($path)) {
                $Errors.Add("exception[$index] permanent classification is not allowed for path '$path'.")
                $rowValid = $false
            }
            elseif (-not $permanentPathPatternCapacities[$path].ContainsKey($pattern)) {
                $Errors.Add("exception[$index] permanent classification is not allowed for pattern '$pattern' on path '$path'.")
                $rowValid = $false
            }
        }
        else {
            # Both issue fields are checked offline. Keeping the registering change distinct from
            # the follow-up owner prevents a debt row from self-guaranteeing its own cleanup.
            $ownerIssueIdentity = Get-CanonicalIssueIdentity -Issue $ownerIssue
            $ownerIssueValid = $null -ne $ownerIssueIdentity
            if (-not $ownerIssueValid) {
                $Errors.Add("exception[$index] ownerIssue must be a MAN issue key or a #<number> GitHub issue.")
                $rowValid = $false
            }

            $registeredByIssueIdentity = Get-CanonicalIssueIdentity -Issue $registeredByIssue
            $registeredByIssueValid = $null -ne $registeredByIssueIdentity
            if (-not $registeredByIssueValid) {
                $Errors.Add("exception[$index] registeredByIssue must be a MAN issue key or a #<number> GitHub issue.")
                $rowValid = $false
            }
            elseif ($ownerIssueValid -and [string]::Equals($registeredByIssueIdentity, $ownerIssueIdentity, [StringComparison]::Ordinal)) {
                $Errors.Add("exception[$index] registeredByIssue must differ from ownerIssue.")
                $rowValid = $false
            }

            $registrationDate = [DateOnly]::MinValue
            $registrationDateValid = [DateOnly]::TryParseExact($registeredOn, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref] $registrationDate)
            if (-not $registrationDateValid) {
                $Errors.Add("exception[$index] registeredOn must use yyyy-MM-dd.")
                $rowValid = $false
            }
            elseif ($registrationDate -gt $today) {
                $Errors.Add("exception[$index] registeredOn must not be in the future: $registeredOn.")
                $rowValid = $false
            }

            $expiry = [DateOnly]::MinValue
            $expiryValid = [DateOnly]::TryParseExact($expiresOn, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref] $expiry)
            if (-not $expiryValid) {
                $Errors.Add("exception[$index] expiresOn must use yyyy-MM-dd.")
                $rowValid = $false
            }
            else {
                if ($expiry -lt $today) {
                    $Errors.Add("exception[$index] expired on $expiresOn.")
                    $rowValid = $false
                }
                if ($registrationDateValid) {
                    if ($expiry -lt $registrationDate) {
                        $Errors.Add("exception[$index] expiresOn must be on or after registeredOn.")
                        $rowValid = $false
                    }
                    elseif (($expiry.DayNumber - $registrationDate.DayNumber) -gt 45) {
                        $Errors.Add("exception[$index] expiresOn must be no later than 45 days after registeredOn.")
                        $rowValid = $false
                    }
                }
            }
        }

        $identity = Get-FindingIdentity -Path $path -Pattern $pattern -LineTextSha256 $hash
        if ($seen.ContainsKey($identity)) {
            $Errors.Add("exception[$index] is a duplicate baseline row for $path [$pattern] $hash.")
            $rowValid = $false
        }
        else {
            $seen[$identity] = $true
        }

        if ($rowValid) {
            $rows.Add([pscustomobject]@{
                Path = $path
                Pattern = $pattern
                LineTextSha256 = $hash
                OccurrenceCount = $occurrenceCount
                Classification = $classification
                OwnerIssue = $ownerIssue
                RegisteredByIssue = $registeredByIssue
                Reason = $reason
                ExitCondition = $exitCondition
                RegisteredOn = $registeredOn
                ExpiresOn = $expiresOn
                Rationale = $rationale
            })
        }
    }

    foreach ($entryPath in $permanentPathPatternCapacities.Keys) {
        $patternCapacities = $permanentPathPatternCapacities[$entryPath]
        foreach ($entryPattern in $patternCapacities.Keys) {
            $actualRows = @(
                $rows | Where-Object {
                    [string]::Equals($_.Classification, $permanentClassification, [StringComparison]::Ordinal) -and
                    [string]::Equals($_.Path, $entryPath, [StringComparison]::Ordinal) -and
                    [string]::Equals($_.Pattern, $entryPattern, [StringComparison]::Ordinal)
                }
            ).Count
            $maximumRows = $patternCapacities[$entryPattern]
            if ($actualRows -gt $maximumRows) {
                $Errors.Add(
                    "permanent allowlist capacity exceeded for path '$entryPath' and pattern '$entryPattern': valid permanent baseline rows=$actualRows, maximum=$maximumRows."
                )
            }
        }
    }

    return $rows.ToArray()
}

$baselineErrors = New-Object System.Collections.Generic.List[string]

try {
    $resolvedSourceRoot = Resolve-RepoInputPath -InputPath $SourceRoot
    $resolvedBaselinePath = Resolve-RepoInputPath -InputPath $BaselinePath

    $sourceFiles = if ([string]::Equals([System.IO.Path]::GetFullPath($resolvedSourceRoot), [System.IO.Path]::GetFullPath($backendRoot), [StringComparison]::Ordinal)) {
        Get-SolutionTestSourceFiles
    }
    else {
        Get-DirectSourceFiles -ResolvedSourceRoot $resolvedSourceRoot
    }

    $sourceFiles = @(Get-NervItemsUniqueSortedByString -Items @($sourceFiles | Where-Object { -not (Test-GeneratedSource -File $_) }) -KeySelector { param($row) [string]$row.FullName } -Comparer ([StringComparer]::Ordinal))
    $findings = @(Get-SourceFindings -Files $sourceFiles)
    $baselineRows = @(Read-Baseline -ResolvedBaselinePath $resolvedBaselinePath -Errors $baselineErrors)

    $matchedFindingKeys = [System.Collections.Hashtable]::new([StringComparer]::Ordinal)
    foreach ($row in $baselineRows) {
        $exactMatches = @(
            $findings |
                Where-Object {
                    [string]::Equals($_.Path, $row.Path, [StringComparison]::Ordinal) -and
                    [string]::Equals($_.Pattern, $row.Pattern, [StringComparison]::Ordinal) -and
                    [string]::Equals($_.LineTextSha256, $row.LineTextSha256, [StringComparison]::Ordinal)
                }
        )
        if ($exactMatches.Count -eq $row.OccurrenceCount) {
            foreach ($match in $exactMatches) {
                $identity = Get-FindingIdentity -Path $match.Path -Pattern $match.Pattern -LineTextSha256 $match.LineTextSha256
                $matchedFindingKeys[$identity] = $true
            }
            continue
        }

        if ($exactMatches.Count -gt 0) {
            $baselineErrors.Add(
                "$($row.Path) [$($row.Pattern)] occurrence count changed: expected $($row.OccurrenceCount), actual $($exactMatches.Count)."
            )
            continue
        }

        $sameSourcePattern = @(
            $findings | Where-Object {
                [string]::Equals($_.Path, $row.Path, [StringComparison]::Ordinal) -and
                [string]::Equals($_.Pattern, $row.Pattern, [StringComparison]::Ordinal)
            }
        )
        if ($sameSourcePattern.Count -gt 0) {
            $baselineErrors.Add("$($row.Path) [$($row.Pattern)] hash no longer matches a current source line.")
        }
        else {
            $baselineErrors.Add("$($row.Path) [$($row.Pattern)] does not match a current finding.")
        }
    }

    $unexplained = @(
        $findings |
            Where-Object {
                -not $matchedFindingKeys.ContainsKey(
                    (Get-FindingIdentity -Path $_.Path -Pattern $_.Pattern -LineTextSha256 $_.LineTextSha256))
            }
    )

    if ($baselineErrors.Count -gt 0 -or $unexplained.Count -gt 0) {
        [Console]::Error.WriteLine(
            "Backend test determinism check failed: findings=$($findings.Count), admitted=$($matchedFindingKeys.Count), unexplained=$($unexplained.Count), baselineErrors=$($baselineErrors.Count)."
        )
        foreach ($errorMessage in $baselineErrors) {
            [Console]::Error.WriteLine("Baseline: $errorMessage")
        }
        foreach ($finding in $unexplained) {
            [Console]::Error.WriteLine(
                "$($finding.Path):$($finding.Line) [$($finding.Pattern)] unexplained source finding; lineTextSha256=$($finding.LineTextSha256); line=$($finding.LineText)"
            )
        }
        exit 1
    }

    $permanentRowCount = @(
        $baselineRows | Where-Object {
            [string]::Equals($_.Classification, $permanentClassification, [StringComparison]::Ordinal)
        }
    ).Count
    $expiringRowCount = @(
        $baselineRows | Where-Object {
            [string]::Equals($_.Classification, $expiringClassification, [StringComparison]::Ordinal)
        }
    ).Count
    Write-Host "Backend test determinism check passed: files=$($sourceFiles.Count), findings=$($findings.Count), admitted=$($matchedFindingKeys.Count), expiringDebtRows=$expiringRowCount, permanentRows=$permanentRowCount."
    exit 0
}
catch {
    [Console]::Error.WriteLine("Backend test determinism check could not run: $($_.Exception.Message)")
    exit 1
}
