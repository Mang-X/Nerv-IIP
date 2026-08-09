# BarcodeLabel 最小可行产品（MVP）实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过创建 BarcodeLabel 来实施 #133，涵盖条码规则、标签模板、打印批次和扫描记录。

**架构：**BarcodeLabel 是位于 `backend/services/Business/BarcodeLabel` 下的 CleanDDD 业务服务。它通过公开 ID 引用 MasterData 和 FileStorage，并记录打印/扫描事实。它不拥有库存余额、WMS 执行状态或 FileStorage 对象键。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换、`Nerv.IIP.Testing` 数据库模式约定辅助工具。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-barcode-label-mvp-design.md` 作为本计划的领域契约。

## 文件

- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/Nerv.IIP.Business.BarcodeLabel.Domain.csproj`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Nerv.IIP.Business.BarcodeLabel.Infrastructure.csproj`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/BarcodeRuleAggregate/BarcodeRule.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelTemplateAggregate/LabelTemplate.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/ScanRecordAggregate/ScanRecord.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/DomainEvents/BarcodeLabelDomainEvents.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Auth/BarcodeLabelPermissionCodes.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/*.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/*.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEvents/BarcodeLabelIntegrationEvents.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEventConverters/BarcodeLabelIntegrationEventConverters.cs`
- 创建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/BarcodeLabel/BarcodeLabelEndpoints.cs`
- 创建：`backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/BarcodeLabelAggregateTests.cs`
- 创建：`backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelEndpointContractTests.cs`
- 创建：`backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelIntegrationEventTests.cs`
- 创建：`backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelSchemaConventionTests.cs`

请求 WAVE2-INTEG 处理的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-barcode-label-mvp.ps1`

## 任务 1：在本地搭建 BarcodeLabel 服务骨架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.BarcodeLabel -o backend/services/Business/BarcodeLabel --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Domain.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Web.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests --framework net10.0
```

- [ ] **步骤 2：删除模板演示代码**

运行：

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/BarcodeLabel
```

预期：无匹配项。

## 任务 2：实施领域模型

- [ ] **步骤 1：编写失败的聚合测试**

覆盖：

1. 创建条码规则时拒绝空白前缀或不受支持的条码类型。
2. 创建标签模板时只存储 FileStorage 文件 ID，不存储对象键。
3. 创建打印批次时，根据规则和源文档生成确定性的标签项。
4. 对相同载荷执行打印批次幂等操作时，返回现有批次。
5. 拒绝存在冲突的打印幂等载荷。
6. 创建扫描记录时必须提供源设备、扫描值和幂等键。
7. 扫描幂等操作拒绝存在冲突的载荷。

- [ ] **步骤 2：实施聚合根和领域事件**

实施规格中的聚合文件和领域事件。ID 使用 `Guid.CreateVersion7()`，并保持生成过程对测试而言具有确定性。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore
```

预期：BarcodeLabel 领域测试通过。

## 任务 3：添加持久化与事件

- [ ] **步骤 1：配置 DbContext**

使用数据库模式 `barcode` 和迁移历史表 `barcode.__EFMigrationsHistory`。

- [ ] **步骤 2：生成迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialBarcodeLabelSchema --project backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Nerv.IIP.Business.BarcodeLabel.Infrastructure.csproj --startup-project backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：添加事件测试**

验证事件名称：

1. `barcode.LabelPrintBatchCreated`
2. `barcode.LabelPrintBatchCompleted`
3. `barcode.LabelScanned`
4. `barcode.ScanRejected`

## 任务 4：添加 API 接口

- [ ] **步骤 1：添加端点契约测试**

覆盖路由形状、权限代码、校验、操作 ID，并验证公开接口不会泄露 `objectKey`/`object_key`。

- [ ] **步骤 2：实施命令、查询和 FastEndpoints**

在 `Endpoints/BarcodeLabel` 下实施规格中的端点。

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore
```

预期：BarcodeLabel Web 测试通过。

## 任务 5：向 WAVE2-INTEG 移交共享修改

- [ ] **步骤 1：记录共享修改**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add BarcodeLabel projects/tests to `backend/Nerv.IIP.sln`.
- Register BarcodeLabel in AppHost.
- Add BarcodeLabel permissions to IAM seed and `authorization-matrix.md`.
- Add `barcode` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-barcode-label-mvp.ps1`.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore
```

预期：两个命令均通过。
