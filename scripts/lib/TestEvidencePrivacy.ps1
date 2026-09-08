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

function Get-NervRetainedFailureGrammar {
    # The retained-failure grammar is a *closed* construction, not a denylist filter.
    #
    # Invariant: every character of a retained failure text is produced by this grammar —
    # a fixed prefix, a closed field-name set, and a closed token alphabet. A substring of the
    # raw failure message reaches the artifact only when it (a) was captured by a named field
    # extractor and (b) matches the token alphabet as a whole token. Anything else collapses to
    # `<redacted-value:$digest>`. New/unknown message shapes therefore fail closed to the fixed
    # prose rather than leaking, and widening what is retained requires editing this grammar.
    #
    # Token order is significant: longer, more specific shapes must precede the shapes they
    # contain (timespan before number, key/value before dotted name), otherwise the shorter
    # alternative wins and the tail of the token lands in a digest gap.
    # Redaction markers are self-delimiting (they end in `>`), so they are matched outside the
    # boundary guards below. Inside them a marker glued to a following identifier — which the gap
    # collapser does produce, e.g. `<redacted-value:…>example.invalid` — would fail the trailing
    # guard, lose its token status, and drag the whole render into the fixed-prose fallback.
    $markers = @(
        '<redacted(?:-body|-value):[0-9a-f]{16}>',
        '<redacted(?:-pem)?>'
    )
    # Digit budget (#3213 round 2). A bare integer is simultaneously the reading this feature exists
    # to keep (`observations=7`, `Expected: 3`, `arrivals=0`) and the highest-risk business payload
    # there is (phone 11, bank card 16-19, national ID 18, order number >=8, money >=5). They are the
    # *same token class*, so no pattern can separate them — only a magnitude bound can. Every
    # diagnostic reading this repository's own helpers emit is either a small count or a `hh:mm:ss`
    # duration, which has its own token, so the bound is set at four integer digits.
    #
    # Registered loss, deliberately not worked around: any reading whose magnitude needs five or
    # more digits (byte counts, tick counts, large ids) is digested and cannot be read back. That
    # is the price of not shipping phone numbers to a public artifact, and it is the honest
    # statement of what this alphabet admits — see docs/governance/testing/evidence.md.
    $boundedNumber = '(?<!["''])-?[0-9]{1,4}(?:\.[0-9]{1,4})?(?!["''])'
    $identifierDigitGuard = '(?![A-Za-z0-9_.]*[0-9]{5})'
    $tokens = @(
        # The key/value shape carries the same digit budget as a bare number; without it
        # `salary=250000` rides through on the key/value alternative after the bare-number
        # alternative has already refused it.
        ('[A-Za-z_][A-Za-z0-9_.]*\s*[:=]\s*(?:' + $boundedNumber + '|true|false|null)'),
        # Quote guards on the digit-bearing shapes only. A number that sits inside quotes is a
        # string payload, not a reading: `"phone":"13800000000"` and `"WO20260907"` would otherwise
        # surrender their digits to the number alternative. The guard is per-alternative rather than
        # global so the `<redacted-…>` markers can still match when a quote precedes them, which the
        # fixed-point re-validation depends on.
        '(?<!["''])[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?!["''])',
        # GUIDs are deliberately NOT admitted. A GUID inside a failure *message* is an entity,
        # aggregate or tenant identifier — a subject identifier, which is exactly what evidence
        # privacy exists to withhold. Nothing is lost for provenance: the retained record already
        # carries `definitionId`, `testInstanceId`, `workflowRunId`, `headSha` and `testedSha` as
        # first-class collector-produced fields rather than as text scraped out of a message.
        $boundedNumber,
        # Dotted names are admitted only when they are *type-shaped*, not merely capitalised.
        # "Uppercase and dotted" is not a type-name predicate: `Zhang.Wei` and `Li.Ming` satisfy it
        # and are people. Admission therefore requires either an exception-style final segment or a
        # literal `Assert.` prefix, which is the whole of what the extractors can legitimately place
        # in `type`. Registered loss: ordinary type names (`Nerv.IIP.Business.Mes.WorkOrder`) are
        # digested; the failing test's full name already locates the code.
        # Case-sensitive on purpose — the alternation as a whole is IgnoreCase for the prose
        # vocabulary, so without `(?-i:)` the capital requirements below would not bind at all.
        # An identifier may not carry a long digit run.
        #
        # ⚠️ DO NOT DELETE AS REDUNDANT. This guard and the decision to retain exception type names
        # verbatim are a *pair*, and neither is closed without the other. Type names are admitted
        # because a type name is a compile-time source identifier and therefore cannot carry
        # run-time data — but that argument holds only for identifiers that are *nothing but* an
        # identifier. `Customer110101199003078888Error` satisfies the suffix rule while embedding a
        # national ID in its own spelling, which is the single case where "it is only source" stops
        # being true. This guard is what removes that exception, so removing the guard silently
        # falsifies the premise the type-name retention rests on and reopens the smuggling path.
        # Real type names never contain five consecutive digits, so the bound costs nothing.
        ('(?-i:' + $identifierDigitGuard + '[A-Z][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\.(?:[A-Za-z_][A-Za-z0-9_]*)?(?:Exception|Failure|Error|Timeout))'),
        ('(?-i:' + $identifierDigitGuard + 'Assert\.[A-Z][A-Za-z0-9_]*)'),
        ('(?-i:' + $identifierDigitGuard + '[A-Za-z_][A-Za-z0-9_]*(?:Exception|Failure|Error|Timeout))'),
        # Closed prose vocabulary. These words appear in the message templates this repository's
        # own test helpers and xUnit emit; they carry no payload. A word outside the list is
        # digested, so forgetting to add one degrades readability, never privacy.
        '(?:actual|after|and|assertion|attempts|but|collection|condition|contains|count|differ|elapsed|elements|empty|equal|error|expected|failing|failure|first|for|found|holds|in|index|is|item|items|last|message|matching|no|none|not|null|observation|observations|of|operation|out|position|retries|satisfied|single|still|string|the|timed|timeout|to|total|true|false|type|values|was|were|with)'
    )
    return [pscustomobject]@{
        Prefix = 'Test failed; retained diagnostics: '
        Prose = 'Test failed; raw failure details are intentionally omitted by evidence privacy policy.'
        FieldNames = @('type', 'expected', 'actual', 'condition', 'operation', 'observations', 'elapsed', 'lastObservation')
        # Token-boundary guards: a token only counts when it is not glued to more identifier
        # material. Without them `WO-20260907-0001` would surrender its digits to the number
        # alternative and `Manufacturing` would surrender an `in` to the prose vocabulary.
        TokenPattern = ('(?:' + ($markers -join '|') + ')|(?<![A-Za-z0-9_.-])(?:' + ($tokens -join '|') + ')(?![A-Za-z0-9_-])')
        SeparatorPattern = '^[ ,:()\[\].''"/-]*$'
        MaximumValueLength = 240
        # Eight fields at MaximumValueLength plus names and separators fit inside this bound, so a
        # legal render can never be truncated into an illegal one and silently fall back to prose.
        MaximumTextLength = 2400
    }
}

