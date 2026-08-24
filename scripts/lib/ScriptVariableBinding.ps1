# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads PowerShell alias and cmdlet parameter metadata for static variable-binding analysis
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

$script:nervScriptVariableScopeQualifiers = @('local', 'script', 'global', 'private', 'variable')
$script:nervScriptVariableBinderCanonicalNames = @('Set-Variable', 'New-Variable')
$script:nervScriptVariableBinderCommands = [Collections.Hashtable]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($seamBinderCanonicalName in $script:nervScriptVariableBinderCanonicalNames) {
    $script:nervScriptVariableBinderCommands[$seamBinderCanonicalName] = $seamBinderCanonicalName
    foreach ($alias in @(Get-Alias -Definition $seamBinderCanonicalName -ErrorAction SilentlyContinue)) {
        $script:nervScriptVariableBinderCommands[[string]$alias.Name] = $seamBinderCanonicalName
    }
}
foreach ($floorEntry in @(
        @{ Alias = 'set'; Canonical = 'Set-Variable' },
        @{ Alias = 'sv'; Canonical = 'Set-Variable' },
        @{ Alias = 'nv'; Canonical = 'New-Variable' })) {
    if (-not $script:nervScriptVariableBinderCommands.ContainsKey($floorEntry.Alias)) {
        $script:nervScriptVariableBinderCommands[$floorEntry.Alias] = $floorEntry.Canonical
    }
}
$script:nervScriptVariableBinderParameterCache = [Collections.Hashtable]::new([StringComparer]::Ordinal)

function Resolve-NervScriptVariableBinderCanonicalName {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $WrittenName)

    if ([string]::IsNullOrEmpty($WrittenName)) { return $null }
    $separator = $WrittenName.LastIndexOf('\', [StringComparison]::Ordinal)
    $bareName = if ($separator -lt 0) { $WrittenName } else { $WrittenName.Substring($separator + 1) }
    if (-not $script:nervScriptVariableBinderCommands.ContainsKey($bareName)) { return $null }
    return [string]$script:nervScriptVariableBinderCommands[$bareName]
}

function Get-NervScriptVariableBindingNameFromText {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $UserPath)

    $candidate = $UserPath.TrimStart('$')
    $separator = $candidate.IndexOf(':', [StringComparison]::Ordinal)
    if ($separator -lt 0) { return $candidate }
    $qualifier = $candidate.Substring(0, $separator)
    if (@($script:nervScriptVariableScopeQualifiers | Where-Object {
                [string]::Equals([string]$_, $qualifier, [StringComparison]::OrdinalIgnoreCase)
            }).Count -eq 0) { return $null }
    return $candidate.Substring($separator + 1)
}

function Get-NervScriptVariableBindingName {
    param([Parameter(Mandatory)] [Management.Automation.VariablePath] $VariablePath)

    return Get-NervScriptVariableBindingNameFromText -UserPath ([string]$VariablePath.UserPath)
}

function Get-NervScriptVariableBinderParameters {
    param([Parameter(Mandatory)] [string] $CanonicalName)

    if ($script:nervScriptVariableBinderParameterCache.ContainsKey($CanonicalName)) {
        return $script:nervScriptVariableBinderParameterCache[$CanonicalName]
    }
    $command = Get-Command -Name $CanonicalName -CommandType Cmdlet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "Script variable binding analysis cannot resolve the '$CanonicalName' cmdlet parameter metadata."
    }
    $parameters = [Collections.Hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($parameter in $command.Parameters.Values) {
        $entry = [pscustomobject]@{ Name = [string]$parameter.Name; TakesValue = ($parameter.ParameterType -ne [switch]) }
        $parameters[[string]$parameter.Name] = $entry
        foreach ($alias in @($parameter.Aliases)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$alias)) { $parameters[[string]$alias] = $entry }
        }
    }
    $script:nervScriptVariableBinderParameterCache[$CanonicalName] = $parameters
    return $parameters
}

function Resolve-NervScriptVariableBinderParameter {
    param(
        [Parameter(Mandatory)] [hashtable] $Parameters,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Written)

    if ([string]::IsNullOrEmpty($Written)) { return $null }
    if ($Parameters.ContainsKey($Written)) { return $Parameters[$Written] }
    $prefixMatches = @($Parameters.Keys | Where-Object { ([string]$_).StartsWith($Written, [StringComparison]::OrdinalIgnoreCase) })
    if ($prefixMatches.Count -eq 0) { return $null }
    $resolvedNames = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($prefixMatches | ForEach-Object { [string]$Parameters[$_].Name }),
        [StringComparer]::OrdinalIgnoreCase)
    if ($resolvedNames.Count -ne 1) { return $null }
    return $Parameters[$prefixMatches[0]]
}

function Get-NervScriptVariableBinderNameArgument {
    param([Parameter(Mandatory)] [Management.Automation.Language.CommandAst] $Binder)

    $canonicalName = Resolve-NervScriptVariableBinderCanonicalName -WrittenName ([string]$Binder.GetCommandName())
    if ([string]::IsNullOrEmpty([string]$canonicalName)) { return $null }
    $parameters = Get-NervScriptVariableBinderParameters -CanonicalName $canonicalName
    $elements = @($Binder.CommandElements)
    $nameArgument = $null
    $index = 1
    while ($index -lt $elements.Count) {
        $element = $elements[$index]
        if ($element -isnot [Management.Automation.Language.CommandParameterAst]) {
            if ($null -eq $nameArgument) { $nameArgument = $element }
            $index++
            continue
        }
        $resolved = Resolve-NervScriptVariableBinderParameter -Parameters $parameters -Written ([string]$element.ParameterName)
        if ($null -eq $resolved) { return $null }
        if ([string]::Equals([string]$resolved.Name, 'Name', [StringComparison]::OrdinalIgnoreCase)) {
            if ($null -ne $element.Argument) { return $element.Argument }
            if (($index + 1) -lt $elements.Count) { return $elements[$index + 1] }
            return $null
        }
        $index += if ($resolved.TakesValue -and $null -eq $element.Argument) { 2 } else { 1 }
    }
    return $nameArgument
}

