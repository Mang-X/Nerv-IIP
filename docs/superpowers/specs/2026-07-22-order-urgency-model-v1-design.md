# 订单紧急度模型 V1 设计

## 背景与范围

MAN-584 / GitHub #1053 要求销售订单、需求、排程和 MES 工单使用同一套可解释紧急度。当前代码已经有一条稳定的跨域业务引用链：`SalesOrderNo` 进入 DemandPlanning 的 `SourceReference`，再进入 MES 的 `SourceDemandReference`；Scheduling 则拥有标准化问题快照和方案，其中已包含交期、工序周期、物料齐套、设备不可用、质量阻断、工装状态、容量冲突和预计完工时间。

V1 只交付统一模型的纵向闭环，不实现 MAN-580 的甘特拖拽、手工重排，也不实现 MAN-588 的局部重排求解。旧 PR #178 修改的 `frontend/packages/scheduling-visualization/**` 和现有 Gantt/Canvas 文件不在本次范围内。

## 方案选择

### 选择：BusinessScheduling 持有派生快照与审计

BusinessScheduling 读取自身已经持久化的标准化 `SchedulingProblem` 和 `SchedulePlan`，以纯计算器生成版本化紧急度结果。它只保存派生结果、业务优先级设置和变更审计，不复制 ERP、DemandPlanning、MES、Quality、IndustrialTelemetry 或 Maintenance 的权威业务实体。

BusinessGateway 只负责授权、操作者注入和转发；前端只展示结果和提交人工业务优先级意图，不计算 CR、Slack 或综合等级。

### 未选择的方案

1. Gateway 读时聚合：没有稳定快照与历史，页面请求时点不同会漂移，且违反 Gateway 不实现行业规则的边界。
2. 每个上游服务各自投影紧急度：会复制规则与结果，跨域发布耦合过大，不适合作为单 PR V1。

## 权威事实与统一身份

| 事实 | 权威拥有者 | V1 使用方式 |
| --- | --- | --- |
| 销售单号、客户、订单行交期 | ERP / DemandPlanning | 仅使用已传递到排程输入的业务引用和交期 |
| 需求来源 | DemandPlanning | `SourceReference` 作为可查询别名 |
| 工单、工序、执行进度、质量 Hold | MES | 使用已进入标准化排程问题/方案的工单与风险事实 |
| 物料齐套 | MES 组合的 Inventory/WMS 事实 | 使用 `SchedulingMaterialReadinessContract` |
| 设备可用性 | IndustrialTelemetry + Maintenance | 使用标准化不可用窗口及 reason code |
| 排程预计完工、容量/工装冲突 | BusinessScheduling | 使用最新方案、冲突和不可排原因 |
| 人工业务优先级 | BusinessScheduling | 新增有原因、操作者、时间和有效期的受审计事实 |

每个紧急度主题以 Scheduling `OrderId` 为主键，并保存 `BusinessReference` 别名。若没有跨域业务引用，别名回退为 `OrderId`；这保证 MES/排程仍可用，同时不会猜测销售订单关联。API 可按任一已登记引用查询同一快照。

## 模型输出

模型版本固定为 `order-urgency-v1`。响应必须包含：

- 综合等级：`critical`、`urgent`、`highRisk`、`attention`、`normal`；
- 三类独立贡献：`businessPriority`、`timeCriticality`、`executionRisk`；
- 每个贡献的原始值、等级、reason codes、事实观测时间和新鲜度；
- `slackHours`、`criticalRatio`、预计完工、预计迟交、剩余周期；
- `calculatedAtUtc`、`modelVersion`、`inputFingerprint` 和趋势；
- 最近等级变化及人工业务优先级变更历史。

模型不生成不可解释总分。综合等级按固定规则选择最高优先级结论：

1. 有效 P0 为 `critical`，有效 P1 至少为 `urgent`；
2. 已迟交、`Slack < 0` 或 `CR < 1` 为 `urgent`；
3. 物料、设备、质量、工装或容量阻塞，以及缺失/陈旧关键事实，为 `highRisk`；
4. `Slack` 小于一个班次、`CR <= 1.2` 或非阻塞风险为 `attention`；
5. 其余为 `normal`。

P2、P3 保留为可解释业务输入，但不单独把正常订单升级为紧急。业务优先级不会覆盖或隐藏时间和执行风险贡献。

## CR、Slack 与风险计算

最新方案存在时，订单预计完工取其最后一道 assignment 的 `EndUtc`；不存在方案时，以当前计算时点加上标准化问题中剩余工序时长总和估算。公式为：

```text
remainingCycle = max(estimatedCompletion - calculatedAt, 0)
slack = dueAt - calculatedAt - remainingCycle
CR = (dueAt - calculatedAt) / remainingCycle
```

