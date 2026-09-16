# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads the Aspire AppHost Program.cs, every .csproj in its project reference closure, and the
#       .cs sources owned by those projects
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

# 复合键、排序与分组一律走仓库既有的序数助手，不在本库自造第二套键编码或排序口径。
. (Join-Path $PSScriptRoot 'OrdinalString.ps1')

<#
#3511 —— 「消费方要的下游基址，AppHost 必须真的注入，且注入的必须是本会话那个端点」。

## 被守的不变量

宿主代码里的

    InternalServiceBaseAddress.Resolve(config, env, "Erp:BaseUrl", "http://localhost:5118")

在 `Development`（`ResolveAllowingTestHost` 还加 `Testing`）下**不配也不会失败**：它静默回落到那个
硬编码地址。AppHost 的资源端口是 `.WithHttpEndpoint(port: fullStackEphemeral ? null : 51xx)`：

  * 非 ephemeral 会话端口固定 5101–5111，**恰好等于回落值** ⇒ 漏注入「碰巧是对的」；
  * ephemeral 会话端口动态分配 ⇒ 回落打到固定端口上**碰巧存在的另一套栈**。

#3313 就是后者：PlatformGateway 回落 5104，连上并行 worktree 的 file-storage，拿到那套栈的 401，
症状（鉴权坏了）与真因（目的地错了）完全不在一个域。因此本库断言的是两件事，不是一件：

  1. **在不在**：每条需求都有对应的 `X__BaseUrl` 注入；
  2. **是什么**：注入的值必须是**提供方资源本身**的 `GetEndpoint("http")`，不是字面量、不是别的资源。

只断言 (1) 会把 `"http://wrong-sentinel:1"` 放过去，而那正是 #3313 的失败形态。

## 需求集怎么来的（不是白名单）

本仓反复实证「身份判据在实现时会退化成白名单」，所以这里**没有任何一处人工枚举的服务名、资源名
或目录名**。三条链路全部由代码推导：

  * **哪些项目算消费方**：从 `Program.cs` 的 `builder.AddProject<Projects.X>("resource")` 取出被托管
    的项目，再按 `.csproj` 的 `ProjectReference` 求传递闭包。扫描面 = 闭包内每个项目自己的 `.cs`。
    新加一个服务、或把 Resolve 调用下沉进某个共享库，都会自动进扫描面；
    `backend/tests/**` 不在任何托管项目的闭包里，因此自动落在面外——不需要一条排除规则。
  * **哪些键算需求**：从扫描面里 `InternalServiceBaseAddress.Resolve` /
    `ResolveAllowingTestHost` 调用的**第三个实参**读出来。不是从 AppHost 现有注入反推
    （那样只能证明「现有的都在」，永远发现不了缺的那条）。
  * **谁是提供方**：由键前缀反查托管项目名（`Erp` ← `Nerv.IIP.Business.Erp.Web`）。命名不符合
    `Nerv.IIP.[Business.]<Name>.(Web|Host)` 的项目会让本库 throw，而不是被静默跳过。

## 失效方向

凡是「读不懂」一律 throw（= 门禁红），不是跳过：调用的第三个实参不是字面量、键不是 `X:BaseUrl`
形态、提供方项目找不到或不唯一、注入语句归属不到已知资源变量、**赋值左值与链接收者指向不同资源**、
提到 `__BaseUrl` 却读不出字面量键的 `WithEnvironment` 形态、`using static` 形式的非限定调用、
以及扫描器没实现的 C# 字面量/条件编译形态——全部 throw。新写法的默认归宿是「被拦下来问」。

## 覆盖边界（声明多少就只断言多少）

  * 只读 `infra/aspire/Nerv.IIP.AppHost/Program.cs` 的**源码文本**，不跑 `aspire publish`。
    因此 AppHost 自己能编译、能跑出正确 compose，不在本库的断言面内——那是
    `scripts/verify-aspire-apphost-environment-artifacts.ps1` 的面。
  * **多注入**（AppHost 注了但没有消费方 Resolve 它）不构成违例，只做信息输出：处置它需要逐条判断
    「删注入还是补消费方」，属 #3512。
  * 不校验 `.WithReference(x)` 是否成对出现。服务发现名与显式基址是两条路（#1317），本库守的是
    显式基址这条；把 `WithReference` 也拉进来会把「等待/依赖拓扑」这件事混进同一条断言里。
  * C# 扫描面的契约是「实现 + 拒绝」，不是「实现 C# 词法」。实现哪几种、拒绝哪几种，以及各自在
    扫描面上的实测出现次数，逐行列在 `Get-NervCSharpScanSurface` 的头部表里——那张表是本库唯一
    有权声明覆盖面的地方，**不要在别处复述它**。走 Roslyn 是更好的终局（#3124 已就同类问题裁决过），
    但它要把这条门禁挪出 pwsh 进程、牵出承载 shard 的 CI 选中问题，属另票。
#>

$script:NervAppHostProgramRelativePath = 'infra/aspire/Nerv.IIP.AppHost/Program.cs'
$script:NervAppHostBaseUrlExemptionRelativePath = 'scripts/apphost-base-url-injection-exemptions.json'
$script:NervAppHostBaseUrlTrackingPattern = '^#[1-9][0-9]*$'
$script:NervAppHostEndpointName = 'http'

# 可以整行置空的预处理指令。它们都**不能改变哪一段代码是活的**，因此跳过不影响扫描结论。
#
# 写成**放行名单**而不是拒绝名单，因为两者失效方向相反：拒绝名单漏一个（`#fooif`）会走置空分支、
# 静默继续扫描 —— 失效方向是绿；放行名单漏一个直接 throw —— 失效方向是红，与本库「读不懂一律
# throw」的姿势一致。这张名单不承重：它只决定「置空还是 throw」，不决定任何一条需求。
$script:NervCSharpSkippableDirectives = @('region', 'endregion', 'pragma', 'line', 'nullable', 'error', 'warning')

# 违例种类闭集。登记表的 kind 必须落在这里，否则一个拼错的种类会静默豁免不掉任何东西。
$script:NervAppHostBaseUrlViolationKinds = @('missing', 'wrong-value')

#region C# 扫描面

