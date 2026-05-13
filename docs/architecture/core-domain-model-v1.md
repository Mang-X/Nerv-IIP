# 一阶领域模型 V1

本文档定义 IAM、AppHub、Ops 在首批纵切中的一阶领域模型。目标不是一次性穷尽所有聚合，而是先明确谁拥有什么事实、谁发布什么事件、谁负责任务闭环。

## 建模原则

1. 先建事实模型，再建流程模型。
2. 不把瞬时状态和持久事实混在一个聚合里。
3. 不为首批闭环过度拆聚合。
4. 先支撑注册、心跳、状态同步、可见和低风险动作，再扩展备份、告警、审批和知识治理。

## IAM 一阶模型

### 目标职责

- 提供平台身份、组织、工厂、环境、角色与权限事实。
- 为 Gateway、AppHub、Ops 提供稳定的组织上下文与权限判断基础。

### 首批聚合建议

- Organization
- Plant
- Environment
- User
- Role
- Membership

### 首批主表建议

- iam_organizations
- iam_plants
- iam_environments
- iam_users
- iam_roles
- iam_memberships
- iam_role_permissions

### 首批事件建议

- OrganizationCreated
- EnvironmentCreated
- EnvironmentArchived
- UserActivated
- RoleGrantedToUser
- PermissionSetChanged

### 首批不做

1. 复杂 ABAC 规则引擎
2. 菜单编排细节
3. 跨组织委派和临时授权流

## AppHub 一阶模型

### 目标职责

- 拥有平台当前管理了哪些应用、实例、节点、版本、能力的事实模型。
- 负责接收注册、心跳、能力声明和状态同步，并沉淀为可查询事实。
- 不负责任务执行、审批与审计闭环。

### 首批聚合建议

- Application
- ApplicationVersion
- ManagedNode
- ApplicationInstance
- CapabilityManifest
- InstanceLiveness

### 首批主表建议

- apphub_applications
- apphub_application_versions
- apphub_managed_nodes
- apphub_application_instances
- apphub_instance_capabilities
- apphub_instance_liveness
- apphub_instance_state_history

### 首批事件建议

- ApplicationRegistered
- ApplicationVersionObserved
- ManagedNodeDiscovered
- CapabilityManifestDeclared
- InstanceHeartbeatReceived
- InstanceStateSynchronized
- InstanceStatusChanged
- InstanceMarkedUnreachable

### 关键边界

1. AppHub 只维护 reported state，不维护运维 desired state。
2. AppHub 可以记录实例当前是否可达，但不拥有重启、停止、备份等动作任务生命周期。
3. AppHub 对外提供应用目录与实例事实查询，不直接生成运维任务。

## Ops 一阶模型

### 目标职责

- 拥有动作请求、任务执行、审计记录、结果回传与失败分类。
- 为所有会改变目标系统状态的动作提供统一任务闭环。
- 不拥有应用目录事实，不替代 AppHub 的实例状态模型。

### 首批聚合建议

- OperationTask
- OperationAttempt
- AuditRecord
- OperationTemplate
- ApprovalRequest

### 首批主表建议

- ops_operation_tasks
- ops_operation_attempts
- ops_audit_records
- ops_operation_templates
- ops_approval_requests

### 首批事件建议

- OperationRequested
- OperationDispatched
- OperationStarted
- OperationCompleted
- OperationFailed
- AuditRecorded
- ApprovalRequested
- ApprovalResolved

### 关键边界

1. Ops 只描述动作生命周期，不直接改写 AppHub 的实例事实。
2. 动作执行完成后，Ops 记录结果；实例最终状态仍以 Agent 后续状态同步驱动 AppHub 更新为准。
3. 审计是 Ops 的一级能力，不是每个服务各自补一份日志表。

## 首批纵切映射

### 应用注册链路

1. Agent 发送注册契约。
2. AppHub 创建或更新 Application、ApplicationVersion、ManagedNode、ApplicationInstance。
3. Gateway 查询到新增实例事实。

### 心跳链路

1. Agent 上报心跳。
2. AppHub 更新 InstanceLiveness 投影。
3. 若超时未收到心跳，再由 AppHub 触发不可达事件或标记。

### 状态同步链路

1. Agent 上报实例状态。
2. AppHub 更新 ApplicationInstance.reportedStatus 与状态历史。
3. 只有状态发生变化时才发 InstanceStatusChanged 事件。

### 重启动作链路

1. 控制台或 Gateway 发起 restart。
2. Ops 创建 OperationTask 与 AuditRecord。
3. Agent 执行动作并回传结果。
4. Ops 记录 OperationCompleted 或 OperationFailed。
5. AppHub 仍等待后续状态同步来更新最终实例事实。

## 当前冻结结论

1. AppHub 拥有应用与实例事实，Ops 拥有动作与审计事实。
2. 心跳和状态同步是两类不同契约，不能合并成一个万能上报接口。
3. AppHub 记录 reported state，Ops 不维护实例事实真相源。
4. restart 这类动作的成功与否由 Ops 记录，实例最终状态是否恢复由 AppHub 后续状态同步确认。
5. IAM 首批先服务于组织、环境与权限上下文，不先扩展复杂授权模型。
