# NERV-183 定时性能回归门禁实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立每天自动运行、可人工验证、使用真实 PostgreSQL、按阈值失败并归档 JSONL 指标的业务性能门禁。

**Architecture:** 新增独立 GitHub Actions workflow，复用现有 `scripts/verify-business-performance-baseline.ps1`，不改变普通 PR CI。先用 PowerShell 结构合同及削弱变异锁定触发器、真实依赖、失败传播、阈值与 artifact，再在 GitHub hosted runner 上多次校准并执行 1 ms 失败探针。

**Tech Stack:** GitHub Actions、PowerShell 7、Ruby YAML parser、PostgreSQL 18、.NET 10、xUnit、JSONL、`actions/upload-artifact@v4`。

## Global Constraints

- Scope 为 M，单 PR；不修改业务实现、数据库 schema、HTTP endpoint、OpenAPI、generated client 或前端。
- workflow 只响应每天一次的 `schedule` 和 `workflow_dispatch`；不得加入 `pull_request` 或普通 `push`。
- 真实依赖必须是 `postgres:18` service container；`NERV_IIP_PERF_POSTGRES` 必须由 workflow 显式注入。
- 性能执行只调用 `scripts/verify-business-performance-baseline.ps1`；不得在 workflow 直接运行 `dotnet`、`docker` 或复制脚本业务逻辑。
- scheduled run 使用非零分场景阈值；人工输入大于 0 时只使用该全局阈值，以便 `1 ms` 失败探针不被分场景阈值覆盖。
- 不得使用 `continue-on-error`、`|| true` 或其他失败吞噬手段。
- artifact 仅包含 `metrics.jsonl` 与 `summary.json`，使用 `if: always()`、`if-no-files-found: error`，保留 30 天；不得上传 raw TRX、stdout/stderr、数据库内容或凭据。
- 所有新增人工文档和协作文本使用简体中文；代码、命令、路径、标识符、配置键和运行证据保持原样。
- NERV-423/NERV-688 的全量 `*PostgresProfileTests` lane 不在本任务范围，不得宣称被本门禁替代或完成。

---

### Task 1: 用失败合同锁定 nightly workflow

**Files:**
- Create: `scripts/tests/nightly-business-performance-workflow.Tests.ps1`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `scripts/lib/ScriptAutomation.ps1` 的 `Invoke-NativeCommandOutput` 与仓库 Ruby 3.4 YAML/JSON 解析约定。
- Produces: `Assert-NightlyBusinessPerformanceWorkflow -Path [string]`，对真实或临时 workflow 返回时无输出，合同违反时抛出精确错误。
- Produces: Script Governance job 中名为 `Test nightly business performance workflow` 的 5 分钟 step。

- [ ] **Step 1: 编写缺少 workflow 时失败的合同测试**

创建带 Script-Governance header 的 PowerShell 测试。解析函数使用仓库既有方式：

```powershell
$rubyProgram = "require 'yaml'; require 'json'; puts JSON.generate(YAML.safe_load(File.read(ARGV.fetch(0))))"
$parsed = Invoke-NativeCommandOutput `
    -Command 'ruby' `
    -Arguments @('-ryaml', '-rjson', '-e', $rubyProgram, $Path) `
    -WorkingDirectory $repoRoot `
    -Name 'parse-nightly-business-performance-workflow'
$workflow = $parsed.Stdout | ConvertFrom-Json -ErrorAction Stop
```

`Assert-NightlyBusinessPerformanceWorkflow` 必须结构化检查两个触发器、只读权限、唯一 job、job/step timeout、PostgreSQL service 与 health check、四个固定 action 版本、受治理脚本参数、人工阈值分支、三个非零分场景阈值路径、artifact allowlist 和失败传播禁令。

- [ ] **Step 2: 运行测试并观察预期红灯**

Run:

```powershell
pwsh scripts/tests/nightly-business-performance-workflow.Tests.ps1
```

Expected: 非零退出，错误明确指出 `.github/workflows/nightly-business-performance.yml` 不存在。

- [ ] **Step 3: 增加削弱变异**

测试把真实文本写入 operating-system temp directory，并逐项替换：删除 `NERV_IIP_PERF_POSTGRES`、把一个 scheduled 阈值改为 `0`、删除 artifact 的 `if: always()`、把 `if-no-files-found` 改为 `warn`、添加 `continue-on-error: true`、在执行行追加 `|| true`。每个变异必须确认替换命中且合同失败，最后在 `finally` 删除自有临时目录。

- [ ] **Step 4: 把合同测试接入 Script Governance**

在 `.github/workflows/ci.yml` 的 `script-governance` job 添加：

```yaml
      - name: Test nightly business performance workflow
        timeout-minutes: 5
        shell: pwsh
        run: ./scripts/tests/nightly-business-performance-workflow.Tests.ps1
```

同时把该 job 上方中文说明中的显式 step 数从 12 改为 13、step 预算合计从 58m 改为 63m；job 仍为无 `if: always()` evidence step 的层级 B，`timeout-minutes: 10` 不变。

