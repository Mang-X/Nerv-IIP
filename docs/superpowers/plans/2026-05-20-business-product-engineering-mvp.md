# 业务产品工程 MVP 实施计划

> **面向智能体执行者：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**构建轻量版 ProductEngineering，涵盖工程文档、工程物料、EBOM、MBOM、工艺路线、ProductionVersion 绑定和 ECO/ECN 发布流程。

**架构：**创建 `backend/services/Business/ProductEngineering`，作为轻量版 PDM/PLM 的归属服务。它存储来自 File Storage 的文件引用、版本化工程事实和发布事件；不实现 CAD 设计、库存、正式工单或 MRP 计算。已发布的 EBOM、MBOM 和工艺路线版本不可变。ProductionVersion 针对一个 SKU、有效期窗口和批量范围绑定已发布的 MBOM 与工艺路线，并为 Planning 和 MES 提供解析 API。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 领域事件/集成事件转换器、xUnit。

---

## MasterData 重对齐依赖

执行本计划前，必须先完成 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。ProductEngineering 必须使用重对齐后的 MasterData 契约来获取 SKU、UOM、资源层级、工作中心、设备资产和参考数据。

对于流程制造，本计划必须将 `Recipe` / `Formula` 和 `ProcessParameter` 视为 ProductEngineering 所有的一等版本化工程事实。MasterData 拥有可复用物料属性、UOM、资源能力和参数定义；ProductEngineering 拥有已发布的产品专用配方/工艺路线版本。

## 输入来源

1. 业务规格需求 `BP-ENG-001` 至 `BP-ENG-004`
2. 架构链路 `CAD/PDM/PLM -> EBOM/MBOM/Routing -> ECO/ECN -> MRP/MES`
3. `business.engineering.*` 下的授权矩阵条目
4. ADR 0011 集成事件信封基线
5. `docs/adr/0013-business-master-data-governance.md`
6. `docs/architecture/business-master-data-process-manufacturing-supplement.md`

## 边界

1. 不解析 CAD 文件，也不存储对象存储键。
2. 不创建采购订单、工单、库存移动或 MRP 建议。
3. 工程变更发布后，不自动更改执行中的 MES 工单。
4. ProductEngineering 不与 MasterData 共享数据表。
5. 不在 ProductEngineering 中存储可复用 UOM、SKU 物料属性、资源层级或设备能力事实；这些事实从 MasterData 解析。

## 文件结构图

```text
backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/
  ProductEngineeringFacts.cs
  AggregatesModel/EngineeringDocumentAggregate/EngineeringDocument.cs
  AggregatesModel/EngineeringItemAggregate/EngineeringItem.cs
  AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs
  AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs
  AggregatesModel/RoutingAggregate/Routing.cs
  AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs
  AggregatesModel/EngineeringChangeAggregate/EngineeringChange.cs
  DomainEvents/ProductEngineeringDomainEvents.cs

backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/
  Application/Auth/EngineeringPermissionCodes.cs
  Application/Commands/*.cs
  Application/Queries/*.cs
  Application/IntegrationEvents/*.cs
  Application/IntegrationEventConverters/*.cs
  Endpoints/Engineering/*.cs

backend/services/Business/ProductEngineering/tests/
  Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringAggregateTests.cs
  Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringEndpointTests.cs
  Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringIntegrationEventTests.cs
  Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringSchemaConventionTests.cs
```

## 任务 1：搭建 ProductEngineering 服务脚手架

**文件：**

- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/Nerv.IIP.Business.ProductEngineering.Domain.csproj`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.ProductEngineering -o backend/services/Business/ProductEngineering --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.ProductEngineering.Domain.Tests -o backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.ProductEngineering.Web.Tests -o backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/Nerv.IIP.Business.ProductEngineering.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj
```

预期：新增项目不引用 Inventory、MES、WMS 或 ERP。