function ConvertTo-NervRetainedDiagnosticValue {
    param([AllowNull()] [string] $Text)

    $grammar = Get-NervRetainedFailureGrammar
    $source = if ($null -eq $Text) { '' } else { $Text }
    # `;` is the field separator of the rendered text, so it can never survive inside a value.
    $source = [regex]::Replace($source, '[\r\n\t;]+', ' ').Trim()
    if ($source.Length -gt $grammar.MaximumValueLength) { $source = $source.Substring(0, $grammar.MaximumValueLength) }
    if ([string]::IsNullOrWhiteSpace($source)) { return '' }

    $tokenRegex = [regex]::new($grammar.TokenPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $separatorRegex = [regex]::new($grammar.SeparatorPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $builder = [Text.StringBuilder]::new()
    $position = 0
    foreach ($match in $tokenRegex.Matches($source)) {
        $gap = $source.Substring($position, $match.Index - $position)
        [void]$builder.Append((Convert-NervRetainedDiagnosticGap -Gap $gap -SeparatorRegex $separatorRegex))
        [void]$builder.Append($match.Value)
        $position = $match.Index + $match.Length
    }
    [void]$builder.Append((Convert-NervRetainedDiagnosticGap -Gap $source.Substring($position) -SeparatorRegex $separatorRegex))
    return [regex]::Replace($builder.ToString(), '\s{2,}', ' ').Trim()
}

function Convert-NervRetainedDiagnosticGap {
    param(
        [AllowNull()] [string] $Gap,
        [Parameter(Mandatory)] [regex] $SeparatorRegex
    )

    if ([string]::IsNullOrEmpty($Gap)) { return '' }
    if ($SeparatorRegex.IsMatch($Gap)) { return $Gap }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Gap.Trim()))).ToLowerInvariant().Substring(0, 16)
    $leading = if ($Gap.StartsWith(' ', [StringComparison]::Ordinal)) { ' ' } else { '' }
    $trailing = if ($Gap.EndsWith(' ', [StringComparison]::Ordinal)) { ' ' } else { '' }
    return "$leading<redacted-value:$digest>$trailing"
}

