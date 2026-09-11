# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the backend stable wire code registries and the frontend display vocabularies
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
    Fails when the backend emits a stable code that the frontend vocabulary does not map to Chinese.

.DESCRIPTION
    A "stable code" is an ASCII token the backend publishes as part of its wire contract and the
    frontend is expected to turn into a sentence a shop-floor operator can act on. The two sides are
    hand-written in different languages and, before #3155, were never compared: rename a code on the
    backend and the frontend table simply stops matching. Nothing goes red — the frontend falls back,
    and the fallback is a designed-in path, not an exception branch.

    There are two channels. They do NOT share a producer and they do NOT share a consumer, so this
    checker compares them separately rather than folding them into one set.

      * Channel A — the error envelope's `message` slot.
        Producers: `public const string SafeCode` / `StableErrorCode` members (the established
        idiom for per-service exception types), plus every `public const string` inside a class whose
        name ends in `StableWireCodes`.
        Consumer: `STABLE_ERROR_MESSAGES` in
        frontend/packages/business-core/src/labels/stableErrorMessages.ts.
        Failure mode when unregistered: the envelope's message IS the bare code with no Chinese in
        it, so the raw ASCII token reaches the screen (on PDA a 400 has no local copy and the chain
        is actionableMessage ?? serverMessage ?? fallback).

      * Channel B — MES readiness block reasons, shaped `CODE: 中文事实`.
        Producer: `MesReadinessReasonCodes`.
        Consumer: `MES_READINESS_REASON_DISPLAYS` in
        frontend/packages/business-core/src/labels/mesReadinessReasons.ts.
        Failure mode when unregistered: the Chinese fact still shows — this channel does NOT put a
        bare code on screen — but `describeMesReadinessReason` takes the fallback branch, so the code
        gets no label/category/next step, and `RELEASE_IGNORED_TASK_BLOCKERS.has(code)` is false, so
        it silently blocks release (#3119's failure mode, replayed).

    Direction. The rule is containment, backend ⊆ frontend, not set equality. The frontend may keep a
    display for a code the backend no longer emits — `EQUIPMENT_UNAVAILABLE` and
    `EQUIPMENT_MAINTENANCE_CONFLICT` have zero producers in backend/**/src today and are legitimately
    retained. Requiring equality would report those as violations and push the repository toward an
    allowlist of permanent exceptions.

    What counts as a code — and what does not. Identity here is "declared as a public const in a
    producer registry", not "looks like a code". That distinction is measured, not stylistic, and it
    is checkable in both directions without counting anything:

      * The shape misses declarations. `WORK_ORDER_NOT_RELEASED` never appears inside a single
        string literal — it is assembled as `WorkOrderNotReleased + ": ..."` — so a scan for
        `"CODE: ..."` literals cannot see it, while this checker reads it straight off the registry.
      * The declarations miss shapes. Codes such as `MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE` exist
        only as literals at their throw sites and are not declared anywhere, so the literal scan
        sees them and this checker does not (they are KnownException messages carrying their own
        Chinese — see the out-of-scope list above).

    Neither set contains the other. No count is quoted here on purpose: the literal-shape number
    moves whenever a code is hoisted into a registry, so it would go stale without ever going red.
    To re-measure, run the scan yourself and say which head you ran it on:
      git grep -rhoE '"[A-Z][A-Z0-9_]{2,}: [^"]*"' -- 'backend/**/src/**/*.cs'

    Registration is therefore an act: declare the const. Codes written as bare literals at the
    emission site are NOT in this checker's face and get no protection from it — that is exactly how
    `WORK_ORDER_NOT_FOUND` stayed invisible next to `WORK_ORDER_NOT_RELEASED` for as long as it did.
    #3155 moved the known ones into registries; a future one written as a bare literal is out of
    scope here on purpose. Closing that residue would take a scan for every way to spell a code,
    which #3214/#3176 measured over three rounds as non-convergent (1139 lines, still evadable).

    Deliberately outside the face, and confirmed outside rather than forgotten:
      * `WmsAuthorizationException.Reason` / `WmsUnprocessableException.ReasonCode` forward caller
        supplied kebab tokens (`resource-not-assigned-to-self` and friends) through `SafeOutboundCode`.
        Those are values flowing through a parameter, not declarations; only the two fallbacks
        (`forbidden`, `unprocessable`) are declared and therefore checked.
      * `KnownException` messages shaped `CODE: 中文` (MES source-unavailable family and friends).
        They carry their own Chinese, so an unregistered one degrades to "prefix plus a readable
        sentence" rather than to a bare token. They are not in either consumer table and are not
        required to be.
      * Field-level validation copy inside `errorData`. Ruled out of scope by #3333.

    Discovery, not a name list. Channel A's producers are found by walking every *.cs under
    backend/** and dropping build and test output by path segment (`tests`, `obj`, `bin`), then
    matching the two member names and the class-name suffix. There is no registered list of producer
    files, so a new service that follows the idiom is picked up without editing this checker. That is
    the property a whitelist would not have (#3122: three evasions, all of them newcomers).

    DO NOT "tidy" that scan face back into a `backend/**/src/**/*.cs` glob. It was written that way
    first and it was wrong: the shared contract assemblies under backend/common/Contracts/** have no
    `src` directory, so `EquipmentRuntimeReasonCodes` — which the readiness registry re-exports —
    fell outside the face. That surfaced as eight "could not be resolved" failures rather than as a
    quiet pass, and only because an unresolvable reference is a hard failure here. The exclusion
    list is the contract; the `/src/` glob is the bug it replaced. Anyone reconciling this comment
    with the code should change the comment, never the face.

    Known consequence of defining the face by exclusion, registered rather than silently tolerated:
    backend/common/Testing/** is inside it, because the excluded segment is `tests` and that
    directory is `Testing` — 21 .cs files on this head (`git ls-files 'backend/common/Testing/**/*.cs'`),
    declaring zero producers. It is left in the face rather than excluded: those are shared libraries
    that could legitimately declare a stable code, and a directory with no producers contributes
    nothing to either comparison. It is a potential source of false positives, not a defect today —
    if a fixture there ever declares a code that is not a real producer, exclude that file
    explicitly instead of widening the excluded word to `test*`, which would also drop product code.

    A gate that reads source text can disarm itself silently: rename the registry, empty it, and a
    containment comparison over two empty sets passes. So every step that could produce an empty set
    fails loudly instead — an unfindable class, an unparseable object literal, an unresolvable const
    reference and a zero-code result are each reported as failures, and the contract test
    scripts/tests/stable-code-frontend-vocabulary.Tests.ps1 pins that behaviour with fixtures.
#>

[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    # Overridden only by the contract test, which points the checker at throwaway fixture trees.
    [string] $BackendRoot = 'backend',

    [string] $StableErrorMessagesPath = 'frontend/packages/business-core/src/labels/stableErrorMessages.ts',

    [string] $ReadinessDisplaysPath = 'frontend/packages/business-core/src/labels/mesReadinessReasons.ts'
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

$ChannelBRegistryClass = 'MesReadinessReasonCodes'
$ChannelASuffix = 'StableWireCodes'
$ChannelAMemberNames = @('SafeCode', 'StableErrorCode')

$errors = [System.Collections.Generic.List[string]]::new()

# Returns the text between the braces of `<...> class <Name>`, brace-matched rather than
# line-counted. Line counting would fold whatever class sits next in the file into the result, and a
# containment check that silently gained codes from the neighbouring type would pass for the wrong
# reason.
function Get-CSharpClassBody {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $ClassName
    )

    $declaration = [regex]::Match($Text, "(?m)^\s*(?:public|internal)\s+static\s+class\s+$([regex]::Escape($ClassName))\b")
    if (-not $declaration.Success) {
        return $null
    }

    $open = $Text.IndexOf('{', $declaration.Index, [StringComparison]::Ordinal)
    if ($open -lt 0) {
        return $null
    }

    $depth = 0
    for ($i = $open; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($open + 1, $i - $open - 1)
            }
        }
    }

    return $null
}

# `public const string Name = "literal";` or `public const string Name = Other.Member;`
# Only `public`: the private members of the readiness registry are normalisation helpers that are
# never emitted as a block reason (`equipment.sourceUnavailable` is folded into
# `equipment.stateUnavailable` before it can reach a reason list), so requiring them in the frontend
# table would be a false positive.
function Get-PublicConstStringMember {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text
    )

    return @([regex]::Matches(
        $Text,
        '(?m)^\s*public\s+const\s+string\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<init>"(?:[^"\\]|\\.)*"|[A-Za-z_][A-Za-z0-9_.]*)\s*;') |
        ForEach-Object {
            [pscustomobject]@{
                Name = $_.Groups['name'].Value
                Init = $_.Groups['init'].Value
            }
        })
}

function ConvertFrom-CSharpStringLiteral {
    param([Parameter(Mandatory)] [string] $Literal)

    return $Literal.Substring(1, $Literal.Length - 2).Replace('\"', '"').Replace('\\', '\')
}

# Resolves `SomeClass.SomeMember` against the backend sources. Used by the readiness registry, which
# re-exports the shared equipment reason codes from Contracts. An unresolvable reference is reported
# rather than skipped: skipping it would quietly shrink the checked set, which is the same outcome as
# deleting the code from the registry.
function Resolve-ConstReference {
    param(
        [Parameter(Mandatory)] [string] $Reference,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.IO.FileInfo[]] $BackendFile
    )

    $lastDot = $Reference.LastIndexOf('.', [StringComparison]::Ordinal)
    if ($lastDot -le 0) {
        return $null
    }

    $typeName = $Reference.Substring(0, $lastDot)
    $typeName = $typeName.Substring($typeName.LastIndexOf('.', [StringComparison]::Ordinal) + 1)
    $memberName = $Reference.Substring($lastDot + 1)

    foreach ($file in $BackendFile) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if (-not $text.Contains("class $typeName", [StringComparison]::Ordinal)) {
            continue
        }

        $body = Get-CSharpClassBody -Text $text -ClassName $typeName
        if ($null -eq $body) {
            continue
        }

        foreach ($member in Get-PublicConstStringMember -Text $body) {
            if ([string]::Equals($member.Name, $memberName, [StringComparison]::Ordinal) -and $member.Init.StartsWith('"', [StringComparison]::Ordinal)) {
                return ConvertFrom-CSharpStringLiteral -Literal $member.Init
            }
        }
    }

    return $null
}

# Top-level keys of `export const <Name>...= { ... }`, brace-matched and depth-aware. A flat regex
# over the whole object would also collect the keys of the nested display records
# (`code:`, `category:`, `label:`, `nextStep:`), which are not codes.
function Get-TypeScriptObjectKey {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $ConstName
    )

    $declaration = [regex]::Match($Text, "(?m)^\s*export\s+const\s+$([regex]::Escape($ConstName))\b")
    if (-not $declaration.Success) {
        return $null
    }

    $open = $Text.IndexOf('{', $declaration.Index, [StringComparison]::Ordinal)
    if ($open -lt 0) {
        return $null
    }

    $keys = [System.Collections.Generic.List[string]]::new()
    $depth = 0
    $i = $open
    while ($i -lt $Text.Length) {
        $ch = $Text[$i]

        if ($ch -eq '/' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '/') {
            while ($i -lt $Text.Length -and $Text[$i] -ne "`n") { $i++ }
            continue
        }
        if ($ch -eq '/' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '*') {
            $close = $Text.IndexOf('*/', $i + 2, [StringComparison]::Ordinal)
            if ($close -lt 0) { return $null }
            $i = $close + 2
            continue
        }
        if ($ch -eq "'" -or $ch -eq '"' -or $ch -eq '`') {
            $quote = $ch
            $start = $i
            $i++
            while ($i -lt $Text.Length -and $Text[$i] -ne $quote) {
                if ($Text[$i] -eq '\') { $i++ }
                $i++
            }
            if ($i -ge $Text.Length) { return $null }
            if ($depth -eq 1) {
                # A quoted key is one whose closing quote is followed by a colon.
                $j = $i + 1
                while ($j -lt $Text.Length -and [char]::IsWhiteSpace($Text[$j])) { $j++ }
                if ($j -lt $Text.Length -and $Text[$j] -eq ':') {
                    $keys.Add($Text.Substring($start + 1, $i - $start - 1))
                }
            }
            $i++
            continue
        }
        if ($ch -eq '{' -or $ch -eq '[') { $depth++; $i++; continue }
        if ($ch -eq '}' -or $ch -eq ']') {
            $depth--
            if ($depth -eq 0) {
                return , $keys.ToArray()
            }
            $i++
            continue
        }
        if ($depth -eq 1 -and ([char]::IsLetter($ch) -or $ch -eq '_' -or $ch -eq '$')) {
            $start = $i
            while ($i -lt $Text.Length -and ([char]::IsLetterOrDigit($Text[$i]) -or $Text[$i] -eq '_' -or $Text[$i] -eq '$')) { $i++ }
            $word = $Text.Substring($start, $i - $start)
            $j = $i
            while ($j -lt $Text.Length -and [char]::IsWhiteSpace($Text[$j])) { $j++ }
            if ($j -lt $Text.Length -and $Text[$j] -eq ':') {
                $keys.Add($word)
            }
            continue
        }

        $i++
    }

    return $null
}