function Get-NervCSharpScanSurface {
    <#
        把一份 C# 源码拆成两个等长的视图：

          Code   —— 注释位与预处理指令位换成空白，字符串字面量原样保留。用来**读内容**（键名、值表达式）。
          Masked —— 在 Code 之上再把字符串/字符字面量的内容换成 'x'。用来**判结构**
                    （`;` 语句边界、括号深度、赋值号），因为字面量里出现的 `;` `(` `=` 不是结构。

        两个视图与原文逐字符对齐（换行一律保留），所以在 Masked 上找到的偏移可以直接切 Code，
        也可以直接换算行号。

        ## 本函数的契约是「实现 + 拒绝」，不是「实现 C# 词法」

        #3124 已经就同一问题裁决过：手搓 C# 词法器在本仓不收敛，改用 Roslyn。本函数**不试图**成为
        那一版的 PowerShell 复刻。它只实现扫描面上真实出现的形态，对没实现的形态**当场 throw**：

        | 形态 | 处置 | 扫描面实测出现次数 |
        |---|---|---|
        | `//` 行注释、`/* */` 块注释 | 实现（置空） | 有 / 0 |
        | 普通字符串 `"…"`（含 `\` 转义与 `$"…"` 插值洞） | 实现 | 有 / 21 |
        | 字符字面量 `'x'` | 实现 | 6（全是 `','` 与 `';'`） |
        | 放行名单内的预处理指令（`#region` / `#pragma` / `#line` / `#nullable` / `#error` / `#warning`） | 实现：整行当非代码置空 | 42 |
        | 原始字符串 `"""…"""` | **throw** | 0 |
        | 逐字字符串 `@"…"` | **throw** | 0 |
        | 其余一切预处理指令（含条件编译 `#if` / `#endif` / `#define` / …） | **throw** | 0 |

        为什么线画在这里，而不是「再实现一种形态」：后者的失效方向是**静默归零**——一个没认对的
        字面量把扫描相位带偏，整份文件的需求集塌成 0，而门禁照样宣称全部通过。throw 的失效方向是红。
        本 PR 的前一版实现过 raw 与 verbatim，并在这两处各复现了一个 #3124 已点名的静默归零缺陷
        （`@""""` 被先数引号读成 raw、吞掉文件其余部分）。**实现一种形态的代价是它必须正确；拒绝它没有这个代价。**

        预处理指令为什么不是一律 throw：面上实测 42 处 `#region` / `#pragma`，一律 throw 是假红，
        那不是 fail-closed 是坏掉。指令行整行不是代码，置空顺带关掉了 `#region Don't touch` 里那个
        撇号被读成字符字面量起点、一路吞到文件尾的通路（撇号根本到不了字符字面量分支）。
        但判据写成**放行名单**而不是拒绝名单：放行名单漏一个的失效方向是红，拒绝名单漏一个是绿
        （`#fooif` 会静默走置空分支）。条件编译指令因此自动落在拒绝面上——它决定哪一段代码是活的，
        本函数不建模这件事。

        扫描面（托管项目闭包内提及 InternalServiceBaseAddress 的 .cs，加 AppHost Program.cs）是
        17 个文件，上表的次数即在该面上实测；它不是对全仓的断言。
    #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $quote = [char]'"'
    $length = $Text.Length
    $code = [char[]]::new($length)
    $masked = [char[]]::new($length)

    # 上下文栈：栈顶要么是代码上下文（可能是一个插值洞），要么是一个普通字符串上下文。
    $contexts = [Collections.Generic.Stack[hashtable]]::new()
    $contexts.Push(@{ Kind = 'code'; IsHole = $false; BraceDepth = 0; Interpolated = $false })

    $index = 0
    while ($index -lt $length) {
        $context = $contexts.Peek()
        $current = $Text[$index]
        $next = if ($index + 1 -lt $length) { $Text[$index + 1] } else { [char]0 }

        if ([string]::Equals([string] $context['Kind'], 'code', [StringComparison]::Ordinal)) {
            if ($current -eq [char]'/' -and $next -eq [char]'/') {
                while ($index -lt $length -and $Text[$index] -ne "`n") {
                    $code[$index] = ' '
                    $masked[$index] = ' '
                    $index++
                }
                continue
            }

            if ($current -eq [char]'/' -and $next -eq [char]'*') {
                $terminated = $false
                while ($index -lt $length) {
                    if ($Text[$index] -eq [char]'*' -and $index + 1 -lt $length -and $Text[$index + 1] -eq [char]'/') {
                        $code[$index] = ' '; $masked[$index] = ' '; $index++
                        $code[$index] = ' '; $masked[$index] = ' '; $index++
                        $terminated = $true
                        break
                    }
                    $replacement = if ($Text[$index] -eq "`n" -or $Text[$index] -eq "`r") { $Text[$index] } else { ' ' }
                    $code[$index] = $replacement
                    $masked[$index] = $replacement
                    $index++
                }
                if (-not $terminated) { throw 'Unterminated block comment while scanning C# source.' }
                continue
            }

            if ($current -eq [char]'#' -and (Test-NervCSharpLineStart -Text $Text -Index $index)) {
                $directive = [regex]::Match($Text.Substring($index, [Math]::Min(32, $length - $index)), '^#\s*(?<name>[A-Za-z]+)')
                $name = if ($directive.Success) { $directive.Groups['name'].Value } else { '' }
                if (-not (Get-NervStringSet -Values $script:NervCSharpSkippableDirectives -Comparer ([StringComparer]::Ordinal)).Contains($name)) {
                    throw "Preprocessor directive '#$name' at line $(Get-NervCSharpLineNumber -Text $Text -Index $index): this scanner only skips directives that cannot change which code is live, and refuses to guess for the rest. See the coverage table in Get-NervCSharpScanSurface."
                }
                while ($index -lt $length -and $Text[$index] -ne "`n") {
                    $code[$index] = ' '
                    $masked[$index] = ' '
                    $index++
                }
                continue
            }

            if ($current -eq [char]"'") {
                $code[$index] = $current
                $masked[$index] = $current
                $index++
                while ($index -lt $length -and $Text[$index] -ne [char]"'") {
                    if ($Text[$index] -eq [char]'\') {
                        $code[$index] = $Text[$index]; $masked[$index] = 'x'; $index++
                        if ($index -ge $length) { break }
                    }
                    $code[$index] = $Text[$index]
                    $masked[$index] = if ($Text[$index] -eq "`n") { "`n" } else { 'x' }
                    $index++
                }
                if ($index -lt $length) {
                    $code[$index] = $Text[$index]
                    $masked[$index] = $Text[$index]
                    $index++
                }
                continue
            }

            # 拒绝面。两条都在**进入**字面量之前拦下，所以扫描相位永远不会被没实现的形态带偏。
            if ($current -eq [char]'@') {
                $probe = $index + 1
                while ($probe -lt $length -and $Text[$probe] -eq [char]'$') { $probe++ }
                if ($probe -lt $length -and $Text[$probe] -eq $quote) {
                    throw "Verbatim string literal at line $(Get-NervCSharpLineNumber -Text $Text -Index $index): this scanner does not implement verbatim strings and refuses to guess. See the coverage table in Get-NervCSharpScanSurface."
                }
            }

            if ($current -eq $quote) {
                $run = 0
                while ($index + $run -lt $length -and $Text[$index + $run] -eq $quote) { $run++ }
                if ($run -ge 3) {
                    throw "Raw string literal at line $(Get-NervCSharpLineNumber -Text $Text -Index $index): this scanner does not implement raw strings and refuses to guess. See the coverage table in Get-NervCSharpScanSurface."
                }
                $code[$index] = $current
                $masked[$index] = $current
                $interpolated = $index -gt 0 -and $Text[$index - 1] -eq [char]'$'
                $index++
                $contexts.Push(@{ Kind = 'string'; Interpolated = $interpolated })
                continue
            }

            if ($context['IsHole']) {
                if ($current -eq [char]'{') { $context['BraceDepth'] = [int] $context['BraceDepth'] + 1 }
                elseif ($current -eq [char]'}') {
                    if ([int] $context['BraceDepth'] -eq 0) {
                        $code[$index] = $current
                        $masked[$index] = $current
                        $index++
                        [void] $contexts.Pop()
                        continue
                    }
                    $context['BraceDepth'] = [int] $context['BraceDepth'] - 1
                }
            }

            $code[$index] = $current
            $masked[$index] = $current
            $index++
            continue
        }

        # 普通字符串上下文。逐字与原始字符串在代码上下文就已经 throw，这里不会遇到。
        if ($current -eq $quote) {
            $code[$index] = $current
            $masked[$index] = $current
            $index++
            [void] $contexts.Pop()
            continue
        }

        if ($current -eq [char]'\') {
            $code[$index] = $current; $masked[$index] = 'x'; $index++
            if ($index -lt $length) {
                $code[$index] = $Text[$index]
                $masked[$index] = if ($Text[$index] -eq "`n") { "`n" } else { 'x' }
                $index++
            }
            continue
        }

        if ($context['Interpolated'] -and $current -eq [char]'{') {
            if ($next -eq [char]'{') {
                $code[$index] = $current; $masked[$index] = 'x'; $index++
                $code[$index] = $Text[$index]; $masked[$index] = 'x'; $index++
                continue
            }
            $code[$index] = $current
            $masked[$index] = $current
            $index++
            $contexts.Push(@{ Kind = 'code'; IsHole = $true; BraceDepth = 0; Interpolated = $false })
            continue
        }

        $code[$index] = $current
        $masked[$index] = if ($current -eq "`n" -or $current -eq "`r") { $current } else { 'x' }
        $index++
    }

    return [pscustomobject]@{
        Code = [string]::new($code)
        Masked = [string]::new($masked)
    }
}

