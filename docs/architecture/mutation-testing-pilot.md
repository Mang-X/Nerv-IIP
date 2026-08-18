# Stryker.NET 单程序集 mutation testing Spike

## 结论

NERV-870 的结论是**有限 go**：Stryker.NET 4.16.0 可以在当前 .NET 10、macOS arm64 环境中对 Scheduling 的单个纯领域文件稳定地产生非零 mutation 证据，且两次运行的 mutant 状态完全一致。它适合 NERV-873 与 NERV-874 继续做边界冻结的人工或显式 pilot；当前不应接入 required PR CI，也不应把 mutation score 设为 KPI。

本次 Spike 只调查工具成本与信号质量，没有修改生产代码、测试、项目文件、包版本、tool manifest、配置、workflow 或脚本。Stryker 的安装、运行目录和原始报告均位于一次性临时目录，未进入仓库。

## Gate 2 与范围

- 级别：`scope:spike`，timebox 为 1 个工程日。
- 交付：只新增本文档，并以一个 ready 文档 PR 独立审核。
- 生产目标：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/Services/OrderUrgencyCalculator.cs`。
- 直接测试项目：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj`。
- 直接测试类：`OrderUrgencyCalculatorTests`。
- 排除：全仓 mutation、补测试、修生产缺陷、CI 接线，以及 Scheduling 其他领域文件。

## 执行环境

| 项目 | 实测值 |
|---|---|
| 基线提交 | `0d6c040922ff89756b0f2697a88354b77db386c3` |
| 操作系统 | macOS 26.5，arm64 |
| .NET SDK | `10.0.302` |
| .NET runtime | `10.0.10` |
| Stryker.NET | `4.16.0` |
| Stryker 默认并发度 | `5`，来自本版本 `--help` |
| 构建配置 | `Debug` |

工具安装输出明确报告版本 `4.16.0`。本版本的 `-v|--version` 是 Dashboard 项目版本参数，并非查询工具版本的布尔开关；直接执行 `--version` 会以“Missing value for option 'version'”失败，因此本文不把该命令伪装成版本探测成功。

## 可复现命令

以下命令使用新的临时目录；`NERV870_TMP` 只指向本次运行拥有的目录。实际运行前先执行工具 `--help`，下列 mutation 参数均来自该帮助输出。

```bash
NERV870_TMP="$(mktemp -d "${TMPDIR%/}/nerv-870-stryker.XXXXXX")"

dotnet tool install dotnet-stryker \
  --tool-path "$NERV870_TMP/tools"

"$NERV870_TMP/tools/dotnet-stryker" --help

/usr/bin/time -p dotnet restore \
  backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj

/usr/bin/time -p dotnet test \
  backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~OrderUrgencyCalculatorTests \
  --logger 'console;verbosity=normal'

/usr/bin/time -p "$NERV870_TMP/tools/dotnet-stryker" \
  --test-project backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj \
  --project Nerv.IIP.Business.Scheduling.Domain.csproj \
  --mutate '**/OrderUrgencyCalculator.cs' \
  --reporter Json \
  --output "$NERV870_TMP/results" \
  --configuration Debug \
  --skip-version-check \
  --break-on-initial-test-failure \
  --verbosity info
```

第二次稳定性运行使用完全相同的参数，只把 `--output` 改为另一个临时子目录，避免覆盖首轮原始报告。

## 基线与 mutation 结果

外部 restore 墙钟为 `3.05s`。随后精确筛选 `OrderUrgencyCalculatorTests` 的基线命令使用 `--no-restore`，结果为 `15 passed / 0 failed / 0 skipped`；测试运行耗时 `0.6502s`，进程墙钟 `2.83s`。

Stryker 的两轮结果如下：

| 指标 | 第 1 轮 | 第 2 轮 |
|---|---:|---:|
| Stryker 内部耗时 | `30.4765s` | `28.0670s` |
| 进程墙钟 | `31.13s` | `28.35s` |
| 测试项目发现测试数 | 36 | 36 |
| 全生产项目生成 mutants | 494 | 494 |
| 被 `--mutate` 过滤 | 381 | 381 |
| 实际执行 mutants | 84 | 84 |
| killed | 53 | 53 |
| survived | 31 | 31 |
| no coverage | 13 | 13 |
| timeout | 0 | 0 |
| compile error（控制台，全分析阶段） | 6 | 6 |
| ignored（block filter） | 10 | 10 |
| mutation score | `54.64%` | `54.64%` |

