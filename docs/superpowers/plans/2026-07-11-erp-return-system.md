# ERP 退货系统实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**在不跨越服务数据边界的前提下，交付 MAN-397 可审计的采购退货与销售 RMA 闭环。

**架构：**WMS 发布已完成的实物退货事实；ERP 记录不可变的冲销单据和凭证；Quality 提供 RMA 贷项决策。公开契约携带稳定的业务标识符，每个消费者使用自己的本地 inbox（收件箱）。

**技术栈：**.NET 10、CleanDDD/NetCorePal、FastEndpoints、EF Core migration、CAP 集成事件、xUnit。

---

### 任务 1：冻结会计政策和公开事件契约

**文件：**
- 创建：`docs/architecture/erp-return-accounting-rules.md`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Erp/ErpIntegrationEvents.cs`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Wms/WmsIntegrationEvents.cs`
- 测试：`backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventContractTests.cs`

- [ ] **步骤 1：编写失败的序列化测试**，覆盖 `erp.SalesReturnAuthorized` 和供应商退货 WMS 完成事件的来源元数据，并断言必需的信封字段和行引用。
- [ ] **步骤 2：运行** `dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore`，确认新契约测试因类型/常量不存在而失败。
- [ ] **步骤 3：添加只增不减的 v1 契约**，包含 `RmaNo`、客户/站点/行事实和 WMS `SourceDocumentType`/`SourceDocumentId`；保留既有字段和事件版本。
- [ ] **步骤 4：重新运行**契约项目并确认通过。

### 任务 2：让 WMS 负责两种实物退货执行

**文件：**
- 修改：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/SupplierReturnAggregate/SupplierReturnRequest.cs`
- 修改：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/ErpSalesReturnAuthorizedIntegrationEventHandler.cs`
- 修改：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs`
- 测试：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsReturnIntegrationEventTests.cs`

- [ ] **步骤 1：编写失败的 WMS 测试**，验证被拒绝的供应商收货会创建一个 `purchase-receipt-return` 出库，重放的 ERP RMA 授权会创建一个经过质量门禁的入库。
- [ ] **步骤 2：运行**指定的 WMS 测试，并确认它因缺少退货执行/消费者行为而失败。
- [ ] **步骤 3：只实现已测试的行为：**根据被拒收货的维度创建出库行，通过 inbox（收件箱）守卫消费 ERP RMA 事件，并发布带原始来源引用的实际 WMS 完成事实。
- [ ] **步骤 4：重新运行** WMS 测试，确认它在不检查其他服务数据库的情况下通过。

### 任务 3：添加 ERP 冲销退货和通知单聚合

**文件：**
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReturnAggregate/PurchaseReturn.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesReturnAuthorizationAggregate/SalesReturnAuthorization.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DebitNoteAggregate/DebitNote.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/CreditNoteAggregate/CreditNote.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- 测试：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpReturnAggregateTests.cs`

- [ ] **步骤 1：编写失败的聚合测试**，覆盖收货行退货上限、AP 借项通知单应用、AR 贷项通知单应用和 RMA 质量状态。
- [ ] **步骤 2：运行** `dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore`，确认新聚合/方法导致测试失败。
- [ ] **步骤 3：实现最小的不可变单据和 AP/AR 应用计数器**；拒绝超额退货、超额贷记和重复状态转换。
- [ ] **步骤 4：重新运行** ERP 领域测试并确认通过。

### 任务 4：持久化并入账 ERP 退货冲销

**文件：**
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpProcurementEntityTypeConfigurations.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- 创建：由 EF 生成的 `AddErpReturnSystem` migration，位于 `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ErpFinanceCommands.cs`
- 测试：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpReturnAccountingTests.cs`

- [ ] **步骤 1：编写失败的命令测试**，覆盖未开票 GR/IR 冲销、已匹配发票的借项通知单/AP 减记，以及凭证借贷平衡的贷项通知单/AR 减记。
- [ ] **步骤 2：运行**指定的 ERP Web 测试，确认它在退货入账实现前失败。
- [ ] **步骤 3：添加表映射、显式列注释/索引、migration 和凭证 factory 方法：**采购退货对未开票数量使用借方 `GR-IR`/贷方 `1401`；借项通知单使用借方 `2202`/贷方 `1401`；贷项通知单使用借方 `6001`/贷方 `1122`。
- [ ] **步骤 4：重新运行** ERP Web 测试和 ERP schema 约定测试。

### 任务 5：接入消费者、API 治理和真实闭环验证

**文件：**
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WmsReturnIntegrationEventHandlers.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/QualityRmaInspectionResultIntegrationEventHandler.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesFinanceEndpoints.cs`
- 修改：`docs/architecture/integration-event-consumption-matrix.md`
- 修改：`docs/architecture/facade-coverage-matrix.json`
- 修改：`docs/architecture/database-schema-catalog.md`
- 测试：`backend/tests/Nerv.IIP.Business.FullChain.Tests/ErpReturnClosurePostgresAcceptanceTests.cs`

- [ ] **步骤 1：编写失败的跨边界验收测试**，驱动 RMA 授权 → WMS 入库完成 → Quality 通过 → 贷项通知单/AR，以及供应商退货 WMS 出库完成 → 采购退货/借项或 GRIR 冲销；重放两个事件并断言只产生一次单据/凭证效果。
- [ ] **步骤 2：运行**目标测试，确认它因事件消费者不存在而失败。
- [ ] **步骤 3：实现带守卫的消费者和延期的 facade 行**，更新事件/schema 文档，并使用 PostgreSQL profile 生成 EF migration。endpoint 仍为延期状态，因此不得改动 Gateway OpenAPI 或生成的客户端代码。
- [ ] **步骤 4：运行** ERP/WMS/契约测试、schema/facade 门禁、完整后端解决方案测试；配置 `NERV_IIP_TEST_POSTGRES` 时，还要运行真实 PostgreSQL 验收测试。