function Test-NervCSharpLineStart {
    <# 从 $Index 往回到上一个换行为止是否只有空白——即 `#` 是否处在预处理指令的位置。 #>
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [int] $Index
    )

    $probe = $Index - 1
    while ($probe -ge 0 -and $Text[$probe] -ne "`n") {
        if (-not [char]::IsWhiteSpace($Text[$probe])) { return $false }
        $probe--
    }
    return $true
}

function Get-NervCSharpArgumentSpan {
    <#
        给定一个紧跟在 `(` 之前的位置，返回该实参列表的闭合括号偏移与按顶层逗号切开的实参原文。
        深度用 Masked 视图数，内容从 Code 视图切 —— 字面量里的括号与逗号不参与切分。
    #>
    param(
        [Parameter(Mandatory)] [string] $Code,
        [Parameter(Mandatory)] [string] $Masked,
        [Parameter(Mandatory)] [int] $OpenParenIndex
    )

    if ($Masked[$OpenParenIndex] -ne '(') { throw "Expected '(' at offset $OpenParenIndex." }
    $depth = 0
    $arguments = [Collections.Generic.List[string]]::new()
    $segmentStart = $OpenParenIndex + 1
    for ($index = $OpenParenIndex; $index -lt $Masked.Length; $index++) {
        $character = $Masked[$index]
        if ($character -eq '(' -or $character -eq '[') { $depth++; continue }
        if ($character -eq ')' -or $character -eq ']') {
            $depth--
            if ($depth -eq 0) {
                $arguments.Add($Code.Substring($segmentStart, $index - $segmentStart))
                return [pscustomobject]@{
                    CloseParenIndex = $index
                    Arguments = @($arguments | ForEach-Object { $_.Trim() })
                }
            }
            continue
        }
        if ($character -eq ',' -and $depth -eq 1) {
            $arguments.Add($Code.Substring($segmentStart, $index - $segmentStart))
            $segmentStart = $index + 1
        }
    }
    throw "Unbalanced argument list starting at offset $OpenParenIndex."
}

function Get-NervCSharpLineNumber {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [int] $Index
    )

    # 用 native IndexOf 逐行跳，不要逐字符扫：本函数在一次报告里被调用上百次，
    # 逐字符版本在 PowerShell 里足以主导整条门禁的耗时。
    $line = 1
    $position = $Text.IndexOf("`n", 0, [StringComparison]::Ordinal)
    while ($position -ge 0 -and $position -lt $Index) {
        $line++
        $position = $Text.IndexOf("`n", $position + 1, [StringComparison]::Ordinal)
    }
    return $line
}