两轮进程墙钟相差 `2.78s`，按较慢一轮计约 `8.9%`；两份 JSON 中的 mutant `id/status` 映射完全一致。相对 `2.83s` 的精确基线测试，mutation 的平均墙钟约为 `10.5x`。

计数需要区分两个口径：控制台的 `494` 是先分析整个生产项目后生成的总数；精确目标文件的 JSON 有 `112` 条记录，分解为 `53 killed + 31 survived + 13 no coverage + 5 compile error + 10 ignored`。控制台在应用文件过滤前另外计入 1 个不出现在目标文件 JSON 中的 compile error，因此控制台为 6。本文同时保留两个事实，不用其中一个覆盖另一个。用于 score 的分母是 `53 + 31 + 13 = 97`，不是 494，也不是 112。

## 信号质量

### 已杀死的代表 mutation

- `Max()` 改为 `Min()`：会破坏业务优先级、时间紧迫度和执行风险取最高严重级的规则，被现有测试杀死。
- `availableHours - remainingHours` 改为加法：会破坏 slack 计算，被现有测试杀死。
- 阻断风险的 `Any()` 改为 `All()`：会让混合风险集合漏报高风险，被现有测试杀死。

这些 mutation 保持方法和返回对象形状不变，但破坏领域语义，说明工具在该目标上能够产生有价值的信号。

### 31 个 survivor 的分类

本次不修改测试，只根据源码、现有断言和 mutation replacement 分类。分类以 Stryker 报告的 mutant id 为定位证据。

**确认或高度可信的覆盖缺口：28 个。**

- 输入与输出边界：`391`（零剩余周期边界）、`396`–`400`（`BusinessReference` 的空白回退与裁剪）。
- 过期业务优先级：`402`、`406`、`409`（未来/到期时间、过期降级和过期原因）。
- 时间计算输出：`413`（预计完成时间）、`420`（零周期 CR）、`423`、`424`（预计延期）。
- 阈值与解释原因：`426`（恰好到期）、`432`（零 slack）、`435`、`436`（负 slack 原因）、`440`（CR 恰好 1）、`447`、`449`（高风险与一班次边界）、`456`、`459`（Attention 与 CR 1.2 边界）、`465`、`466`（承诺内原因）。
- 非阻断执行风险与空风险解释：`482`、`483`、`487`、`488`。

这些项都能改变公开结果值、级别或解释原因，不能当作等价 mutation 丢弃。后续应按领域边界聚类建精确测试 issue，而不是为每个 Stryker id 机械建票。

**合同尚未冻结、可能只是低价值内部细节：3 个。**

- `383`：移除显式 null guard 后仍会由后续解引用失败；差别主要是异常类型和参数名。只有在该方法的参数校验异常形状属于承诺时才值得补精确测试。
- `468`、`469`：改变 `ExecutionRiskContribution.Facts` 的排序方向，但稳定排序后的 `ReasonCodes` 不变。若 `Facts` 顺序是公开可复现合同则属于真缺口；若调用方只依赖集合内容，则属于不应追逐的内部排序细节。

**确认等价 mutation：0 个。**

抽查全部 31 个 survivor 后，没有找到能在所有合法输入上证明等价的 mutation。几个看似冗余的条件替换仍会在零周期、阈值相等、过期或空白输入上改变结果。没有证明时不得把 survivor 标成等价来抬高分数。

另外 13 个 no-coverage 主要落在非法输入异常消息、过期原因、缺失 due、overdue/attention 原因等分支。它们没有进入“survived”，但同样说明 Stryker 的覆盖采集能暴露未执行路径。

## 工具限制与接入成本