function Read-FrontendVocabulary {
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [string] $ConstName,
        [Parameter(Mandatory)] [string] $ChannelLabel
    )

    $fullPath = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $errors.Add("$ChannelLabel consumer vocabulary does not exist: $RelativePath.")
        return @()
    }

    $text = Get-Content -LiteralPath $fullPath -Raw
    $keys = Get-TypeScriptObjectKey -Text $text -ConstName $ConstName
    if ($null -eq $keys) {
        $errors.Add(
            "$RelativePath no longer declares a parseable 'export const $ConstName' object literal, so the " +
            "$ChannelLabel display vocabulary cannot be read. If the table moved, update this checker with " +
            'it; leaving it unable to find the consumer would turn the gate into a no-op.')
        return @()
    }

    $keys = @(Get-NervStringsSorted -Values @($keys) -Comparer ([StringComparer]::Ordinal) -Unique)
    if ($keys.Count -eq 0) {
        $errors.Add(
            "$ConstName in $RelativePath yielded no keys; every backend code is 'missing' from an empty " +
            'table, and an emptied table must be reported as a broken producer rather than as a wall of ' +
            'violations or — if the comparison were also removed — as a pass.')
    }

    return $keys
}

$backendFullRoot = Join-Path $RepositoryRoot $BackendRoot
$backendFiles = @()
if (-not (Test-Path -LiteralPath $backendFullRoot -PathType Container)) {
    $errors.Add("Backend root does not exist: $BackendRoot.")
}
else {
    # Product code only. The face is defined by exclusion rather than by requiring a `/src/` segment:
    # the shared contract assemblies under backend/common/Contracts/** have no `src` directory, and
    # `EquipmentRuntimeReasonCodes` — which the readiness registry re-exports — lives there. Requiring
    # `/src/` silently dropped it, which surfaced as eight unresolvable references rather than as a
    # quiet pass only because unresolvable references are a hard failure.
    $backendFiles = @(Get-ChildItem -LiteralPath $backendFullRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](?:tests|obj|bin)[\\/]' })
    if ($backendFiles.Count -eq 0) {
        $errors.Add(
            "No C# product source files were found under $BackendRoot; an empty producer scan makes both " +
            'containment checks vacuous.')
    }
}

