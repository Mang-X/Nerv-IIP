# PR #2824 R2 本地证据

日期：2026-08-30

范围：Issue #2277，ERP 单域，Scope M。本文只记录 R2 修复候选在本地实际执行的命令和结果；不把本地结果表述为 exact-head CI、合并或 tracker 完成。

## 变异矩阵

以下变异均只在工作树中临时注入。每个错误实现得到 RED 后立即恢复，未进入提交。定向命令统一为：

```text
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~<测试名>'
```

| # | 变异位置与等价错误 | 鉴别性测试 | RED 实际结果 | 恢复后 GREEN |
|---|---|---|---|---|
| 1 | `WorkOrderCostVarianceQueries.ValidateBasis` 只删除 `reportNos.Contains(original.ReportNo)`，允许原报告不属于当前 settlement membership | `Reversal_original_must_belong_to_the_current_settlement_membership` | 预期 `unavailable`，错误实现得到 `available` | 通过 |
| 2 | `WorkOrderCostVarianceQueries.ValidateBasis` 删除报告及原报告的 operation/workCenter scope 校验，允许跨工序、跨工作中心误配 | `Reversal_original_must_match_the_current_operation_and_work_center_scope` | 预期 `unavailable`，错误实现得到 `available` | 通过 |
| 3 | `WorkOrderCostVarianceQueries.ValidateBasis` 删除 `HasValidNumericScale` 守卫，让 PostgreSQL `numeric(18,6)` 静默舍入后的值继续参与计算 | `Snapshot_numeric_scale_beyond_six_digits_fails_closed` | 预期 `unavailable`，错误实现得到 `available` | 通过 |
| 4 | 聚合 `OverflowException` 路径恢复为通用 `Unavailable`，丢弃已知 operation lineage、分页和总数 | `Aggregate_numeric_overflow_preserves_operation_lineage_pagination_and_total_count` | `TotalOperations` 预期 2，错误实现得到 0 | 通过 |

四项恢复后的组合命令：

```text
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Reversal_original_must_belong_to_the_current_settlement_membership|FullyQualifiedName~Reversal_original_must_match_the_current_operation_and_work_center_scope|FullyQualifiedName~Snapshot_numeric_scale_beyond_six_digits_fails_closed|FullyQualifiedName~Aggregate_numeric_overflow_preserves_operation_lineage_pagination_and_total_count'
```

结果：4 passed，0 failed，0 skipped。

## 真实 PostgreSQL 18

使用独立临时 `postgres:18-alpine` 容器和受管测试入口 `NERV_IIP_TEST_POSTGRES` 执行；测试仍通过 `PostgreSqlTestDatabase` 创建、迁移、隔离并清理成员库。临时容器在测试后停止，并回读确认名称不存在。

```text
NERV_IIP_TEST_POSTGRES=<独立 PostgreSQL 18 管理连接> dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj -c Release --no-restore
```

结果：392 passed，1 Redis/CAP policy-skipped，0 failed。R2 鉴别向量位于既有受治理身份 `ErpCostAccountingPostgresAcceptanceTests.PostgreSQL_concurrent_report_and_actual_settlement_leave_only_actual_labor_active`，实际覆盖：

- 只读取最高 active settlement revision，并排除旧 revision；
- `PageNumber=2`、`PageSize=2` 的非默认分页和真实 `TotalOperations=3`；
- 正中点 `1.0000005 -> 1.000001` 与负中点 `-0.0000005 -> -0.000001` 的 `AwayFromZero`；
- 输入 scale 大于 6 时，数据库值虽被 `numeric(18,6)` 舍入，持久化的来源 scale 证据仍使查询失败关闭为 `numeric_scale_out_of_range`。

## 其余门禁

```text
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj -c Release --no-restore
dotnet test backend/tests/Nerv.IIP.VocabularyGovernance.Tests/Nerv.IIP.VocabularyGovernance.Tests.csproj -c Release --no-restore
dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj -c Release --no-restore
pwsh scripts/tests/postgres-test-lane.Tests.ps1
pwsh scripts/tests/test-evidence.Tests.ps1
dotnet tool run dotnet-ef migrations has-pending-model-changes --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --context ApplicationDbContext
dotnet format backend/Nerv.IIP.sln --verify-no-changes --no-restore --include <本 PR 的 8 个 C# 改动文件>
git diff --check
```

结果：

- ERP 快速层：374 passed，19 policy-skipped，0 failed；
- R1 + R2 关键向量：8 passed，0 failed，0 skipped；
- VocabularyGovernance：25/25 passed；
- FacadeCoverage：10/10 passed；
- PostgreSQL lane contract 与 test-evidence governance：通过；
- EF migration：`No changes have been made to the model since the last migration.`；
- 改动文件 format 与 whitespace：通过。

未运行真实 Redis/CAP，因此对应 1 个用例按登记策略跳过；Gateway、generated client、UI 与完整月结不在本票范围内。
