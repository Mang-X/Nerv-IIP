# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the governed local lane/verify entrypoints with the PowerShell parser
#   Writes:
#     - Nothing
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
#3295：把「跑真库 `dotnet test` 的本地 lane / verify 入口」那一族的 1800 秒预算钉成**参数**。

判据用 AST 而不是行级文本扫描：本仓已实证行级源码扫描护栏对格式敏感（换行书写即隐身是假绿、
按代码文本锚豁免则多一个空格就红），而 `-TimeoutSeconds 1800` 这种绑定恰好是最容易换行书写的
形状。这里直接问 PowerShell 自己的解析器，写法再怎么折行都判得一样。

**覆盖边界（不自称完备）**：本文件只管下面点名的四个入口。它**不**会发现将来新增的第五个同族
入口——那种「护栏自称完备」的说法比有洞更坏，所以这里明写出来。仓库里另有约 20 个
`verify-business-*.ps1` 也跑 `dotnet test`，但它们各自用 180/240/300/600/900 的**单项目**预算，
不属于本族，也不在本文件的判定面内。
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

# 该族的成员身份：本机可跑、对真 PostgreSQL/Redis 跑 `dotnet test`、此前把 1800 写死成字面量。
# CI 传不传这个参数无所谓——默认值就是原来的字面量，所以 CI 行为按构造不变。
$governedEntrypoints = @(
    'scripts/run-postgres-test-lane.ps1'
    'scripts/run-redis-cap-test-lane.ps1'
    'scripts/verify-backend-real-postgres-tests.ps1'
    'scripts/verify-world-history.ps1'
)

foreach ($relativePath in $governedEntrypoints) {
    $path = Join-Path $repoRoot $relativePath
    Assert-Contract (Test-Path -LiteralPath $path) "Governed local lane entrypoint '$relativePath' is missing."

    $parseErrors = $null
    $tokens = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    Assert-Contract (@($parseErrors).Count -eq 0) "'$relativePath' must parse cleanly; got $(@($parseErrors).Count) parse error(s)."

    # --- 1. 预算必须是参数，默认值必须还是 1800（改法不许改掉现有默认行为）。
    $parameters = @($ast.ParamBlock.Parameters | Where-Object {
        [string]::Equals($_.Name.VariablePath.UserPath, 'TimeoutSeconds', [StringComparison]::Ordinal)
    })
    Assert-Contract ($parameters.Count -eq 1) "'$relativePath' must declare exactly one -TimeoutSeconds parameter so a local run on a CPU-shared machine can raise the budget (#2870 / #3295); found $($parameters.Count)."
    $parameter = $parameters[0]
    Assert-Contract ($parameter.StaticType -eq [int]) "'$relativePath' must type -TimeoutSeconds as [int]; found '$($parameter.StaticType)'."
    $default = $parameter.DefaultValue
    Assert-Contract ($default -is [System.Management.Automation.Language.ConstantExpressionAst] -and [int]$default.Value -eq 1800) "'$relativePath' must keep 1800 as the -TimeoutSeconds default so CI behaviour is unchanged; found '$($default)'."

    # --- 2. 待办 3：秒轴上下界由 Invoke-NativeCommandOutput 拥有，调用方再抄一遍就是第二个魔数。
    $validateRangeAttributes = @($parameter.Attributes | Where-Object {
        $_ -is [System.Management.Automation.Language.AttributeAst] -and
        $_.TypeName.GetReflectionAttributeType() -eq [System.Management.Automation.ValidateRangeAttribute]
    })
    Assert-Contract ($validateRangeAttributes.Count -eq 0) "'$relativePath' must not repeat a ValidateRange on -TimeoutSeconds; the seconds-axis bounds are owned by Invoke-NativeCommandOutput (#3271), and a second copy is exactly the magic number #3295 removes."

    # --- 3. 承重：参数必须真的走到 dotnet 调用点。少了这一条，上面两条对一个从未被读到的
    #        参数也照样全绿——那正是「接线 ≠ 通路」。
    $dotnetInvocations = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        $null -ne $node.GetCommandName() -and
        $node.GetCommandName().StartsWith('Invoke-DotNet', [StringComparison]::OrdinalIgnoreCase)
    }, $true))
    Assert-Contract ($dotnetInvocations.Count -ge 1) "'$relativePath' must still invoke dotnet through ScriptAutomation; found no Invoke-DotNet* call, so the budget contract above would hold vacuously."

    foreach ($invocation in $dotnetInvocations) {
        $elements = @($invocation.CommandElements)
        $boundIndex = -1
        for ($index = 0; $index -lt $elements.Count; $index++) {
            $element = $elements[$index]
            if ($element -is [System.Management.Automation.Language.CommandParameterAst] -and
                [string]::Equals($element.ParameterName, 'TimeoutSeconds', [StringComparison]::OrdinalIgnoreCase)) {
                $boundIndex = $index
                break
            }
        }
        $location = "$relativePath`:$($invocation.Extent.StartLineNumber)"
        Assert-Contract ($boundIndex -ge 0) "The dotnet invocation at $location must bind -TimeoutSeconds explicitly; Invoke-DotNetOutput would otherwise fall back to its own 60s default."
        $argument = if ($null -ne $elements[$boundIndex].Argument) { $elements[$boundIndex].Argument } elseif ($boundIndex + 1 -lt $elements.Count) { $elements[$boundIndex + 1] } else { $null }
        Assert-Contract ($argument -is [System.Management.Automation.Language.VariableExpressionAst]) "The dotnet invocation at $location must pass the caller-supplied budget, not a constant; found '$($argument)'. A constant here is the #3295 defect: the caller has no way to raise it."
        Assert-Contract ([string]::Equals($argument.VariablePath.UserPath, 'TimeoutSeconds', [StringComparison]::Ordinal)) "The dotnet invocation at $location must pass `$TimeoutSeconds, not '`$$($argument.VariablePath.UserPath)'; a differently named variable would shadow the declared parameter and silently pin the budget again."
    }
}

Write-Host "Local lane timeout budget contract verified across $($governedEntrypoints.Count) entrypoints."
