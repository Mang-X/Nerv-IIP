# Issue 417 BusinessApproval 缺口实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过使工作流行为可执行，并要求 ProductEngineering ECO 发布校验已批准的 BusinessApproval 链，闭合首个真实的 BusinessApproval 后端循环。

**架构：**BusinessApproval 保持为业务审批事实来源，而不是 Ops 的替代品。最新 `main` 已提供公开的 `Nerv.IIP.Contracts.Approval` 契约和 #411 的 ERP 采购订单审批循环。本计划现在使用该契约，保留 main 中的 ERP PO 行为，并通过使工作流行为可执行，以及要求 ProductEngineering ECO 发布在归档受影响版本前校验已批准的 BusinessApproval 链，闭合剩余的 #417 缺口。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core PostgreSQL migration、CAP 集成事件、xUnit、`Nerv.IIP.Messaging.CAP`。

---

### Task 1：公开审批事件契约

**文件：**
- 修改： `backend/common/Contracts/Nerv.IIP.Contracts.Approval/Nerv.IIP.Contracts.Approval.csproj`
- 修改： `backend/common/Contracts/Nerv.IIP.Contracts.Approval/ApprovalIntegrationEvents.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs`
- 修改： `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalIntegrationEventTests.cs`

- [x] 添加失败测试，证明 BusinessApproval 转换器会发出公开的 `Nerv.IIP.Contracts.Approval` 信封事件。
- [x] 添加契约项目，其中包含与 ADR 0011 信封兼容的 started/step/completed/overdue 事件记录。
- [x] 将事件类型/来源常量移至契约项目，并更新转换器/测试。

### Task 2：工作流策略与条件

**文件：**
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalTemplateAggregate/ApprovalTemplate.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/ApprovalTemplateStepEntityTypeConfiguration.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/ApprovalStepEntityTypeConfiguration.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Endpoints/Approvals/ApprovalEndpoints.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/Templates/CreateOrUpdateApprovalTemplateCommand.cs`
- 修改： `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/ApprovalAggregateTests.cs`

- [x] 为同一步骤 `any` 审批、同一步骤 `all` 审批和简单条件路由添加失败的领域测试。
- [x] 向模板/运行时步骤添加 `CompletionPolicy`（`all`/`any`）和 `ConditionExpression`。
- [x] 根据单据引用元数据评估受支持的 MVP 条件：空条件始终适用；`documentType=<value>` 和 `sourceService=<value>` 路由到匹配步骤。
- [x] 确保待处理任务排序在某个先前步骤编号下的每个活动组都满足其策略时，将该步骤编号视为已完成。

### Task 3：委托强制执行

**文件：**
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/Chains/ResolveApprovalStepCommand.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/ApprovalDecisionEntityTypeConfiguration.cs`
- 修改： `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalEndpointContractTests.cs`

- [x] 添加失败的处理器测试，证明有效受托人可以代表原审批人批准，而已撤销/过期的委托不能。
- [x] 在命令处理器中加载有效且匹配的委托。
- [x] 当受托人处理委托人的步骤时，在决策上记录 `OnBehalfOfActorType` 和 `OnBehalfOfActorRef`。

### Task 4：超时升级

**文件：**
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/DomainEvents/ApprovalDomainEvents.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- 创建： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/Chains/CheckOverdueApprovalStepsCommand.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Endpoints/Approvals/ApprovalEndpoints.cs`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs`
- 修改： `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalEndpointContractTests.cs`
- 修改： `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalIntegrationEventTests.cs`

- [x] 添加失败的命令测试，验证到期的待处理步骤只会被标记一次逾期。
- [x] 添加受内部服务授权的 endpoint，使逾期检测具有真实触发面，并使用服务时钟而非调用方提供的时间。
- [x] 为 Notification/工作台消费者添加步骤逾期状态和 `ApprovalStepOverdue` 集成事件。
- [x] 保持轻量升级：发出事件并保留分派；自动重新分派属于后续策略扩展。

### Task 5：ProductEngineering ECO 审批门禁

**文件：**
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Program.cs`
- 修改： `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- 修改： `docs/architecture/implementation-readiness.md`

- [x] 添加失败的 ProductEngineering 命令处理器测试，证明 ECO 发布要求匹配且已批准的 BusinessApproval 链。
- [x] 添加失败的命令校验器/处理器测试，阻止直接使用任意审批引用，但幂等的已发布记录除外。
- [x] 添加受保护的 HTTP 校验器，使用内部服务令牌读取 BusinessApproval 链详情；已拒绝/已退回/不匹配的链会阻止发布。
- [x] 更新就绪文档以说明 ECO 门禁，并保留最新 main 中来自 #411 的 ERP PO 审批闭环。

### Task 6：Migration 与验证

**文件：**
- 创建： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/*Issue417ApprovalWorkflowGaps*`
- 修改： `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- 修改： `docs/architecture/database-schema-catalog.md`

- [x] 为新的工作流/委托/逾期列生成 BusinessApproval migration。
- [x] 更新 schema 目录注释。
- [x] 运行聚焦的 BusinessApproval 和 ProductEngineering 测试，然后运行 BusinessApproval 验证脚本。