- [ ] **步骤 2：提交脚手架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/ProductEngineering
git commit -m "feat: scaffold product engineering service"
```

## 任务 2：添加版本化工程聚合

**文件：**

- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/ProductEngineeringFacts.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringDocumentAggregate/EngineeringDocument.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringItemAggregate/EngineeringItem.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringChangeAggregate/EngineeringChange.cs`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringAggregateTests.cs`

- [ ] **步骤 1：为发布后的不可变性编写失败测试**

为以下场景创建测试：

```csharp
EngineeringDocument.Register("org-001", "env-dev", "file-001", "cad-drawing", "A");
EngineeringItem.Create("org-001", "env-dev", "ENG-1000", "Pump Assembly");
EngineeringBom.CreateDraft("org-001", "env-dev", "ENG-1000", "A").AddLine("ENG-1001", 2m, "EA").Release(DateOnly.FromDateTime(DateTime.UtcNow));
ManufacturingBom.CreateDraft("org-001", "env-dev", "SKU-FG-1000", "A").AddLine("SKU-RM-1000", 1.5m, "KG").Release(DateOnly.FromDateTime(DateTime.UtcNow));
Routing.CreateDraft("org-001", "env-dev", "SKU-FG-1000", "A").AddOperation(10, "WC-CNC-01", 30).Release(DateOnly.FromDateTime(DateTime.UtcNow));
ProductionVersion.Create("org-001", "env-dev", "SKU-FG-1000", "mbom-A", "routing-A", DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, 10, true, EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
EngineeringChange.Open("org-001", "env-dev", "ECO-0001", "release mbom A").Approve("approval-chain-001").Release();
```

断言已发布的 EBOM、MBOM 和工艺路线对 `AddLine`、`AddOperation` 和 `Rename` 调用抛出 `InvalidOperationException` 并予以拒绝。

初始预期结果：FAIL，因为聚合尚不存在。

- [ ] **步骤 2：实现聚合规则**

实现以下不变量：

| 聚合 | 不变量 |
| --- | --- |
| EngineeringDocument | `fileId + version` 是幂等键；只存储 `fileId`，不存储对象存储键。 |
| EngineeringItem | 生命周期为 `draft`、`released`、`archived`；已发布物料不能直接重命名。 |
| EngineeringBom | 同一版本内的子项不能重复；已发布版本不可变。 |
| ManufacturingBom | 所有行都引用 SKU 编码；已发布版本不可变。 |
| Routing | 工序顺序必须唯一且为正数；工作中心编码为必填项。 |
| ProductionVersion | 只绑定已发布的 MBOM/工艺路线，拒绝无效的有效期/批量窗口，并且归档版本不能为新工单解析。 |
| EngineeringChange | 发布必须提供审批引用和受影响版本列表。 |

创建名为 `EngineeringDocumentRegisteredDomainEvent`、`EngineeringBomReleasedDomainEvent`、`ManufacturingBomReleasedDomainEvent`、`RoutingReleasedDomainEvent` 和 `EngineeringChangeReleasedDomainEvent` 的领域事件。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
```

预期：PASS。

- [ ] **步骤 4：提交领域实现**

运行：

```powershell
git add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests
git commit -m "feat: add product engineering versioned aggregates"
```

## 任务 3：添加持久化和集成事件

**文件：**

- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/*.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEvents/ProductEngineeringIntegrationEvents.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEventConverters/ProductEngineeringIntegrationEventConverters.cs`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringIntegrationEventTests.cs`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringSchemaConventionTests.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：定义稳定的事件契约**

创建以下记录类型：

```csharp
public sealed record BomReleasedIntegrationEvent(string BomVersionId, string BomType, string ItemOrSkuCode, IReadOnlyCollection<BomReleasedLine> Lines, DateOnly EffectiveDate);
public sealed record RoutingReleasedIntegrationEvent(string RoutingVersionId, string SkuCode, IReadOnlyCollection<RoutingReleasedOperation> Operations, DateOnly EffectiveDate);
public sealed record EngineeringChangeReleasedIntegrationEvent(string ChangeId, IReadOnlyCollection<string> AffectedVersionIds, DateOnly EffectiveDate);
```

测试必须序列化这些记录，并断言属性名称保持 camelCase。

- [ ] **步骤 2：添加 EF 映射**

使用 `product_engineering` schema 和以下数据表：`engineering_documents`、`engineering_items`、`engineering_boms`、`manufacturing_boms`、`routings`、`production_versions`、`engineering_changes`。为每个业务列添加注释，并为组织/环境与编码/版本的组合添加唯一索引。

- [ ] **步骤 3：生成迁移并更新目录**

运行：

```powershell
dotnet ef migrations add InitialProductEngineering --project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj --startup-project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj --output-dir Migrations
```

预期：迁移只创建 `product_engineering` schema 对象。

- [ ] **步骤 4：运行持久化和事件测试**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductEngineeringIntegrationEventTests|FullyQualifiedName~ProductEngineeringSchemaConventionTests"
```

预期：PASS。

- [ ] **步骤 5：提交持久化和事件实现**

运行：

```powershell
git add backend/services/Business/ProductEngineering docs/architecture/database-schema-catalog.md
git commit -m "feat: persist product engineering releases"
```

## 任务 4：添加工程 API 接口面

**文件：**

- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Auth/EngineeringPermissionCodes.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/RegisterEngineeringDocumentCommand.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseEngineeringBomCommand.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseManufacturingBomCommand.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseRoutingCommand.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/CreateProductionVersionCommand.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductionVersions/ResolveProductionVersionQuery.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseEngineeringChangeCommand.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ListEngineeringBomsQuery.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/GetEngineeringChangeQuery.cs`
- 新增：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/Engineering/EngineeringEndpoints.cs`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringEndpointTests.cs`
- 新增：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringOpenApiTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **步骤 1：编写端点测试**

覆盖：

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/engineering/documents` | `business.engineering.documents.manage` |
| `POST /api/business/v1/engineering/items` | `business.engineering.items.manage` |
| `POST /api/business/v1/engineering/engineering-boms/release` | `business.engineering.boms.manage` |
| `POST /api/business/v1/engineering/manufacturing-boms/release` | `business.engineering.boms.manage` |
| `POST /api/business/v1/engineering/routings/release` | `business.engineering.routings.manage` |
| `POST /api/business/v1/engineering/engineering-changes/release` | `business.engineering.changes.manage` |
| `GET /api/business/v1/engineering/engineering-boms` | `business.engineering.boms.read` |
| `GET /api/business/v1/engineering/routings` | `business.engineering.routings.read` |
| `GET /api/business/v1/engineering/production-versions` | `business.engineering.production-versions.read` |
| `GET /api/business/v1/engineering/production-versions/resolve` | `business.engineering.production-versions.read` |
| `POST /api/business/v1/engineering/production-versions` | `business.engineering.production-versions.manage` |
| `PUT /api/business/v1/engineering/production-versions/{productionVersionId}` | `business.engineering.production-versions.manage` |
| `POST /api/business/v1/engineering/production-versions/{productionVersionId}/archive` | `business.engineering.production-versions.manage` |

测试必须断言已发布版本不能通过 API 更改。

- [ ] **步骤 2：实现权限常量和 IAM 初始数据**

只使用以下常量：

```csharp
business.engineering.documents.read
business.engineering.documents.manage
business.engineering.boms.read
business.engineering.boms.manage
business.engineering.changes.read
business.engineering.changes.manage
```

- [ ] **步骤 3：实现处理器**

命令校验组织/环境范围、文件登记和版本发布所用的幂等键，以及 File Storage 引用结构。本切片在本地校验 `fileId`、`fileName`、`contentType` 和 `version` 字段，并拒绝空白文件引用；它不调用 File Storage，因为 ProductEngineering 必须保持可独立测试。

- [ ] **步骤 4：运行 API 测试**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

预期：PASS。

- [ ] **步骤 5：提交 API 实现**

运行：

```powershell
git add backend/services/Business/ProductEngineering backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose product engineering api"
```

## 任务 5：添加验证与就绪状态说明

**文件：**

- 新增：`scripts/verify-business-product-engineering-mvp.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：添加验证脚本**

在脚本内运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

- [ ] **步骤 2：运行最终验证**

运行：

```powershell
scripts/verify-business-product-engineering-mvp.ps1
git diff --check
```

预期：两个命令均以退出码 `0` 结束。

- [ ] **步骤 3：提交验证文档**

运行：

```powershell
git add scripts/verify-business-product-engineering-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record product engineering readiness"
```

## 自审清单

1. `BP-ENG-001` 至 `BP-ENG-004` 均由测试和端点覆盖。
2. 已发布的 EBOM、MBOM、Routing 和 EngineeringChange 事实不可变；ProductionVersion 只绑定已发布的 MBOM/Routing，并为 MES/MRP 解析有效且未归档的版本。
3. 事件使用兼容 ADR 0011 信封的载荷，且不包含对象存储键。
4. ProductEngineering 只存储文件引用和已发布的工程事实。