function Get-NervScriptVariableBinderLiteralNames {
    param([Parameter(Mandatory)] [Management.Automation.Language.Ast] $Argument)

    $names = [Collections.Generic.List[string]]::new()
    if ($Argument -is [Management.Automation.Language.StringConstantExpressionAst]) {
        $names.Add([string]$Argument.Value)
        return $names
    }
    if ($Argument -is [Management.Automation.Language.ArrayLiteralAst]) {
        foreach ($element in @($Argument.Elements)) {
            foreach ($name in @(Get-NervScriptVariableBinderLiteralNames -Argument $element)) { $names.Add($name) }
        }
        return $names
    }
    $groupedStatements = $null
    if ($Argument -is [Management.Automation.Language.ParenExpressionAst]) { $groupedStatements = @($Argument.Pipeline) }
    elseif ($Argument -is [Management.Automation.Language.ArrayExpressionAst] -or
        $Argument -is [Management.Automation.Language.SubExpressionAst]) {
        $groupedStatements = @($Argument.SubExpression.Statements)
    }
    if ($null -ne $groupedStatements) {
        foreach ($statement in $groupedStatements) {
            if ($statement -isnot [Management.Automation.Language.PipelineAst]) { continue }
            foreach ($pipelineElement in @($statement.PipelineElements)) {
                if ($pipelineElement -isnot [Management.Automation.Language.CommandExpressionAst]) { continue }
                foreach ($name in @(Get-NervScriptVariableBinderLiteralNames -Argument $pipelineElement.Expression)) { $names.Add($name) }
            }
        }
    }
    return $names
}

function Get-NervScriptVariableCommandLiteralBindingNames {
    param([Parameter(Mandatory)] [Management.Automation.Language.CommandAst] $Command)

    $argument = Get-NervScriptVariableBinderNameArgument -Binder $Command
    if ($null -eq $argument) { return @() }
    $names = [Collections.Generic.List[string]]::new()
    foreach ($literalName in @(Get-NervScriptVariableBinderLiteralNames -Argument $argument)) {
        $name = Get-NervScriptVariableBindingNameFromText -UserPath ([string]$literalName)
        if (-not [string]::IsNullOrWhiteSpace([string]$name)) { $names.Add($name) }
    }
    return $names
}

function Test-NervScriptVariableNameTextMayMatch {
    param(
        [Parameter(Mandatory)] [string] $CandidateName,
        [Parameter(Mandatory)] [string] $Name)

    $candidate = Get-NervScriptVariableBindingNameFromText -UserPath $CandidateName
    if ([string]::IsNullOrWhiteSpace([string]$candidate)) { return $true }
    return [string]::Equals($candidate, $Name, [StringComparison]::OrdinalIgnoreCase)
}

function Test-NervScriptVariableProviderPathMayMatch {
    param(
        [AllowNull()] [Management.Automation.Language.Ast] $Candidate,
        [Parameter(Mandatory)] [string] $Name)

    if ($Candidate -isnot [Management.Automation.Language.StringConstantExpressionAst]) { return $false }
    $path = [string]$Candidate.Value
    if (-not $path.StartsWith('variable:', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $variablePath = $path.Substring('variable:'.Length).TrimStart('/', '\')
    return Test-NervScriptVariableNameTextMayMatch -CandidateName $variablePath -Name $Name
}

function Test-NervScriptVariableSetItemCommandWritesName {
    param(
        [Parameter(Mandatory)] [Management.Automation.Language.CommandAst] $Command,
        [Parameter(Mandatory)] [string] $Name)

    $commandName = [string]$Command.GetCommandName()
    if (-not (@('Set-Item', 'Microsoft.PowerShell.Management\Set-Item', 'si') | Where-Object {
                [string]::Equals([string]$_, $commandName, [StringComparison]::OrdinalIgnoreCase)
            })) { return $false }
    $elements = @($Command.CommandElements)
    $positionals = [Collections.Generic.List[Management.Automation.Language.Ast]]::new()
    $namedTargets = [Collections.Generic.List[Management.Automation.Language.Ast]]::new()
    for ($index = 1; $index -lt $elements.Count; $index++) {
        $element = $elements[$index]
        if ($element -isnot [Management.Automation.Language.CommandParameterAst]) {
            $positionals.Add($element)
            continue
        }
        $parameterName = [string]$element.ParameterName
        if (-not ([string]::Equals($parameterName, 'Path', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($parameterName, 'LiteralPath', [StringComparison]::OrdinalIgnoreCase))) { continue }
        $argument = if ($null -ne $element.Argument) { $element.Argument }
        elseif ($index + 1 -lt $elements.Count -and $elements[$index + 1] -isnot [Management.Automation.Language.CommandParameterAst]) {
            $index++
            $elements[$index]
        }
        else { $null }
        if ($null -ne $argument) { $namedTargets.Add($argument) }
    }
    foreach ($candidate in $namedTargets) {
        if (Test-NervScriptVariableProviderPathMayMatch -Candidate $candidate -Name $Name) { return $true }
    }
    return $namedTargets.Count -eq 0 -and $positionals.Count -gt 0 -and
        (Test-NervScriptVariableProviderPathMayMatch -Candidate $positionals[0] -Name $Name)
}
