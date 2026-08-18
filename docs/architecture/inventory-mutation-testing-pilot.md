# Inventory StockLedger mutation testing 试点

## 结论

NERV-874 在纯领域边界上完成了 Stryker.NET 4.16.0 pilot。目标只包含
`StockLedger.cs` 与其现有 Domain.Tests 项目，不依赖 EF Core、PostgreSQL、
Npgsql、DbContext 或任何 provider。两轮初始 mutation 与两轮补测试后 mutation
都在单轮 2 分钟硬预算内完成，同一阶段的 mutant `id/status` 映射完全一致。

本次只补一条高价值账本边界测试：未预留出库可以恰好耗尽全部可用库存，
同时不得侵占已预留库存。Stryker mutant `236` 把生产条件
`nextOnHand < ReservedQuantity` 改为 `nextOnHand <= ReservedQuantity`；它在补测试前
连续两轮 `Survived`，补测试后连续两轮 `Killed`。这构成可审计的 red-green 证据，
且生产代码未修改。

## 冻结范围

- 基线提交：`05596a5053dba76800c8b236cec26771fe69ae8d`
- 生产目标：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- 直接测试项目：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj`
- 测试文件：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- mutation 选择器：`**/StockLedger.cs`
- Stryker.NET：`4.16.0`
- 单轮 mutation 墙钟预算：`120s`，超时后终止本次进程组并以退出码 `124` 失败关闭
- 排除：生产代码、项目/包配置、tool manifest、workflow、共享脚本、Web、Infrastructure、数据库与其它测试项目

## 执行环境与命令

| 项目 | 实测值 |
| --- | --- |
| 操作系统 | macOS 26.5.2，arm64 |
| .NET SDK | 10.0.302 |
| .NET runtime | 10.0.10 |
| Stryker 默认并发度 | 5，来自本版本 `--help` |
| 构建配置 | Debug |

工具、输出和原始 JSON 都位于本次拥有的临时目录
`/tmp/nerv-874-stryker.UBqRX5`，未进入仓库。实际先执行：

```bash
dotnet tool install dotnet-stryker \
  --version 4.16.0 \
  --tool-path /tmp/nerv-874-stryker.UBqRX5/tools

/tmp/nerv-874-stryker.UBqRX5/tools/dotnet-stryker --help

dotnet restore \
  backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj

dotnet test \
  backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~InventoryAggregateTests \
  --logger 'console;verbosity=normal'
```

每轮 mutation 都由外层进程组预算器强制限制为 120 秒，内部命令保持不变，
仅为每轮指定独立的 `--output`：

```bash
/tmp/nerv-874-stryker.UBqRX5/tools/dotnet-stryker \
  --test-project backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj \
  --project Nerv.IIP.Business.Inventory.Domain.csproj \
  --mutate '**/StockLedger.cs' \
  --reporter Json \
  --output /tmp/nerv-874-stryker.UBqRX5/results-<phase>-<run> \
  --configuration Debug \
  --skip-version-check \
  --break-on-initial-test-failure \
  --verbosity info
