# BusinessApproval MVP 设计

## 目标

将 BusinessApproval 建设为模板、审批链、审批步骤、决策和审批结果事件的业务单据审批事实来源。

BusinessApproval 处理 ECO、采购申请、工单、盘点差异和销售折扣等领域单据。它不取代 Ops 平台操作审批。

## 当前状态

BusinessApproval 当前没有服务目录。Ops 持有平台操作任务以及平台审批/审计生命周期。IAM 持有用户、角色、权限和授权范围。

## 持有的事实

BusinessApproval 持有：

1. ApprovalTemplate：单据类型、步骤定义、审批人策略和有效状态。
2. ApprovalChain：业务单据引用对应的审批实例。
3. ApprovalStep：有序审批步骤、分配的审批人引用、状态和截止日期。
4. ApprovalDecision：批准/驳回/退回操作、操作人、意见和时间戳。
5. ApprovalDocumentReference：来源服务、单据类型、单据 ID 和可选的行 ID。

BusinessApproval 不持有：

1. IAM 用户、角色、权限或成员关系事实。
2. Ops 操作任务、操作尝试或平台审批生命周期。
3. ProductEngineering、Inventory、MES、WMS 或 ERP 持有的业务单据状态。
4. Notification 投递状态。

## API 范围

| API | 用途 | 权限 |
| --- | --- | --- |
| `POST /api/business/v1/approvals/templates` | 创建或更新审批模板。 | `business.approvals.manage` |
| `GET /api/business/v1/approvals/templates` | 列出审批模板。 | `business.approvals.read` |
| `POST /api/business/v1/approvals/chains` | 为业务单据启动审批链。 | `business.approvals.manage` |
| `GET /api/business/v1/approvals/chains/{chainId}` | 读取审批链详情和决策历史。 | `business.approvals.read` |
| `GET /api/business/v1/approvals/tasks` | 列出用户或服务上下文中的待处理审批步骤。 | `business.approvals.read` |
| `POST /api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve` | 批准、驳回或退回一个步骤。 | `business.approvals.manage` |

## 规则

1. 审批链根据有效模板创建。
2. 审批链引用来源服务和业务单据引用；它不复制来源单据载荷。
3. 步骤按配置顺序处理。在 MVP 中，`parallelGroupKey` 作为模板/查询元数据携带，用于对编号相同的步骤分组；它不实现任一人批准即可通过的语义，同一 `stepNo` 中每个待处理审批人都必须批准，下一步骤才能处理。
4. 只有同一操作人重复提交相同决策载荷时，重复的审批人操作才具有幂等性。
5. 拒绝同一操作人提交的冲突重复操作。
6. 除非未来版本增加重新打开行为，否则被驳回或退回的审批链为终态。
7. 业务服务消费审批结果事件，并更新其自身的单据状态。
8. ApprovalTemplate 可以引用 IAM 用户 ID、组或权限代码，但 BusinessApproval 不复制 IAM 角色或成员关系。

## Business Console 使用方式

2026-07-02 前端集成：业务单据页面通过 BusinessGateway facade（门面层）和 `@nerv-iip/api-client` 的 business-console 稳定导出入口（barrel）使用 BusinessApproval。ECO/ECN 和 NCR 页面链接到真实审批链，显示审批链状态、当前步骤、审批人角色/引用和决策历史，并携带单据筛选条件深链返回审批中心。页面不得暴露自由文本审批引用字段，也不得伪造静态审批状态。当某个领域尚无持久化的草稿单据引用时，可以关联一条现有的真实审批链，但不得虚构假单据 ID。

## 事件

BusinessApproval 发布采用 ADR 0011 信封格式的事件：

1. `businessApproval.ApprovalStarted`
2. `businessApproval.StepResolved`
3. `businessApproval.ApprovalApproved`
4. `businessApproval.ApprovalRejected`
5. `businessApproval.ApprovalReturned`

事件携带公开审批 ID、来源单据引用、操作人引用和结果状态。事件不携带 IAM 角色内部信息或完整业务单据载荷。

## 权限

初始权限代码：

1. `business.approvals.read`
2. `business.approvals.manage`

## 持久化

默认 schema：`business_approval`。

必需的表：

1. `approval_templates`
2. `approval_template_steps`
3. `approval_chains`
4. `approval_steps`
5. `approval_decisions`

每张表和每个业务列都需要 schema 注释。PostgreSQL migration 历史记录必须使用 `business_approval.__EFMigrationsHistory`。

## 测试

验收要求：

1. 针对模板启用、审批链创建、步骤有序处理和终态的 Domain 测试。
2. 针对重复及冲突审批人操作的 Domain 测试。
3. 针对路由形态、授权、校验和 operation ID 的 Web 测试。
4. 使用 `Nerv.IIP.Testing` 的 schema 约定测试。
5. 针对审批启动、步骤处理、批准、驳回和退回事件的集成事件转换器/序列化测试。
6. 证明 BusinessApproval Domain 或 Infrastructure 未引用 Ops 类型的测试。
