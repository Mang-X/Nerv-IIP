# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs scripts/verify-apphost-base-url-injection.ps1 against the repository and against
#       throwaway exemption registries
#     - Re-evaluates the checker in-process against mutated copies of the AppHost source text
#   Writes:
#     - OS temporary directory: exemption registry fixtures (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes every fixture directory in finally
#   Requires:
#     - PowerShell 7

<#
本文件是 #3511 那条门禁的鉴别力证据，不是它的形状快照。

## 为什么需要它

`scripts/verify-aspire-apphost-environment-artifacts.ps1` 在同一格上实测是**绿**的：PR #3484 把
当时新增的 `Program.cs:885`（gateway 的 `FileStorage__BaseUrl`）删回缺陷态，那个脚本仍然 EXIT=0，
而同一轮的哨兵格（把另一条注入的值改成 `http://wrong-sentinel:1`）EXIT=1 —— 说明变异确实生效、
跑法正确，那一格的绿是**护栏没鉴别力**。所以「新护栏在 main 上是绿的」什么都不证明，下面这些
变异格才证明它会红。

## 矩阵

  * CONTROL —— 未变异的源码文本，走与变异格**完全相同**的读文本→跑报告通路，必须绿。
    它验的是跑法；哨兵验的是变异真的落了地。
  * 删除扫描 —— 对**每一条**当前被满足的需求，删掉满足它的那行注入，必须报出恰好那一对的
    `missing`。逐条而不是抽样：抽样等于重新引入一份名单。
  * 值扫描 —— 对**每一条**同样的需求，把注入值换成 `"http://wrong-sentinel:1"`，必须报出恰好
    那一对的 `wrong-value`。这一格回答「我打的是『在不在』还是『是什么』」——只有它红，
    才说明门禁挡得住 #3313 那种「注了，但指向别的东西」。
  * 每个变异格都先断言「变异后的文本确实与原文不同」并逐字校验被改的那一行，
    避免锚点没命中却把绿读成「护栏通过」。
  * 登记表纪律 —— 豁免表自己的失效形态（不匹配任何违例、无票、字段缺、重复、种类错、
    拿 `missing` 的登记去盖 `wrong-value`）各一格。登记表是本票的交付物，它烂掉必须有人发现。
  * 扫描面失明 —— 注释里、字符串里的调用不算数；逐字字符串、原始字符串、插值洞里的引号、
    字符字面量 `'"'` 之后的**真**调用仍然算数。一个不处理这些形态的扫描器会在这里整段丢失需求，
    而丢失的方向是静默变绿。
  * fail-closed —— 读不懂的写法（非字面量键、`using static`、归属不到资源的注入、重复注入、
    AddProject 没绑到 var）必须 throw，不是跳过。
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/AppHostBaseUrlInjection.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-apphost-base-url-injection.ps1'
$appHostProgramPath = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("nerv-iip-apphost-base-url-{0}" -f [Guid]::NewGuid().ToString('N'))

$failures = [Collections.Generic.List[string]]::new()
$checked = 0

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    $script:checked++
    if (-not $Condition) { $script:failures.Add($Message) }
}

function Invoke-Verifier {
    param(
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $Name
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $verifierPath) + $Arguments) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 300 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

function Get-ThrownMessage {
    param([Parameter(Mandatory)] [scriptblock] $Action)

    try {
        & $Action | Out-Null
        return $null
    }
    catch {
        return [string] $_.Exception.Message
    }
}

function New-FixtureSource {
    <#
        合成一份 C# 夹具：前缀写在行首（C# 的预处理指令必须是行首第一个非空白 token，
        把它塞进一行式夹具里得到的是无效 C#，那样测的就不是扫描器而是夹具了），
        被扫描的调用单独占一行。
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Prefix,
        [Parameter(Mandatory)] [string] $Body
    )

    return @('class C', '{', '    void M()', '    {', $Prefix, "        $Body", '    }', '}') -join "`n"
}

