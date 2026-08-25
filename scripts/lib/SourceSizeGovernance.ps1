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

function Get-NervGeneratedSourceReason {
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path
    )

    $normalizedPath = $Path.Replace('\', '/')
    $segments = $normalizedPath.Split('/', [StringSplitOptions]::RemoveEmptyEntries)
    $fileName = if ($segments.Count -eq 0) { $normalizedPath } else { $segments[-1] }
    $extension = [IO.Path]::GetExtension($fileName)

    foreach ($segment in $segments) {
        if ([string]::Equals($segment, 'vendor', [StringComparison]::Ordinal)) { return 'vendor-directory' }
        foreach ($excludedSegment in @('bin', 'obj', 'node_modules', 'dist', 'coverage', 'artifacts')) {
            if ([string]::Equals($segment, $excludedSegment, [StringComparison]::Ordinal)) {
                return 'build-or-dependency-directory'
            }
        }
    }

    if ($normalizedPath.StartsWith('frontend/packages/api-client/src/generated/', [StringComparison]::Ordinal)) {
        return 'generated-api-client'
    }
    if ([string]::Equals($extension, '.cs', [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($segment in $segments) {
            if ([string]::Equals($segment, 'Migrations', [StringComparison]::Ordinal)) {
                return 'entity-framework-migration'
            }
        }
        if ($fileName.EndsWith('.Designer.cs', [StringComparison]::OrdinalIgnoreCase)) { return 'designer-csharp' }
        if ($fileName.EndsWith('.g.cs', [StringComparison]::OrdinalIgnoreCase)) { return 'generated-csharp' }
    }
    if ($fileName.Contains('.generated.', [StringComparison]::OrdinalIgnoreCase)) { return 'generated-file-suffix' }

    return $null
}

function Test-NervGovernedSourcePath {
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Path,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string[]] $GovernedExtension
    )

    $extension = [IO.Path]::GetExtension($Path)
    $extensionMatch = $false
    foreach ($candidate in $GovernedExtension) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            throw 'Governed extensions must not contain empty values.'
        }
        $normalizedCandidate = if ($candidate.StartsWith('.', [StringComparison]::Ordinal)) { $candidate } else { ".$candidate" }
        if ([string]::Equals($extension, $normalizedCandidate, [StringComparison]::OrdinalIgnoreCase)) {
            $extensionMatch = $true
            break
        }
    }

    return $extensionMatch -and $null -eq (Get-NervGeneratedSourceReason -Path $Path)
}

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
