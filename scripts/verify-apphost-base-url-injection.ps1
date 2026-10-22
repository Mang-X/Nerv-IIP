# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads infra/aspire/Nerv.IIP.AppHost/Program.cs, every .csproj in its project reference
#       closure, and the .cs sources owned by those projects
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
    Fails when a hosted service resolves a downstream base address the AppHost never injects, or
    injects as something other than that downstream resource's session endpoint.

.DESCRIPTION
    详细机制、需求集的推导方式与覆盖边界写在 scripts/lib/AppHostBaseUrlInjection.ps1 的头部注释里，
    这里只说这条门禁在干什么：

      需求集 = 托管项目闭包内每一处 `InternalServiceBaseAddress.Resolve*(…, "X:BaseUrl", …)`；
      注入集 = AppHost 里按语句归属到各资源的 `.WithEnvironment("X__BaseUrl", …)`；
      违例   = 需求没有对应注入（missing），或注入的不是提供方资源的 `GetEndpoint("http")`（wrong-value）。

    两类违例缺一不可。#3313 的失败形态是「连上了端口上碰巧存在的另一套栈」——只断言「在不在」的
    护栏对它零鉴别力，PR #3484 已经实测过一次：删掉当时新增的那条注入，既有的
    verify-aspire-apphost-environment-artifacts.ps1 仍然 EXIT=0。

    #3512 销账后 main 上零违例，scripts/apphost-base-url-injection-exemptions.json 的登记表是空的。
    登记表只能随销账缩短：一条登记不再匹配任何真实违例就变成 stale，本脚本照样红。

.PARAMETER RepositoryRoot
    仓库根。默认是本脚本的上级目录。

.PARAMETER AppHostProgramPath
    被读的 AppHost 源码。默认 <RepositoryRoot>/infra/aspire/Nerv.IIP.AppHost/Program.cs。
    单独开出来是为了让哨兵格能把变异过的副本喂进来，而闭包与消费方扫描仍走真实仓库。

.PARAMETER ExemptionPath
    豁免登记表。默认 <RepositoryRoot>/scripts/apphost-base-url-injection-exemptions.json。
#>

[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $AppHostProgramPath,
    [string] $ExemptionPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/AppHostBaseUrlInjection.ps1')

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$reportArguments = @{ RepositoryRoot = $RepositoryRoot }
if (-not [string]::IsNullOrWhiteSpace($AppHostProgramPath)) { $reportArguments['AppHostProgramPath'] = $AppHostProgramPath }
if (-not [string]::IsNullOrWhiteSpace($ExemptionPath)) { $reportArguments['ExemptionPath'] = $ExemptionPath }

$report = Get-NervAppHostBaseUrlInjectionReport @reportArguments

Write-Host "AppHost base address injection closure:"
Write-Host "  hosted project resources: $($report.Resources.Count)"
Write-Host "  base address requirements enumerated from consumer code: $($report.Requirements.Count)"
Write-Host "  base address injections parsed from the AppHost source: $($report.Injections.Count)"
Write-Host "  violations: $($report.Violations.Count) (exempted $($report.Violations.Count - $report.UnexemptedViolations.Count))"

$sortedExemptions = @(Get-NervItemsSortedByString -Items @($report.Exemptions) `
    -KeySelector { param($row) Get-NervStringCompositeKey -Components @($row.ConsumerResource, $row.Key) } `
    -Comparer ([StringComparer]::Ordinal))
foreach ($exemption in $sortedExemptions) {
    Write-Host "    exempted [$($exemption.Kind)] $($exemption.ConsumerResource) <- $($exemption.Key) (tracking $($exemption.Tracking)) — $($exemption.Reason)"
}

# 信息项，不参与判定：AppHost 注了但没有任何消费方 Resolve 的键。本门禁不替它做决定，也不假装
# 覆盖了它。#3512 判过其中两条——business-scheduling <- FileStorage__BaseUrl 与
# gateway <- VictoriaLogs__BaseUrl：消费方直读 Configuration["X:BaseUrl"] 而不走
# InternalServiceBaseAddress.Resolve\*，所以本库枚举不到（盲区见 #3536），各自在 AppHost 源码里
# 有注释点名消费者，删掉它们本门禁照绿、运行时才炸。
# ⚠️ 这句只覆盖那两条具名项，不覆盖下面这个循环打印出的每一行：本门禁不校验键名拼写，
# 一个拼错的键（如 Erpp__BaseUrl）同样会以「无消费方」的形态出现在这里，那是待查项不是已判项。
Write-Host "  injections with no consumer (informational only, not asserted): $($report.UnconsumedInjections.Count)"
foreach ($injection in $report.UnconsumedInjections) {
    Write-Host "    unconsumed $($injection.Resource) <- $($injection.EnvironmentName) (AppHost line $($injection.Line))"
}

$failures = [Collections.Generic.List[string]]::new()
foreach ($violation in $report.UnexemptedViolations) {
    $failures.Add("[$($violation.Kind)] $($violation.Detail)")
}
foreach ($exemption in $report.StaleExemptions) {
    $failures.Add("[stale-exemption] '$($exemption.ConsumerResource)' / '$($exemption.Key)' is registered as an exempted $($exemption.Kind) violation against $($exemption.Tracking), but no such violation exists any more. Delete the registration.")
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "AppHost base address injection check failed with $($failures.Count) finding(s):"
    foreach ($failure in $failures) { Write-Host "  FAIL $failure" }

    exit 1
}

Write-Host ''
Write-Host "Every one of the $($report.Requirements.Count) base addresses resolved by a hosted service is injected by the AppHost as that downstream resource's session endpoint, except the $($report.Exemptions.Count) registered exemption(s)."