- [ ] **Step 5: 再次运行并确认仍因生产 workflow 缺失而红**

Run:

```powershell
pwsh scripts/tests/nightly-business-performance-workflow.Tests.ps1
```

Expected: 与 Step 2 相同的缺文件失败，证明 CI wiring 没有制造假绿。

- [ ] **Step 6: 提交 RED 证据**

```bash
git add scripts/tests/nightly-business-performance-workflow.Tests.ps1 .github/workflows/ci.yml
git commit -m "test(ci): 锁定 NERV-183 nightly 性能合同"
```

---

### Task 2: 实现独立 scheduled performance workflow

**Files:**
- Create: `.github/workflows/nightly-business-performance.yml`
- Test: `scripts/tests/nightly-business-performance-workflow.Tests.ps1`

**Interfaces:**
- Consumes: `scripts/verify-business-performance-baseline.ps1` 参数 `Scenario`、`Profile`、`Rows`、`MaxElapsedMilliseconds`、`InventoryMaxElapsedMilliseconds`、`MesMaxElapsedMilliseconds`、`ErpMaxElapsedMilliseconds`、`MetricsOutputPath`、`SummaryOutputPath`。
- Produces: workflow `Nightly Business Performance`，job ID `business-performance`，artifact `business-performance-${{ github.run_id }}-${{ github.run_attempt }}`。
- Produces: `artifacts/business-performance/nightly/metrics.jsonl` 和 `artifacts/business-performance/nightly/summary.json`。

- [ ] **Step 1: 新增最小 workflow**

使用每天 `17:00 UTC`（北京时间次日 `01:00`）的 `cron: '0 17 * * *'`，并定义 `workflow_dispatch.inputs.max_elapsed_milliseconds` 为 string、默认 `0`。job 使用 `ubuntu-latest`、`timeout-minutes: 45`，PostgreSQL service 使用数据库 `nerv_iip_performance`、用户与密码 `nerv`，端口 `5432:5432`，health option 为 `pg_isready -U nerv -d nerv_iip_performance`。

显式 step 预算为 checkout 3m、setup 5m、cache 8m、performance 20m、artifact 5m，总计 41m，小于 job 45m。初始校准阈值均为 `600000` ms。执行 step 根据 `$env:MANUAL_MAX_ELAPSED_MILLISECONDS` 二选一：大于 0 时仅传 `-MaxElapsedMilliseconds`；否则只传三个分场景阈值。共同参数固定为 `-Scenario all -Profile nightly -Rows 25` 和两条显式输出路径。

- [ ] **Step 2: 运行合同测试并观察绿色**

Run:

```powershell
pwsh scripts/tests/nightly-business-performance-workflow.Tests.ps1
```

Expected: 退出码 0，所有真实结构与六种削弱变异通过。

- [ ] **Step 3: 运行相关治理门禁**

Run:

```powershell
pwsh scripts/check-script-governance.ps1
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/backend-test-shards.Tests.ps1
git diff --check
```

Expected: 全部退出码 0；workflow budget 识别新 workflow 的 41m < 45m evidence-publishing 预算；无 raw evidence upload 或 `continue-on-error`。

- [ ] **Step 4: 提交 GREEN 实现**

```bash
git add .github/workflows/nightly-business-performance.yml
git commit -m "feat(ci): 接入 nightly 业务性能门禁"
```

---

### Task 3: 更新架构状态并完成本地复核

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Test: `scripts/tests/nightly-business-performance-workflow.Tests.ps1`

**Interfaces:**
- Consumes: Task 2 的 workflow 名称、artifact 路径和边界。
- Produces: 第 47 条性能基线状态的当前事实，不把尚未执行的远端 run 写成已通过。

- [ ] **Step 1: 更新第 47 条**

补充独立 workflow 路径、每天 17:00 UTC、人工触发、PostgreSQL 18、JSONL/summary 30 天 artifact、分场景非零阈值与 1 ms 失败探针机制；明确它不替代 NERV-423/NERV-688。

- [ ] **Step 2: 运行本地收口验证**

Run:

```powershell
pwsh scripts/tests/nightly-business-performance-workflow.Tests.ps1
pwsh scripts/check-script-governance.ps1
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/backend-test-shards.Tests.ps1
git diff --check
```

Expected: 全部退出码 0。

- [ ] **Step 3: 提交文档**

```bash
git add docs/architecture/implementation-readiness.md
git commit -m "docs(ci): 记录 nightly 性能门禁边界"
```

---

### Task 4: 独立审核、远端校准与 PR 交付

**Files:**
- Modify after calibration: `.github/workflows/nightly-business-performance.yml`
- No committed runtime artifacts.

**Interfaces:**
- Consumes: pushed branch、GitHub Actions run artifact、Task 2 的初始 `600000` ms 阈值。
- Produces: 校准后的三个分场景阈值、至少三次 calibration run、一次 1 ms failure run、一次 final success run、PR 与 Linear 验收记录。

- [ ] **Step 1: 独立审核全部 branch diff**

