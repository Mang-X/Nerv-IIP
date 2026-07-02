# 需求计划到完工入库

这条路径帮助计划员、生产主管和 MES 班组长理解从需求到完工入库的当前闭环。它覆盖 DemandPlanning、Scheduling、MES、Quality、Inventory/WMS 的已暴露入口，不把完整高级 APS 或甘特工作台写成已实现。

## 适用角色

- 计划员：维护需求、运行 MRP、接受计划建议。
- 排程员：查看 APS lite 结果和生产计划就绪状态。
- 生产主管：释放工单、派工、跟踪在制和报工。
- 仓储员：处理完工入库请求和库存过账结果。

## 前置资料

- 已发布生产版本，且 MBOM、Routing、工作中心和设备资料可解析。
- 库存或采购计划能够满足关键物料需求。
- 质量检验和设备可用性规则已经按当前实现完成基础配置。

## 页面入口

| 环节 | Business Console 路由 | 当前事实或缺口 |
| --- | --- | --- |
| 需求与 MRP | `/planning` | 已在需求与计划域暴露，用于需求、MRP 和计划建议。 |
| 排产工作台 | `/scheduling` | 已在需求与计划域暴露，消费 APS lite / Scheduling facade；甘特仍是缺口。 |
| MES 生产驾驶舱 | `/mes` | 已在制造执行域暴露。 |
| 生产计划 | `/mes/plans` | 已在制造执行域暴露，可查看来源计划和转工单状态。 |
| 工单列表 | `/mes/work-orders` | 已在制造执行域暴露。 |
| 工单详情 | `/mes/work-orders/:workOrderId` | 已有对象详情页；不作为常驻菜单。 |
| 齐套与物料 | `/mes/materials` | 已在制造执行域暴露。 |
| 派工 | `/mes/dispatch` | 已在制造执行域暴露。 |
| 工序执行 | `/mes/operation-tasks` | 已在制造执行域暴露。 |
| 报工记录 | `/mes/production-reports` | 已在制造执行域暴露。 |
| 完工入库请求 | `/mes/receipts` | 已在制造执行域暴露。 |
| 规则排程结果 | `/mes/schedules` | 已在制造执行域暴露为过渡入口，不等同高级 APS。 |

## 操作步骤

1. 在 `/planning` 查看需求、运行 MRP，并检查 pegging 和计划建议。
2. 接受可执行的计划建议，进入生产计划或 MES 可消费的计划视图。
3. 在 `/mes/foundation` 检查开工前基础准备，缺 SKU、生产版本、工作中心或设备范围时先补上下文。
4. 在 `/mes/plans` 查看可转入 MES 执行的计划，确认计划就绪状态。
5. 在 `/mes/work-orders` 创建或释放工单；进入 `/mes/work-orders/:workOrderId` 查看工序、用料和阻塞原因。
6. 在 `/mes/materials` 跟踪齐套和领料，必要时联动 WMS/Inventory。
7. 在 `/mes/dispatch` 和 `/mes/operation-tasks` 派工、开工、暂停、恢复或完工。
8. 在 `/mes/production-reports` 记录良品、不良和返工；需要质检时进入 `/mes/quality` 或 Quality 页面。
9. 在 `/mes/receipts` 创建完工入库请求，等待 Inventory/WMS 过账事实回写。

## 业务对象/单据流

Demand -> MRP Run -> Planning Suggestion -> APS/Scheduling Result -> Production Plan -> Work Order -> Operation Task -> Production Report -> Finished Goods Receipt -> Inventory Movement。

## 状态变化

- 计划建议从待处理变为已接受或忽略。
- 工单从创建、释放、执行中到完成；工序任务可经历待派工、执行中、暂停、完成或异常。
- 报工产生良品、不良、返工等执行事实，完工入库请求等待库存过账结果。

## 结果校验

- 在 `/planning` 能看到需求、MRP 结果和计划建议的处理状态。
- 在 `/mes/plans` 或 `/mes/work-orders` 能追到计划进入生产计划、工单和工序任务后的状态。
- 在 `/mes/production-reports` 能看到良品、不良和返工等报工事实。
- 在 `/mes/receipts` 能看到完工入库请求，并能通过库存移动或失败诊断解释 Inventory/WMS 回写结果。
- 如果链路中断，当前卡点通常是生产版本缺失、物料或设备上下文不足、排程结果无法转工单，或过账失败诊断入口不够直达。

## 常见失败/空态

- `/mes/foundation` 无上下文：先选择 SKU、生产版本、工作中心、设备或工单，不把全局 readiness 误判为业务阻塞。
- 计划建议无法执行：通常缺生产版本、物料可用性、工作中心能力或设备运行事实。
- 工单详情为空：确认工单号来自当前组织/环境和当前列表，不使用未确认的默认编号。
- 完工入库失败：查看 Inventory posting failed 或 WMS movement request 失败诊断。

## 当前限制

- `/mes/schedules` 是规则排程结果和显式运行动作，不承载甘特，也不承担高级 APS 算法。
- 高级 APS 优化器、甘特/RFC 展示和跨域高级报表仍是后续工作。
- PDA 当前复用 business-console facade；独立 `/api/mobile/v1/**` 和离线 outbox/sync 后置。

[内部缺口记录](/internal/gaps/planning-to-finished-goods)
