# ADR 0017: 业务主链 process manager 与补偿策略

- Status: Accepted
- Date: 2026-06-25

## Context

ADR 0003 已经将跨服务状态传播基线冻结为集成事件，禁止通过共享数据库表或直接写入其它服务 schema 协作。ADR 0011 已经冻结 IntegrationEvent envelope、版本、幂等、DLQ、poison message 和 replay 规则。ADR 0012 进一步要求业务服务之间默认通过公开 Contracts、OpenAPI、IntegrationEvent 和 IAM 授权上下文协作，Gateway 不能承载业务规则或流程状态。

#419 的集成总览显示，当前仓库已经具备多条点对点事件闭环：ERP 采购收货通过 `inventory.InventoryMovementRequested` 请求 Inventory 入账，ERP 销售发货释放通过 `wms.OutboundOrderRequested` 驱动 WMS 出库，WMS 与 Inventory、MES 与 Inventory、Quality 与 Inventory/MES、Scheduling 与 MES、Maintenance 与 MES 等链路也采用 choreography、消费者本地幂等和 DLQ/replay 门禁。

但 #485 仍保留两条主链的治理问题：

1. procure-to-pay：planning/purchase requisition -> purchase order -> receipt -> inventory -> supplier invoice/AP/payment。
2. order-to-cash：quotation/sales order -> delivery order -> WMS outbound -> inventory -> AR/collection。

两条主链后续将通过 #508、#509、#510 等子任务继续补齐事件消费者。如果没有明确 ADR，后续 PR 容易在 choreography + local compensation 与中心 saga/process-manager 之间反复摇摆。

## Decision

当前阶段不为 procure-to-pay 或 order-to-cash 引入中心 saga/process-manager/orchestrator。

Nerv-IIP 继续采用 choreography + local compensation：

1. 每个服务只拥有自己的领域事实、补偿事实、inbox/outbox/DLQ 记录和幂等状态。
2. 跨服务推进通过 ADR 0011 的公开 IntegrationEvent、必要的内部服务 API 或 Gateway facade 读面完成，不通过中心流程表改写下游事实。
3. Gateway、BusinessGateway、PlatformGateway、Business Console 和 acceptance harness 都不得成为隐式 process manager。
4. 单个业务服务可以有服务内部 workflow/state machine，但该状态只能表达本服务拥有的事实，不能代表跨服务全局流程真相。
5. 补偿优先表现为拥有副作用服务的领域事实和事件，而不是由外部服务直接回滚其数据库。

该决策适用于当前单平台、私有化、可自部署 profile。它也适用于 `Messaging:Provider=InMemory`、`Redis` 和 `RabbitMQ` profile 下的业务事件路径；broker profile 改变不自动改变流程所有权模型。

## Current Flow Guidance

### Procure-to-pay

Procurement、Inventory、Quality、WMS 和 Finance 之间继续采用事件化接力：

1. DemandPlanning 接受采购建议后，ERP 按 #508 消费 `demandPlanning.PlanningSuggestionAccepted`，只在目标为 `BusinessErp/PurchaseRequisition` 时创建采购申请。
2. ERP 继续拥有采购申请、RFQ、供应商报价、采购订单、采购收货、AP/付款和最小财务候选事实。
3. ERP 采购收货可以发布或转换为公开库存移动请求；当前代码事实中该路径由 ERP converter 发布目标/执行服务命名空间下的 `inventory.InventoryMovementRequested`。Inventory 继续拥有库存台账和入账结果。
4. #509 应在 ERP 边界内从采购收货事实派生 AP candidate/payable，金额来自 ERP 拥有的采购订单单价和收货数量，不从 Inventory、WMS 或前端获取财务金额。
5. 如果收货、质检、入库或 AP 候选失败，失败服务记录本地失败/DLQ，并发布自己拥有的失败或补偿事实；ERP 可以消费下游失败事实更新采购/财务状态，但不能直接改下游 schema。

### Order-to-cash

Sales、WMS、Inventory 和 Finance 之间继续采用事件化接力：

1. ERP 继续拥有商机、报价、销售订单、发货单、AR/收款和最小财务候选事实。
2. ERP 发货释放继续通过 `wms.OutboundOrderRequested` 请求 WMS 出库执行。
3. WMS 继续拥有出库执行、拣货、复核、WCS 回执和出库完成事实；Inventory 继续拥有库存扣减与 posting 成功/失败事实。
4. #510 应由 ERP 消费 `wms.OutboundOrderCompleted`，识别 ERP 发货来源后创建 AR candidate/receivable。金额来自 ERP 拥有的销售订单/发货行单价和数量，不从 WMS payload 获取财务金额。
5. 对非 ERP 发货来源或无法匹配 ERP 发货单的 WMS 完成事件，ERP 应按契约语义选择忽略或进入本地 DLQ；不能创建中心流程实例等待人工补全。

## Compensation Event Rules

补偿事件不是独立的通用事件类型族。它们必须遵守 ADR 0011，并按领域事实命名。