审核者必须检查 spec compliance 与代码质量，重点验证真实 PostgreSQL、非 skip、失败传播、artifact allowlist、timeout budget 和 NERV-423/NERV-688 边界。所有 Critical/Important finding 进入修复与独立复审循环。

- [ ] **Step 2: 推送分支并创建 draft PR**

```bash
git push -u origin codex/nerv-183-nightly-performance
gh pr create --draft --base main --head codex/nerv-183-nightly-performance --title "feat(ci): NERV-183 接入 nightly 业务性能门禁" --body-file /tmp/nerv-183-pr-body.md
```

PR 正文使用中文，说明产品文档无影响、未修改业务 endpoint/facade matrix，并分别列出本地与远端证据。

- [ ] **Step 3: 连续触发三次初始校准运行**

```bash
gh workflow run nightly-business-performance.yml --ref codex/nerv-183-nightly-performance
```

每次等待终态、下载 artifact 并核对三个场景各一行、`passed=true`。对每个场景计算：

```text
calibratedThresholdMs = max(30000, ceil((2 * maxObservedMs) / 1000) * 1000)
```

把三个结果写回 scheduled 分场景阈值，提交并推送。

- [ ] **Step 4: 执行失败探针**

```bash
gh workflow run nightly-business-performance.yml --ref codex/nerv-183-nightly-performance -f max_elapsed_milliseconds=1
```

Expected: run conclusion 为 failure；下载 artifact 后 `summary.json.passed=false`、`violations` 非空，且 `metrics.jsonl` 包含 Inventory、MES、ERP 三行。

- [ ] **Step 5: 执行最终成功运行**

```bash
gh workflow run nightly-business-performance.yml --ref codex/nerv-183-nightly-performance
```

Expected: run conclusion 为 success；artifact 中三个场景齐全、summary `passed=true`，证明不是 skip。

- [ ] **Step 6: 更新 PR 和 Linear，转为等待审核**

把 calibration runs、失败探针、最终成功 run、artifact 名称、校准公式与最终阈值写入 PR 正文和 NERV-183 中文评论。将 draft PR 转为 ready；保持 Linear 为 In Progress 或团队约定的审核中状态，不标记 Done，不合并 PR。

### Task 5: 审核后输入边界与治理声明收口

**Files:**
- Modify: `.github/workflows/nightly-business-performance.yml`
- Modify: `scripts/tests/nightly-business-performance-workflow.Tests.ps1`
- Modify: `scripts/verify-business-performance-baseline.ps1`
- Modify: `docs/architecture/implementation-readiness.md`

**Interfaces:**
- Consumes: `workflow_dispatch.inputs.max_elapsed_milliseconds` 的字符串值。
- Produces: invariant integer 的三态语义——`0` 使用定时分场景阈值、`>0` 仅使用人工全局阈值、无法解析或 `<0` 显式失败。

- [ ] **Step 1: 写入负数输入的 RED 行为合同**

在 `scripts/tests/nightly-business-performance-workflow.Tests.ps1` 中执行真实 workflow 的 performance run block，但把最终 verifier 调用替换为记录参数的测试函数。以 `MANUAL_MAX_ELAPSED_MILLISECONDS=-1` 执行时，断言脚本抛出包含 `must be greater than or equal to 0` 的确定性错误，且测试 verifier 未被调用。

- [ ] **Step 2: 运行合同并确认预期 RED**

```powershell
pwsh scripts/tests/nightly-business-performance-workflow.Tests.ps1
```

Expected: FAIL，因为当前 `-1` 会进入 scheduled 分支并调用 verifier。

- [ ] **Step 3: 实现最小输入解析与校验**

在 performance step 中用 `[int]::TryParse(..., [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]...)` 解析输入；解析失败时显式 throw，小于 `0` 时显式 throw，后续分支仅使用解析后的整数。

- [ ] **Step 4: 同步治理声明与工程文档**

在 `scripts/verify-business-performance-baseline.ps1` 的 `Writes` 中声明 `-MetricsOutputPath`/`-SummaryOutputPath` 指定路径；在 `implementation-readiness.md` 记录人工输入三态语义与合并后校准验收条件，不宣称 hosted run 已执行。

- [ ] **Step 5: 运行相关门禁**

```powershell
pwsh scripts/tests/nightly-business-performance-workflow.Tests.ps1
pwsh scripts/tests/business-performance-metrics-completeness.Tests.ps1
pwsh scripts/check-script-governance.ps1
pwsh scripts/tests/ordinal-comparison-layers.Tests.ps1
pwsh scripts/tests/test-evidence.Tests.ps1
pwsh scripts/tests/backend-test-shards.Tests.ps1
```

Expected: 全部 exit 0；随后运行 `git diff --check`。

- [ ] **Step 6: 更新 Linear 验收记录并推送 PR**

在 NERV-183 明确登记：默认分支合并后至少三次校准、`1 ms` 失败探针、正式阈值成功 run，以及三个阈值之和必须明显低于性能 step 预算（否则同步调整预算）。保持 Issue 为 In Progress，直到 hosted 验收全部完成。
