# Script-Governance:
#   Category: library
#   SideEffects:
#     - Deletes an exact diagnostic artifact path when its post-write scan detects retained secret material
#   Writes:
#     - A caller-declared canonical result through atomic file replacement
#     - Same-directory diagnostic artifact and .<leaf>.<guid>.tmp files through atomic file replacement
#   Cleanup:
#     - Removes owned temporary canonical files after each persistence attempt
#     - Removes owned diagnostic temp files after each persistence attempt
#     - Removes the exact diagnostic temp or published artifact whose scan detects retained secret material
#   Requires:
#     - PowerShell 7

. (Join-Path $PSScriptRoot 'AcceptanceScenarioMatrix.ps1')
. (Join-Path $PSScriptRoot 'ScriptVariableBinding.ps1')

function Assert-NervAcceptanceCanonicalPhysicalPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Context,
        [Parameter(Mandatory)] [ValidateSet('File', 'Directory')] [string] $PathType
    )

    Assert-NervAcceptanceString -Value $Path -Context "$Context path"
    $fullPath = [IO.Path]::GetFullPath($Path)
    $providedPath = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) {
        [IO.Path]::TrimEndingDirectorySeparator($Path)
    }
    else { $Path }
    $canonicalPath = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) {
        [IO.Path]::TrimEndingDirectorySeparator($fullPath)
    }
    else { $fullPath }
    if (-not [string]::Equals($providedPath, $canonicalPath, [StringComparison]::Ordinal)) {
        throw "$Context must be a canonical absolute path."
    }
    $testPathType = if ([string]::Equals($PathType, 'Directory', [StringComparison]::Ordinal)) { 'Container' } else { 'Leaf' }
    if (-not (Test-Path -LiteralPath $canonicalPath -PathType $testPathType)) {
        throw "$Context must identify one existing $($PathType.ToLowerInvariant())."
    }

    $item = Get-Item -LiteralPath $canonicalPath -Force
    while ($null -ne $item) {
        if (-not [string]::IsNullOrEmpty([string]$item.LinkTarget) -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Context must not contain a symbolic link or reparse point."
        }
        $item = if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent }
    }
    return $canonicalPath
}

function Resolve-NervAcceptanceCanonicalOutputPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Context
    )

    $canonicalRepositoryRoot = Assert-NervAcceptanceCanonicalPhysicalPath -Path $RepositoryRoot -Context "$Context repository root" -PathType Directory
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [string]::Equals($Path, $Path.Trim(), [StringComparison]::Ordinal)) {
        throw "$Context must be a canonical absolute path inside the repository root."
    }
    try { $canonicalPath = [IO.Path]::GetFullPath($Path) }
    catch { throw "$Context must be a canonical absolute path inside the repository root." }
    if (-not [string]::Equals($Path, $canonicalPath, [StringComparison]::Ordinal)) {
        throw "$Context must be a canonical absolute path inside the repository root."
    }
    $relativePath = [IO.Path]::GetRelativePath($canonicalRepositoryRoot, $canonicalPath).Replace([IO.Path]::DirectorySeparatorChar, '/')
    if (-not (Test-NervAcceptanceChangedPath -Path $relativePath)) {
        throw "$Context must remain inside the repository root."
    }

    $candidate = Split-Path -Parent $canonicalPath
    while (-not [string]::IsNullOrEmpty($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (-not [string]::IsNullOrEmpty([string]$item.LinkTarget) -or
                ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Context must not contain a symbolic link or reparse point."
            }
        }
        if ([string]::Equals([IO.Path]::TrimEndingDirectorySeparator($candidate), $canonicalRepositoryRoot, [StringComparison]::Ordinal)) { break }
        $parent = Split-Path -Parent $candidate
        if ([string]::Equals($parent, $candidate, [StringComparison]::Ordinal)) { break }
        $candidate = $parent
    }
    if (Test-Path -LiteralPath $canonicalPath) {
        $target = Get-Item -LiteralPath $canonicalPath -Force
        if ($target.PSIsContainer -or -not [string]::IsNullOrEmpty([string]$target.LinkTarget) -or
            ($target.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Context must identify a regular file path without symbolic links or reparse points."
        }
    }
    return $canonicalPath
}

function Write-NervAcceptanceCanonicalJson {
    param(
        [Parameter(Mandatory)] [object] $Value,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Context
    )

    $canonicalPath = Resolve-NervAcceptanceCanonicalOutputPath -Path $Path -RepositoryRoot $RepositoryRoot -Context $Context
    $directory = Split-Path -Parent $canonicalPath
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = Join-Path $directory ".$([IO.Path]::GetFileName($canonicalPath)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporaryPath, (($Value | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $canonicalPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
    return $canonicalPath
}

function Assert-NervAcceptanceCanonicalIntegerField {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Context
    )

    $value = $Object.PSObject.Properties[$Name].Value
    if (-not (Test-NervAcceptanceInteger -Value $value) -or [int64]$value -lt 0) {
        throw "$Context $Name must be a non-negative JSON integer."
    }
    return [int64]$value
}

function Test-NervAcceptanceWmsPickingReadbacks {
    param(
        [AllowEmptyCollection()] [object[]] $Readbacks = @(),
        [AllowEmptyCollection()] [decimal[]] $RequestedQuantities = @()
    )

    if ($Readbacks.Count -eq 0 -or $Readbacks.Count -ne $RequestedQuantities.Count) { return $false }
    for ($index = 0; $index -lt $Readbacks.Count; $index++) {
        $readback = $Readbacks[$index]
        if ($null -eq $readback -or
            -not [string]::Equals([string]$readback.status, 'Completed', [StringComparison]::OrdinalIgnoreCase) -or
            [decimal]$readback.executedQuantity -ne [decimal]$readback.plannedQuantity -or
            [decimal]$readback.executedQuantity -ne $RequestedQuantities[$index] -or
            [string]::IsNullOrWhiteSpace([string]$readback.completedAtUtc)) {
            return $false
        }
    }
    return $true
}

