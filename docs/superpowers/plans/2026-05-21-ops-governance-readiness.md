# Ops 治理就绪实施计划

> **面向智能体工作者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**让 Ops 具备超出第二纵切 `lifecycle.restart` 工作流的能力，同时不阻塞 Notification 和 FileStorage MVP 分支。

**架构：**首批变更限定在 Ops 和稳定共享契约内。添加模板作为操作代码默认值的来源，修复 SDK/客户端兼容性，增加任务/审计视图的读取 API，发布增量式集成事件，并强化迁移/ID 生成行为，但不改变 Gateway、Console、Notification 或 FileStorage 接线。

**技术栈：**.NET 10、FastEndpoints、EF Core、NetCorePal CleanDDD 模式、xUnit、`Nerv.IIP.Contracts.Ops`、`Nerv.IIP.Sdk.Ops`。

---

## 范围

本阶段范围：

1. 修复 `Sdk.Ops` 响应信封（envelope）处理。
2. 添加 `OperationTemplate` 聚合、EF 映射、仓储支持、迁移、契约和 Ops API 端点。
3. 让运维任务创建通过模板验证操作代码，而不是硬编码 `lifecycle.restart`。
4. 添加任务列表和审计记录查询 API 端点。
5. 添加已请求（requested）/已领取（claimed）/已记录审计（audit-recorded）增量集成事件契约和转换器，不改变现有已完成（completed）/已失败（failed）事件。
6. 为 Ops/AppHub `Persistence:AutoMigrate=true` 添加仅限 Development 的保护。
7. 用抗冲突 ID 替换基于计数的 Ops ID 生成方式。

范围外：

1. 面向模板、审计或审批的 Gateway/Console 外观层。
2. ApprovalRequest 运行时工作流和待审批（pending-approval）状态。
3. Notification/FileStorage 直接集成。
4. 对现有已完成（completed）/已失败（failed）集成事件契约的破坏性变更。

## 工作树

分支：`codex/ops-governance-readiness`

路径：`C:\WorkFile\Focus\项目\数字工厂\Nerv-IIP-worktrees\ops-governance-readiness`

基线已通过：

```powershell
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Domain.Tests\Nerv.IIP.Ops.Domain.Tests.csproj --no-restore
dotnet test backend\tests\Nerv.IIP.Contracts.Ops.Tests\Nerv.IIP.Contracts.Ops.Tests.csproj --no-restore
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Web.Tests\Nerv.IIP.Ops.Web.Tests.csproj --no-restore
```

## 任务 1：SDK 响应信封兼容性

**文件：**

- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.Ops/OpsClient.cs`
- 测试：`backend/tests/Nerv.IIP.Contracts.Ops.Tests/OpsContractJsonTests.cs`；如果已有 SDK 测试项目，则创建 SDK 专项测试。

- [ ] 添加一个预期失败的测试，证明 `HttpOpsClient` 可以读取 `{"data":{...}}` Ops 响应。
- [ ] 在 `OpsClient.cs` 中添加内部响应信封记录类型（record）。
- [ ] 更新所有响应读取方法以解包 `ResponseData<T>`，并在 data 字段为空时保留有用的错误信息。
- [ ] 运行 `dotnet test backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj --no-restore`。

## 任务 2：迁移安全与 ID 生成

**文件：**

- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/OperationTaskRepository.cs`
- 测试：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsServiceReadinessTests.cs`

- [ ] 添加预期失败的测试，验证 Production + PostgreSQL + `Persistence:AutoMigrate=true` 会拒绝启动。
- [ ] 添加与 IAM 行为一致、仅限 Development 的守卫。
- [ ] 添加预期失败的测试或仓储级断言，证明任务 ID 不使用基于计数的生成方式。
- [ ] 用确定性前缀加 Guid v7/字符串 ID 模式替换 `NextTaskIdAsync`，确保并发创建时不会冲突。
- [ ] 运行 Ops Web 测试。

## 任务 3：OperationTemplate 基础

**文件：**

- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/AggregatesModel/OperationTemplateAggregate/OperationTemplate.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/DomainEvents/OperationTaskDomainEvents.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/InMemoryOpsStateStore.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/ApplicationDbContext.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/OperationTemplateEntityTypeConfiguration.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/OperationTaskRepository.cs`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsContracts.cs`
- 新建/修改：Ops 迁移文件。
- 测试：`backend/services/Ops/tests/Nerv.IIP.Ops.Domain.Tests/OperationTaskAggregateTests.cs`
- 测试：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

- [ ] 添加预期失败的领域测试，验证缺少模板时拒绝不受支持的操作代码。
- [ ] 添加预期失败的领域测试，验证从已启用模板创建任务。
- [ ] 添加 `OperationTemplate`，包含操作代码、显示名称、JSON 参数模式（schema）、风险等级、默认最大尝试次数、默认租约时长秒数、是否需要审批、启用标志和时间戳。
- [ ] 添加使用 `ops.operation_templates` 的 EF 配置和 DbSet。
- [ ] 添加按操作代码获取模板以及添加/更新模板的仓储方法。
- [ ] 更改任务创建，使其接受模板默认值并移除硬编码的 `lifecycle.restart` 检查。
- [ ] 写入种子数据或延迟提供内置 `lifecycle.restart` 模板，使现有测试和第二纵切行为继续通过。
- [ ] 运行 Ops Domain 和 Ops Web 测试。

## 任务 4：模板、任务列表与审计 API 端点

**文件：**

- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTemplates/OperationTemplateEndpoints.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListOperationTasksQuery.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListAuditRecordsQuery.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTasks/OperationTaskEndpoints.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/AuditRecords/AuditRecordEndpoints.cs`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsContracts.cs`
- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.Ops/OpsClient.cs`
- 测试：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

- [ ] 为模板创建/列表/详情添加预期失败的 API 端点测试。
- [ ] 为 `GET /api/ops/v1/operation-tasks` 分页列表添加预期失败的 API 端点测试。
- [ ] 为 `GET /api/ops/v1/audit-records` 添加预期失败的 API 端点测试。
- [ ] 使用 `ApplicationDbContext` 投影实现查询处理器。
- [ ] 仅当本阶段契约稳定时，才添加任务列表和模板读取的 SDK 方法。
- [ ] 运行 Ops Web 测试。

## 任务 5：增量式集成事件

**文件：**

- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsIntegrationEvents.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/OperationTaskRequestedIntegrationEventConverter.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/OperationTaskClaimedIntegrationEventConverter.cs`
- 新建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/AuditRecordedIntegrationEventConverter.cs`
- 测试：`backend/tests/Nerv.IIP.Contracts.Ops.Tests/OpsContractJsonTests.cs`
- 测试：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskIntegrationEventConverterTests.cs`

- [ ] 为已请求（requested）/已领取（claimed）/已记录审计（audit-recorded）事件添加预期失败的 JSON 往返测试。
- [ ] 添加增量式事件记录类型和载荷记录类型，不修改已完成（completed）/已失败（failed）记录类型。
- [ ] 添加转换器测试。
- [ ] 根据需要，从现有领域事件或新的窄领域事件实现转换器。
- [ ] 运行契约测试和 Ops Web 转换器测试。

## 验证

完成前运行：

```powershell
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Domain.Tests\Nerv.IIP.Ops.Domain.Tests.csproj --no-restore
dotnet test backend\tests\Nerv.IIP.Contracts.Ops.Tests\Nerv.IIP.Contracts.Ops.Tests.csproj --no-restore
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Web.Tests\Nerv.IIP.Ops.Web.Tests.csproj --no-restore
dotnet build backend\Nerv.IIP.sln --no-restore
```