# --- Channel A: error-envelope stable codes -------------------------------------------------------
$channelACodes = [System.Collections.Generic.List[pscustomobject]]::new()
foreach ($file in $backendFiles) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')

    foreach ($memberName in $ChannelAMemberNames) {
        foreach ($match in [regex]::Matches(
                $text,
                "(?m)^\s*public\s+const\s+string\s+$([regex]::Escape($memberName))\s*=\s*(?<lit>`"(?:[^`"\\]|\\.)*`")\s*;")) {
            $channelACodes.Add([pscustomobject]@{
                Code = ConvertFrom-CSharpStringLiteral -Literal $match.Groups['lit'].Value
                Source = $relative
            })
        }
    }

    foreach ($match in [regex]::Matches($text, "(?m)^\s*(?:public|internal)\s+static\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*$([regex]::Escape($ChannelASuffix)))\b")) {
        $className = $match.Groups['name'].Value
        $body = Get-CSharpClassBody -Text $text -ClassName $className
        if ($null -eq $body) {
            $errors.Add("$relative declares class $className but its body could not be brace-matched.")
            continue
        }

        $members = @(Get-PublicConstStringMember -Text $body)
        if ($members.Count -eq 0) {
            $errors.Add(
                "$relative declares the stable wire code registry $className but it holds no " +
                "'public const string' members; an emptied registry contributes nothing to the containment " +
                'check and would pass silently.')
            continue
        }

        foreach ($member in $members) {
            if (-not $member.Init.StartsWith('"', [StringComparison]::Ordinal)) {
                $resolved = Resolve-ConstReference -Reference $member.Init -BackendFile $backendFiles
                if ($null -eq $resolved) {
                    $errors.Add("${relative}: $className.$($member.Name) is initialised from '$($member.Init)', which could not be resolved to a string literal.")
                    continue
                }

                $channelACodes.Add([pscustomobject]@{ Code = $resolved; Source = "$relative ($className.$($member.Name))" })
                continue
            }

            $channelACodes.Add([pscustomobject]@{
                Code = ConvertFrom-CSharpStringLiteral -Literal $member.Init
                Source = "$relative ($className.$($member.Name))"
            })
        }
    }
}

if ($backendFiles.Count -gt 0 -and $channelACodes.Count -eq 0) {
    $errors.Add(
        "No channel A stable codes were found under $BackendRoot (looked for 'public const string " +
        "$($ChannelAMemberNames -join "'/'")' members and '*$ChannelASuffix' registry classes). An empty " +
        'producer set is contained in anything, so the check would pass vacuously.')
}

# --- Channel B: MES readiness block reason codes --------------------------------------------------
$channelBCodes = [System.Collections.Generic.List[pscustomobject]]::new()
$readinessRegistryFile = @($backendFiles | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw).Contains("class $ChannelBRegistryClass", [StringComparison]::Ordinal)
}) | Select-Object -First 1