function Test-NervAcceptanceWmsCompletionReplay {
    param(
        [Parameter(Mandatory)] [object] $FirstCompletion,
        [Parameter(Mandatory)] [object] $ReplayCompletion
    )

    $firstRequestId = [string]$FirstCompletion.data.requestId
    $replayRequestId = [string]$ReplayCompletion.data.requestId
    return -not [string]::IsNullOrWhiteSpace($firstRequestId) -and
        [string]::Equals($replayRequestId, $firstRequestId, [StringComparison]::Ordinal)
}

function Test-NervAcceptanceWmsCompletedOutboundReadback {
    param([Parameter(Mandatory)] [AllowNull()] [object] $Readback)

    return $null -ne $Readback -and
        [string]::Equals([string]$Readback.status, 'Completed', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace([string]$Readback.completedAtUtc)
}

function Protect-NervAcceptanceWmsDiagnosticText {
    param(
        [AllowNull()] [string] $Text,
        [AllowEmptyCollection()] [string[]] $SensitiveValues = @()
    )

    if ($null -eq $Text) { return $null }
    $protected = Protect-ScriptAutomationText -Text $Text
    foreach ($sensitiveValue in @($SensitiveValues)) {
        if (-not [string]::IsNullOrWhiteSpace($sensitiveValue)) {
            $protected = $protected.Replace($sensitiveValue, '<redacted>', [StringComparison]::Ordinal)
        }
    }
    return $protected
}

function Write-NervAcceptanceWmsDiagnosticArtifact {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [AllowNull()] [string] $Content,
        [AllowEmptyCollection()] [string[]] $SensitiveValues = @()
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPath = Split-Path -Parent $fullPath
    [IO.Directory]::CreateDirectory($parentPath) | Out-Null
    $temporaryPath = Join-Path $parentPath ".$(Split-Path -Leaf $fullPath).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $protectedContent = Protect-NervAcceptanceWmsDiagnosticText -Text $Content -SensitiveValues $SensitiveValues
        [IO.File]::WriteAllText($temporaryPath, "$protectedContent`n", [Text.UTF8Encoding]::new($false))
        [void](Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $temporaryPath -SensitiveValues $SensitiveValues)
        [IO.File]::Move($temporaryPath, $fullPath, $true)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "WMS diagnostic artifact was not persisted: $fullPath"
        }
        [void](Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $fullPath -SensitiveValues $SensitiveValues)
        return [pscustomobject][ordered]@{
            artifactPath = $fullPath
            evidenceWritten = $true
            secretsRedacted = $true
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Assert-NervAcceptanceWmsDiagnosticArtifactRedacted {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [AllowEmptyCollection()] [string[]] $SensitiveValues = @()
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "WMS diagnostic artifact was not persisted: $Path"
    }
    $persistedContent = [IO.File]::ReadAllText($Path)
    $finding = $null
    foreach ($sensitiveValue in @($SensitiveValues)) {
        if (-not [string]::IsNullOrWhiteSpace($sensitiveValue) -and
            $persistedContent.Contains($sensitiveValue, [StringComparison]::Ordinal)) {
            $finding = 'declared sensitive value'
            break
        }
    }
    if ($null -eq $finding) {
        foreach ($pattern in @(
            '(?i)\bBearer\s+(?!<redacted>\b)[A-Za-z0-9._~+/=-]{6,}',
            '(?i)\b(?:password|pwd)\b\s*[=:]\s*["'']?(?!<redacted>\b)[^\s"'',;}]{4,}',
            '(?i)\b[a-z][a-z0-9+.-]*://[^\s/:@]+:(?!<redacted>@)[^\s/@]+@'
        )) {
            if ([regex]::IsMatch($persistedContent, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $finding = 'secret pattern'
                break
            }
        }
    }
    if ($null -ne $finding) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw "WMS diagnostic artifact retained a ${finding}: $Path"
    }
    return $true
}

function New-NervAcceptanceWmsSuccessfulDiagnosticEvidence {
    param(
        [Parameter(Mandatory)] [object] $WriteProof,
        [Parameter(Mandatory)] [object] $FailureCaptureSupported
    )

    Assert-NervAcceptanceObjectSchema -Object $WriteProof -AllowedFields @('artifactPath', 'evidenceWritten', 'secretsRedacted') -RequiredFields @('artifactPath', 'evidenceWritten', 'secretsRedacted') -Context 'MAN-527 successful diagnostic write proof'
    if ($WriteProof.evidenceWritten -isnot [bool] -or $WriteProof.secretsRedacted -isnot [bool] -or
        [string]::IsNullOrWhiteSpace([string]$WriteProof.artifactPath) -or
        -not [bool]$WriteProof.evidenceWritten -or -not [bool]$WriteProof.secretsRedacted) {
        throw 'MAN-527 successful diagnostics require an actual persisted artifact and a passing post-write sensitive-value scan.'
    }
    if ($FailureCaptureSupported -isnot [bool] -or -not [bool]$FailureCaptureSupported) {
        throw 'MAN-527 successful diagnostics require a passing failure-capture contract proof.'
    }
    return [pscustomobject][ordered]@{
        failureCaptureSupported = [bool]$FailureCaptureSupported
        failureDiagnosticsCaptured = $false
        secretsRedacted = [bool]$WriteProof.secretsRedacted
        artifactPaths = @()
        errors = @()
    }
}

function Test-NervAcceptanceWmsVerifierContract {
    param([Parameter(Mandatory)] [string] $Path)

    $tokens = $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        return [pscustomobject][ordered]@{ failureCaptureSupported = $false; pickingReadbackWired = $false; completionReplayWired = $false; outboundCompletionWired = $false }
    }

    $hasAncestor = {
        param(
            [Management.Automation.Language.Ast] $Node,
            [Management.Automation.Language.Ast] $ExpectedAncestor)
        $ancestor = $Node.Parent
        while ($null -ne $ancestor) {
            if ([object]::ReferenceEquals($ancestor, $ExpectedAncestor)) { return $true }
            $ancestor = $ancestor.Parent
        }
        return $false
    }
    $typedInventory = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.AssignmentStatementAst] -or
                    $node -is [Management.Automation.Language.CommandAst] -or
                    $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -or
                    $node -is [Management.Automation.Language.FunctionDefinitionAst] -or
                    $node -is [Management.Automation.Language.TryStatementAst] -or
                    $node -is [Management.Automation.Language.ReturnStatementAst]
            }, $true))
    $assignments = [Collections.Generic.List[object]]::new()
    $commands = [Collections.Generic.List[object]]::new()
    $invokeMembers = [Collections.Generic.List[object]]::new()
    $functions = [Collections.Generic.List[object]]::new()
    $tryStatements = [Collections.Generic.List[object]]::new()
    $returnStatements = [Collections.Generic.List[object]]::new()
    $conditionalNodeFlags = [Collections.Generic.Dictionary[Management.Automation.Language.Ast, bool]]::new()
    $inactiveNodeFlags = [Collections.Generic.Dictionary[Management.Automation.Language.Ast, bool]]::new()
    $commandsByName = [Collections.Hashtable]::new([StringComparer]::Ordinal)
    foreach ($node in $typedInventory) {
        if ($node -is [Management.Automation.Language.AssignmentStatementAst]) { $assignments.Add($node) }
        elseif ($node -is [Management.Automation.Language.CommandAst]) {
            $commands.Add($node)
            $commandName = [string]$node.GetCommandName()
            if (-not [string]::IsNullOrEmpty($commandName)) {
                if (-not $commandsByName.ContainsKey($commandName)) {
                    $commandsByName[$commandName] = [Collections.Generic.List[object]]::new()
                }
                $commandsByName[$commandName].Add($node)
            }
        }
        elseif ($node -is [Management.Automation.Language.InvokeMemberExpressionAst]) { $invokeMembers.Add($node) }
        elseif ($node -is [Management.Automation.Language.FunctionDefinitionAst]) { $functions.Add($node) }
        elseif ($node -is [Management.Automation.Language.TryStatementAst]) { $tryStatements.Add($node) }
        elseif ($node -is [Management.Automation.Language.ReturnStatementAst]) {
            $returnStatements.Add($node)
            continue
        }
        $conditional = $false
        $inactive = $false
        $ancestor = $node.Parent
        while ($null -ne $ancestor) {
            if ($ancestor -is [Management.Automation.Language.IfStatementAst] -or
                $ancestor -is [Management.Automation.Language.LoopStatementAst] -or
                $ancestor -is [Management.Automation.Language.SwitchStatementAst] -or
                $ancestor -is [Management.Automation.Language.TernaryExpressionAst]) { $conditional = $true }
            if ($ancestor -is [Management.Automation.Language.FunctionDefinitionAst] -or
                $ancestor -is [Management.Automation.Language.ScriptBlockExpressionAst]) { $inactive = $true }
            if ($conditional -and $inactive) { break }
            $ancestor = $ancestor.Parent
        }
        $conditionalNodeFlags[$node] = $conditional
        $inactiveNodeFlags[$node] = $inactive
    }
    $exportFunctions = [Collections.Generic.List[object]]::new()
    foreach ($function in $functions) {
        if ([string]::Equals($function.Name, 'Export-Man527FailureDiagnostics', [StringComparison]::Ordinal) -and
            -not $conditionalNodeFlags[$function] -and -not $inactiveNodeFlags[$function]) {
            $exportFunctions.Add($function)
        }
    }
    $orderedCaptureCalls = [Collections.Generic.List[object]]::new()
    $topLevelTryStatements = [Collections.Generic.List[object]]::new()
    foreach ($tryStatement in $tryStatements) {
        if ($null -ne $tryStatement.Finally -and -not $inactiveNodeFlags[$tryStatement]) {
            $topLevelTryStatements.Add($tryStatement)
        }
    }
    foreach ($tryStatement in $topLevelTryStatements) {
        foreach ($catchClause in @($tryStatement.CatchClauses)) {
            $captureCandidates = if ($commandsByName.ContainsKey('Export-Man527FailureDiagnostics')) { @($commandsByName['Export-Man527FailureDiagnostics']) } else { @() }
            foreach ($captureCall in @($captureCandidates | Where-Object { & $hasAncestor $_ $catchClause.Body })) {
                if (-not $conditionalNodeFlags[$captureCall] -and
                    -not $inactiveNodeFlags[$captureCall]) {
                    [void]$orderedCaptureCalls.Add($captureCall)
                }
            }
        }
    }

    $getAssignmentVariableNames = {
        param([Management.Automation.Language.AssignmentStatementAst] $Assignment)

        $variableNodes = if ($Assignment.Left -is [Management.Automation.Language.VariableExpressionAst]) {
            @($Assignment.Left)
        }
        elseif ($Assignment.Left -is [Management.Automation.Language.ConvertExpressionAst] -and
            $Assignment.Left.Child -is [Management.Automation.Language.VariableExpressionAst]) {
            @($Assignment.Left.Child)
        }
        else {
            @($Assignment.Left.FindAll({ param($node) $node -is [Management.Automation.Language.VariableExpressionAst] }, $true))
        }
        foreach ($variableNode in $variableNodes) {
            Get-NervScriptVariableBindingName -VariablePath $variableNode.VariablePath
        }
    }
    $testAssignmentWritesVariable = {
        param(
            [Management.Automation.Language.AssignmentStatementAst] $Assignment,
            [string] $VariableName)

        foreach ($assignmentName in @(& $getAssignmentVariableNames $Assignment)) {
            if ([string]::Equals([string]$assignmentName, $VariableName, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        return $false
    }
    $contractVariableNames = @(
        'pickingLifecycleCompleted',
        'completionHttpReplayConverged',
        'completedOutboundOrder',
        'businessEvidence')
    $writesByVariable = [Collections.Hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($variableName in $contractVariableNames) {
        $writesByVariable[$variableName] = [Collections.Generic.HashSet[object]]::new()
    }
    foreach ($assignment in $assignments) {
        if ($inactiveNodeFlags[$assignment]) { continue }
        foreach ($assignmentName in @(& $getAssignmentVariableNames $assignment)) {
            if ($writesByVariable.ContainsKey([string]$assignmentName)) {
                [void]$writesByVariable[[string]$assignmentName].Add($assignment)
            }
        }
    }
    foreach ($command in $commands) {
        if ($inactiveNodeFlags[$command]) { continue }
        $commandName = [string]$command.GetCommandName()
        $binderName = Resolve-NervScriptVariableBinderCanonicalName -WrittenName $commandName
        if (-not [string]::IsNullOrEmpty($binderName)) {
            foreach ($boundName in @(Get-NervScriptVariableCommandLiteralBindingNames -Command $command)) {
                if ($writesByVariable.ContainsKey([string]$boundName)) {
                    [void]$writesByVariable[[string]$boundName].Add($command)
                }
            }
            continue
        }
        $itemName = Resolve-NervScriptVariableItemCanonicalName -WrittenName $commandName
        if (-not [string]::IsNullOrEmpty($itemName)) {
            foreach ($variableName in $contractVariableNames) {
                if (Test-NervScriptVariableItemCommandWritesName -Command $command -Name $variableName) {
                    [void]$writesByVariable[$variableName].Add($command)
                }
            }
        }
    }
    $getConstantBinding = {
        param([string] $VariableName)

        $allWrites = @($writesByVariable[$VariableName])
        if ($allWrites.Count -ne 1) { return $null }
        $binding = $allWrites[0]
        if ($binding -isnot [Management.Automation.Language.CommandAst] -or
            $conditionalNodeFlags[$binding] -or $inactiveNodeFlags[$binding] -or
            -not [string]::Equals([string]$binding.GetCommandName(), 'New-Variable', [StringComparison]::Ordinal) -or
            $binding.CommandElements.Count -ne 7 -or
            $binding.CommandElements[1] -isnot [Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals([string]$binding.CommandElements[1].ParameterName, 'Name', [StringComparison]::OrdinalIgnoreCase) -or
            $null -ne $binding.CommandElements[1].Argument -or
            $binding.CommandElements[2] -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
            -not [string]::Equals([string]$binding.CommandElements[2].Value, $VariableName, [StringComparison]::Ordinal) -or
            $binding.CommandElements[3] -isnot [Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals([string]$binding.CommandElements[3].ParameterName, 'Option', [StringComparison]::OrdinalIgnoreCase) -or
            $null -ne $binding.CommandElements[3].Argument -or
            $binding.CommandElements[4] -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
            -not [string]::Equals([string]$binding.CommandElements[4].Value, 'Constant', [StringComparison]::Ordinal) -or
            $binding.CommandElements[5] -isnot [Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals([string]$binding.CommandElements[5].ParameterName, 'Value', [StringComparison]::OrdinalIgnoreCase) -or
            $null -ne $binding.CommandElements[5].Argument -or
            $binding.CommandElements[6] -isnot [Management.Automation.Language.ParenExpressionAst]) { return $null }
        return $binding
    }
    $testConstantBinding = {
        param([string] $CommandName, [string] $VariableName)
        $binding = & $getConstantBinding $VariableName
        if ($null -eq $binding) { return $false }
        $valuePipeline = $binding.CommandElements[6].Pipeline
        if ($valuePipeline -isnot [Management.Automation.Language.PipelineAst] -or
            $valuePipeline.PipelineElements.Count -ne 1 -or
            $valuePipeline.PipelineElements[0] -isnot [Management.Automation.Language.CommandAst]) { return $false }
        $sourceCommand = $valuePipeline.PipelineElements[0]
        $matchingCommands = if ($commandsByName.ContainsKey($CommandName)) { @($commandsByName[$CommandName]) } else { @() }
        $directMatches = @($matchingCommands | Where-Object { [object]::ReferenceEquals($_, $sourceCommand) })
        return $directMatches.Count -eq 1 -and
            [string]::Equals([string]$sourceCommand.GetCommandName(), $CommandName, [StringComparison]::Ordinal) -and
            -not $conditionalNodeFlags[$sourceCommand] -and
            -not $inactiveNodeFlags[$sourceCommand]
    }

    $hasActivePsVariableSet = @($invokeMembers | Where-Object {
            $invoke = $_
            if ($inactiveNodeFlags[$invoke] -or $conditionalNodeFlags[$invoke] -or
                $invoke.Member -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
                -not [string]::Equals([string]$invoke.Member.Value, 'Set', [StringComparison]::OrdinalIgnoreCase) -or
                $invoke.Expression -isnot [Management.Automation.Language.MemberExpressionAst]) { return $false }
            $psVariable = $invoke.Expression
            if ($psVariable.Member -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
                -not [string]::Equals([string]$psVariable.Member.Value, 'PSVariable', [StringComparison]::OrdinalIgnoreCase) -or
                $psVariable.Expression -isnot [Management.Automation.Language.MemberExpressionAst]) { return $false }
            $sessionState = $psVariable.Expression
            return $sessionState.Member -is [Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals([string]$sessionState.Member.Value, 'SessionState', [StringComparison]::OrdinalIgnoreCase) -and
                $sessionState.Expression -is [Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $sessionState.Expression.VariablePath), 'ExecutionContext', [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0

    $testOutboundCompletion = {
        if ($hasActivePsVariableSet -or -not (& $testConstantBinding 'Wait-WmsOutboundOrder' 'completedOutboundOrder')) { return $false }
        $waitFunctions = @($functions | Where-Object {
                [string]::Equals($_.Name, 'Wait-WmsOutboundOrder', [StringComparison]::Ordinal) -and
                    -not $conditionalNodeFlags[$_] -and
                    -not $inactiveNodeFlags[$_]
                })
        if ($waitFunctions.Count -ne 1) { return $false }
        $completedPredicateCandidates = if ($commandsByName.ContainsKey('Test-NervAcceptanceWmsCompletedOutboundReadback')) { @($commandsByName['Test-NervAcceptanceWmsCompletedOutboundReadback']) } else { @() }
        $completedPredicates = @($completedPredicateCandidates | Where-Object { & $hasAncestor $_ $waitFunctions[0].Body })
        if ($completedPredicates.Count -ne 1) { return $false }
        $predicate = $completedPredicates[0]
        if ($predicate.CommandElements.Count -ne 3 -or
            $predicate.CommandElements[1] -isnot [Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals([string]$predicate.CommandElements[1].ParameterName, 'Readback', [StringComparison]::OrdinalIgnoreCase) -or
            $null -ne $predicate.CommandElements[1].Argument -or
            -not [string]::Equals([string]$predicate.CommandElements[2].Extent.Text, '$rows[0]', [StringComparison]::Ordinal)) { return $false }
        $requireCompletedGate = $predicate.Parent
        while ($null -ne $requireCompletedGate -and $requireCompletedGate -isnot [Management.Automation.Language.BinaryExpressionAst]) { $requireCompletedGate = $requireCompletedGate.Parent }
        if ($null -eq $requireCompletedGate -or $requireCompletedGate.Operator -ne [Management.Automation.Language.TokenKind]::Or -or
            $requireCompletedGate.Left -isnot [Management.Automation.Language.UnaryExpressionAst] -or
            $requireCompletedGate.Left.TokenKind -ne [Management.Automation.Language.TokenKind]::Not -or
            $requireCompletedGate.Left.Child -isnot [Management.Automation.Language.VariableExpressionAst] -or
            -not [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $requireCompletedGate.Left.Child.VariablePath), 'RequireCompleted', [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $predicateIfAncestors = @()
        $predicateAncestor = $predicate.Parent
        while ($null -ne $predicateAncestor -and $predicateAncestor -ne $waitFunctions[0]) {
            if ($predicateAncestor -is [Management.Automation.Language.IfStatementAst]) { $predicateIfAncestors += $predicateAncestor }
            $predicateAncestor = $predicateAncestor.Parent
        }
        if ($predicateIfAncestors.Count -ne 1) { return $false }
        $guardCondition = $predicateIfAncestors[0].Clauses[0].Item1
        if ($guardCondition -isnot [Management.Automation.Language.PipelineAst] -or
            $guardCondition.PipelineElements.Count -ne 1 -or
            $guardCondition.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $guardCondition.PipelineElements[0].Expression -isnot [Management.Automation.Language.BinaryExpressionAst] -or
            $guardCondition.PipelineElements[0].Expression.Operator -ne [Management.Automation.Language.TokenKind]::And) { return $false }
        $combinedGate = $guardCondition.PipelineElements[0].Expression
        $rowsGate = $combinedGate.Left
        if ($rowsGate -isnot [Management.Automation.Language.BinaryExpressionAst] -or
            $rowsGate.Operator -ne [Management.Automation.Language.TokenKind]::Ieq -or
            $rowsGate.Left -isnot [Management.Automation.Language.MemberExpressionAst] -or
            $rowsGate.Left.Expression -isnot [Management.Automation.Language.VariableExpressionAst] -or
            -not [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $rowsGate.Left.Expression.VariablePath), 'rows', [StringComparison]::OrdinalIgnoreCase) -or
            $rowsGate.Left.Member -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
            -not [string]::Equals([string]$rowsGate.Left.Member.Value, 'Count', [StringComparison]::OrdinalIgnoreCase) -or
            $rowsGate.Right -isnot [Management.Automation.Language.ConstantExpressionAst] -or
            $rowsGate.Right.Value -ne 1 -or
            $combinedGate.Right -isnot [Management.Automation.Language.ParenExpressionAst] -or
            $combinedGate.Right.Pipeline.PipelineElements.Count -ne 1 -or
            $combinedGate.Right.Pipeline.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            -not [object]::ReferenceEquals($combinedGate.Right.Pipeline.PipelineElements[0].Expression, $requireCompletedGate)) { return $false }
        $guardReturns = @($returnStatements | Where-Object {
                $guardReturn = $_
                @($predicateIfAncestors[0].Clauses | Where-Object { & $hasAncestor $guardReturn $_.Item2 }).Count -gt 0 -and
                $null -ne $guardReturn.Pipeline -and
                    [string]::Equals([string]$guardReturn.Pipeline.Extent.Text, '$rows[0]', [StringComparison]::Ordinal)
            })
        if ($predicateIfAncestors[0].Clauses.Count -ne 1 -or $guardReturns.Count -ne 1) { return $false }

        $waitCallCandidates = if ($commandsByName.ContainsKey('Wait-WmsOutboundOrder')) { @($commandsByName['Wait-WmsOutboundOrder']) } else { @() }
        $waitCalls = @($waitCallCandidates | Where-Object {
                    $command = $_
                    $binding = & $getConstantBinding 'completedOutboundOrder'
                    $ancestor = $command.Parent
                    while ($null -ne $ancestor -and $ancestor -isnot [Management.Automation.Language.CommandAst]) { $ancestor = $ancestor.Parent }
                    [object]::ReferenceEquals($ancestor, $binding) -and
                        -not $conditionalNodeFlags[$command] -and
                        -not $inactiveNodeFlags[$command]
                })
        if ($waitCalls.Count -ne 1) { return $false }
        $requireCompletedParameters = @($waitCalls[0].CommandElements | Where-Object {
                $_ -is [Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals([string]$_.ParameterName, 'RequireCompleted', [StringComparison]::OrdinalIgnoreCase) -and
                    $null -eq $_.Argument
            })
        if ($requireCompletedParameters.Count -ne 1) { return $false }

        $businessEvidenceBinding = & $getConstantBinding 'businessEvidence'
        if ($null -eq $businessEvidenceBinding) { return $false }
        $businessEvidenceValue = $businessEvidenceBinding.CommandElements[6]
        if ($businessEvidenceValue.Pipeline.PipelineElements.Count -ne 1 -or
            $businessEvidenceValue.Pipeline.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $businessEvidenceValue.Pipeline.PipelineElements[0].Expression -isnot [Management.Automation.Language.ConvertExpressionAst] -or
            $businessEvidenceValue.Pipeline.PipelineElements[0].Expression.Child -isnot [Management.Automation.Language.HashtableAst]) { return $false }
        $businessEvidenceTable = $businessEvidenceValue.Pipeline.PipelineElements[0].Expression.Child
        $wmsPairs = @($businessEvidenceTable.KeyValuePairs | Where-Object {
                $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
                    [string]::Equals([string]$_.Item1.Value, 'wmsOutboundOrder', [StringComparison]::Ordinal)
            })
        if ($wmsPairs.Count -ne 1 -or
            $wmsPairs[0].Item2 -isnot [Management.Automation.Language.PipelineAst] -or
            $wmsPairs[0].Item2.PipelineElements.Count -ne 1 -or
            $wmsPairs[0].Item2.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $wmsPairs[0].Item2.PipelineElements[0].Expression -isnot [Management.Automation.Language.ConvertExpressionAst] -or
            $wmsPairs[0].Item2.PipelineElements[0].Expression.Child -isnot [Management.Automation.Language.HashtableAst]) { return $false }
        $wmsTable = $wmsPairs[0].Item2.PipelineElements[0].Expression.Child
        $readbackPairs = @($wmsTable.KeyValuePairs | Where-Object {
                $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
                    [string]::Equals([string]$_.Item1.Value, 'completionReadback', [StringComparison]::Ordinal)
            })
        if ($readbackPairs.Count -ne 1 -or
            $readbackPairs[0].Item2 -isnot [Management.Automation.Language.PipelineAst] -or
            $readbackPairs[0].Item2.PipelineElements.Count -ne 1 -or
            $readbackPairs[0].Item2.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst] -or
            $readbackPairs[0].Item2.PipelineElements[0].Expression -isnot [Management.Automation.Language.ConvertExpressionAst] -or
            $readbackPairs[0].Item2.PipelineElements[0].Expression.Child -isnot [Management.Automation.Language.HashtableAst]) { return $false }
        $readbackTable = $readbackPairs[0].Item2.PipelineElements[0].Expression.Child
        foreach ($field in @('status', 'completedAtUtc')) {
            $fieldPairs = @($readbackTable.KeyValuePairs | Where-Object {
                    $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
                        [string]::Equals([string]$_.Item1.Value, $field, [StringComparison]::Ordinal)
                })
            if ($fieldPairs.Count -ne 1 -or
                $fieldPairs[0].Item2 -isnot [Management.Automation.Language.PipelineAst] -or
                $fieldPairs[0].Item2.PipelineElements.Count -ne 1 -or
                $fieldPairs[0].Item2.PipelineElements[0] -isnot [Management.Automation.Language.CommandExpressionAst]) { return $false }
            $memberRead = $fieldPairs[0].Item2.PipelineElements[0].Expression
            if ($memberRead -isnot [Management.Automation.Language.MemberExpressionAst] -or
                $memberRead.Expression -isnot [Management.Automation.Language.VariableExpressionAst] -or
                -not [string]::Equals((Get-NervScriptVariableBindingName -VariablePath $memberRead.Expression.VariablePath), 'completedOutboundOrder', [StringComparison]::OrdinalIgnoreCase) -or
                $memberRead.Member -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
                -not [string]::Equals([string]$memberRead.Member.Value, $field, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        }
        return $true
    }

    return [pscustomobject][ordered]@{
        failureCaptureSupported = $exportFunctions.Count -eq 1 -and $orderedCaptureCalls.Count -eq 1
        pickingReadbackWired = -not $hasActivePsVariableSet -and (& $testConstantBinding 'Test-NervAcceptanceWmsPickingReadbacks' 'pickingLifecycleCompleted')
        completionReplayWired = -not $hasActivePsVariableSet -and (& $testConstantBinding 'Test-NervAcceptanceWmsCompletionReplay' 'completionHttpReplayConverged')
        outboundCompletionWired = & $testOutboundCompletion
    }
}

function New-NervAcceptanceWmsDeliveryCanonicalResult {
    param(
        [Parameter(Mandatory)] [object] $Provenance,
        [Parameter(Mandatory)] [string] $Track,
        [Parameter(Mandatory)] [object] $BusinessEvidence,
        [Parameter(Mandatory)] [object] $TestCounters,
        [Parameter(Mandatory)] [object] $CleanupEvidence,
        [Parameter(Mandatory)] [object] $DiagnosticEvidence,
        [Parameter(Mandatory)] [object] $Volatile
    )

    Assert-NervAcceptanceObjectSchema -Object $Provenance -AllowedFields @('repository', 'runId', 'runAttempt', 'testedSha', 'manifestDigest', 'scenarioId') -RequiredFields @('repository', 'runId', 'runAttempt', 'testedSha', 'manifestDigest', 'scenarioId') -Context 'MAN-527 canonical provenance'
    if (-not (Test-NervAcceptanceRepositoryIdentifier -Repository ([string]$Provenance.repository))) { throw 'MAN-527 canonical repository must be a canonical owner/name identifier.' }
    if ([string]$Provenance.runId -cnotmatch '^[1-9][0-9]*$') { throw 'MAN-527 canonical runId must be a positive decimal identifier.' }
    if (-not (Test-NervAcceptanceInteger -Value $Provenance.runAttempt) -or [int64]$Provenance.runAttempt -le 0) { throw 'MAN-527 canonical runAttempt must be positive.' }
    if ([string]$Provenance.testedSha -cnotmatch '^[0-9a-f]{40}$') { throw 'MAN-527 canonical testedSha must be a lowercase 40-character Git SHA.' }
    if ([string]$Provenance.manifestDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'MAN-527 canonical manifestDigest must be a lowercase SHA-256 digest.' }
    if (-not [string]::Equals([string]$Provenance.scenarioId, 'wms-delivery-erp', [StringComparison]::Ordinal)) { throw "MAN-527 canonical scenarioId must be 'wms-delivery-erp'." }
    if ($Track -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') { throw 'MAN-527 canonical track identifier must be canonical.' }

    Assert-NervAcceptanceObjectSchema -Object $BusinessEvidence -AllowedFields @('verifiedAtUtc', 'scenarioStatus', 'deliveryOrderNo', 'transport', 'persistence', 'wmsOutboundOrder', 'erpDelivery', 'accountReceivable', 'repeatedEvent', 'repeatedEventConverged', 'cleanup', 'diagnostics') -RequiredFields @('scenarioStatus', 'deliveryOrderNo', 'wmsOutboundOrder', 'erpDelivery', 'accountReceivable', 'repeatedEvent', 'repeatedEventConverged') -Context 'MAN-527 business evidence'
    if (-not [string]::Equals([string]$BusinessEvidence.scenarioStatus, 'passed', [StringComparison]::Ordinal)) { throw 'MAN-527 canonical success requires passed business evidence.' }
    Assert-NervAcceptanceObjectSchema -Object $TestCounters -AllowedFields @('total', 'executed', 'passed', 'failed', 'skipped') -RequiredFields @('total', 'executed', 'passed', 'failed', 'skipped') -Context 'MAN-527 canonical TRX counters'
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'skipped')) { [void](Assert-NervAcceptanceCanonicalIntegerField -Object $TestCounters -Name $name -Context 'MAN-527 canonical TRX counters') }
    if ([int64]$TestCounters.total -ne 1 -or [int64]$TestCounters.executed -ne 1 -or [int64]$TestCounters.passed -ne 1 -or [int64]$TestCounters.failed -ne 0 -or [int64]$TestCounters.skipped -ne 0) {
        throw 'MAN-527 canonical success requires exact TRX counts expected=1 discovered=1 passed=1 failed=0 skipped=0.'
    }
    Assert-NervAcceptanceObjectSchema -Object $CleanupEvidence -AllowedFields @('managedProcessIds', 'managedProcessRemaining', 'databaseName', 'exactDatabaseRemaining', 'postgres', 'redis', 'errors') -RequiredFields @('managedProcessRemaining', 'exactDatabaseRemaining', 'postgres', 'redis', 'errors') -Context 'MAN-527 canonical cleanup evidence'
    foreach ($name in @('managedProcessRemaining', 'exactDatabaseRemaining')) { [void](Assert-NervAcceptanceCanonicalIntegerField -Object $CleanupEvidence -Name $name -Context 'MAN-527 canonical cleanup evidence') }
    if ($CleanupEvidence.errors -isnot [array]) { throw 'MAN-527 canonical cleanup errors must be an array.' }
    $cleanupErrors = @($CleanupEvidence.errors)
    $pendingOwnedResources = 0
    foreach ($provider in @('postgres', 'redis')) {
        if ([string]::Equals([string]$CleanupEvidence.PSObject.Properties[$provider].Value, 'owned-pending-cleanup', [StringComparison]::Ordinal)) {
            $pendingOwnedResources++
        }
    }
    $ownedResourcesRemaining = [int64]$CleanupEvidence.managedProcessRemaining + [int64]$CleanupEvidence.exactDatabaseRemaining + $pendingOwnedResources
    $cleanupErrorCodes = @($cleanupErrors | ForEach-Object {
        $separatorIndex = ([string]$_).IndexOf(':', [StringComparison]::Ordinal)
        if ($separatorIndex -gt 0) { ([string]$_).Substring(0, $separatorIndex) } else { 'cleanup-error' }
    })
    if ($ownedResourcesRemaining -ne 0 -or $cleanupErrors.Count -ne 0) {
        throw 'MAN-527 canonical success requires zero cleanup remaining counts and no pending owned resources.'
    }

    $outboundAssigned = -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.wmsOutboundOrder.firstAssignment.poolCode) -and -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.wmsOutboundOrder.firstAssignment.operatorPrincipalId)
    if ($BusinessEvidence.wmsOutboundOrder.pickingLifecycleCompleted -isnot [bool] -or $BusinessEvidence.wmsOutboundOrder.completionHttpReplayConverged -isnot [bool] -or $BusinessEvidence.repeatedEventConverged -isnot [bool]) {
        throw 'MAN-527 canonical business checkpoint flags must be JSON booleans.'
    }
    $pickingLifecycleCompleted = [bool]$BusinessEvidence.wmsOutboundOrder.pickingLifecycleCompleted
    $outboundCompleted = Test-NervAcceptanceWmsCompletedOutboundReadback -Readback $BusinessEvidence.wmsOutboundOrder.completionReadback
    $deliveryCompleted = [string]::Equals([string]$BusinessEvidence.erpDelivery.status, 'completed', [StringComparison]::OrdinalIgnoreCase) -and [decimal]$BusinessEvidence.erpDelivery.shippedQuantity -eq 2 -and -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.erpDelivery.shippedAtUtc) -and -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.erpDelivery.completedAtUtc)
    $receivableCreated = -not [string]::IsNullOrWhiteSpace([string]$BusinessEvidence.accountReceivable.receivableNo) -and [string]::Equals([string]$BusinessEvidence.accountReceivable.sourceDocumentNo, [string]$BusinessEvidence.deliveryOrderNo, [StringComparison]::Ordinal)
    $completionReplayConverged = [bool]$BusinessEvidence.wmsOutboundOrder.completionHttpReplayConverged
    $repeatedEventConverged = [bool]$BusinessEvidence.repeatedEventConverged
    if (-not $outboundAssigned -or -not $pickingLifecycleCompleted -or -not $outboundCompleted -or -not $deliveryCompleted -or -not $receivableCreated -or -not $completionReplayConverged -or -not $repeatedEventConverged) { throw 'MAN-527 canonical success requires every business checkpoint to have converged.' }

    Assert-NervAcceptanceObjectSchema -Object $DiagnosticEvidence -AllowedFields @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted', 'artifactPaths', 'errors') -RequiredFields @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted', 'artifactPaths', 'errors') -Context 'MAN-527 canonical diagnostic evidence'
    foreach ($name in @('failureCaptureSupported', 'failureDiagnosticsCaptured', 'secretsRedacted')) {
        if ($DiagnosticEvidence.PSObject.Properties[$name].Value -isnot [bool]) { throw "MAN-527 canonical diagnostic evidence $name must be a JSON boolean." }
    }
    if (-not [bool]$DiagnosticEvidence.failureCaptureSupported) { throw 'MAN-527 canonical success requires diagnostic failure capture support.' }
    if ([bool]$DiagnosticEvidence.failureDiagnosticsCaptured) { throw 'MAN-527 canonical success must not claim failure diagnostics were captured.' }
    if (-not [bool]$DiagnosticEvidence.secretsRedacted) { throw 'MAN-527 canonical diagnostic secrets must be redacted.' }
    if ($DiagnosticEvidence.artifactPaths -isnot [array] -or @($DiagnosticEvidence.artifactPaths).Count -ne 0) { throw 'MAN-527 canonical success must not retain failure diagnostic artifacts.' }
    if ($DiagnosticEvidence.errors -isnot [array] -or @($DiagnosticEvidence.errors).Count -ne 0) { throw 'MAN-527 canonical diagnostic capture errors must be empty.' }

    Assert-NervAcceptanceObjectSchema -Object $Volatile -AllowedFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'ports', 'paths') -RequiredFields @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'ports', 'paths') -Context 'MAN-527 canonical volatile evidence'
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = $Provenance
        track = $Track
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{ identity = 'Nerv.IIP.Business.FullChain.Tests.ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts'; expected = 1; discovered = [int]$TestCounters.total; passed = [int]$TestCounters.passed; failed = [int]$TestCounters.failed; skipped = [int]$TestCounters.skipped }
        businessFacts = [pscustomobject][ordered]@{ outboundAssigned = $outboundAssigned; pickingLifecycleCompleted = $pickingLifecycleCompleted; outboundCompleted = $outboundCompleted; deliveryCompleted = $deliveryCompleted; receivableCreated = $receivableCreated; completionReplayConverged = $completionReplayConverged; repeatedEventConverged = $repeatedEventConverged }
        diagnostics = [pscustomobject][ordered]@{ schemas = @('erp', 'inventory', 'wms'); failureCaptureSupported = [bool]$DiagnosticEvidence.failureCaptureSupported; failureDiagnosticsCaptured = [bool]$DiagnosticEvidence.failureDiagnosticsCaptured; secretsRedacted = [bool]$DiagnosticEvidence.secretsRedacted }
        cleanup = [pscustomobject][ordered]@{ managedProcessesRemaining = [int]$CleanupEvidence.managedProcessRemaining; disposableDatabasesRemaining = [int]$CleanupEvidence.exactDatabaseRemaining; ownedResourcesRemaining = [int]$ownedResourcesRemaining; errorCodes = @($cleanupErrorCodes) }
        volatile = [pscustomobject][ordered]@{ databaseName = [string]$Volatile.databaseName; processIds = @($Volatile.processIds); capSuffix = [string]$Volatile.capSuffix; startedAtUtc = [string]$Volatile.startedAtUtc; completedAtUtc = [string]$Volatile.completedAtUtc; cleanupErrors = @($cleanupErrors); ports = $Volatile.ports; paths = $Volatile.paths }
    }
}
