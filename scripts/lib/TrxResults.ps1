# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads VSTest TRX files supplied by callers
#   Writes:
#     - None
#   Requires:
#     - PowerShell 7

function Read-NervTrxDocument {
    param([Parameter(Mandatory)] [string] $Path)

    try {
        $document = [Xml.XmlDocument]::new()
        $document.Load($Path)
    }
    catch {
        throw [IO.InvalidDataException]::new("Failed to parse TRX '$([IO.Path]::GetFullPath($Path))'.")
    }

    $definitions = @($document.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
    $results = @($document.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    $counters = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw [IO.InvalidDataException]::new("TRX is missing ResultSummary/Counters in '$([IO.Path]::GetFullPath($Path))'.")
    }

    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = $total - $executed
    $actualPassed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Passed', [StringComparison]::Ordinal) }).Count
    $actualFailed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Failed', [StringComparison]::Ordinal) }).Count
    $actualSkipped = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'NotExecuted', [StringComparison]::Ordinal) }).Count
    if ($total -ne $results.Count -or $executed -ne ($passed + $failed) -or
        $passed -ne $actualPassed -or $failed -ne $actualFailed -or $skipped -ne $actualSkipped) {
        throw [IO.InvalidDataException]::new("TRX ResultSummary/Counters do not match Results in '$([IO.Path]::GetFullPath($Path))'.")
    }

    return [pscustomobject]@{
        Root = $document.DocumentElement
        Definitions = $definitions
        Results = $results
        Total = $total
        Executed = $executed
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
    }
}

function Get-NervTestLaneMemberTrxResult {
    param(
        [Parameter(Mandatory)] [string] $LaneLabel,
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string[]] $ExpectedTestIdentities,
        [switch] $AllowInvalid
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "$LaneLabel must produce exactly one TRX file; observed $($trxFiles.Count)." }
    $trx = Read-NervTrxDocument -Path $trxFiles[0].FullName
    $definitionById = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $trx.Definitions) {
        $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
        if ($null -ne $method) { $definitionById[[string]$definition.id] = "$($method.className).$($method.name)" }
    }
    $observed = @(
        foreach ($result in $trx.Results) {
            $definitionId = $result.GetAttribute('testId')
            if ($definitionById.ContainsKey($definitionId)) { $definitionById[$definitionId] }
            else { $result.GetAttribute('testName') }
        }
    )
    $expected = @($ExpectedTestIdentities)
    [Array]::Sort($observed, [StringComparer]::Ordinal)
    [Array]::Sort($expected, [StringComparer]::Ordinal)
    $identitiesMatch = [string]::Equals(($observed -join "`n"), ($expected -join "`n"), [StringComparison]::Ordinal)
    $valid = $identitiesMatch -and $trx.Passed -eq $expected.Count -and $trx.Failed -eq 0 -and $trx.Skipped -eq 0
    if (-not $AllowInvalid -and -not $identitiesMatch) { throw "$LaneLabel TRX identities do not equal the frozen member identities." }
    if (-not $AllowInvalid -and -not $valid) {
        throw "$LaneLabel requires $($expected.Count) passed, 0 failed and 0 skipped; observed $($trx.Passed) passed, $($trx.Failed) failed and $($trx.Skipped) skipped."
    }
    return [pscustomobject]@{
        total = $trx.Total
        passed = $trx.Passed
        failed = $trx.Failed
        skipped = $trx.Skipped
        identities = $observed
        identitiesMatch = $identitiesMatch
        valid = $valid
    }
}
