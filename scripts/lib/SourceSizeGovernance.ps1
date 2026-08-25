# Script-Governance:
#   Category: library
#   SideEffects:
#     - Provides pure source-size governance policy functions
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function Get-NervSourcePhysicalLineCount {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Text
    )

    if ($Text.Length -eq 0) { return 0 }

    $lineBoundaryCount = [regex]::Matches($Text, "`r`n|`r|`n").Count
    $endsWithLineBoundary = $Text.EndsWith("`n", [StringComparison]::Ordinal) -or
        $Text.EndsWith("`r", [StringComparison]::Ordinal)

    return $lineBoundaryCount + $(if ($endsWithLineBoundary) { 0 } else { 1 })
}

function Get-NervSourceSizeViolation {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('A', 'M', 'R')]
        [string] $Status,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path,

        [AllowNull()]
        [Nullable[int]] $BaseLineCount,

        [Parameter(Mandatory)]
        [ValidateRange(0, 2147483647)]
        [int] $HeadLineCount,

        [Parameter(Mandatory)]
        [ValidateRange(1, 2147483647)]
        [int] $MaximumLines
    )

    $rule = $null
    if ([string]::Equals($Status, 'A', [StringComparison]::Ordinal)) {
        if ($HeadLineCount -gt $MaximumLines) { $rule = 'new-file-over-limit' }
    }
    else {
        if ($null -eq $BaseLineCount) {
            throw "Status '$Status' requires a base line count."
        }

        if ($BaseLineCount -le $MaximumLines -and $HeadLineCount -gt $MaximumLines) {
            $rule = 'file-crosses-limit'
        }
        elseif ($BaseLineCount -gt $MaximumLines -and $HeadLineCount -gt $BaseLineCount) {
            $rule = 'oversized-file-growth'
        }
    }

    if ($null -eq $rule) { return $null }

    return [pscustomobject][ordered]@{
        Rule = $rule
        Status = $Status
        Path = $Path
        BaseLineCount = $BaseLineCount
        HeadLineCount = $HeadLineCount
        MaximumLines = $MaximumLines
    }
}
