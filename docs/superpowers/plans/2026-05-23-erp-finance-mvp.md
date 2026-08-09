# ERP 财务 MVP 实施计划

> **供代理执行者使用：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**通过向 ERP 添加财务 MVP 事实来实现 #139：AP、AR、借贷平衡的凭证和成本候选。

**架构：**采购收货和销售发货事件形状稳定后，财务能力在 ERP 服务中扩展。财务能力根据 ERP、WMS、Inventory 和 MES 的公开事实创建候选事实和过账事实。它不实现完整的总账结账。

**技术栈：**.NET 10、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`。

## 前置条件

1. ERP 采购收货事实已经存在。
2. ERP 销售发货单事实已经存在。
3. 已完成的第 1/2 波服务能够提供 WMS、Inventory 和 MES 公开事实。

## 文件

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/JournalVoucherAggregate/JournalVoucher.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/CostCandidateAggregate/CostCandidate.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpFinanceDomainEvents.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/Finance*.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Auth/ErpPermissionCodes.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Finance/*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/Finance*.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpFinanceIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpFinanceEndpoints.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpFinanceAggregateTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpFinanceEndpointContractTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpFinanceIntegrationEventTests.cs`
- 修改：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

请求 ERP-INTEG 处理的共享文件：

- 为财务权限补充 IAM 初始数据和授权矩阵。
- 在 schema 目录中补充财务表。
- `scripts/verify-business-erp-finance-mvp.ps1`.
- 最终脚本 `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`。

## 任务 1：实现财务领域

- [ ] **步骤 1：编写失败的聚合测试**

覆盖：

1. 应付账款金额必须为正数。
2. AP 已付金额不能超过未结金额。
3. 应收账款金额必须为正数。
4. AR 已收金额不能超过未结金额。
5. 除非借方合计等于贷方合计，否则会计凭证不能过账。
6. 已过账凭证不可变。
7. 成本候选至少引用一个来源事实：MES 报工、Inventory 库存移动或 WMS 完成事实。

- [ ] **步骤 2：实现财务聚合**

显式使用十进制精度，并将货币代码建模为必填的稳定字段。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ErpFinanceAggregateTests
```

预期：财务领域测试通过。

## 任务 2：扩展持久化

- [ ] **步骤 1：添加财务映射**

在 schema `erp` 中映射 AP、AR、会计凭证、凭证明细和成本候选表。

- [ ] **步骤 2：添加迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddErpFinanceSchema --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：运行 schema 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSchemaConventionTests
```

预期：schema 测试通过。

## 任务 3：添加财务 API 和事件

- [ ] **步骤 1：添加端点契约测试**

验证：

1. `POST /api/business/v1/erp/finance/payables`
2. `POST /api/business/v1/erp/finance/receivables`
3. `POST /api/business/v1/erp/finance/cost-candidates`
4. `POST /api/business/v1/erp/finance/vouchers`
5. `GET /api/business/v1/erp/finance/summary`

- [ ] **步骤 2：实现命令、查询和端点**

对于下游事实可能重复投递的情况，按来源单据引用保持财务命令的幂等性。

- [ ] **步骤 3：添加事件转换器测试**

验证：

1. `erp.AccountPayableCreated`
2. `erp.AccountReceivableCreated`
3. `erp.CostCandidateCreated`
4. `erp.JournalVoucherPosted`

- [ ] **步骤 4：添加事件处理器测试**

使用公共事件契约或桩实现，覆盖根据 ERP 收货、ERP 发货、WMS 完成、Inventory 库存移动或 MES 报工创建候选事实。处理器测试不得引用其他服务的 Domain 或 Infrastructure 项目。

- [ ] **步骤 5：运行 ERP Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：ERP Web 测试通过。

## 任务 4：交接共享变更

- [ ] **步骤 1：记录共享变更**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add finance permissions to IAM seed and `authorization-matrix.md`.
- Add finance tables to `database-schema-catalog.md`.
- Add/update `scripts/verify-business-erp-finance-mvp.ps1`.
- Add final `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`.
- Update readiness to state ERP is implemented only after procurement, sales and finance focused verifies pass.
```

- [ ] **步骤 2：运行聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。

## 自审清单

1. 借贷不平衡的凭证不能过账。
2. AP/AR 不能超额付款或超额收款。
3. 财务能力存储候选事实，而不是完整的总账结账事实。
4. 事件处理器只使用公共契约。