1. **所有权**：产生原始副作用的服务拥有对应补偿副作用。Inventory 拥有库存预留释放、库存移动冲销和库存状态转移补偿；WMS 拥有仓储任务取消/失败/释放；ERP 拥有 AP/AR 或 AP/AR candidate 撤销、发票 hold、采购/销售单据状态调整；Quality 拥有检验处置和不合格评审事实。
2. **命名**：事件名使用 `领域名.过去式事实`，例如 `erp.AccountPayableVoided`、`erp.ReceiptMatchHeld`、`inventory.StockReservationReleased`、`inventory.StockMovementReversalPosted`、`wms.OutboundOrderCancellationRecorded`。不得使用命令式名称，例如 `erp.CompensatePayable` 或 `inventory.RollbackStock`。若 #509/#510 最终采用 AP/AR candidate 聚合或事件名，补偿事件必须沿用同一个业务名词，避免正向事件使用 `AccountPayable` 而补偿事件改用 `AccountPayableCandidate`。
3. **请求事实**：如果一个服务需要请求另一服务执行补偿，事件仍必须表达已经发生的业务事实或意图事实，例如 `wms.OutboundCancellationRequested` 或 `erp.PurchaseReceiptCorrectionRecorded`。跨服务“请求执行”事件使用目标/执行服务命名空间，与当前 `inventory.InventoryMovementRequested`、`wms.OutboundOrderRequested` 保持一致；只有表达发布方自身已经发生的事实时才使用发布方命名空间。消费者是否执行补偿由消费者自己的领域规则决定。
4. **幂等键**：补偿事件必须携带稳定 `idempotencyKey`，至少覆盖 `organizationId`、`environmentId`、原始 source document、source line 和 compensation reason。重复投递、replay 和人工重试不得重复产生副作用。
5. **因果链**：补偿事件必须保留 `correlationId`，并用 `causationId` 指向导致补偿的命令、事件或 DLQ 修复动作。Payload 应包含原始业务引用，不复制其它服务内部 ID 或数据库主键。
6. **失败处理**：补偿失败仍进入本服务 DLQ/失败状态；不得吞异常并把补偿标记为成功。需要人工介入时，服务发布本服务拥有的 hold/failed fact，并由 Notification、Business Console 或后续运营入口展示。
7. **可见性**：补偿状态通过拥有服务的公开 query/API 或事件投影暴露。Acceptance tests 应通过公开 API 或 integration-event-visible 结果验证，不读取其它服务数据库。

## Revisit Triggers

如果出现以下任一条件，应创建新的 ADR 或修订本 ADR，重新评估 process-manager/orchestration：

1. 同一用户可见流程需要一个跨 ERP/WMS/Inventory/Quality/Finance 的稳定 pending workflow 实例，并且该实例本身需要搜索、暂停、恢复、重新分配、审批或 SLA 计时。
2. 跨服务 timeout ownership 无法由任一拥有服务本地表达，例如“WMS 未在 4 小时内完成出库后必须自动取消 ERP 发货、释放 Inventory 预留并通知销售”的时钟既不属于 WMS，也不属于 ERP。
3. 单一业务失败需要三个及以上服务按严格顺序执行补偿，且任何一步失败都需要中心化重试、人工跳步或流程级状态机可视化。
4. 运维 replay、DLQ 修复和审计需要按“流程实例”而不是按事件/单据/服务来统一重放，否则无法定位或恢复客户现场问题。
5. 跨服务流程需要动态分支、人工作业池、长期等待外部回调或可配置流程定义，服务本地状态机和事件消费者已经不能保持可理解性。
6. 私有化部署 profile 引入独立流程引擎或外部工作流系统作为客户硬性需求，并且其运维、备份、授权、审计和离线交付成本已被评估。

这些触发条件出现前，不得在 #508、#509、#510 或相邻 PR 中引入中心 saga 基础设施、全局 workflow 表、跨服务 transaction coordinator 或 Gateway 级 orchestration。

## Consequences

1. #508、#509、#510 应继续实现具体事件消费者、服务本地 inbox/idempotency/DLQ 和真实跨边界测试，而不是等待 process manager。
2. 事件发布方必须把可供消费者判定来源、金额、数量、状态和幂等的公开事实放进公共 Contracts；缺字段时采用 ADR 0011 兼容的 additive contract 变更。
3. 跨服务一致性仍是最终一致。页面和 API 必须能表达 pending、posted、failed、held、voided 等业务状态，而不是假设同步提交完成整条链。
4. 补偿语义会分散在拥有服务内，短期比中心流程图更难“一眼看完”；对应收益是服务边界更清晰、单机/私有化运维成本更低、DLQ/replay 与当前消息基线一致。
5. 若未来触发 process-manager 评估，现有事件、幂等键、DLQ 和补偿事实仍可作为流程管理器的输入，不需要回退到共享数据库或跨服务事务。

## Implementation Notes

1. #508 应把 DemandPlanning accepted purchase suggestion 到 ERP PurchaseRequisition 的创建做成 ERP 消费者路径；保留既有 HTTP API 兼容，不让前端编造 ERP requisition number。
2. #509 应优先在 ERP 内部从采购收货事实创建 AP candidate/payable，并用收货号、行号、组织、环境和金额来源保护幂等。
3. #510 应优先由 ERP 消费 WMS outbound completion，匹配 ERP delivery/sales order 后创建 AR candidate/receivable；非 ERP 来源应忽略或按契约错误进入 DLQ。
4. 以上 PR 的测试必须证明真实事件/converter/consumer 路径和重复投递行为；metadata-only acceptance surface 不能替代闭环验证。
5. 若这些 PR 需要新增或提升公共事件，必须同步 Contracts DTO、事件常量、序列化测试、消费者契约测试，以及 OpenAPI/api-client 或 schema/catalog 影响说明。

## Out of Scope

1. 本 ADR 不实现 saga/process-manager、流程引擎、workflow 表或新消费者。
2. 本 ADR 不改变任何现有 event payload、database schema、OpenAPI snapshot 或 generated api-client。
3. 本 ADR 不关闭 #485，也不实现 #507、#508、#509、#510 或 #511。
