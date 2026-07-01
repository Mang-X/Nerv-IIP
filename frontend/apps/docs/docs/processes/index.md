# 核心业务流程图

本页用五张流程图概览当前产品主线。图中的页面入口只表示当前有可访问或窄化工作台的业务表面，不代表每个高级子能力都已完整交付。

## 工程资料

工程资料：EBOM -> MBOM -> 工艺路线 -> 生产版本。

```mermaid
flowchart LR
  SKU[SKU / UOM] --> Item[Engineering Item]
  Item --> Doc[Engineering Document]
  Item --> EBOM[EBOM]
  EBOM --> MBOM[MBOM]
  MBOM --> Routing[Routing]
  StdOp[Standard Operation] --> Routing
  Routing --> PV[Production Version]
  PV --> Plan[Planning / MES reference]
```

## 计划生产

计划生产：需求 -> MRP -> APS -> 生产计划 -> 工单 -> 报工 -> 入库。

```mermaid
flowchart LR
  Demand[Demand] --> MRP[MRP run]
  MRP --> Suggestion[Planning suggestion]
  Suggestion --> APS[APS lite / Scheduling result]
  APS --> Plan[Production plan]
  Plan --> WO[Work order]
  WO --> Task[Operation task]
  Task --> Report[Production report]
  Report --> Receipt[Finished goods receipt]
  Receipt --> Inventory[Inventory movement]
```

## 仓储库存

仓储库存：收货 -> 上架 -> 库存 -> 拣货 -> 出库。

```mermaid
flowchart LR
  Inbound[Inbound order] --> Receiving[Receiving]
  Receiving --> Putaway[Putaway]
  Putaway --> Stock[Stock balance]
  Stock --> Reservation[Reservation]
  Reservation --> Picking[Picking task]
  Picking --> Outbound[Outbound order]
  Outbound --> Movement[Inventory movement]
  Movement --> Posted[Posted]
  Movement --> Failed[Failed]
```

## 质量审批

质量审批：检验 -> NCR -> 审批 -> 处置 -> 放行/返工/报废。

```mermaid
flowchart LR
  Inspection[Quality inspection] --> Record[Inspection record]
  Record --> NCR[NCR]
  NCR --> Approval[Approval record]
  Approval --> Disposition[Disposition]
  Disposition --> Release[Release]
  Disposition --> Rework[Rework]
  Disposition --> Scrap[Scrap]
```

## 设备维护

设备维护：报警 -> 维修工单 -> 备件 -> 恢复 -> 可靠性指标。

```mermaid
flowchart LR
  Runtime[Equipment runtime fact] --> Alarm[Alarm]
  Alarm --> WorkOrder[Maintenance work order]
  WorkOrder --> SpareParts[Spare part issue request]
  WorkOrder --> Restore[Repair complete / recovery]
  Restore --> Metrics[MTBF / MTTR / availability]
```

## 当前限制

- APS lite 与 MES 规则排程已经可解释计划到执行的基础链路；高级 APS 优化器和正式甘特展示仍后置。
- 质量审批图表达当前 Quality NCR 与 BusinessApproval 的目标业务链路；具体页面仍以 `/quality/inspections`、`/quality/ncrs` 和审批中心已暴露能力为准。
- 设备维护图覆盖报警、维修工单、备件请求和可靠性指标；完整 CMMS 工作台和高级点检/保养计划体验仍需继续深化。

[内部缺口记录](/internal/gaps/core-processes)
