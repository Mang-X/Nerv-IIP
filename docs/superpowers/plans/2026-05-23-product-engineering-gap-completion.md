# 产品工程缺口补全实施计划

> **面向智能体执行者：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**扩展现有 ProductEngineering 服务，使其能力从 ProductionVersion 延伸到工程文档、工程物料、EBOM、MBOM、工艺路线和 ECO/ECN 发布事实，从而完成 #127。

**架构：**这是增量计划，不是搭建骨架的计划。保留现有 ProductEngineering 服务、数据库迁移基线和 ProductionVersion API，再围绕它们添加缺失的聚合和发布 API。ProductEngineering 拥有已发布的工程事实并发出发布事件；它只能通过公开 ID 引用 MasterData 和 FileStorage。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core PostgreSQL、xUnit、CAP 风格的集成事件转换、`Nerv.IIP.Testing` 数据库模式约定辅助工具。

---

## 当前代码事实

现有文件包括：

1. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs`
2. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
3. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ProductionVersions/ProductionVersionEndpoints.cs`
4. `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductionVersionAggregateTests.cs`
5. `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductionVersionApiContractTests.cs`

不得为此服务运行 `dotnet new`。

## 文件

- 修改：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/ProductEngineeringFacts.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringDocumentAggregate/EngineeringDocument.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringItemAggregate/EngineeringItem.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringChangeAggregate/EngineeringChange.cs`
- 修改：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs`
- 修改：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringDocumentEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringItemEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringBomEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/ManufacturingBomEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/RoutingEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringChangeEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEvents/ProductEngineeringIntegrationEvents.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEventConverters/ProductEngineeringIntegrationEventConverters.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/EngineeringDocuments/EngineeringDocumentEndpoints.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/EngineeringBoms/EngineeringBomEndpoints.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ManufacturingBoms/ManufacturingBomEndpoints.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/Routings/RoutingEndpoints.cs`
- 新建：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/EngineeringChanges/EngineeringChangeEndpoints.cs`
- 新建：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringReleaseAggregateTests.cs`
- 新建：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- 新建：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringIntegrationEventTests.cs`
- 新建：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringSchemaConventionTests.cs`

由 #140 请求的共享文件：

- `backend/Nerv.IIP.sln`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-product-engineering-mvp.ps1`

## 任务 1：建立当前 ProductEngineering 的基线

- [ ] **步骤 1：读取当前聚合和端点事实**

读取：

```powershell
Get-Content backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs
Get-Content backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ProductionVersions/ProductionVersionEndpoints.cs
Get-Content backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductionVersionApiContractTests.cs
```

预期：在添加新的发布事实前，已理解当前 ProductionVersion 行为。

- [ ] **步骤 2：运行聚焦的基线测试**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

预期：变更前测试通过。如果测试失败，必须在实施前将失败测试名称记录到 PR 中。

## 任务 2：添加工程发布领域模型

- [ ] **步骤 1：添加预期失败的领域测试**

在 `ProductEngineeringReleaseAggregateTests.cs` 中添加测试，覆盖以下场景：

1. EngineeringDocument 登记 FileStorage 文件引用，并拒绝空白的 `fileId`。
2. EngineeringItem 为一个 SKU 编码和修订版创建已发布的物料引用。
3. EngineeringBom 发布后，其组件不可变。
4. ManufacturingBom 发布时引用已发布的 EngineeringBom 和工艺配方/公式行。
5. Routing 发布时创建有序的工序步骤，并包含工作中心引用。
6. EngineeringChange 发布时引用受影响的文档、EBOM、MBOM、工艺路线或 ProductionVersion ID。
7. ProductionVersion 不得绑定未发布的 MBOM 或工艺路线。

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ProductEngineeringReleaseAggregateTests
```

实施前预期：由于新的聚合类型尚不存在，编译失败。

- [ ] **步骤 2：实施聚合根和值对象**

实施“文件”一节列出的聚合文件。新实体 ID 使用 `Guid.CreateVersion7()`，基础设施层仓储使用异步仓储模式，并使用公开的 MasterData/FileStorage ID，而不是跨服务对象引用。

必须具备以下聚合行为：

1. `EngineeringDocument.Register(...)`
2. `EngineeringItem.CreateRevision(...)`
3. `EngineeringBom.Release(...)`
4. `ManufacturingBom.ReleaseFromEngineeringBom(...)`
5. `Routing.Release(...)`
6. `EngineeringChange.Release(...)`
7. `ProductionVersion.Create(...)` 或等效方法必须通过 ProductEngineering 中可用的状态字段验证已发布的 MBOM 和工艺路线引用。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
```

预期：所有 ProductEngineering 领域测试均通过。

## 任务 3：添加持久化和事件

- [ ] **步骤 1：添加 DbSet 和实体配置**

更新 `ApplicationDbContext.cs`，为所有新聚合根添加 DbSet，并添加满足以下要求的实体配置：

1. 使用 `product_engineering` 数据库模式。
2. 包含表注释和列注释。
3. 为文档编号/修订版、物料编码/修订版、BOM 编码/修订版和工艺路线编码/修订版等业务键建立必需的唯一索引。
4. 数据库迁移历史记录已经配置为 `product_engineering.__EFMigrationsHistory`。

- [ ] **步骤 2：生成数据库迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add CompleteProductEngineeringReleaseFacts --project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj --startup-project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj --output-dir Migrations
```

预期：在 ProductEngineering 基础设施层的数据库迁移目录下创建新的数据库迁移。

- [ ] **步骤 3：添加事件转换器测试**

创建 `ProductEngineeringIntegrationEventTests.cs` 并验证以下事件名称：

1. `productEngineering.BomReleased`
2. `productEngineering.RoutingReleased`
3. `productEngineering.ProductionVersionCreated`
4. `productEngineering.EngineeringChangeReleased`

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ProductEngineeringIntegrationEventTests
```

预期：事件转换器测试通过。

## 任务 4：添加 API 接口

- [ ] **步骤 1：添加端点契约测试**

创建 `ProductEngineeringReleaseApiContractTests.cs` 并断言：

1. 新端点必须进行内部服务授权。
2. 操作 ID 保持稳定并使用 ProductEngineering 命名。
3. 发布命令拒绝空白的组织、环境、编码、修订版和文件 ID。
4. 解析 ProductionVersion 的行为仍与现有测试预期一致。

- [ ] **步骤 2：实施端点和命令**

在“文件”一节列出的端点文件夹下添加 FastEndpoints。必须提供以下端点：

1. `POST /api/business/v1/engineering/documents`
2. `POST /api/business/v1/engineering/items`
3. `POST /api/business/v1/engineering/engineering-boms/release`
4. `POST /api/business/v1/engineering/manufacturing-boms/release`
5. `POST /api/business/v1/engineering/routings/release`
6. `POST /api/business/v1/engineering/engineering-changes/release`
7. `GET /api/business/v1/engineering/engineering-boms`
8. `GET /api/business/v1/engineering/routings`

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

预期：所有 ProductEngineering Web 层测试均通过。

## 任务 5：向 #140 移交共享变更

- [ ] **步骤 1：创建 PR 摘要章节**

在本次会话的 PR 正文中包含以下内容：

```markdown
## Shared Changes Needed

- Add ProductEngineering projects/tests to `backend/Nerv.IIP.sln` if missing.
- Register ProductEngineering in AppHost after Web project compiles.
- Add ProductEngineering permissions to IAM seed and `authorization-matrix.md`.
- Add ProductEngineering schema entries to `database-schema-catalog.md`.
- Add or refresh `scripts/verify-business-product-engineering-mvp.ps1`.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。