if ($backendFiles.Count -gt 0 -and $null -eq $readinessRegistryFile) {
    $errors.Add(
        "No file under $BackendRoot declares 'class $ChannelBRegistryClass', so the readiness " +
        'block reason codes cannot be read. If the registry moved, update this checker with it.')
}
elseif ($null -ne $readinessRegistryFile) {
    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $readinessRegistryFile.FullName).Replace('\', '/')
    $body = Get-CSharpClassBody -Text (Get-Content -LiteralPath $readinessRegistryFile.FullName -Raw) -ClassName $ChannelBRegistryClass
    if ($null -eq $body) {
        $errors.Add("$relative declares $ChannelBRegistryClass but its class body could not be brace-matched.")
    }
    else {
        foreach ($member in Get-PublicConstStringMember -Text $body) {
            if ($member.Init.StartsWith('"', [StringComparison]::Ordinal)) {
                $channelBCodes.Add([pscustomobject]@{
                    Code = ConvertFrom-CSharpStringLiteral -Literal $member.Init
                    Source = "$relative ($ChannelBRegistryClass.$($member.Name))"
                })
                continue
            }

            $resolved = Resolve-ConstReference -Reference $member.Init -BackendFile $backendFiles
            if ($null -eq $resolved) {
                $errors.Add("${relative}: $ChannelBRegistryClass.$($member.Name) is initialised from '$($member.Init)', which could not be resolved to a string literal.")
                continue
            }

            $channelBCodes.Add([pscustomobject]@{ Code = $resolved; Source = "$relative ($ChannelBRegistryClass.$($member.Name))" })
        }

        if ($channelBCodes.Count -eq 0) {
            $errors.Add(
                "$ChannelBRegistryClass in $relative yielded no 'public const string' codes; an empty producer " +
                'set is contained in anything, so the readiness containment check would pass vacuously.')
        }
    }
}

