# ERP 采购 MVP 实施计划

> **面向自主代理：**必须使用以下子技能之一逐项实施本计划：superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**通过创建 ERP 服务骨架以及从计划建议到采购收货的采购/SRM-lite 流程来实施 #137。

**架构：**ERP 是 `backend/services/Business/Erp` 下的 CleanDDD 业务服务。本计划只创建服务基础和采购事实。销售、财务、AppHost 注册和最终 ERP 聚合分别由其它计划负责。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换、`Nerv.IIP.Testing` 数据库 schema 约定辅助工具。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`。

## 文件

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/Nerv.IIP.Business.Erp.Domain.csproj`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseRequisitionAggregate/PurchaseRequisition.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/RequestForQuotationAggregate/RequestForQuotation.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SupplierQuotationAggregate/SupplierQuotation.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseOrderAggregate/PurchaseOrder.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpProcurementDomainEvents.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/Procurement*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Auth/ErpPermissionCodes.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Procurement/*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpProcurementIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpProcurementAggregateTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpProcurementEndpointContractTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpProcurementIntegrationEventTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

需由 ERP-INTEG 处理的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-erp-procurement-mvp.ps1`

## 任务 1：在本地搭建 ERP 服务骨架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Erp -o backend/services/Business/Erp --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Erp.Domain.Tests -o backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Erp.Web.Tests -o backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests --framework net10.0
```

- [ ] **步骤 2：删除模板演示代码**

运行：

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Erp
```

预期：无匹配项。

## 任务 2：实施采购领域

- [ ] **步骤 1：编写会失败的聚合测试**

覆盖：

1. 可以从 DemandPlanning 建议引用创建采购申请。
2. RFQ 必须至少包含一个供应商和一个询价物料。
3. 供应商报价拒绝非正数的数量或价格。
4. 采购订单拒绝空行项目。
5. 采购收货数量不得超过采购订单的未结数量。
6. 采购收货会发出领域事件，并且记录后不可变。

- [ ] **步骤 2：实施采购聚合和值对象**

跨服务引用使用公开编码或 ID：`suggestionId`、`supplierCode`、`skuCode`、`siteCode`、`purchaseOrderId`。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ErpProcurementAggregateTests
```

预期：采购领域测试通过。

## 任务 3：添加持久化和数据库 schema 防护门禁

- [ ] **步骤 1：配置 DbContext**

使用数据库 schema `erp` 和迁移历史表 `erp.__EFMigrationsHistory`。只为采购聚合添加 DbSet 映射。添加数据库 schema 测试，拒绝库存余额或仓储执行所有权泄漏。

- [ ] **步骤 2：生成初始迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialErpProcurementSchema --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：运行数据库 schema 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSchemaConventionTests
```

预期：数据库 schema 约定测试通过。

## 任务 4：添加采购 API 和事件

- [ ] **步骤 1：添加端点契约测试**

验证路由、operation ID（操作标识）、权限代码和 `InternalServiceAuthorizationPolicy.Name`：

1. `POST /api/business/v1/erp/purchase-requisitions/from-suggestion`
2. `POST /api/business/v1/erp/rfqs`
3. `POST /api/business/v1/erp/supplier-quotations`
4. `POST /api/business/v1/erp/purchase-orders`
5. `POST /api/business/v1/erp/purchase-receipts`
6. `GET /api/business/v1/erp/purchase-orders`

- [ ] **步骤 2：实施命令、查询和 FastEndpoints**

将业务逻辑保留在命令处理器和领域聚合中。启动过程不得映射 Minimal API 路由。

- [ ] **步骤 3：添加事件转换器测试**

验证：

1. `erp.PurchaseRequisitionCreated`
2. `erp.PurchaseOrderReleased`
3. `erp.PurchaseReceiptRecorded`

- [ ] **步骤 4：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：ERP Web 测试通过。

## 任务 5：移交共享变更

- [ ] **步骤 1：记录共享变更**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add ERP projects/tests to `backend/Nerv.IIP.sln`.
- Register ERP in AppHost after at least procurement compiles.
- Add ERP procurement permissions to IAM seed and `authorization-matrix.md`.
- Add `erp` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-erp-procurement-mvp.ps1`.
- Reserve local port 5118 for `business-erp` unless the port matrix changes.
```

- [ ] **步骤 2：运行聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。

## 自审清单

1. 采购接受计划建议引用，不要求访问 DemandPlanning 内部实现。
2. 收货数量不得超过订购数量。
3. ERP 不存储 Inventory 余额、WMS 任务状态或 MES 工序状态。
4. 共享变更已明确移交给 ERP-INTEG。