function Get-NervCSharpStringLiteralValue {
    <#
        实参原文是不是一个**普通字符串字面量**；是就返回它的值，不是就返回 $null。
        故意不接受逐字/插值/常量引用：那些形态下「这条调用要的是哪个键」不能由本库单独判定，
        调用方据此 throw 比猜一个值安全。
    #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Argument)

    $trimmed = $Argument.Trim()
    if ($trimmed.Length -lt 2 -or -not $trimmed.StartsWith('"', [StringComparison]::Ordinal) -or -not $trimmed.EndsWith('"', [StringComparison]::Ordinal)) {
        return $null
    }
    $body = $trimmed.Substring(1, $trimmed.Length - 2)
    if ($body.Contains('"', [StringComparison]::Ordinal) -or $body.Contains('\', [StringComparison]::Ordinal)) { return $null }
    return $body
}

#endregion

#region 托管项目与闭包

function Get-NervAppHostProjectResources {
    <#
        从 AppHost 源码取出 `builder.AddProject<Projects.X>("resource")`：资源名、项目类型名，以及
        承接它的变量名（后续注入语句按变量名归属到资源）。
    #>
    param(
        [Parameter(Mandatory)] [string] $AppHostProgramText,
        [psobject] $Surface
    )

    # 扫描面可以由调用方喂进来：一次报告要读三遍同一份源码，而字符级扫描是本库最贵的一步。
    $surface = if ($null -ne $Surface) { $Surface } else { Get-NervCSharpScanSurface -Text $AppHostProgramText }
    $resources = [Collections.Generic.List[object]]::new()
    $pattern = [regex] 'builder\s*\.\s*AddProject\s*<\s*Projects\s*\.\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*>\s*\(\s*"(?<resource>[^"]+)"'
    foreach ($match in $pattern.Matches($surface.Code)) {
        $statementStart = $surface.Masked.LastIndexOf(';', $match.Index, [StringComparison]::Ordinal)
        $prefix = $surface.Code.Substring($statementStart + 1, $match.Index - $statementStart - 1)
        $variableMatch = [regex]::Match($prefix, 'var\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=')
        if (-not $variableMatch.Success) {
            throw "AppHost resource '$($match.Groups['resource'].Value)' is not bound to a `var` declaration; the injection attribution in this library reads that name."
        }
        $resources.Add([pscustomobject]@{
            Resource = $match.Groups['resource'].Value
            ProjectTypeName = $match.Groups['type'].Value
            Variable = $variableMatch.Groups['name'].Value
            Line = Get-NervCSharpLineNumber -Text $AppHostProgramText -Index $match.Index
        })
    }

    if ($resources.Count -eq 0) { throw 'The AppHost source declares no project resource; the scan surface has collapsed.' }
    $duplicateVariables = @(Get-NervStringGroups -Items @($resources) -KeySelector { param($row) [string] $row.Variable } -Comparer ([StringComparer]::Ordinal) |
        Where-Object { $_.Count -gt 1 })
    if ($duplicateVariables.Count -gt 0) {
        throw "AppHost project resources reuse variable name(s): $(@($duplicateVariables | ForEach-Object Name) -join ', ')."
    }
    return $resources
}

function Get-NervProjectFileIndex {
    <#
        全仓 .csproj 的「Aspire 生成的 Projects.X 类型名 → 文件路径」索引。
        Aspire 把项目名里的 `.` 与 `-` 换成 `_` 生成那个类型名，这里按同一规则反查。
    #>
    param([Parameter(Mandatory)] [string] $RepositoryRoot)

    $index = @{}
    foreach ($file in [IO.Directory]::EnumerateFiles($RepositoryRoot, '*.csproj', [IO.SearchOption]::AllDirectories)) {
        $normalized = $file.Replace('\', '/')
        if ($normalized.Contains('/obj/', [StringComparison]::Ordinal) -or
            $normalized.Contains('/bin/', [StringComparison]::Ordinal) -or
            $normalized.Contains('/node_modules/', [StringComparison]::Ordinal)) { continue }
        $typeName = [IO.Path]::GetFileNameWithoutExtension($file).Replace('.', '_').Replace('-', '_')
        if ($index.ContainsKey($typeName)) {
            throw "Two project files map to the Aspire project type name '$typeName': '$($index[$typeName])' and '$file'."
        }
        $index[$typeName] = $file
    }
    return $index
}

function Get-NervProjectReferenceClosure {
    <#
        一个 .csproj 的传递 ProjectReference 闭包（含自身），按绝对路径返回。
    #>
    param([Parameter(Mandatory)] [string] $ProjectPath)

    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue((Resolve-Path -LiteralPath $ProjectPath).Path)
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        if (-not $visited.Add($current)) { continue }
        $projectDirectory = [IO.Path]::GetDirectoryName($current)
        $projectText = [IO.File]::ReadAllText($current)
        foreach ($match in [regex]::Matches($projectText, '<ProjectReference\s+Include\s*=\s*"(?<path>[^"]+)"')) {
            $referencePath = $match.Groups['path'].Value.Replace('\', [IO.Path]::DirectorySeparatorChar)
            $candidate = [IO.Path]::GetFullPath((Join-Path $projectDirectory $referencePath))
            if (-not [IO.File]::Exists($candidate)) {
                throw "Project '$current' references '$($match.Groups['path'].Value)', which does not exist."
            }
            $pending.Enqueue($candidate)
        }
    }
    return $visited
}

function Get-NervProviderServiceName {
    <#
        托管项目名 → 配置键前缀。`Nerv.IIP.Business.Erp.Web` → `Erp`，`Nerv.IIP.FileStorage.Web`
        → `FileStorage`。不符合 `Nerv.IIP.[Business.]<Name>.(Web|Host)` 的项目直接 throw：
        让新命名撞上一次人工裁决，好过悄悄退出提供方集合、把它的消费方需求变成「查无此人」。
    #>
    param([Parameter(Mandatory)] [string] $ProjectName)

    if (-not $ProjectName.StartsWith('Nerv.IIP.', [StringComparison]::Ordinal)) {
        throw "Hosted project '$ProjectName' does not use the 'Nerv.IIP.' prefix; the base-url key prefix cannot be derived from it."
    }
    $remainder = $ProjectName.Substring('Nerv.IIP.'.Length)
    $suffix = @('.Web', '.Host') | Where-Object { $remainder.EndsWith($_, [StringComparison]::Ordinal) } | Select-Object -First 1
    if ($null -eq $suffix) {
        throw "Hosted project '$ProjectName' ends with neither '.Web' nor '.Host'; the base-url key prefix cannot be derived from it."
    }
    $remainder = $remainder.Substring(0, $remainder.Length - $suffix.Length)
    if ($remainder.StartsWith('Business.', [StringComparison]::Ordinal)) {
        $remainder = $remainder.Substring('Business.'.Length)
    }
    if ($remainder.Contains('.', [StringComparison]::Ordinal)) {
        throw "Hosted project '$ProjectName' does not reduce to a single base-url key prefix (got '$remainder')."
    }
    return $remainder
}

#endregion

#region 消费方需求

function Get-NervBaseUrlResolveCallSites {
    <#
        一份源码里所有 `InternalServiceBaseAddress.Resolve` / `.ResolveAllowingTestHost` 调用要的键。
    #>
    param(
        [Parameter(Mandatory)] [string] $SourceText,
        [Parameter(Mandatory)] [string] $SourcePath
    )

    $surface = Get-NervCSharpScanSurface -Text $SourceText

    # 非限定调用（`using static`）会让下面这条按类型名锚定的扫描完全失明；它不是「少认一种写法」，
    # 是整份文件的需求静默归零，所以在这里拦下来。
    if ([regex]::IsMatch($surface.Masked, 'using\s+static\s+[A-Za-z0-9_.]*InternalServiceBaseAddress\s*;')) {
        throw "'$SourcePath' imports InternalServiceBaseAddress with 'using static'; this scanner only recognises type-qualified calls."
    }

    $callSites = [Collections.Generic.List[object]]::new()
    $pattern = [regex] 'InternalServiceBaseAddress\s*\.\s*(?<method>Resolve|ResolveAllowingTestHost)\s*\('
    foreach ($match in $pattern.Matches($surface.Masked)) {
        $span = Get-NervCSharpArgumentSpan -Code $surface.Code -Masked $surface.Masked -OpenParenIndex ($match.Index + $match.Length - 1)
        $line = Get-NervCSharpLineNumber -Text $SourceText -Index $match.Index
        if ($span.Arguments.Count -ne 4) {
            throw "'$SourcePath':$line calls InternalServiceBaseAddress.$($match.Groups['method'].Value) with $($span.Arguments.Count) arguments; this scanner reads the configuration key from the third of four."
        }
        $key = Get-NervCSharpStringLiteralValue -Argument $span.Arguments[2]
        if ($null -eq $key) {
            throw "'$SourcePath':$line passes a non-literal configuration key to InternalServiceBaseAddress.$($match.Groups['method'].Value); the requirement set cannot be derived from it."
        }
        $keyMatch = [regex]::Match($key, '^(?<service>[A-Za-z][A-Za-z0-9]*):BaseUrl$')
        if (-not $keyMatch.Success) {
            throw "'$SourcePath':$line resolves configuration key '$key', which is not of the form '<Service>:BaseUrl'."
        }
        $callSites.Add([pscustomobject]@{
            Key = $key
            ProviderService = $keyMatch.Groups['service'].Value
            SourcePath = $SourcePath
            Line = $line
        })
    }
    return $callSites
}

#endregion

#region AppHost 注入

function Get-NervAppHostChainReceiverResource {
    <#
        一条 `<左值> = <接收者>.…WithEnvironment(…)` 链里，`.WithEnvironment` 实际作用在哪个资源上。

        两种接收者形态：链头自己就是 `builder.AddProject<Projects.X>("r")`（`var r = …` 声明式），
        或链头是一个标识符（`x = x.…` 回填式）。两者都读不出来时 throw —— 猜一个接收者，就是把
        「注入落在哪个资源上」这件事交给一条启发式，而它正是本库要守的东西。
    #>
    param(
        [Parameter(Mandatory)] [psobject] $Surface,
        [Parameter(Mandatory)] [int] $Start,
        [Parameter(Mandatory)] [int] $End,
        [Parameter(Mandatory)] [hashtable] $ResourceByVariable,
        [Parameter(Mandatory)] [int] $Line,
        [Parameter(Mandatory)] [string] $EnvironmentName
    )

    $receiverCode = $Surface.Code.Substring($Start, $End - $Start)
    $addProject = [regex]::Match($receiverCode, 'AddProject\s*<\s*Projects\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*>\s*\(\s*"(?<resource>[^"]+)"')
    if ($addProject.Success) { return $addProject.Groups['resource'].Value }

    $identifier = [regex]::Match($Surface.Masked.Substring($Start, $End - $Start).TrimStart(), '^(?<name>[A-Za-z_][A-Za-z0-9_]*)')
    if ($identifier.Success -and $ResourceByVariable.ContainsKey($identifier.Groups['name'].Value)) {
        return $ResourceByVariable[$identifier.Groups['name'].Value]
    }

    throw "AppHost line $Line injects '$EnvironmentName' on a chain whose receiver this library cannot resolve to a declared project resource: $(($receiverCode -replace '\s+', ' ').Trim())"
}

function Get-NervAppHostBaseUrlInjections {
    <#
        按语句归属解析 `.WithEnvironment("X__BaseUrl", <expr>)`。

        归属规则：一条语句（以顶层 `;` 为界）里，第一个 `.WithEnvironment` 之前最后一个赋值目标是
        这条链作用的资源变量。`var x = builder.AddProject…`、`x = x.WithEnvironment(…)`、
        以及写在 `if (…) { x = x … ; }` 里的回填，三种形态都落在这条规则下。

        ⚠️ **赋值左值不等于接收者**，两者不同的那一版**能编译**（都是
        `IResourceBuilder<ProjectResource>`）。`a = b.WithEnvironment("B__BaseUrl", …)` 在运行时把环境
        变量注到 **b** 上，`a` 一条也拿不到 —— 这正是 #3313 的失败形态，而只看左值的归属会判它绿。
        所以这里不止读左值：**同时读出接收者，二者指向的资源不一致即 throw**。

        为什么是 throw 而不是「改按接收者归属」：左值与接收者不一致时，除了注入落在别的资源上，
        那条语句还把左值变量重新绑到了另一个 builder，后续所有对该变量的回填都会跟着搬家。
        这是一个需要人看的错误，不是一个本库有权替人选边的歧义。**本库在这一格上的职责是不猜。**

        归属不到已知资源变量的注入、以及提到 `__BaseUrl` 却读不出字面量键的 `WithEnvironment`
        调用形态（例如回调式 `ctx => ctx.EnvironmentVariables[…] = …`，它在运行时后写覆盖）
        ⇒ 一律 throw，不静默丢弃。
    #>
    param(
        [Parameter(Mandatory)] [string] $AppHostProgramText,
        [psobject] $Surface
    )

    $surface = if ($null -ne $Surface) { $Surface } else { Get-NervCSharpScanSurface -Text $AppHostProgramText }
    $resources = Get-NervAppHostProjectResources -AppHostProgramText $AppHostProgramText -Surface $surface
    $resourceByVariable = @{}
    foreach ($resource in $resources) { $resourceByVariable[$resource.Variable] = $resource.Resource }

    $injections = [Collections.Generic.List[object]]::new()
    $pattern = [regex] '\.\s*WithEnvironment\s*\('
    foreach ($match in $pattern.Matches($surface.Masked)) {
        $span = Get-NervCSharpArgumentSpan -Code $surface.Code -Masked $surface.Masked -OpenParenIndex ($match.Index + $match.Length - 1)
        $line = Get-NervCSharpLineNumber -Text $AppHostProgramText -Index $match.Index
        $name = if ($span.Arguments.Count -eq 2) { Get-NervCSharpStringLiteralValue -Argument $span.Arguments[0] } else { $null }
        $nameMatch = if ($null -eq $name) { $null } else { [regex]::Match($name, '^(?<service>[A-Za-z][A-Za-z0-9]*)__BaseUrl$') }
        if ($null -eq $nameMatch -or -not $nameMatch.Success) {
            # 读不出字面量键的调用绝大多数与基址无关（`Persistence__Provider`、helper 里的
            # `WithEnvironment($name, $value)`），跳过它们是对的。但如果这条调用的原文里出现了
            # `__BaseUrl`，跳过就等于把一条**能在运行时后写覆盖基址**的语句放过去 —— 实测
            # `.WithEnvironment(ctx => ctx.EnvironmentVariables["X__BaseUrl"] = "http://wrong-sentinel:1")`
            # 在旧版本上是 GREEN。所以这一格按「读不懂就红」处置，而不是按「读不懂就跳过」。
            $callText = $surface.Code.Substring($match.Index, $span.CloseParenIndex - $match.Index + 1)
            if ($callText.Contains('__BaseUrl', [StringComparison]::Ordinal)) {
                throw "AppHost line $line has a WithEnvironment call that mentions a '__BaseUrl' key in a form this library cannot read: $(($callText -replace '\s+', ' ').Trim())"
            }
            continue
        }

        $statementStart = $surface.Masked.LastIndexOf(';', $match.Index, [StringComparison]::Ordinal)
        $statementPrefix = $surface.Masked.Substring($statementStart + 1, $match.Index - $statementStart - 1)
        $assignments = [regex]::Matches($statementPrefix, '(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=(?![=>])')
        if ($assignments.Count -eq 0) {
            throw "AppHost line $line injects '$name' in a statement with no assignment target; this library attributes injections by the assigned resource variable."
        }
        $lastAssignment = $assignments[$assignments.Count - 1]
        $targetVariable = $lastAssignment.Groups['name'].Value
        if (-not $resourceByVariable.ContainsKey($targetVariable)) {
            throw "AppHost line $line injects '$name' onto '$targetVariable', which is not a project resource declared by builder.AddProject."
        }

        $receiverStart = $statementStart + 1 + $lastAssignment.Index + $lastAssignment.Length
        $receiverResource = Get-NervAppHostChainReceiverResource `
            -Surface $surface `
            -Start $receiverStart `
            -End $match.Index `
            -ResourceByVariable $resourceByVariable `
            -Line $line `
            -EnvironmentName $name
        if (-not [string]::Equals($receiverResource, $resourceByVariable[$targetVariable], [StringComparison]::Ordinal)) {
            throw "AppHost line $line assigns to '$targetVariable' (resource '$($resourceByVariable[$targetVariable])') but the WithEnvironment chain that injects '$name' runs on resource '$receiverResource'. At run time the variable is rebound and '$name' lands on '$receiverResource', so '$($resourceByVariable[$targetVariable])' gets nothing — the #3313 failure shape. This library refuses to pick a side."
        }

        $injections.Add([pscustomobject]@{
            Resource = $receiverResource
            ProviderService = $nameMatch.Groups['service'].Value
            EnvironmentName = $name
            ValueExpression = ($span.Arguments[1] -replace '\s+', ' ').Trim()
            Line = $line
        })
    }
    return $injections
}

#endregion

function Get-NervRequirementsSorted {
    <# 需求按 (消费方资源, 键) 序数排序。报告与门禁输出共用同一口径，不各排一次。 #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Requirements)

    return @(Get-NervItemsSortedByString -Items @($Requirements) `
        -KeySelector { param($row) Get-NervStringCompositeKey -Components @($row.ConsumerResource, $row.Key) } `
        -Comparer ([StringComparer]::Ordinal))
}

function Get-NervAppHostBaseUrlInjectionReport {
    <#
        求出需求集、注入集与两者的差，并把豁免登记表套上去。

        返回对象的 `Violations` 是**未豁免**的违例；`Exemptions` / `StaleExemptions` 让调用方能分别
        报告「当前靠豁免绿着的那些条」和「票已销账但登记没删的那些条」。豁免表自身的形态错误
        （字段缺失、票号不成形、重复、或指向一条并不存在的违例）在这里 throw。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [string] $AppHostProgramPath,
        [string] $AppHostProgramText,
        [string] $ExemptionPath,

        # 只喂给变异矩阵：本报告最贵的一步是把闭包内 2000+ 个 .cs 读一遍，而针对 AppHost 源码的
        # 变异不会改变这一步的结果。传一个空 hashtable 进来即可跨调用复用；不传则每次重算。
        # 缓存的是**消费方那一侧**，AppHost 侧每次都从被测文本重新解析——否则变异就白做了。
        [hashtable] $Cache
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    if ([string]::IsNullOrEmpty($AppHostProgramPath)) {
        $AppHostProgramPath = Join-Path $resolvedRoot $script:NervAppHostProgramRelativePath
    }
    if (-not $PSBoundParameters.ContainsKey('AppHostProgramText')) {
        if (-not (Test-Path -LiteralPath $AppHostProgramPath -PathType Leaf)) {
            throw "AppHost source '$AppHostProgramPath' does not exist."
        }
        $AppHostProgramText = [IO.File]::ReadAllText($AppHostProgramPath)
    }
    if ([string]::IsNullOrEmpty($ExemptionPath)) {
        $ExemptionPath = Join-Path $resolvedRoot $script:NervAppHostBaseUrlExemptionRelativePath
    }

    if ($null -eq $Cache) { $Cache = @{} }
    foreach ($bucket in @('ProjectIndex', 'Closure', 'CallSites')) {
        if (-not $Cache.ContainsKey($bucket)) { $Cache[$bucket] = $null }
    }
    if ($null -eq $Cache['ProjectIndex']) { $Cache['ProjectIndex'] = Get-NervProjectFileIndex -RepositoryRoot $resolvedRoot }
    if ($null -eq $Cache['Closure']) { $Cache['Closure'] = @{} }
    if ($null -eq $Cache['CallSites']) { $Cache['CallSites'] = @{} }

    $projectIndex = $Cache['ProjectIndex']
    $appHostSurface = Get-NervCSharpScanSurface -Text $AppHostProgramText
    $resources = Get-NervAppHostProjectResources -AppHostProgramText $AppHostProgramText -Surface $appHostSurface

    $resourceByProviderService = @{}
    $closureByResource = @{}
    foreach ($resource in $resources) {
        if (-not $projectIndex.ContainsKey($resource.ProjectTypeName)) {
            throw "AppHost resource '$($resource.Resource)' hosts Projects.$($resource.ProjectTypeName), for which no .csproj exists under '$resolvedRoot'."
        }
        $projectPath = $projectIndex[$resource.ProjectTypeName]
        $providerService = Get-NervProviderServiceName -ProjectName ([IO.Path]::GetFileNameWithoutExtension($projectPath))
        if ($resourceByProviderService.ContainsKey($providerService)) {
            throw "Base-url key prefix '$providerService' is claimed by both resource '$($resourceByProviderService[$providerService])' and '$($resource.Resource)'."
        }
        $resourceByProviderService[$providerService] = $resource.Resource
        if (-not $Cache['Closure'].ContainsKey($projectPath)) {
            $Cache['Closure'][$projectPath] = Get-NervProjectReferenceClosure -ProjectPath $projectPath
        }
        $closureByResource[$resource.Resource] = $Cache['Closure'][$projectPath]
    }

    # 扫描面 = 托管项目闭包内每个项目自己的 .cs。谁在闭包里由 ProjectReference 说了算，
    # 不由任何目录 glob 说了算。
    $ownerProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($closure in $closureByResource.Values) {
        foreach ($project in $closure) { [void] $ownerProjects.Add($project) }
    }

    $callSitesByProject = @{}
    foreach ($project in $ownerProjects) {
        if ($Cache['CallSites'].ContainsKey($project)) {
            $callSitesByProject[$project] = $Cache['CallSites'][$project]
            continue
        }
        $projectDirectory = [IO.Path]::GetDirectoryName($project)
        $callSites = [Collections.Generic.List[object]]::new()
        foreach ($file in [IO.Directory]::EnumerateFiles($projectDirectory, '*.cs', [IO.SearchOption]::AllDirectories)) {
            $normalized = $file.Replace('\', '/')
            if ($normalized.Contains('/obj/', [StringComparison]::Ordinal) -or $normalized.Contains('/bin/', [StringComparison]::Ordinal)) { continue }
            $sourceText = [IO.File]::ReadAllText($file)
            if (-not $sourceText.Contains('InternalServiceBaseAddress', [StringComparison]::Ordinal)) { continue }
            foreach ($callSite in (Get-NervBaseUrlResolveCallSites -SourceText $sourceText -SourcePath $normalized.Substring($resolvedRoot.Length).TrimStart('/'))) {
                $callSites.Add($callSite)
            }
        }
        $Cache['CallSites'][$project] = $callSites
        $callSitesByProject[$project] = $callSites
    }

    $requirements = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($resource in $resources) {
        foreach ($project in $closureByResource[$resource.Resource]) {
            foreach ($callSite in $callSitesByProject[$project]) {
                if (-not $resourceByProviderService.ContainsKey($callSite.ProviderService)) {
                    throw "$($callSite.SourcePath):$($callSite.Line) resolves '$($callSite.Key)', but no AppHost project resource provides the '$($callSite.ProviderService)' prefix."
                }
                if ([string]::Equals($resourceByProviderService[$callSite.ProviderService], $resource.Resource, [StringComparison]::Ordinal)) {
                    # 自指：宿主自己 Resolve 自己的基址。AppHost 无从注入「自己指向自己」的会话端点，
                    # 而且这不是跨服务调用，遇到时当场 throw 好过悄悄算成一条永远无法满足的需求。
                    throw "Resource '$($resource.Resource)' resolves its own base address key '$($callSite.Key)' at $($callSite.SourcePath):$($callSite.Line)."
                }
                $requirementKey = Get-NervStringCompositeKey -Components @($resource.Resource, $callSite.Key)
                if (-not $seen.Add($requirementKey)) { continue }
                $requirements.Add([pscustomobject]@{
                    ConsumerResource = $resource.Resource
                    Key = $callSite.Key
                    EnvironmentName = "$($callSite.ProviderService)__BaseUrl"
                    ProviderService = $callSite.ProviderService
                    ProviderResource = $resourceByProviderService[$callSite.ProviderService]
                    ProviderVariable = @($resources | Where-Object { [string]::Equals($_.Resource, $resourceByProviderService[$callSite.ProviderService], [StringComparison]::Ordinal) })[0].Variable
                    CallSite = "$($callSite.SourcePath):$($callSite.Line)"
                })
            }
        }
    }

    $injections = Get-NervAppHostBaseUrlInjections -AppHostProgramText $AppHostProgramText -Surface $appHostSurface
    $injectionByPair = @{}
    foreach ($injection in $injections) {
        $pairKey = Get-NervStringCompositeKey -Components @($injection.Resource, $injection.EnvironmentName)
        if ($injectionByPair.ContainsKey($pairKey)) {
            # 同一资源上同一个键注入两次时 Aspire 取后者。只看后者会让前者的值无人校验，
            # 而「两条里有一条是错的」正是合并事故的常见形态，所以这里不挑一条，直接红。
            throw "AppHost injects '$($injection.EnvironmentName)' on resource '$($injection.Resource)' twice (lines $($injectionByPair[$pairKey].Line) and $($injection.Line))."
        }
        $injectionByPair[$pairKey] = $injection
    }

    $violations = [Collections.Generic.List[object]]::new()
    foreach ($requirement in (Get-NervRequirementsSorted -Requirements $requirements)) {
        $pairKey = Get-NervStringCompositeKey -Components @($requirement.ConsumerResource, $requirement.EnvironmentName)
        if (-not $injectionByPair.ContainsKey($pairKey)) {
            $violations.Add([pscustomobject]@{
                Kind = 'missing'
                ConsumerResource = $requirement.ConsumerResource
                Key = $requirement.Key
                Detail = "AppHost never injects '$($requirement.EnvironmentName)' on resource '$($requirement.ConsumerResource)', so $($requirement.CallSite) falls back to its hardcoded development address."
            })
            continue
        }
        $injection = $injectionByPair[$pairKey]
        $expected = "$($requirement.ProviderVariable).GetEndpoint(`"$script:NervAppHostEndpointName`")"
        if (-not [string]::Equals($injection.ValueExpression, $expected, [StringComparison]::Ordinal)) {
            $violations.Add([pscustomobject]@{
                Kind = 'wrong-value'
                ConsumerResource = $requirement.ConsumerResource
                Key = $requirement.Key
                Detail = "AppHost line $($injection.Line) injects '$($requirement.EnvironmentName)' on resource '$($requirement.ConsumerResource)' as '$($injection.ValueExpression)', expected '$expected' — the session endpoint of resource '$($requirement.ProviderResource)'."
            })
        }
    }

    $requiredPairs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($requirement in $requirements) {
        [void] $requiredPairs.Add((Get-NervStringCompositeKey -Components @($requirement.ConsumerResource, $requirement.EnvironmentName)))
    }
    $unconsumedInjections = @(Get-NervItemsSortedByString -Items @($injections | Where-Object {
        $injectionKey = Get-NervStringCompositeKey -Components @($_.Resource, $_.EnvironmentName)
        -not $requiredPairs.Contains($injectionKey)
    }) -KeySelector { param($row) Get-NervStringCompositeKey -Components @($row.Resource, $row.EnvironmentName) } -Comparer ([StringComparer]::Ordinal))

    $exemptions = Read-NervAppHostBaseUrlExemption -Path $ExemptionPath
    $matchedExemptionKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $unexempted = [Collections.Generic.List[object]]::new()
    foreach ($violation in $violations) {
        $violationKey = Get-NervStringCompositeKey -Components @($violation.ConsumerResource, $violation.Key)
        $exemption = @($exemptions | Where-Object {
            [string]::Equals((Get-NervStringCompositeKey -Components @($_.ConsumerResource, $_.Key)), $violationKey, [StringComparison]::Ordinal)
        })
        if ($exemption.Count -eq 1 -and [string]::Equals([string] $exemption[0].Kind, $violation.Kind, [StringComparison]::Ordinal)) {
            [void] $matchedExemptionKeys.Add($violationKey)
            continue
        }
        $unexempted.Add($violation)
    }
    $staleExemptions = @($exemptions | Where-Object {
        $exemptionKey = Get-NervStringCompositeKey -Components @($_.ConsumerResource, $_.Key)
        -not $matchedExemptionKeys.Contains($exemptionKey)
    })

    return [pscustomobject]@{
        AppHostProgramPath = $AppHostProgramPath
        ExemptionPath = $ExemptionPath
        Resources = $resources
        Requirements = @(Get-NervRequirementsSorted -Requirements $requirements)
        Injections = $injections
        Violations = @($violations)
        UnexemptedViolations = @($unexempted)
        Exemptions = @($exemptions)
        StaleExemptions = $staleExemptions
        UnconsumedInjections = $unconsumedInjections
    }
}

function Read-NervAppHostBaseUrlExemption {
    <#
        豁免登记表。每条必须点名 (资源, 键, 违例种类)、给出理由并挂一张票。

        上界不是「不许有」，是三条同时成立：
          * 每条都必须**当下真的匹配一条违例** —— 销账后不删登记 ⇒ stale ⇒ 红。登记表只能随销账缩短；
          * 每条都必须带 `#<issue>` 形态的票号 ⇒ 无票新增写不出来；
          * 元组逐字匹配（资源 + 键 + 种类），没有通配、没有按资源整体豁免 ⇒ 一条登记只能盖一个格。

        ⚠️ 覆盖边界：本库**不**校验那张票真的存在或仍开着——那要联网查 GitHub，而 script-governance
        job 既无 token 也不该为一个注释性字段引入网络依赖。指向已关闭 issue 的登记本库抓不到；
        抓它的是那张票自己的验收条目。
    #>
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Base-url injection exemption registry '$Path' does not exist."
    }
    $document = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($null -eq $document.PSObject.Properties['exemptions']) {
        throw "Exemption registry '$Path' has no 'exemptions' array."
    }

    $entries = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in @($document.exemptions)) {
        foreach ($field in @('consumerResource', 'key', 'kind', 'tracking', 'reason')) {
            if ($null -eq $entry.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string] $entry.$field)) {
                throw "Exemption registry '$Path' has an entry with a missing or empty '$field'."
            }
        }
        if (-not [regex]::IsMatch([string] $entry.tracking, $script:NervAppHostBaseUrlTrackingPattern)) {
            throw "Exemption registry '$Path' has tracking '$($entry.tracking)', which is not of the form '#<issue>'."
        }
        if (-not (Get-NervStringSet -Values $script:NervAppHostBaseUrlViolationKinds -Comparer ([StringComparer]::Ordinal)).Contains([string] $entry.kind)) {
            throw "Exemption registry '$Path' has unknown kind '$($entry.kind)'."
        }
        if (-not $seen.Add("$($entry.consumerResource)`u{241F}$($entry.key)")) {
            throw "Exemption registry '$Path' registers '$($entry.consumerResource)' / '$($entry.key)' more than once."
        }
        $entries.Add([pscustomobject]@{
            ConsumerResource = [string] $entry.consumerResource
            Key = [string] $entry.key
            Kind = [string] $entry.kind
            Tracking = [string] $entry.tracking
            Reason = [string] $entry.reason
        })
    }
    return @($entries)
}
