# Script-Governance:
#   Category: check
#   SideEffects:
#     - Loads the source-size governance policy library
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/SourceSizeGovernance.ps1'
. $libraryPath

function Assert-Equal {
    param(
        [AllowNull()] [object] $Actual,
        [AllowNull()] [object] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

# Mutation killed: treating empty text as one physical line.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text '') 0 'Empty text has no physical lines'
# Mutation killed: counting only newline terminators and missing the final unterminated line.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text 'one') 1 'Single unterminated line counts once'
# Mutation killed: adding a phantom line after a trailing LF.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text "one`n") 1 'Trailing LF does not add a line'
# Mutation killed: counting CR and LF separately.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text "one`r`ntwo") 2 'CRLF is one line boundary'
# Mutation killed: ignoring classic-Mac CR line boundaries.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text "one`rtwo`r") 2 'CR boundaries are physical lines'

# Mutation killed: changing the new-file comparison from greater-than to greater-than-or-equal.
Assert-True ($null -eq (Get-NervSourceSizeViolation -Status A -Path 'src/New.cs' -BaseLineCount $null -HeadLineCount 1000 -MaximumLines 1000)) 'New file at the limit must pass'
Assert-Equal (Get-NervSourceSizeViolation -Status A -Path 'src/New.cs' -BaseLineCount $null -HeadLineCount 1001 -MaximumLines 1000).Rule 'new-file-over-limit' 'New file over the limit must fail'
# Mutation killed: applying the fixed 1000-line ceiling to already oversized files.
Assert-Equal (Get-NervSourceSizeViolation -Status M -Path 'src/Legacy.cs' -BaseLineCount 1200 -HeadLineCount 1201 -MaximumLines 1000).Rule 'oversized-file-growth' 'Oversized legacy growth must fail'
Assert-True ($null -eq (Get-NervSourceSizeViolation -Status M -Path 'src/Legacy.cs' -BaseLineCount 1200 -HeadLineCount 1200 -MaximumLines 1000)) 'Oversized legacy hold must pass'
Assert-True ($null -eq (Get-NervSourceSizeViolation -Status M -Path 'src/Legacy.cs' -BaseLineCount 1200 -HeadLineCount 1100 -MaximumLines 1000)) 'Oversized legacy shrink must pass'
# Mutation killed: allowing a file that starts within the limit to cross it.
Assert-Equal (Get-NervSourceSizeViolation -Status M -Path 'src/Crosses.cs' -BaseLineCount 999 -HeadLineCount 1001 -MaximumLines 1000).Rule 'file-crosses-limit' 'Threshold crossing must fail'

Write-Host 'Source size governance contracts passed.'
