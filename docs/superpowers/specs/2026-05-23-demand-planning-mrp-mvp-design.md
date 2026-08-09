# DemandPlanning MPS/MRP MVP 设计

## 目标

将 DemandPlanning 构建为以下计划事实的来源：需求来源、主生产计划、确定性的按日时间桶 MRP 运行、计划采购建议、计划工单建议和供需追溯关系。

DemandPlanning 解释 ERP 为何应采购物料，以及 MES 为何应创建工作。它自身不创建正式的采购单据或生产单据。

## 当前状态

DemandPlanning 尚无服务目录。第 1 波次现在提供了两项必需的上游事实：

1. ProductEngineering 暴露已发布的 BOM、routing 和 ProductionVersion 事实，其中包括 ProductionVersion 解析（resolve）API。
2. Inventory 暴露库存移动、库存台账和库存可用量事实。

## 拥有的事实

DemandPlanning 拥有：

1. DemandSource：销售订单需求、安全库存需求或人工计划需求。
2. ForecastInput：按 SKU/UOM/site/period 划分的预测需求，并带有向前/向后冲减窗口。
3. MasterProductionSchedule：按 SKU 和日期划分时间桶的计划成品需求。
4. MrpRun：一次计算运行、输入快照元数据、计划跨度和状态。
5. PlanningSuggestion：计划采购、计划工单、重排、取消或加急建议。
6. PeggingLink：从建议追溯到需求、BOM 组件、库存输入和上游版本引用的关系。

DemandPlanning 不拥有：

1. 已发布的 EBOM、MBOM、Routing 或 ProductionVersion 事实。
2. 库存余额、预留或移动。
3. 请购单、RFQ、采购订单或收货。
4. 正式 MES 工单或工序任务。
5. 客户订单或发票状态。

## 输入

MVP 通过 adapter 接受输入，因此无需启动其他服务即可测试 MRP 算法：

| 输入 | 来源 | MVP 处理方式 |
| --- | --- | --- |
| 已发布生产版本 | ProductEngineering 解析/列表 API 或 fixture adapter | 在运行中对 productionVersionId、mbomVersionId 和 routingVersionId 创建快照。 |
| 已发布 MBOM 行 | ProductEngineering 事件/API 快照 | MVP 使用单层 BOM 展开。 |
| 库存可用量 | Inventory `GET /api/inventory/v1/availability` 或 fixture adapter | 按 SKU/UOM/site 对可用数量创建快照。 |
| 需求来源 | DemandPlanning command/API | 作为计划输入归 DemandPlanning 所有。 |
| 预测输入 | DemandPlanning forecast command/API | 销售订单需求会在配置的向前/向后窗口中冲减预测；只有剩余预测进入 MRP。 |
| 计划参数 | DemandPlanning 本地默认值 | 使用按日时间桶，不进行有限产能优化。 |

## MVP 计算规则

1. MRP 按日时间桶运行。
2. 首个版本支持单层 MBOM 展开。
3. 成品净需求等于需求数量减去成品可用数量。
4. 只有成品净需求为正时，才生成计划工单建议。
5. 组件毛需求等于计划工单数量乘以 MBOM 中每个父项所需的组件数量。
6. 只有组件净需求为正时，才生成计划采购建议。
7. 所有建议都携带追溯引用，指向需求来源和输入版本事实。
8. 对于延期、提前或无法匹配的计划收货，分别创建 `reschedule-in`、`reschedule-out` 或 `cancel` 异常建议，并关联到计划收货；如果存在相关需求，也应关联该需求。
9. 建议一旦被接受、拒绝或关闭，就不可变更。
10. 重新运行会创建新的 MrpRun，不会改写过去已接受的建议。

## 确定性 fixture

实施必须将此 fixture 保留为专用回归测试：

| 输入 | 值 |
| --- | --- |
| 需求 | `SKU-FG-1000`，数量 `10`，到期日 `2026-06-01` |
| 成品可用量 | `SKU-FG-1000`，数量 `2` |
| MBOM | `SKU-FG-1000` 需要 `SKU-RM-1000`，数量 `3` |
| 组件可用量 | `SKU-RM-1000`，数量 `5` |

预期建议：

| 建议 | 数量 |
| --- | --- |
| `SKU-FG-1000` 的计划工单 | `8` |
| `SKU-RM-1000` 的计划采购 | `19` |

## API 接口面

| API | 用途 | 权限 |
| --- | --- | --- |
| `POST /api/business/v1/planning/demands` | 创建或更新需求来源。 | `business.planning.demands.manage` |
| `GET /api/business/v1/planning/demands` | 列出需求来源。 | `business.planning.demands.read` |
| `POST /api/business/v1/planning/forecasts` | 创建或更新预测输入。 | `business.planning.demands.manage` |
| `GET /api/business/v1/planning/forecasts` | 列出预测输入。 | `business.planning.demands.read` |
| `POST /api/business/v1/planning/mrp-runs` | 针对一个计划跨度运行确定性 MRP。 | `business.planning.mrp.run` |
| `GET /api/business/v1/planning/mrp-runs` | 列出 MRP 运行。 | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/mrp-runs/{runId}/pegging` | 读取一次运行的供需追溯关系。 | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/suggestions` | 列出建议。 | `business.planning.mrp.read` |
| `POST /api/business/v1/planning/suggestions/{suggestionId}/accept` | 将建议标记为已被下游服务接受。 | `business.planning.suggestions.manage` |

## 事件

DemandPlanning 发布使用 ADR 0011 envelope 的事件：

1. `demandPlanning.MrpRunCompleted`
2. `demandPlanning.PlannedPurchaseSuggested`
3. `demandPlanning.PlannedWorkOrderSuggested`
4. `demandPlanning.PlanningSuggestionAccepted`

事件携带公开 ID、SKU/UOM/site/date 维度、数量、适用时的 productionVersionId，以及追溯引用。事件不得携带数据库行内部信息或跨 schema 外键。

## 权限

初始权限代码：

1. `business.planning.demands.read`
2. `business.planning.demands.manage`
3. `business.planning.mrp.read`
4. `business.planning.mrp.run`
5. `business.planning.suggestions.manage`

## 持久化

默认 schema：`demand_planning`。

必需的表：

1. `demand_sources`
2. `master_production_schedules`
3. `mrp_runs`
4. `planning_suggestions`
5. `mrp_pegging_links`

每个表和业务列都需要 schema 注释。PostgreSQL migration 历史记录必须使用 `demand_planning.__EFMigrationsHistory`。

## 测试

验收要求：

1. 覆盖需求来源生命周期、MRP 运行状态和建议生命周期的领域测试。
2. 使用上述确定性 fixture 的纯 MRP 计算器测试。
3. 覆盖路由结构、授权、验证和操作 ID 的 Web 测试。
4. 使用 `Nerv.IIP.Testing` 的 schema 约定测试。
5. 覆盖 DemandPlanning 事件名称的集成事件转换器/序列化测试。
6. adapter 测试必须证明 ProductEngineering 和 Inventory 输入以快照表示，而不是通过跨服务读取表来获取。
