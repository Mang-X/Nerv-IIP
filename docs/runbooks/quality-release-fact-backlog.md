# Quality 工单发布事实缺失巡检 Runbook

本页承载「工单发布事实尚未同步到质量」这一存量状态的观测与恢复操作。业务语义见 [`../product/mes/design.md`](../product/mes/design.md) §3.12；配置键、指标名、默认值与扫描行为以 Quality 当前代码和配置为准。

## 这个状态是什么

工序的报工事实已进入 Quality，工单发布事实没有。表现为 `quality.periodic_inspection_operations` 里存在该工序的行，但 `sku_code` / `released_at_utc` 为空，而子表 `quality.periodic_inspection_production_reports` 里有行。

后果：首件确认读面对该工序恒回 `not-synchronized`，报工门禁按 fail closed 持续拒绝并提示「请稍后重试」。**该状态不会自愈**——报工再来多少次都不会补上发布事实。

## 观测

Quality 的巡检 `BackgroundService` 默认关闭。开关关闭时它不建计时器、不建数据库 scope、也不注册指标，`/metrics` 上不会出现下面两个族。

配置入口 `Quality:ReleaseFactBacklog`：

| 键 | 含义 |
| --- | --- |
| `Enabled` | 巡检开关；关闭即无任何副作用 |
| `Interval` | 扫描间隔；非正值回落到默认值 |
| `StaleAfter` | 年龄下限，见下节；非正值回落到默认值 |
| `Scopes[]` / `OrganizationId` + `EnvironmentId` | 要扫的 `organizationId + environmentId`；一个都没配时巡检直接退出并留一条 warning |

指标（Prometheus 文本格式，经 Quality 已有的 `/metrics` 暴露，label 为 `organization` / `environment`）：

| 指标 | 含义 |
| --- | --- |
| `nerv_iip_quality_release_fact_backlog_operations` | 当前卡住的工序行数 |
| `nerv_iip_quality_release_fact_backlog_oldest_age_seconds` | 其中最早那一条报工事实距今的秒数 |

每个已配置 scope 每轮都会写读数（包括写 0），因此「读到 0」与「没扫过」可以区分。

### 年龄下限怎么定

`StaleAfter` 是**年龄下限，不是告警阈值**：低于它的行仍可能自行恢复，计进来会让指标在正常流量下抖出非零值。

取值依据是该状态还能自行恢复多久。发布投影缺失只有一条不需要人介入的恢复通道——CAP 对 `mes.WorkOrderReleased` 的自动重投；业务事实非法那一支由消费者守卫写死信后正常返回、不触发重投，因此进入重试循环的只剩基础设施故障，预算为 `FailedRetryCount × FailedRetryInterval`。

**不配 `StaleAfter` 时下限由服务自己算**：取「基线」与「当前生效的 CAP 重投预算」的较大者。预算里的重投间隔读的是**生效值**（`Cap:FailedRetryInterval`，也覆盖 `Cap__FailedRetryInterval` 环境变量），因此调大该键时下限自动跟着抬高，不需要人工重算；`FailedRetryCount` 在本仓没有配置键，取 CAP 默认值。

显式配了 `StaleAfter` 就以显式值为准——那是运维的明示选择。把它配到低于自愈预算，Gauge 会开始把还能自愈的行计成积压。

## 恢复

读数持续大于 0 时，调用 MES 的内部运维端点补上发布投影：

```
POST /internal/business-mes/v1/work-order-release-projection-backfill
```

选取口径与幂等性见 [`../product/mes/design.md`](../product/mes/design.md) §3.12。重复执行不改变质量侧投影。

回填在 Quality 侧落地后，下一轮巡检的两个指标会回落到 0——这就是「跑成没跑成」的验收读数。**不要**用手工建首件任务的方式绕开：那会把门禁的 fail closed 变成 fail open。

## 停止条件

- 指标恒为 0 但现场仍报「请稍后重试」：先确认扫的 scope 与现场的 `organizationId + environmentId` 一致，再回到读面判据核对，不要直接改判据。
- 回填执行后读数不下降：停止重复调用，先核对回填端点返回的 `WorkOrdersPublished` / `OperationsPublished` 与 Quality 侧消费是否落地（CAP 消费失败会留死信记录），再决定下一步。
- 读数在正常流量下反复抖动出非零值：说明 `StaleAfter` 被调得低于自愈预算，先恢复下限再看趋势。

## 回滚

把 `Quality:ReleaseFactBacklog:Enabled` 置回 `false` 即可；巡检只读不写，关闭它不影响门禁、读面与已完成的回填。
