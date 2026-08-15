# BusinessApproval 最小可行产品（MVP）实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过创建 BusinessApproval 来实施 #134，涵盖业务审批模板、审批链、审批步骤、决策记录和审批结果事件。

**架构：**BusinessApproval 是位于 `backend/services/Business/Approval` 下的 CleanDDD 业务服务。它通过公开 ID 引用 IAM 用户/上下文，并为业务服务发出审批结果事件。它不替代 Ops 操作审批，也不复制 IAM 角色事实。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换、`Nerv.IIP.Testing` 数据库模式约定辅助工具。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-business-approval-mvp-design.md` 作为本计划的领域契约。

## 文件

- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/Nerv.IIP.Business.Approval.Domain.csproj`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Nerv.IIP.Business.Approval.Infrastructure.csproj`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalTemplateAggregate/ApprovalTemplate.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalStepAggregate/ApprovalStep.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalDecisionAggregate/ApprovalDecision.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/DomainEvents/ApprovalDomainEvents.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Auth/ApprovalPermissionCodes.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/*.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Queries/*.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEvents/ApprovalIntegrationEvents.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Endpoints/Approvals/ApprovalEndpoints.cs`
- 创建：`backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/ApprovalAggregateTests.cs`
- 创建：`backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalEndpointContractTests.cs`
- 创建：`backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalIntegrationEventTests.cs`
- 创建：`backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalSchemaConventionTests.cs`

请求 WAVE2-INTEG 处理的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-approval-mvp.ps1`

## 任务 1：在本地搭建 BusinessApproval 服务骨架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Approval -o backend/services/Business/Approval --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Approval.Domain.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Approval.Web.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests --framework net10.0
```

- [ ] **步骤 2：删除模板演示代码**

运行：

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Approval
```

预期：无匹配项。

## 任务 2：实施领域模型

- [ ] **步骤 1：编写失败的聚合测试**

覆盖：

1. 已启用模板针对源文档引用启动审批链。
2. 有序步骤必须依次完成处理。
3. 同一操作者重复相同决策时具有幂等性。
4. 拒绝同一操作者重复作出存在冲突的决策。
5. 被拒绝的审批链处于终态。
6. 获批的审批链仅在最后一个必需步骤完成后发出审批通过领域事件。

- [ ] **步骤 2：实施聚合根**

实施模板、审批链、步骤和决策聚合。IAM 事实仅保留为字符串引用。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj --no-restore
```

预期：BusinessApproval 领域测试通过。

## 任务 3：添加持久化与事件

- [ ] **步骤 1：配置 DbContext**

使用数据库模式 `business_approval` 和迁移历史表 `business_approval.__EFMigrationsHistory`。

- [ ] **步骤 2：生成迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialBusinessApprovalSchema --project backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Nerv.IIP.Business.Approval.Infrastructure.csproj --startup-project backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：添加事件测试**

验证事件名称：

1. `businessApproval.ApprovalStarted`
2. `businessApproval.StepResolved`
3. `businessApproval.ApprovalApproved`
4. `businessApproval.ApprovalRejected`
5. `businessApproval.ApprovalReturned`

## 任务 4：添加 API 接口

- [ ] **步骤 1：添加端点契约测试**

覆盖路由、权限代码、校验、操作 ID 和待处理任务查询行为。

- [ ] **步骤 2：实施命令、查询和 FastEndpoints**

在 `Endpoints/Approvals` 下实施规格中的端点。

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj --no-restore
```

预期：BusinessApproval Web 测试通过。

## 任务 5：向 WAVE2-INTEG 移交共享修改

- [ ] **步骤 1：记录共享修改**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add Approval projects/tests to `backend/Nerv.IIP.sln`.
- Register Approval in AppHost.
- Add BusinessApproval permissions to IAM seed and `authorization-matrix.md`.
- Add `business_approval` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-approval-mvp.ps1`.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj --no-restore
```

预期：两个命令均通过。