$stableErrorKeys = Read-FrontendVocabulary -RelativePath $StableErrorMessagesPath -ConstName 'STABLE_ERROR_MESSAGES' -ChannelLabel 'Channel A'
$readinessKeys = Read-FrontendVocabulary -RelativePath $ReadinessDisplaysPath -ConstName 'MES_READINESS_REASON_DISPLAYS' -ChannelLabel 'Channel B'

function Test-Containment {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [pscustomobject[]] $ProducedCode,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $VocabularyKey,
        [Parameter(Mandatory)] [string] $VocabularyName,
        [Parameter(Mandatory)] [string] $VocabularyPath,
        [Parameter(Mandatory)] [string] $Consequence
    )

    $known = [System.Collections.Generic.HashSet[string]]::new([string[]] $VocabularyKey, [System.StringComparer]::Ordinal)
    $reported = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $ProducedCode) {
        if ($known.Contains($entry.Code) -or -not $reported.Add($entry.Code)) {
            continue
        }

        $errors.Add(
            "The backend publishes the stable code '$($entry.Code)' (declared in $($entry.Source)) but " +
            "$VocabularyName in $VocabularyPath does not map it. $Consequence Register the code in the " +
            'frontend vocabulary, or delete the backend declaration if the code is gone. The rule is ' +
            'one-directional: the frontend may keep entries the backend no longer emits.')
    }
}