function New-ExemptionRegistry {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Entry
    )

    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $path = Join-Path $fixtureRoot "$Name.json"
    (@{ exemptions = $Entry } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $path -Encoding utf8
    return $path
}

try {
    # ── A. 真实仓库 ────────────────────────────────────────────────────────────────────────────
    $realRun = Invoke-Verifier -Name 'apphost-base-url-real-repository'
    Assert-Contract $realRun.Passed "The checker must pass on the repository as it stands; it said: $($realRun.Message)"
    Assert-Contract ($realRun.Message -match 'base address requirements enumerated from consumer code: (?<count>[1-9][0-9]*)') `
        'The checker must report how many requirements it enumerated; a collapsed scan surface would otherwise read as a clean pass.'

    $cache = @{}
    $originalText = [IO.File]::ReadAllText($appHostProgramPath)
    $baseline = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache

    Assert-Contract ($baseline.UnexemptedViolations.Count -eq 0) `
        "The repository must have no unexempted base-url violation; found: $(@($baseline.UnexemptedViolations | ForEach-Object { "$($_.ConsumerResource)/$($_.Key)" }) -join ', ')"
    Assert-Contract ($baseline.StaleExemptions.Count -eq 0) `
        "Every registered exemption must still match a live violation; stale: $(@($baseline.StaleExemptions | ForEach-Object { "$($_.ConsumerResource)/$($_.Key)" }) -join ', ')"
    Assert-Contract ($baseline.Requirements.Count -gt 0) 'The requirement set must not be empty.'
    Assert-Contract ($baseline.Injections.Count -gt 0) 'The injection set must not be empty.'

    # CONTROL：与下面每个变异格走同一条通路，只是不变异。它验跑法，不验鉴别力。
    $control = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache
    Assert-Contract ($control.UnexemptedViolations.Count -eq 0) 'CONTROL cell: the unmutated text must go through the mutation harness clean.'

    # ── B/C. 逐条删除与逐条改值 ────────────────────────────────────────────────────────────────
    $violatingPairs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($violation in $baseline.Violations) {
        [void] $violatingPairs.Add((Get-NervStringCompositeKey -Components @($violation.ConsumerResource, $violation.Key)))
    }

    $injectionByPair = @{}
    foreach ($injection in $baseline.Injections) { $injectionByPair["$($injection.Resource)/$($injection.EnvironmentName)"] = $injection }

    # 用 String.Split 而不是 -split：`-split "x", -1` 的负数是「取末尾 N 段」，不是「不限段数」，
    # 它会静默返回整份文本当作一行。
    $originalLines = $originalText.Split("`n")
    $deleteCells = 0
    $valueCells = 0
    foreach ($requirement in $baseline.Requirements) {
        $requirementPair = Get-NervStringCompositeKey -Components @($requirement.ConsumerResource, $requirement.Key)
        if ($violatingPairs.Contains($requirementPair)) { continue }
        $injection = $injectionByPair["$($requirement.ConsumerResource)/$($requirement.EnvironmentName)"]
        $lineIndex = $injection.Line - 1
        $lineText = $originalLines[$lineIndex]

        # 锚点校验：变异必须落在自以为落的那一行。本仓已实测过「文本替换的锚点多处命中 ⇒ 变异静默
        # 落到别的方法上、读数成了假绿」，所以这里先逐字确认这一行确实就是那条注入。
        Assert-Contract ($lineText.Contains($requirement.EnvironmentName)) `
            "Mutation anchor check: AppHost line $($injection.Line) should carry '$($requirement.EnvironmentName)' but reads '$lineText'."

        $deletedLines = [Collections.Generic.List[string]]::new($originalLines)
        $deletedLines.RemoveAt($lineIndex)
        $deletedText = $deletedLines -join "`n"
        Assert-Contract (-not [string]::Equals($deletedText, $originalText, [StringComparison]::Ordinal)) `
            "Mutation sentinel: deleting AppHost line $($injection.Line) must change the text."
        $deletedReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $deletedText -Cache $cache
        $missing = @($deletedReport.UnexemptedViolations | Where-Object {
            [string]::Equals($_.Kind, 'missing', [StringComparison]::Ordinal) -and
            [string]::Equals($_.ConsumerResource, $requirement.ConsumerResource, [StringComparison]::Ordinal) -and
            [string]::Equals($_.Key, $requirement.Key, [StringComparison]::Ordinal)
        })
        Assert-Contract ($missing.Count -eq 1) `
            "Deleting AppHost line $($injection.Line) must make '$($requirement.ConsumerResource)' <- '$($requirement.Key)' a missing violation; the checker reported $($deletedReport.UnexemptedViolations.Count) unexempted violation(s)."
        $deleteCells++

        $wrongLines = [Collections.Generic.List[string]]::new($originalLines)
        $wrongLines[$lineIndex] = "    .WithEnvironment(`"$($requirement.EnvironmentName)`", `"http://wrong-sentinel:1`")"
        $wrongText = $wrongLines -join "`n"
        Assert-Contract (-not [string]::Equals($wrongText, $originalText, [StringComparison]::Ordinal)) `
            "Mutation sentinel: rewriting AppHost line $($injection.Line) must change the text."
        $wrongReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $wrongText -Cache $cache
        $wrongValue = @($wrongReport.UnexemptedViolations | Where-Object {
            [string]::Equals($_.Kind, 'wrong-value', [StringComparison]::Ordinal) -and
            [string]::Equals($_.ConsumerResource, $requirement.ConsumerResource, [StringComparison]::Ordinal) -and
            [string]::Equals($_.Key, $requirement.Key, [StringComparison]::Ordinal)
        })
        Assert-Contract ($wrongValue.Count -eq 1) `
            "Pointing AppHost line $($injection.Line) at a foreign address must make '$($requirement.ConsumerResource)' <- '$($requirement.Key)' a wrong-value violation; the checker reported $($wrongReport.UnexemptedViolations.Count) unexempted violation(s)."
        $valueCells++
    }

    Assert-Contract ($deleteCells -eq $baseline.Requirements.Count - $baseline.Violations.Count) `
        "The deletion sweep must cover every satisfied requirement; covered $deleteCells of $($baseline.Requirements.Count - $baseline.Violations.Count)."
    Assert-Contract ($valueCells -eq $deleteCells) 'The value sweep must cover exactly the cells the deletion sweep covered.'

    # 「注的是别的资源的端点」与「注的是个字面量」是两种不同的错，两种都必须红。
    $firstSatisfied = @($baseline.Requirements | Where-Object {
        $candidatePair = Get-NervStringCompositeKey -Components @($_.ConsumerResource, $_.Key)
        -not $violatingPairs.Contains($candidatePair)
    })[0]
    $foreignProvider = @($baseline.Resources | Where-Object { -not [string]::Equals($_.Resource, $firstSatisfied.ProviderResource, [StringComparison]::Ordinal) -and -not [string]::Equals($_.Resource, $firstSatisfied.ConsumerResource, [StringComparison]::Ordinal) })[0]
    $foreignInjection = $injectionByPair["$($firstSatisfied.ConsumerResource)/$($firstSatisfied.EnvironmentName)"]
    $foreignLines = [Collections.Generic.List[string]]::new($originalLines)
    $foreignLines[$foreignInjection.Line - 1] = "    .WithEnvironment(`"$($firstSatisfied.EnvironmentName)`", $($foreignProvider.Variable).GetEndpoint(`"http`"))"
    $foreignReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText ($foreignLines -join "`n") -Cache $cache
    Assert-Contract (@($foreignReport.UnexemptedViolations | Where-Object { [string]::Equals($_.Kind, 'wrong-value', [StringComparison]::Ordinal) }).Count -eq 1) `
        "Injecting '$($firstSatisfied.EnvironmentName)' as another resource's endpoint ('$($foreignProvider.Resource)') must be a wrong-value violation."

    # ── E. 豁免登记表纪律 ──────────────────────────────────────────────────────────────────────
    $liveViolation = if ($baseline.Violations.Count -gt 0) { $baseline.Violations[0] } else { $null }
    if ($null -eq $liveViolation) {
        # 登记表清空后（#3512 销账）这一格失去输入。它只能跟着一起退役，不能悄悄不跑。
        Assert-Contract $false 'The exemption-discipline cells need a live violation to register; once the registry is empty they must be retired together with it, not silently skipped.'
    }
    else {
        $liveEntry = @{
            consumerResource = $liveViolation.ConsumerResource
            key = $liveViolation.Key
            kind = $liveViolation.Kind
            tracking = '#3512'
            reason = 'fixture'
        }

        $exemptedReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache `
            -ExemptionPath (New-ExemptionRegistry -Name 'valid' -Entry @($liveEntry))
        Assert-Contract ($exemptedReport.UnexemptedViolations.Count -eq 0) 'A well-formed registration must exempt exactly the violation it names.'

        $emptyReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache `
            -ExemptionPath (New-ExemptionRegistry -Name 'empty' -Entry @())
        Assert-Contract ($emptyReport.UnexemptedViolations.Count -eq $baseline.Violations.Count) `
            'Removing the registry entries must surface every live violation; the registry is what keeps main green, not the checker being blind.'

        $staleEntry = $liveEntry.Clone()
        $staleEntry['key'] = 'NoSuchService:BaseUrl'
        $staleReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache `
            -ExemptionPath (New-ExemptionRegistry -Name 'stale' -Entry @($staleEntry))
        Assert-Contract ($staleReport.StaleExemptions.Count -eq 1) `
            'A registration that matches no live violation must be reported as stale; that is the only thing that keeps the registry shrinking as #3512 lands.'
        $staleRun = Invoke-Verifier -Name 'apphost-base-url-stale-exemption' -Arguments @('-ExemptionPath', (Join-Path $fixtureRoot 'stale.json'))
        Assert-Contract (-not $staleRun.Passed) 'A stale registration must make the checker exit non-zero, not merely print a note.'

        $crossKindEntry = $liveEntry.Clone()
        $crossKindEntry['kind'] = if ([string]::Equals($liveViolation.Kind, 'missing', [StringComparison]::Ordinal)) { 'wrong-value' } else { 'missing' }
        $crossKindReport = Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache `
            -ExemptionPath (New-ExemptionRegistry -Name 'cross-kind' -Entry @($crossKindEntry))
        Assert-Contract ($crossKindReport.UnexemptedViolations.Count -eq 1) `
            'An exemption registered for one violation kind must not cover the other kind on the same pair.'

        $malformed = @(
            @{ Name = 'no-tracking'; Entry = @{ consumerResource = $liveViolation.ConsumerResource; key = $liveViolation.Key; kind = $liveViolation.Kind; tracking = '3512'; reason = 'fixture' }; Expect = 'not of the form' }
            @{ Name = 'blank-reason'; Entry = @{ consumerResource = $liveViolation.ConsumerResource; key = $liveViolation.Key; kind = $liveViolation.Kind; tracking = '#3512'; reason = '  ' }; Expect = "missing or empty 'reason'" }
            @{ Name = 'unknown-kind'; Entry = @{ consumerResource = $liveViolation.ConsumerResource; key = $liveViolation.Key; kind = 'skip'; tracking = '#3512'; reason = 'fixture' }; Expect = 'unknown kind' }
        )
        foreach ($case in $malformed) {
            $path = New-ExemptionRegistry -Name $case.Name -Entry @($case.Entry)
            $message = Get-ThrownMessage -Action { Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache -ExemptionPath $path }
            Assert-Contract ($null -ne $message -and $message.Contains($case.Expect)) `
                "A '$($case.Name)' registration must be rejected with a message naming the defect; got: $message"
        }

        $duplicatePath = New-ExemptionRegistry -Name 'duplicate' -Entry @($liveEntry, $liveEntry.Clone())
        $duplicateMessage = Get-ThrownMessage -Action { Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $originalText -Cache $cache -ExemptionPath $duplicatePath }
        Assert-Contract ($null -ne $duplicateMessage -and $duplicateMessage.Contains('more than once', [StringComparison]::Ordinal)) `
            "A pair registered twice must be rejected; got: $duplicateMessage"
    }

    # ── F. 消费方扫描面 ────────────────────────────────────────────────────────────────────────
    $callTemplate = 'InternalServiceBaseAddress.ResolveAllowingTestHost(builder.Configuration, builder.Environment, "Erp:BaseUrl", "http://localhost:5118");'
    $blindCases = @(
        @{ Name = 'line comment'; Source = "class C { void M() { // $callTemplate`n } }" }
        @{ Name = 'block comment'; Source = "class C { void M() { /* $callTemplate */ } }" }
        @{ Name = 'string literal'; Source = "class C { void M() { var s = `"$($callTemplate.Replace('"', '\"'))`"; } }" }
    )
    foreach ($case in $blindCases) {
        $found = @(Get-NervBaseUrlResolveCallSites -SourceText $case.Source -SourcePath "fixture/$($case.Name).cs")
        Assert-Contract ($found.Count -eq 0) "A Resolve call that only appears inside a $($case.Name) must not become a requirement; found $($found.Count)."
    }

    # 每一种前置写法都会让「按引号配对」的朴素扫描器从这里开始整段错位，之后真正的调用就读不到了。
    # 失效方向是静默变绿（需求凭空消失），所以逐种钉住。
    # 实现面：每一种前置写法都会让「按引号配对」的朴素扫描器从这里开始整段错位，之后真正的调用
    # 就读不到了。失效方向是静默变绿（需求凭空消失），所以逐种钉住。
    $prefixCases = @(
        @{ Name = 'interpolated hole containing a quote'; Prefix = 'var i = $"{ d["key"] } tail";' }
        @{ Name = 'char literal holding a comma'; Prefix = "var c = ',';" }
        @{ Name = 'comment marker inside a url literal'; Prefix = 'var u = "http://localhost:5118";' }
        # #3124 点名的那一类：预处理指令行里的撇号。指令行整行按非代码置空，所以它到不了
        # 字符字面量分支。前一版实现里它会开一个 char literal 一路吞到文件尾，需求集静默归零。
        @{ Name = "preprocessor directive containing an apostrophe"; Prefix = "#region Don't touch`n#endregion" }
        @{ Name = 'pragma with an apostrophe in a trailing comment'; Prefix = "#pragma warning disable S1118 // don't ask" }
    )
    foreach ($case in $prefixCases) {
        $source = New-FixtureSource -Prefix $case.Prefix -Body "var a = $callTemplate"
        $found = @(Get-NervBaseUrlResolveCallSites -SourceText $source -SourcePath "fixture/prefix.cs")
        Assert-Contract ($found.Count -eq 1 -and [string]::Equals($found[0].Key, 'Erp:BaseUrl', [StringComparison]::Ordinal)) `
            "A real Resolve call following a $($case.Name) must still be enumerated; found $($found.Count)."
    }

    # 拒绝面。这三种形态本扫描器不实现，遇到必须 throw —— 不是「少认一种写法」而是「不猜」。
    # 前一版实现了 raw 与 verbatim，并在这两处各复现了一个 #3124 已裁决的静默归零缺陷：
    # `@""""` 被先数引号读成 raw，吞掉文件其余部分，需求集归零而门禁照绿。
    $refusedCases = @(
        @{ Name = 'verbatim string containing one escaped quote'; Prefix = 'var q = @"""";'; Expect = 'does not implement verbatim strings' }
        @{ Name = 'raw string literal'; Prefix = 'var r = """{"op":"move"}""";'; Expect = 'does not implement raw strings' }
        @{ Name = 'conditional compilation directive'; Prefix = "#if DEBUG`n#endif"; Expect = 'does not model which branch is live' }
    )
    foreach ($case in $refusedCases) {
        $source = New-FixtureSource -Prefix $case.Prefix -Body "var a = $callTemplate"
        $message = Get-ThrownMessage -Action { Get-NervBaseUrlResolveCallSites -SourceText $source -SourcePath 'fixture/refused.cs' }
        Assert-Contract ($null -ne $message -and $message.Contains($case.Expect, [StringComparison]::Ordinal)) `
            "A $($case.Name) must be refused out loud rather than silently shifting the scan out of phase; got: $message"
        # 同一份夹具去掉那条真实调用后**仍然** throw：证明这一格红的原因是拒绝面本身，
        # 不是「恰好没找到调用」。
        $emptyMessage = Get-ThrownMessage -Action {
            Get-NervBaseUrlResolveCallSites -SourceText (New-FixtureSource -Prefix $case.Prefix -Body 'var x = InternalServiceBaseAddress;') -SourcePath 'fixture/refused-empty.cs'
        }
        Assert-Contract ($null -ne $emptyMessage -and $emptyMessage.Contains($case.Expect, [StringComparison]::Ordinal)) `
            "The refusal for $($case.Name) must come from the scanner, not from anything about the call it happens to precede; got: $emptyMessage"
    }

    $failClosedConsumer = @(
        @{ Name = 'non-literal key'; Source = 'class C { void M() { InternalServiceBaseAddress.Resolve(c, e, ErpKey, "http://localhost:5118"); } }'; Expect = 'non-literal configuration key' }
        @{ Name = 'using static import'; Source = "using static Nerv.IIP.ServiceAuth.InternalServiceBaseAddress;`nclass C { }"; Expect = "'using static'" }
        @{ Name = 'key of another shape'; Source = 'class C { void M() { InternalServiceBaseAddress.Resolve(c, e, "Erp:Endpoint", "http://localhost:5118"); } }'; Expect = "not of the form '<Service>:BaseUrl'" }
        @{ Name = 'unexpected arity'; Source = 'class C { void M() { InternalServiceBaseAddress.Resolve(c, e, "Erp:BaseUrl"); } }'; Expect = 'arguments' }
    )
    foreach ($case in $failClosedConsumer) {
        $message = Get-ThrownMessage -Action { Get-NervBaseUrlResolveCallSites -SourceText $case.Source -SourcePath 'fixture/fail-closed.cs' }
        Assert-Contract ($null -ne $message -and $message.Contains($case.Expect)) `
            "A $($case.Name) must stop the scan rather than silently drop the requirement; got: $message"
    }

    # ── G. AppHost 侧 fail-closed（对真实 Program.cs 做变异，不用合成夹具） ─────────────────────
    # 合成 AppHost 会把断言钉死在真实消费方代码上：gateway 或 ops 任何时候新增一处 Resolve，
    # 合成夹具就少声明一个提供方而假红，并要求回来改夹具。这里改成变异真实文件。
    $sampleInjection = $baseline.Injections[0]
    $appHostMutations = @(
        @{
            Name = 'the same key injected twice on one resource'
            Lines = { param($lines) $copy = [Collections.Generic.List[string]]::new($lines); $copy.Insert($sampleInjection.Line, $lines[$sampleInjection.Line - 1]); $copy }
            Expect = 'twice'
        }
        @{
            Name = 'an injection attributed to a variable that is no declared resource'
            Lines = { param($lines) $copy = [Collections.Generic.List[string]]::new($lines); $copy.Insert($lines.Count - 1, "stray = stray`n    .WithEnvironment(`"$($sampleInjection.EnvironmentName)`", $($baseline.Resources[0].Variable).GetEndpoint(`"http`"));"); $copy }
            Expect = 'which is not a project resource'
        }
        @{
            Name = 'AddProject not bound to a var'
            Lines = { param($lines) $copy = [Collections.Generic.List[string]]::new($lines); $copy.Insert($lines.Count - 1, 'builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host-clone");'); $copy }
            Expect = 'is not bound to a'
        }
        @{
            # ⛔ 审核第 1 轮在这一格上实测旧版 EXIT=0：左值与接收者不同的那一版能编译，而注入会
            # 落到接收者上，左值那个资源一条也拿不到 —— 正是 #3313 的失败形态。
            Name = 'the assignment target and the chain receiver naming different resources'
            Lines = {
                param($lines)
                $copy = [Collections.Generic.List[string]]::new($lines)
                $other = @($baseline.Resources | Where-Object { -not [string]::Equals($_.Resource, $sampleInjection.Resource, [StringComparison]::Ordinal) })[0]
                $target = @($baseline.Resources | Where-Object { [string]::Equals($_.Resource, $sampleInjection.Resource, [StringComparison]::Ordinal) })[0]
                $copy.Insert($lines.Count - 1, "$($target.Variable) = $($other.Variable)`n    .WithEnvironment(`"$($sampleInjection.EnvironmentName)`", $($baseline.Resources[0].Variable).GetEndpoint(`"http`"));")
                $copy
            }
            Expect = 'but the WithEnvironment chain that injects'
        }
        @{
            # 回调式 WithEnvironment 读不出字面量键，但它在运行时**后写覆盖**基址。
            # 旧版对这一格 continue（GREEN）。
            Name = 'a callback-form WithEnvironment that mentions a base-url key'
            Lines = {
                param($lines)
                $copy = [Collections.Generic.List[string]]::new($lines)
                $copy[$sampleInjection.Line - 1] = "    .WithEnvironment(ctx => ctx.EnvironmentVariables[`"$($sampleInjection.EnvironmentName)`"] = `"http://wrong-sentinel:1`")"
                $copy
            }
            Expect = 'in a form this library cannot read'
        }
        @{
            Name = 'a hosted project type with no .csproj'
            Lines = { param($lines) @($lines | ForEach-Object { $_.Replace('Projects.Nerv_IIP_Ops_Web', 'Projects.Nerv_IIP_Ops_Absent_Web') }) }
            Expect = 'for which no .csproj exists'
        }
    )
    foreach ($case in $appHostMutations) {
        $mutatedText = (& $case.Lines $originalLines) -join "`n"
        Assert-Contract (-not [string]::Equals($mutatedText, $originalText, [StringComparison]::Ordinal)) `
            "Mutation sentinel: '$($case.Name)' must change the AppHost text."
        $message = Get-ThrownMessage -Action { Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $mutatedText -Cache $cache }
        Assert-Contract ($null -ne $message -and $message.Contains($case.Expect, [StringComparison]::Ordinal)) `
            "$($case.Name) must stop the checker rather than be dropped; got: $message"
    }

    # 「没有资源提供这个键前缀」需要一个消费方要了、AppHost 没声明的前缀。它是唯一必须用合成
    # AppHost 的一格，所以断言只吃「拒绝的理由」，不吃前缀名 —— 消费方代码改了也不会假红。
    $soleConsumer = @($baseline.Requirements)[0]
    $soleResource = @($baseline.Resources | Where-Object { [string]::Equals($_.Resource, $soleConsumer.ConsumerResource, [StringComparison]::Ordinal) })[0]
    $soleProjectType = $soleResource.ProjectTypeName
    $lonelyText = "var $($soleResource.Variable) = builder.AddProject<Projects.$soleProjectType>(`"$($soleResource.Resource)`").WithHttpEndpoint(port: 5100, name: `"http`");"
    $lonelyMessage = Get-ThrownMessage -Action {
        Get-NervAppHostBaseUrlInjectionReport -RepositoryRoot $repoRoot -AppHostProgramText $lonelyText -Cache $cache
    }
    Assert-Contract ($null -ne $lonelyMessage -and $lonelyMessage.Contains('no AppHost project resource provides the', [StringComparison]::Ordinal)) `
        "An AppHost that hosts a consumer without declaring the resource it resolves must be refused; got: $lonelyMessage"

}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "  FAIL $failure" }
    throw "AppHost base address injection contract: $($failures.Count) of $checked assertion(s) failed."
}

Write-Host "AppHost base address injection contract: $checked assertions passed (deletion sweep $deleteCells cells, value sweep $valueCells cells, CONTROL clean)."