当剩余周期为零时，若尚未过交期则 CR 为空且时间贡献正常；已过交期则输出迟交 reason code。计算器逐项保留物料、设备、质量、工装、容量和数据新鲜度贡献，不把它们压成分数。

## 重算、确定性、幂等与审计

- 计划创建、问题快照变化、计划失效和业务优先级变化时立即重算受影响订单；
- 后台扫描器周期重算未完成主题，使时间自然流逝能够升级/降级；
- list/detail 查询会补算缺失或已跨过当前时间桶的主题，避免后台短暂停止时返回旧结论；
- 普通订单使用小时桶，已接近交期或已有风险的订单使用 15 分钟桶；桶起点是计算时点，纯计算器不读取系统时间；
- 幂等键由组织、环境、订单、模型版本、标准化输入指纹、有效业务优先级 revision 和计算时间桶组成；重复重算返回同一快照，不追加重复历史；
- 仅综合等级、贡献等级、reason code 集合或关键数值变化时记录新的可见变化，所有快照仍保留可审计输入。

人工优先级变更使用单调 revision 和 append-only change 记录。Gateway 从认证主体生成 actor；客户端不能提交 actor。reason 必填，`expiresAtUtc` 可空；过期设置自动不再参与综合结论，但保留历史。

## 缺失、陈旧与失败语义

关键问题快照缺失、反序列化失败、超过允许新鲜度、交期缺失或剩余周期无法解释时，模型 fail closed 为 `highRisk`，并输出明确的 `urgency.source.*` reason code；禁止返回 `normal` 或用 HTTP 200 代替业务事实成功。单项风险来源缺失时只标记对应贡献，不伪造其状态。

相同输入必须得到相同输出；集合在计算前按稳定键排序，数值统一使用 UTC 和固定精度。数据库或系统故障仍作为系统错误返回，不能伪装成业务风险。

## HTTP、Facade 与前端

BusinessScheduling 新增：

- `GET /api/business/v1/scheduling/order-urgencies`：按 scope 和可选引用批量读取；
- `GET /api/business/v1/scheduling/order-urgencies/{orderReference}`：详情与历史；
- `PUT /api/business/v1/scheduling/order-urgencies/{orderReference}/business-priority`：设置 P0-P3、原因和有效期。

三者都分类为 `exposed`，同 PR 完成 BusinessGateway facade、OpenAPI 导出、generated api-client 和稳定 barrel。服务 endpoint 使用 internal policy；Gateway 使用 Scheduling read/manage 权限，并由 Gateway 注入 actor。

前端新增共享 `OrderUrgencyCell` 和 composable。同一 facade 结果以引用索引，进入以下关键入口：

1. ERP 销售订单列表：按 `salesOrderNo`；
2. DemandPlanning 需求列表：按 `sourceReference`；
3. MES 工单列表：按 `workOrderId`，有 `sourceDemandReference` 时同时命中销售/需求别名；
4. 排程方案详情的订单 assignment：按 `orderId`。

单元格显示综合等级和主原因，hover 展示三类摘要，点击右侧 Sheet 展示贡献、阈值、计算版本/时间、历史和业务优先级修改。共享显示模式支持综合、业务优先级、CR、Slack、执行风险和预计迟交，只改变表现，不改变结果。详情提供进入 `/scheduling` 的入口，不实现拖拽或自动重排。

所有 UI 使用现有 `Nv*` 组件与语义 token，不修改 NvUI 基础组件，也不触碰 #178 的可视化包。

## 数据库与迁移

Scheduling schema 新增：

- `order_urgency_business_priorities`：当前有效设置和 revision；
- `order_urgency_business_priority_changes`：append-only 人工变更审计；
- `order_urgency_snapshots`：版本化模型输出、输入指纹、计算桶和可解释 JSON。

索引以 `organization_id + environment_id + order_id` 为 scope，快照幂等唯一索引包含模型版本、输入指纹、业务优先级 revision 和计算桶。所有表/列带注释，更新 schema catalog，并使用 PostgreSQL profile 生成 EF migration。

## 验证

测试覆盖：

- CR/Slack 边界、时间流逝升级和确定性；
- P0/P1/P2/P3 贡献不遮蔽其它维度；
- 物料短缺、设备不可用、质量阻塞、工装/容量风险；
- 缺失/陈旧事实 fail closed；
- 重算幂等、快照/等级变化和人工优先级审计；
- Gateway actor 注入与 facade 契约；
- 四个页面使用同一引用索引和共享展示组件；
- Scheduling schema convention、facade coverage、OpenAPI/codegen、frontend typecheck/test/build。

真实全栈只在需要时使用 `./nerv.ps1 fullstack run`，不会启动或遗留诊断 AppHost。