if ($errors.Count -eq 0) {
    Test-Containment -ProducedCode $channelACodes.ToArray() -VocabularyKey $stableErrorKeys `
        -VocabularyName 'STABLE_ERROR_MESSAGES' -VocabularyPath $StableErrorMessagesPath `
        -Consequence 'That envelope carries the bare code as its message, so the raw ASCII token reaches the operator''s screen.'

    Test-Containment -ProducedCode $channelBCodes.ToArray() -VocabularyKey $readinessKeys `
        -VocabularyName 'MES_READINESS_REASON_DISPLAYS' -VocabularyPath $ReadinessDisplaysPath `
        -Consequence 'describeMesReadinessReason then takes its fallback branch, so the code gets no label or next step and RELEASE_IGNORED_TASK_BLOCKERS.has(code) is false — it silently blocks release.'
}

if ($errors.Count -gt 0) {
    Write-Host 'Stable code to frontend vocabulary consistency failed:'
    foreach ($failure in $errors) {
        Write-Host "  $failure"
    }

    exit 1
}

$channelADistinct = @(Get-NervStringsSorted -Values @($channelACodes | ForEach-Object { $_.Code }) -Comparer ([StringComparer]::Ordinal) -Unique)
$channelBDistinct = @(Get-NervStringsSorted -Values @($channelBCodes | ForEach-Object { $_.Code }) -Comparer ([StringComparer]::Ordinal) -Unique)

Write-Host 'Stable code to frontend vocabulary consistency passed:'
Write-Host "  Channel A: backend declares $($channelADistinct.Count) envelope stable codes; STABLE_ERROR_MESSAGES maps $($stableErrorKeys.Count) values."
Write-Host "  Channel B: backend declares $($channelBDistinct.Count) readiness block reason codes; MES_READINESS_REASON_DISPLAYS maps $($readinessKeys.Count) codes."
Write-Host '  Every backend code has a frontend display (backend is contained in the frontend vocabulary).'

exit 0
