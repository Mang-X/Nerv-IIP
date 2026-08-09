# Inventory MVP 设计

## 目标

将 Inventory 构建为库存移动、库存台账余额、库存可用量和盘点调整的唯一业务事实来源。

## 当前状态

Inventory 尚无服务目录。BusinessMasterData 可作为 SKU、UOM、站点、产线、工作中心、业务伙伴和参考数据校验的第 0 层参考来源。WMS、ERP、MES 和 DemandPlanning 尚无稳定的 Inventory 契约。

## 所有权事实

Inventory 拥有以下事实：

1. StockLocation：仓库、区域、库位或逻辑库存位置的编码与状态。
2. StockLedger：按组织、环境、SKU、UOM、站点、位置、批次、序列号、质量状态和所有权引用划分的当前数量。
3. StockMovement：仅追加的库存移动记录，包含移动类型、来源单据引用、幂等键和带符号数量。
4. StockCountTask：用于差异确认的盘点执行单头与实盘行。
5. StockCountAdjustment：根据已批准盘点差异生成的移动。

Inventory 不拥有：

1. WMS 入库、出库、拣货、上架或 WCS 任务状态。
2. ERP 采购、销售、发票、应付、应收或计价事实。
3. MES 工单、工序、报工或物料发料执行状态。
4. MasterData 的 SKU、UOM、伙伴、站点或设备事实。
5. 除库存事实所存质量状态之外的 Quality 检验决策。

## MVP 命令与查询

| API | 用途 | 说明 |
| --- | --- | --- |
| `POST /api/inventory/v1/locations` | 创建或更新库存位置。 | 按组织、环境和位置编码实现幂等。 |
| `POST /api/inventory/v1/movements` | 过账入库、出库、转移、调整或盘点移动。 | 必须提供 `idempotencyKey`；键重复而载荷冲突时拒绝请求。 |
| `GET /api/inventory/v1/availability` | 按 SKU、UOM、站点、位置以及可选批次/序列号查询可用数量。 | 返回现有量、预留量和可用量。在引入预留之前，MVP 中的预留量为 `0`。 |
| `POST /api/inventory/v1/count-tasks` | 创建库存盘点任务。 | 盘点任务记录范围和预期台账快照版本。 |
| `POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments` | 确认盘点差异并过账调整移动。 | 创建 StockCountAdjustment 事实和 StockMovement 记录。 |

## 移动规则

1. StockMovement 仅允许追加。
2. 除非移动类型配置为显式更正，否则拒绝产生负现有量。
3. 移动幂等键在每组组织、环境、来源服务和来源单据引用内唯一。
4. 数量按适合流程制造的十进制精度存储。
5. UOM 换算通过 MasterData 契约解析或存储于快照字段；Inventory 不拥有换算规则。
6. 批次和序列号值可选，但 MasterData 公开 SKU 可追溯策略后，这些值必须遵循该策略。

## 可用量规则

1. 现有量是当前 StockLedger 数量。
2. MVP 的预留量为 `0`。
3. 可用量等于现有量减去预留量。
4. 查询结果包含聚合所使用的台账维度，使 WMS、ERP 和 DemandPlanning 能够避免含义不明确的合计值。

## 事件

Inventory 发布符合 ADR 0011 信封格式的事件：

1. `inventory.StockMovementPosted`
2. `inventory.StockCountVarianceConfirmed`
3. `inventory.StockAvailabilityChanged`

事件必须携带公开单据引用、SKU/UOM/位置维度、移动数量和相关 ID。事件不得携带数据库行内部信息或跨服务外键。

## 权限

初始权限编码：

1. `business.inventory.locations.manage`
2. `business.inventory.movements.create`
3. `business.inventory.ledger.read`
4. `business.inventory.counts.manage`

## 持久化

默认 schema：`inventory`。

必需的数据表：

1. `stock_locations`
2. `stock_ledgers`
3. `stock_movements`
4. `stock_count_tasks`
5. `stock_count_adjustments`

每张表和每个业务列都必须具有 schema 注释。PostgreSQL 迁移历史记录必须使用 `inventory.__EFMigrationsHistory`。

## 测试

验收要求：

1. 覆盖移动过账、幂等键重复、负库存拒绝和盘点调整生成的领域测试。
2. 覆盖内部授权、请求校验、路由形状和稳定 operation ID 的 Web 测试。
3. 覆盖可用量聚合的查询测试。
4. 使用 `Nerv.IIP.Testing` 的 schema 约定测试。
5. 覆盖符合 ADR 0011 的事件名称和载荷的事件转换器测试。
