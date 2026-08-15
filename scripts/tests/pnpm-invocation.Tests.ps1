# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs Resolve-PnpmInvocation normalization unit checks (no process launch)
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$root = "$repoRoot"
$frontend = [System.IO.Path]::GetFullPath((Join-Path $root 'frontend'))

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [string] $Case,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Expected,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Actual
    )

    if ((-not [string]::Equals([string]($Actual), [string]($Expected), [StringComparison]::Ordinal))) {
        throw "[$Case] expected '$Expected' but got '$Actual'."
    }
}

# 1. -C <dir>：cwd 对齐到目标目录，参数对被剔除，其余参数保留。
$case = Resolve-PnpmInvocation -Arguments @('-C', 'frontend', '--filter', '@x/y', 'typecheck') -WorkingDirectory $root
Assert-Equal -Case 'dash-C aligns cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'dash-C strips pair' -Expected '--filter @x/y typecheck' -Actual ($case.Arguments -join ' ')

# 2. --dir <dir>：同 -C。
$case = Resolve-PnpmInvocation -Arguments @('--dir', 'frontend', 'install') -WorkingDirectory $root
Assert-Equal -Case 'double-dash-dir aligns cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'double-dash-dir strips pair' -Expected 'install' -Actual ($case.Arguments -join ' ')

# 3. --dir=<dir>：同 -C。
$case = Resolve-PnpmInvocation -Arguments @('--dir=frontend', 'install') -WorkingDirectory $root
Assert-Equal -Case 'dir-equals aligns cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'dir-equals strips flag' -Expected 'install' -Actual ($case.Arguments -join ' ')

# 4. 小写 -c 属于下游命令（如 playwright test -c），不得被消费。
$case = Resolve-PnpmInvocation -Arguments @('exec', 'playwright', 'test', '-c', 'playwright.config.ts') -WorkingDirectory $frontend
Assert-Equal -Case 'lowercase -c untouched cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'lowercase -c untouched args' -Expected 'exec playwright test -c playwright.config.ts' -Actual ($case.Arguments -join ' ')

# 5. -- 之后的参数属于下游命令，--dir 不得被吞、cwd 不受影响。
$case = Resolve-PnpmInvocation -Arguments @('run', 'x', '--', '--dir=foo', '-C', 'bar') -WorkingDirectory $frontend
Assert-Equal -Case 'double-dash truncation cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'double-dash truncation args' -Expected 'run x -- --dir=foo -C bar' -Actual ($case.Arguments -join ' ')

# 6. 未显式传 WorkingDirectory：默认 <repoRoot>/frontend。
$case = Resolve-PnpmInvocation -Arguments @('generate:api')
Assert-Equal -Case 'default frontend cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'default frontend args' -Expected 'generate:api' -Actual ($case.Arguments -join ' ')

# 7. 多次 -C/--dir：各自基于原始 cwd 解析，末者胜（对齐 pnpm“解析完参数再一次性切目录”的语义）。
$case = Resolve-PnpmInvocation -Arguments @('-C', 'docs', '--dir', 'frontend', 'install') -WorkingDirectory $root
Assert-Equal -Case 'last dir wins cwd' -Expected $frontend -Actual $case.WorkingDirectory
Assert-Equal -Case 'last dir wins args' -Expected 'install' -Actual ($case.Arguments -join ' ')

# 8. 绝对路径目标直接采用。
$case = Resolve-PnpmInvocation -Arguments @('-C', $frontend, 'install') -WorkingDirectory $root
Assert-Equal -Case 'absolute target cwd' -Expected $frontend -Actual $case.WorkingDirectory

Write-Host 'pnpm invocation normalization tests passed.'