function Test-NervRetainedFailureText {
    param([AllowNull()] [string] $Text)

    $grammar = Get-NervRetainedFailureGrammar
    if ($null -eq $Text) { return $false }
    if ([string]::Equals($Text, $grammar.Prose, [StringComparison]::Ordinal)) { return $true }
    if (-not $Text.StartsWith($grammar.Prefix, [StringComparison]::Ordinal)) { return $false }
    if ($Text.Length -gt $grammar.MaximumTextLength) { return $false }

    $body = $Text.Substring($grammar.Prefix.Length)
    if ([string]::IsNullOrWhiteSpace($body)) { return $false }
    $tokenRegex = [regex]::new($grammar.TokenPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    $separatorRegex = [regex]::new($grammar.SeparatorPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    foreach ($field in $body -split '; ') {
        $separator = $field.IndexOf('=', [StringComparison]::Ordinal)
        if ($separator -le 0) { return $false }
        $name = $field.Substring(0, $separator)
        if (-not (@($grammar.FieldNames) | Where-Object { [string]::Equals($_, $name, [StringComparison]::Ordinal) })) { return $false }
        $value = $field.Substring($separator + 1)
        if ([string]::IsNullOrEmpty($value)) { return $false }
        # A value is legal only when it is *entirely* token alphabet plus separators, which is the
        # same closure the renderer produces. That is what makes accepting an already-retained text
        # verbatim safe: anything that passes this check discloses nothing the grammar would not
        # have produced itself, so a raw message that merely imitates the prefix cannot ride through.
        $position = 0
        foreach ($match in $tokenRegex.Matches($value)) {
            if (-not $separatorRegex.IsMatch($value.Substring($position, $match.Index - $position))) { return $false }
            $position = $match.Index + $match.Length
        }
        if (-not $separatorRegex.IsMatch($value.Substring($position))) { return $false }
    }
    return $true
}

function ConvertTo-NervRetainedFailureText {
    param([AllowNull()] [string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

    $grammar = Get-NervRetainedFailureGrammar
    # Retention runs twice on the same value (parser, then normalized-TRX writer) and the writer's
    # output is re-parsed by the round-trip contract, so retention must be a fixed point. The check
    # is full grammar re-validation rather than a prefix sniff.
    if (Test-NervRetainedFailureText $Text) { return $Text }

    $source = [string]$Text
    $fields = [Collections.Specialized.OrderedDictionary]::new()
    # `type` goes through the same value reducer as every other field. It used to be written
    # straight from the extractor's capture, which meant the extractor's regex — not the token
    # alphabet — decided what could land in it, and the two do not agree: the capture class allows
    # dots, so anything ending in `Error` rode through unreduced and unbounded. Nothing may reach a
    # retained field except through `ConvertTo-NervRetainedDiagnosticValue`.
    $typeCandidate = $null
    $typeMatch = [regex]::Match($source, '(?m)^\s*(?<value>[A-Za-z_][A-Za-z0-9_.]*(?:Exception|Error))\s*:')
    if (-not $typeMatch.Success) {
        $assertMatch = [regex]::Match($source, '(?m)Assert\.(?<value>[A-Za-z]+)\(\)\s*Failure')
        if ($assertMatch.Success) { $typeCandidate = "Assert.$($assertMatch.Groups['value'].Value)" }
    }
    else { $typeCandidate = $typeMatch.Groups['value'].Value }
    if (-not [string]::IsNullOrWhiteSpace($typeCandidate)) {
        $reducedType = ConvertTo-NervRetainedDiagnosticValue $typeCandidate
        if (-not [string]::IsNullOrWhiteSpace($reducedType)) { $fields['type'] = $reducedType }
    }

    $namedCaptures = @(
        [pscustomobject]@{ Field = 'condition'; Pattern = "(?m)Condition\s*'(?<value>[^']*)'" },
        [pscustomobject]@{ Field = 'operation'; Pattern = "(?m)Operation\s*'(?<value>[^']*)'" },
        [pscustomobject]@{ Field = 'expected'; Pattern = '(?m)^[^\S\r\n]*Expected:[^\S\r\n]*(?<value>.*)$' },
        [pscustomobject]@{ Field = 'actual'; Pattern = '(?m)^[^\S\r\n]*Actual:[^\S\r\n]*(?<value>.*)$' },
        [pscustomobject]@{ Field = 'observations'; Pattern = '(?m)\((?<value>[0-9]+)\s+observations\)' },
        [pscustomobject]@{ Field = 'elapsed'; Pattern = '(?m)(?:timed out after|not satisfied after)\s+(?<value>[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?)' },
        [pscustomobject]@{ Field = 'lastObservation'; Pattern = '(?m)Last observation:[^\S\r\n]*(?<value>.*)$' }
    )
    foreach ($capture in $namedCaptures) {
        $match = [regex]::Match($source, $capture.Pattern)
        if (-not $match.Success) { continue }
        $value = ConvertTo-NervRetainedDiagnosticValue $match.Groups['value'].Value
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        $fields[[string]$capture.Field] = $value
    }
    if (@($fields.Keys).Count -eq 0) { return $grammar.Prose }

    $rendered = @(foreach ($field in @($grammar.FieldNames)) {
        if ($fields.Contains($field)) { "$field=$($fields[$field])" }
    })
    $retained = Protect-ScriptAutomationText ($grammar.Prefix + ($rendered -join '; '))
    if ($retained.Length -gt $grammar.MaximumTextLength) { $retained = $retained.Substring(0, $grammar.MaximumTextLength) }
    # Belt and braces: if the shared redactor or the length bound produced anything the grammar
    # would not itself emit, fall back to the fixed prose rather than shipping an unvalidated text.
    if (-not (Test-NervRetainedFailureText $retained)) { return $grammar.Prose }
    return $retained
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
