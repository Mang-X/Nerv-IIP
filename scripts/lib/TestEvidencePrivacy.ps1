# Script-Governance:
#   Category: library
#   SideEffects:
#     - None; defines retained test-evidence privacy functions
#   Writes:
#     - None
#   Requires:
#     - PowerShell 7

function ConvertTo-NervRetainedDisplayName {
    param([AllowNull()] [string] $Text)

    $source = if ($null -eq $Text) { '' } else { $Text }
    if ([string]::IsNullOrWhiteSpace($source)) {
        return [pscustomobject]@{ text = (Protect-ScriptAutomationText $source); redactionCount = 0 }
    }

    $pattern = [regex]::new('(?i)(?<prefix>(?:^|[(,]\s*))(?<label>(?:body|requestBody|responseBody)\s*:\s*)')
    $builder = [Text.StringBuilder]::new()
    $position = 0
    $redactionCount = 0
    while ($position -lt $source.Length) {
        $match = $pattern.Match($source, $position)
        if (-not $match.Success) {
            [void]$builder.Append($source.Substring($position))
            break
        }

        [void]$builder.Append($source.Substring($position, $match.Index - $position))
        [void]$builder.Append($match.Groups['prefix'].Value)
        [void]$builder.Append($match.Groups['label'].Value)
        $valueStart = $match.Index + $match.Length
        $valueEnd = $valueStart
        if ($valueStart -lt $source.Length -and ($source[$valueStart] -eq [char]'"' -or $source[$valueStart] -eq [char]"'")) {
            $valueEnd = Find-NervQuotedTextEnd -Text $source -QuoteStart $valueStart
        }
        else {
            $depth = 0
            while ($valueEnd -lt $source.Length) {
                $character = $source[$valueEnd]
                if ($character -eq [char]'"' -or $character -eq [char]"'") {
                    $valueEnd = Find-NervQuotedTextEnd -Text $source -QuoteStart $valueEnd
                    continue
                }
                # `[char]` casts, not `-in` over string literals: `-in` compares as *strings*, which is
                # culture-aware. Char equality is numeric and is what a brace matcher wants.
                elseif ($character -eq [char]'{' -or $character -eq [char]'[' -or $character -eq [char]'(') { $depth++ }
                elseif ($character -eq [char]'}' -or $character -eq [char]']') { if ($depth -gt 0) { $depth-- } }
                elseif ($character -eq [char]')' -and $depth -eq 0) { break }
                elseif ($character -eq [char]')' -and $depth -gt 0) { $depth-- }
                elseif ($character -eq [char]',' -and $depth -eq 0) { break }
                $valueEnd++
            }
        }

        $rawValue = $source.Substring($valueStart, $valueEnd - $valueStart)
        if ($rawValue -cmatch '^["'']<redacted-body:[0-9a-f]{16}>["'']$') {
            [void]$builder.Append($rawValue)
            $position = $valueEnd
            continue
        }
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($rawValue))).ToLowerInvariant().Substring(0, 16)
        [void]$builder.Append("`"<redacted-body:$digest>`"")
        $redactionCount++
        $position = $valueEnd
    }
    [pscustomobject]@{ text = (Protect-ScriptAutomationText $builder.ToString()); redactionCount = $redactionCount }
}

function ConvertTo-NervRetainedFailureText {
    param([AllowNull()] [string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return 'Test failed; raw failure details are intentionally omitted by evidence privacy policy.'
}

function Get-NervRetainedSkipReason {
    param([Parameter(Mandatory)] [object] $Record)
    if (-not (Test-NervHasProperty -Object $Record -Name 'skipPolicyId') -or [string]::IsNullOrWhiteSpace([string]$Record.skipPolicyId)) {
        return 'Skipped; raw reason omitted because no approved policy matched.'
    }
    $safe = Protect-ScriptAutomationText ([string]$Record.skipReason)
    if ($safe.Length -gt 512) { return $safe.Substring(0, 512) }
    return $safe
}
