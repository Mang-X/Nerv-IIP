# 质量检验 MVP 实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**实施 #132，在保留现有 NCR 行为的同时，扩展既有 Quality 服务，加入检验计划、检验记录和检验结果事件。

**架构：**这是针对既有服务的增量计划。Quality 继续拥有 NCR，并在同一 `quality` schema 中增加 InspectionPlan 和 InspectionRecord 聚合。Quality 发布检验结果事件，也可以根据失败记录创建 NCR，但绝不直接修改 Inventory、WMS、ERP 或 MES。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core PostgreSQL、xUnit、CAP 风格的集成事件转换、`Nerv.IIP.Testing` schema 约定辅助工具。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-quality-inspection-mvp-design.md` 作为本计划的领域契约。

## 当前代码事实

现有 Quality 文件包括：

1. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs`
2. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
3. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/NonconformanceReports/NonconformanceReportEndpoints.cs`
4. `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/NonconformanceReportAggregateTests.cs`
5. `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs`

不得为 Quality 运行 `dotnet new`。

## 文件

- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/QualityFacts.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionPlanAggregate/InspectionPlan.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionPlanEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionRecordEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Auth/BusinessPermissionCodes.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionPlans/CreateInspectionPlanCommand.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionPlans/ActivateInspectionPlanCommand.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/OpenNcrFromInspectionCommand.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionPlans/ListInspectionPlansQuery.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionRecords/ListInspectionRecordsQuery.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/InspectionPlans/InspectionPlanEndpoints.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/InspectionRecords/InspectionRecordEndpoints.cs`
- 创建：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/InspectionAggregateTests.cs`
- 创建：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionEndpointContractTests.cs`
- 创建：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionIntegrationEventTests.cs`
- 修改：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs`

#140 请求的共享文件：

- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-quality-inspection-mvp.ps1`

## Task 1：建立当前 Quality 基线

- [ ] **步骤 1：阅读当前 NCR 行为**

阅读：

```powershell
Get-Content backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs
Get-Content backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/NonconformanceReports/NonconformanceReportEndpoints.cs
Get-Content backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs
```

预期：理解并保留现有 NCR 行为。

- [ ] **步骤 2：运行聚焦的基线测试**

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
```

预期：测试在变更前通过。如果失败，在 PR 中记录失败的测试。

## Task 2：添加检验领域模型

- [ ] **步骤 1：编写聚合测试**

创建 `InspectionAggregateTests.cs`，覆盖：

1. 草稿状态的检验计划可以添加特性。
2. 已激活的检验计划不能修改执行特性。
3. 新计划版本取代旧计划。
4. 所有必需特性均通过时，检验记录判定为通过。
5. 任一必需特性失败时，检验记录判定为拒收。
6. 检验失败时，可以创建与检验记录关联的 NCR。

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~InspectionAggregateTests
```

实施前预期：由于检验聚合尚不存在，编译失败。

- [ ] **步骤 2：实施 InspectionPlan 和 InspectionRecord**

实施 `InspectionPlan.cs` 和 `InspectionRecord.cs`，提供对来源单据、SKU、合作方、工作中心和文件附件 ID 的公开引用。实体 ID 使用 `Guid.CreateVersion7()`。

- [ ] **步骤 3：添加 NCR 关联行为**

仅按关联 NCR 与检验记录 ID、来源引用所需扩展 `NonconformanceReport`。除非失败的回归测试证明存在缺陷，否则不得更改现有 NCR 状态转换。

- [ ] **步骤 4：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
```

预期：所有 Quality 领域测试通过。

## Task 3：添加持久化与事件

- [ ] **步骤 1：配置 EF 映射**

在 `quality` schema 中为检验计划和记录添加 DbSet 与实体配置。保留现有 `quality.__EFMigrationsHistory` 配置。

- [ ] **步骤 2：生成 migration**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddQualityInspectionFacts --project backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj --startup-project backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj --output-dir Migrations
```

预期：migration 添加检验表，且不删除或重建 NCR 表。

- [ ] **步骤 3：添加事件转换器测试**

创建 `QualityInspectionIntegrationEventTests.cs` 并验证：

1. `quality.InspectionPassed`
2. `quality.InspectionRejected`
3. 现有 NCR 事件名仍通过测试。

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore --filter FullyQualifiedName~QualityInspectionIntegrationEventTests
```

预期：事件转换器测试通过。

## Task 4：添加 API 接口

- [ ] **步骤 1：添加 endpoint 契约测试**

创建 `QualityInspectionEndpointContractTests.cs`，覆盖：

1. 检验 endpoint 要求内部服务授权。
2. `POST /api/business/v1/quality/inspection-plans` 创建计划。
3. `POST /api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate` 激活计划。
4. `POST /api/business/v1/quality/inspection-records` 记录通过和拒收结果。
5. `POST /api/business/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr` 创建 NCR。
6. OpenAPI operation ID 保持稳定。
7. 现有 NCR endpoint 测试仍通过。

- [ ] **步骤 2：实施 command、query 和 FastEndpoints**

实施“文件”章节列出的 command、query 和 endpoint 文件。使用 Quality 检验规格中定义的权限。

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
```

预期：所有 Quality Web 测试通过。

## Task 5：向 #140 移交共享变更

- [ ] **步骤 1：记录共享变更**

在本会话的 PR 正文中包含：

```markdown
## Shared Changes Needed

- Add Quality inspection permissions to IAM seed and `authorization-matrix.md`.
- Add new inspection tables to `database-schema-catalog.md`.
- Add or refresh `scripts/verify-business-quality-inspection-mvp.ps1`.
- Update readiness to say Quality inspection is complete after focused tests pass.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。
