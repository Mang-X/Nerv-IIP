# ERP 销售 MVP 实施计划

> **面向自主代理：**必须使用以下子技能之一逐项实施本计划：superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**通过向 ERP 服务添加销售/CRM-lite/OMS-lite 事实来实施 #138。

**架构：**在 #137 的 ERP 骨架已存在后，销售能力扩展 `backend/services/Business/Erp`。它拥有销售机会、报价单、销售订单和发货单请求事实。WMS 拥有仓储执行；Inventory 拥有余额和库存移动。

**技术栈：**.NET 10、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`。

## 前置条件

1. `backend/services/Business/Erp` 已存在。
2. ERP 的领域、基础设施和 Web 项目能够编译。
3. 采购计划已建立 ERP 权限代码、端点契约和数据库 schema 约定模式。

## 文件

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/OpportunityAggregate/Opportunity.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/QuotationAggregate/Quotation.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpSalesDomainEvents.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/Sales*.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Auth/ErpPermissionCodes.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Sales/*.cs`
- 修改：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesEndpoints.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpSalesAggregateTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesEndpointContractTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesIntegrationEventTests.cs`
- 修改：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

需由 ERP-INTEG 处理的共享文件：

- IAM 初始数据及授权矩阵中的销售权限补充项。
- 数据库 schema 目录中的销售表补充项。
- `scripts/verify-business-erp-sales-mvp.ps1`。

## 任务 1：实施销售领域

- [ ] **步骤 1：编写会失败的聚合测试**

覆盖：

1. 销售机会必须包含客户引用和主题。
2. 报价单必须包含行项目以及正数的数量/价格。
3. 未批准的报价单不能创建销售订单。
4. 已过期或已拒绝的报价单不能创建销售订单。
5. 发货单数量不得超过销售订单的剩余数量。
6. 发货单会发出 `DeliveryOrderReleased` 领域事件。

- [ ] **步骤 2：实施销售聚合**

只使用公开 ID 和单据引用。除稳定引用/快照外，不得存储 WMS 任务状态、Inventory 余额或客户主数据字段。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSalesAggregateTests
```

预期：销售领域测试通过。

## 任务 2：扩展持久化

- [ ] **步骤 1：添加销售映射**

在数据库 schema `erp` 中映射销售机会、报价单、销售订单和发货单表。

- [ ] **步骤 2：添加迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddErpSalesSchema --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：运行数据库 schema 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSchemaConventionTests
```

预期：数据库 schema 测试通过。

## 任务 3：添加销售 API 和事件

- [ ] **步骤 1：添加端点契约测试**

验证：

1. `POST /api/business/v1/erp/opportunities`
2. `POST /api/business/v1/erp/quotations`
3. `POST /api/business/v1/erp/quotations/{quotationId}/approve`
4. `POST /api/business/v1/erp/sales-orders`
5. `POST /api/business/v1/erp/delivery-orders`
6. `GET /api/business/v1/erp/sales-orders`

- [ ] **步骤 2：实施命令、查询和端点**

保持审批状态显式。如果后续接入 BusinessApproval，只在报价单上存储审批链引用。

- [ ] **步骤 3：添加事件转换器测试**

验证：

1. `erp.DeliveryOrderReleased`
2. 如果实现发布了 `erp.SalesOrderCreated`，则可选择验证该事件。

- [ ] **步骤 4：运行 ERP Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：ERP Web 测试通过。

## 任务 4：移交共享变更

- [ ] **步骤 1：记录共享变更**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add sales permissions to IAM seed and `authorization-matrix.md`.
- Add sales tables to `database-schema-catalog.md`.
- Add/update `scripts/verify-business-erp-sales-mvp.ps1`.
- Confirm WMS outbound integration uses public delivery order references only.
```

- [ ] **步骤 2：运行聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。

## 自审清单

1. 销售下达会创建发货请求事实，而不是 WMS 执行事实。
2. 发货数量不得超过订购数量。
3. 报价单审批是显式的，并且已由测试覆盖。
4. 共享变更已明确移交给 ERP-INTEG。
