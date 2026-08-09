# ADR 0020：工业遥测历史数据库存储

- 状态：已接受
- 日期：2026-07-07

## 背景

IndustrialTelemetry 以前存储 `TelemetrySummary` 桶，用于当前历史数据和告警评估，但 MAN-373 / GitHub #689 要求建立历史数据库基础，支持原始采样明细、按小时和按日降采样、保留期清理，以及共同使用 `TelemetryTag.SamplingPolicy`。

在更大规模集群部署之前，部署基线仍是私有化单机 Windows/Aspire 安装。因此，除非当前 PostgreSQL 路径无法达到目标，否则首条生产路径中的历史数据库不得引入强制 TimescaleDB 扩展或第二个时序数据库。

## 决策

使用现有 IndustrialTelemetry PostgreSQL schema，以及原生表和与提供方无关的索引：

1. `telemetry_raw_samples` 存储服务接受的最细粒度桶明细：最小值、最大值、加权平均输入值、首值、末值、样本数、桶起止时间和来源幂等元数据。
2. `telemetry_rollups` 存储 `Hourly` 和 `Daily` 汇总值，包括最小值、最大值、加权平均值、首值和末值。汇总键由组织、环境、设备、标签、粒度和窗口开始时间组成。
3. `telemetry_summaries` 继续作为告警规则评估和现有客户端的兼容写入路径，但 `/equipment/telemetry/history` 从历史数据库表读取。
4. Connector Host 和 IndustrialTelemetry 存储共同解析 `TelemetryTag.SamplingPolicy`。采集器可以从 `sample-10s`、`sample-1m` 或 `bucket=30s;raw=7d;hourly=90d;daily=730d` 推导桶秒数；如果写入的桶宽度与已配置的标签策略不一致，存储层将拒绝写入。
5. 保留期清理按层执行：原始、小时和日窗口分别在各自配置时长届满后删除。服务从 `TelemetryTag.SamplingPolicy` 读取 `raw=`、`hourly=` 和 `daily=` 值；若未提供，则回退到 `IndustrialTelemetry:Historian` 中配置的作用域默认值。
6. `TelemetryHistorianScheduler` 是生产运行路径。它通过 `IndustrialTelemetry:Historian:Enabled` 按部署选择性启用，要求显式指定组织/环境作用域，按 UTC 汇总窗口运行，先降采样再清理，并按作用域隔离失败。

TimescaleDB 仍是可选的未来优化，而不是本次范围的基线依赖。如果后续基准测试证据表明原生 PostgreSQL 无法满足目标客户配置，迁移路径应保持相同的领域/应用契约，只迁移基础设施存储策略。

## 已考虑的替代方案

1. **强制使用 TimescaleDB 超表（hypertable）**：当前基线不采用，因为这会给私有化单机安装增加扩展安装、备份/恢复和离线部署要求。
2. **仅保留 `telemetry_summaries`**：不采用，因为汇总数据不保留首值/末值，也不区分保留期层级，无法提供原始明细历史。
3. **立即引入外部历史数据库服务**：不采用，因为跨服务历史数据所有权和 Connector 投递契约尚未稳定到足以引入新的物理依赖。

## 影响

历史数据读取可以从同一服务自有 schema 中选择原始、小时或日数据行，无需跨 schema 外键。首个实现使用普通表和索引；如果保留期删除的成本过高，后续可以在同一 schema 内增加 PostgreSQL 分区。

降采样必须具备幂等性。重新运行同一汇总窗口不得创建第二条小时或日数据，并应记录确定性的历史数据来源序列，供运维人员诊断。

保留期任务必须在受影响窗口完成降采样后运行。运维人员应设置足够长的原始数据保留窗口，以覆盖 Connector 延迟投递。PostgreSQL 保留期清理针对已建索引的 Unix 时间列执行集合式删除；非关系型测试提供方保留实体删除回退路径。

## 性能说明

本次范围可复现的本地检查如下：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --filter IndustrialTelemetryHistorianTests -v:minimal
```

该测试覆盖原始数据写入、策略强制执行、选择性启用的调度器执行、小时/日加权汇总和保留期清理。每次调度器运行时，降采样读取数量有界的待处理小时/日窗口；`IndustrialTelemetry:Historian` 限制的是窗口数量（`MaxPendingHourlyWindows`、`MaxPendingDailyWindows`），而不是原始数据行数。对于数据量更大的客户容量评估，在启用更短的原始数据保留期之前，应设置 `NERV_IIP_TEST_POSTGRES` 并对以 PostgreSQL 为后端的测试夹具运行同一命令；首个生产 schema 已包含该基准测试应覆盖的幂等索引和范围/窗口索引。
