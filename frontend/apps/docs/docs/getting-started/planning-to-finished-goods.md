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

| 目标 | Business Console 路由 |
| --- | --- |
| 需求与 MRP | `/planning` |
| MES 生产驾驶舱 | `/mes` |
| 生产计划 | `/mes/plans` |
| 工单列表 | `/mes/work-orders` |
| 工单详情 | `/mes/work-orders/:workOrderId` |
| 齐套与物料 | `/mes/materials` |
| 派工 | `/mes/dispatch` |
| 工序执行 | `/mes/operation-tasks` |
| 报工与完工 | `/mes/production-reports` |
| 完工入库请求 | `/mes/receipts` |
| 规则排程结果 | `/mes/schedules` |

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

## 成功结果

- 生产主管能在 MES 驾驶舱看到工单、工序、在制、阻塞和角色待办。
- 完工入库请求能形成库存移动请求，并由 Inventory/WMS 回写过账成功或失败事实。
- 计划员能把需求、MRP 建议、排程结果和生产执行状态连起来解释。

## 常见失败/空态

- `/mes/foundation` 无上下文：先选择 SKU、生产版本、工作中心、设备或工单，不把全局 readiness 误判为业务阻塞。
- 计划建议无法执行：通常缺生产版本、物料可用性、工作中心能力或设备运行事实。
- 工单详情为空：确认工单 ID 来自当前组织/环境，不使用演示默认 ID 假设。
- 完工入库失败：查看 Inventory posting failed 或 WMS movement request 失败诊断。

## 当前限制

- `/mes/schedules` 是规则排程结果和显式运行动作，不承载甘特，也不承担高级 APS 算法。
- 高级 APS 优化器、甘特/RFC 展示和跨域高级报表仍是后续工作。
- PDA 当前复用 business-console facade；独立 `/api/mobile/v1/**` 和离线 outbox/sync 后置。

[内部缺口记录](/internal/gaps/planning-to-finished-goods)