- restore/build：基线可明确执行一次 restore 后用 `dotnet test --no-restore`；Stryker 4.16.0 的帮助没有提供 `--no-restore`，运行时实际调用 `dotnet build ... -c Debug`。因此 mutation run 不能声称完全离线或不触发隐式 restore，CI 接入前必须验证锁文件、NuGet 可用性和缓存语义。
- 测试选择器：本版本帮助没有暴露 test-case filter。`--mutate` 只把生产 mutation 限定到一个文件，Stryker 仍发现整个直接测试项目的 36 个测试，再由 coverage-based test optimization 选择 mutant 对应测试。不能把 baseline 的 15 个精确测试等同于 mutation run 只发现 15 个测试。
- 源生成：生产项目引用 `NetCorePal.Extensions.CodeAnalysis`；Stryker 对该项目的分析、构建和 36 个测试成功，说明当前程序集级源生成没有阻塞本次运行。
- 强类型 ID：`OrderUrgencyCalculator` 本身使用字符串和普通 record，没有直接 mutation 强类型 ID 生成代码。本次成功不能外推为强类型 ID mutation 已验证；NERV-873/874 若选中生成类型，必须单独保留编译与 mutant 证据。
- 报告：原始 JSON 包含本机仓库绝对路径，不适合直接提交。此次只在临时目录保留；未来若要上传 artifact，需要先定义脱敏、保留期和缺失失败策略。
- 版本：不固定版本的 `dotnet tool install` 会随时间漂移。后续 pilot 必须固定 `dotnet-stryker` 为 `4.16.0`，升级另行取证。
- 统计：compile error 不进入 mutation score，文件过滤又发生在项目分析之后；报告必须同时给出生成、过滤、执行和评分口径，禁止只报一个百分比。

## CI 结论

当前对 required PR CI 是 **no-go**。原因不是本机耗时过高，而是尚缺少受治理的工具版本固定、无网络/restore 策略、原始报告脱敏、artifact 生命周期、预算超时与失败关闭合同；并且只有一台 macOS arm64 机器上的两轮证据，不能代表 GitHub hosted runner 成本。

可以把它作为显式的本地或手动 pilot：单文件、单测试项目、固定版本、临时输出、两轮状态一致，并以 mutant 分类而非 score 作为决策输入。若未来要进入 CI，应另开治理票，而不是在 NERV-873/874 的领域 pilot 中顺手修改 workflow 或脚本。

## NERV-873 与 NERV-874 前置建议

### NERV-873 Scheduling pilot

- **go**，冻结生产目标为 `OrderUrgencyCalculator.cs`，直接测试项目为 `Nerv.IIP.Business.Scheduling.Domain.Tests.csproj`，工具版本固定为 `4.16.0`。
- 单次 mutation 墙钟预算先定为 2 分钟；超时直接失败并保留诊断，不自动扩大目标或测试项目。该预算约为本机较慢实测的 `3.9x`，仅用于 pilot，不代表 CI SLA。
- 先用已杀死的“严重级 `Max()` 改 `Min()`”作为保持输出形状但破坏领域语义的 red 证据。
- 对 28 个高可信缺口按输入校验、业务优先级过期、时间阈值/输出、执行风险四组复核并开精确后续测试 issue；对 3 个合同未冻结项先裁决合同，不为抬分数补脆弱断言。
- 全部原始报告继续留在临时或受治理 artifact，PR 只提交报告与票面明确允许的测试变更，不修改 CI。

### NERV-874 Inventory pilot

- **条件 go**：先在 ledger、idempotency、FEFO 三者中只选一个纯领域或无外部依赖的小组件，并冻结一个生产文件和一个直接测试项目；不得把本次 Scheduling 的 30 秒成本外推到 PostgreSQL 测试。
- 首选能由纯领域测试证明的 FEFO 顺序或幂等策略；若目标不变量必须依赖 PostgreSQL，票面必须明确 real-provider 证据、独立数据库归属与清理责任，InMemory 结果不得冒充数据库约束。
- 首次单次 mutation 墙钟预算定为 5 分钟，连续两轮要求 mutant 状态计数完全一致；超预算、零执行、skip、timeout 或清理失败均 fail closed。
- 版本、restore、测试发现、报告脱敏与统计口径沿用本报告，不接触 workflow、共享脚本或全仓 mutation。

## 未验证项

- 未在 GitHub hosted runner、Linux、Windows 或 CI 中运行。
- 未运行整个 backend solution，也未运行 Scheduling Web tests、PostgreSQL lane 或 FullChain。
- 未验证 Dashboard、baseline、HTML、GitLab 等 reporter。
- 未验证真实强类型 ID mutation、Inventory 目标、PostgreSQL mutation 或外部依赖清理。
- 未验证 Stryker 4.16.0 以外版本。