```

Stryker 4.16.0 没有 test-case filter；`--mutate` 只限制生产文件。初始两轮均发现
整个直接测试项目的 26 条测试，补测试后两轮均发现 27 条测试。

## 基线与 mutation 结果

显式 restore 墙钟为 `0.90s`。初始精确 baseline 为
`26 passed / 0 failed / 0 skipped`，测试执行 `0.6218s`，进程墙钟 `1.61s`。
新增测试单独运行结果为 `1 passed / 0 failed / 0 skipped`，测试执行 `0.6400s`，
进程墙钟 `1.61s`。

| 指标 | 初始第 1 轮 | 初始第 2 轮 | 补测试后第 1 轮 | 补测试后第 2 轮 |
| --- | ---: | ---: | ---: | ---: |
| Stryker 内部耗时 | `29.9193s` | `28.7920s` | `33.7410s` | `40.0089s` |
| 进程墙钟 | `30.24s` | `29.04s` | `34.03s` | `40.32s` |
| 外层预算器观测 | `30.30s` | `29.09s` | `34.07s` | `40.37s` |
| 全生产项目生成 mutants | 673 | 673 | 673 | 673 |
| 被 `--mutate` 过滤 | 412 | 412 | 412 | 412 |
| 目标文件 mutants | 261 | 261 | 261 | 261 |
| 实际执行 mutants | 171 | 171 | 171 | 171 |
| killed | 102 | 102 | 103 | 103 |
| survived | 69 | 69 | 68 | 68 |
| no coverage | 57 | 57 | 57 | 57 |
| timeout | 0 | 0 | 0 | 0 |
| compile error | 4 | 4 | 4 | 4 |
| ignored（block filter） | 29 | 29 | 29 | 29 |
| mutation score | `44.74%` | `44.74%` | `45.18%` | `45.18%` |
| 同阶段 `id/status` 映射 | 完全一致 | 完全一致 | 完全一致 | 完全一致 |

计数口径必须分开：Stryker 先分析整个生产项目并生成 673 个 mutant，随后按文件过滤
412 个。目标文件 JSON 有 257 条记录，另有控制台报告的 4 个 compile error，合计
261。用于 score 的分母是 `killed + survived + no coverage`；compile error、ignored
和文件过滤项不进入 score。本票不把 score 当作 KPI。

## red-green 证据

新增测试
`Unreserved_outbound_can_consume_all_available_quantity_without_touching_reserved_stock`
构造 `onHand=10`、`reserved=8` 后执行 `outbound=-2`，断言终态为
`onHand=8`、`reserved=8`、`available=0`。

| 阶段 | mutant `236` | 精确替换 | 结论 |
| --- | --- | --- | --- |
| 补测试前两轮 | `Survived` | `nextOnHand <= ReservedQuantity` | 既有测试无法发现恰好耗尽可用库存被错误拒绝 |
| 新测试本身 | `Passed` | 生产条件保持 `<` | 正确合同允许边界操作 |
| 补测试后两轮 | `Killed` | `nextOnHand <= ReservedQuantity` | 新测试在 Stryker 注入错误条件时变红 |

## 68 个 survivor 的完整分类

补测试后剩余 68 个 survivor；按 mutant ID 全量分类如下。分类依据是公开领域结果、
生产前置条件和现有异常合同，不为抬高分数追逐内部细节。

| 分类 | 数量 | mutant ID | 处置 |
| --- | ---: | --- | --- |
| 真覆盖缺口：movement 幂等作用域 | 2 | `211`、`212` | NERV-890 |
| 真覆盖缺口：数量与估值边界 | 5 | `226`、`264`、`279`、`293`、`334` | NERV-891 |
| 真覆盖缺口：盘点冻结、版本与事件 | 12 | `248`、`251`、`253`、`270`–`272`、`284`–`286`、`298`–`300` | NERV-892 |
| 真覆盖缺口：movement 维度拒绝 | 8 | `357`–`360`、`372`、`373`、`375`、`376` | NERV-893 |
| 真覆盖缺口：reservation 维度拒绝 | 15 | `258`、`275`、`289`、`382`、`384`–`394` | NERV-894 |
| 真覆盖缺口：效期来源空白归一化 | 1 | `413` | NERV-895 |
| 确认等价 | 4 | `213`、`234`、`330`、`338` | 不建追分票 |
| 内部实现细节或未冻结的异常形状 | 21 | `177`–`184`、`207`、`223`、`230`、`239`、`243`、`249`、`257`、`268`、`274`、`283`、`288`、`297`、`380` | 不建追分票 |

等价项理由：`213` 的 organization/environment 判定已由调用前的完整维度检查保证；
`234` 与 `330` 只改变零数量分支，而 `StockMovement` 构造已拒绝零数量；`338` 只改变
入库后 `nextOnHand == 0` 的分支，而有效入库在非负台账上必然得到正数。

内部细节项分三类：`177`–`184` 是 EF 私有构造路径的属性默认初始化；`207`、`257`、
`274`、`288` 只改变 null 输入的精确异常形状；其余 ID 只清空异常文案。当前公开合同没有
冻结这些形状，且本票明确不进入 provider，因此不补脆弱断言。

## 验证

- 最终直接测试项目：`27 passed / 0 failed / 0 skipped`，测试执行 `1.0986s`，
  进程墙钟 `2.96s`。
- 受影响 fast lane `business-core-a`：11 个分类项目均产生 TRX，合计
  `1441 passed / 0 failed / 0 skipped`，进程墙钟 `93.08s`。
- 额外执行但不作为受影响 lane 替代证据的 `business-core-b`：6 个分类项目均产生 TRX，
  合计 `676 passed / 0 failed / 0 skipped`，进程墙钟 `112.01s`。
- `dotnet format --verify-no-changes` 与 `git diff --check` 在最终 diff 上通过。

## 未验证项

- 未运行 PostgreSQL、EF InMemory、Web、FullChain 或任何真实依赖；目标不需要这些证据。
- 未在 GitHub hosted runner、Linux、Windows 或 required CI 中运行 Stryker。
- 未验证 Stryker 4.16.0 以外版本，也未接入 workflow、共享脚本或 tool manifest。
- 原始 JSON 含本机绝对路径，只保留在临时目录，未提交或上传。
