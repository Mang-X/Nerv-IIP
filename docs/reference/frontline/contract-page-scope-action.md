# 现场作业契约—页面—范围—动作 Reference

本页索引 Business Console / Business PDA 现场作业的**当前公开契约、页面消费、主体范围与动作事实来源**。它不维护 P0/P1 缺口标签、Issue 完成状态、路线图 owner 或日期化审计。

精确 operation、DTO、错误码和响应字段必须回到 BusinessGateway OpenAPI、Gateway 实现、generated client、业务服务和当前页面代码核实。M2 拆分前的逐行盘点保留在 [`../../reports/frontline-contract-page-scope-action-matrix-2026-08-28.md`](../../reports/frontline-contract-page-scope-action-matrix-2026-08-28.md)，放在 `docs/reports/` 根部是为了保持原 `../../backend` / `../../frontend` 历史链接深度。真实验收证据口径见 [`acceptance-evidence.md`](acceptance-evidence.md)。

## 事实优先级

1. `frontend/packages/api-client/openapi/business-gateway-console.v1.json`：BusinessGateway 当前公开 operation / schema。
2. `backend/gateway/BusinessGateway/**`：授权、scope 解析、代理与 facade 实现。
3. `frontend/packages/api-client/src/generated/business-console/**` 与稳定 barrel：generated client 当前调用面。
4. 各业务服务聚合、命令、查询与持久化：领域生命周期和最终副作用。
5. `frontend/apps/business-pda/**`、`frontend/apps/business-console/**`：当前页面实际消费。按钮可见性或本地状态不能替代服务端裁决。

## 作业范围词汇

| 范围 | Reference 判定 |
| --- | --- |
| `Self` | 服务端基于已认证当前 principal 验证 assignment/ownership 后过滤；客户端提交任意 user ID 不构成 Self。 |
| `Team` | 服务端由当前主体的有效班组成员/授权关系裁决，不信任客户端自行声明 team。 |
| `WorkCenter` | 服务端验证目标工作中心属于当前主体授权范围；仅存在 `workCenterId` 查询参数不等于已经建立授权范围。 |
| `Workshop` | 服务端验证请求车间属于当前主体授权范围。 |
| `Organization` | 只以 `organizationId + environmentId` 为硬 scope 的组织业务队列；不能称为“我的任务”。 |

`GET /api/business-console/v1/me/work-context` 提供当前 principal 的候选/授权范围上下文，但某个域是否真正消费该 scope，仍必须按该域 Gateway/服务实现逐条核对。

## 横切契约

- Gateway 请求必须经过 bearer、`organizationId + environmentId` 与 operation permission 裁决；前端隐藏按钮只属于 UX。
- 下游非 2xx 状态不能被页面改写成成功；无法解析的下游响应不能用旧行、默认值或上次成功状态替代。
- 受治理写操作若返回 `OperationReceipt`，`confirmed` 与 `accepted` 必须按公开语义区分；`accepted` 需要沿提供的公开读面回读最终状态。
- `allowedActions` / `blockReasons` 只有在服务端实际返回时才是权威动作许可。没有该字段的域继续以各自聚合状态、版本、权限与领域错误裁决，前端不得猜造通用动作表。
- 强 ID（如 `operationTaskId`、`warehouseTaskId`、`inspectionTaskId`、`workOrderId`、`alarmEventId`）和幂等/版本字段必须沿公开契约传递；页面顺序、显示编号或本地选择不能替代强 ID。

## 当前主要现场表面矩阵

| 域 / 页面 | 当前公开读写面 | 当前范围事实 | 动作事实来源 |
| --- | --- | --- | --- |
| MES `/mes/operation`、`/mes/work-orders*`、`/mes/report` | operation task、work order、reportable task 以及相应工单/报工命令 | operation/work-order/report 主路径支持服务端校验的 Self/Team/WorkCenter/Workshop/Organization；写入继续按强 ID + 所选授权 scope 命中目标 | MES 返回的 `allowedActions/blockReasons/evaluatedAtUtc`、聚合生命周期、版本/幂等与公开回读 |
| MES `/mes/dispatch` | dispatch task 列表 | Organization 内 assignment/userId 业务过滤；客户端可控 `assignedUserId` 不能证明 Self | 行阻塞事实与服务端权限/状态；不要把筛选后的“本人”行冒充 principal-bound inbox |
| MES `/mes/issue`、`/mes/receipt` | 线边发料/收料与完工入库请求 | 当前以 Organization 业务范围为主 | request 强 ID、幂等键、accepted receipt 与后续 request/inventory link 回读 |
| WMS `/wms/putaway`、`/wms/pick` | 上架/拣货任务创建、派工与 start/progress/exception/complete | 任务按当前实现的 `self` / `work-pool` / `site` 范围返回与执行；客户端不能注入授权主体 | 任务 `allowedActions/blockReasons/version`，动作 `expectedVersion + idempotencyKey` |
| WMS `/wms/review`、`/wms/count` | 出库复核、盘点执行 | 同样由当前服务端 operator/pool/site 归属与版本事实裁决 | 父单/盘点聚合状态、版本、幂等键以及库存过账公开回读 |
| Quality `/quality/tasks` | 检验任务列表/详情、派工、领取、提交记录 | Self 绑定当前 principal；Team 绑定授权班组；管理读面可有 Organization 视角 | 任务 `allowedActions/blockReasons/version`、当前 inspector/team assignment、提交幂等与强 ID 回执 |
| Quality inspection record / NCR 页面 | inspection record、NCR 列表/详情与处置/复检命令 | 记录/NCR 主体为 Organization 业务事实；NCR 查看与管理权限分离，不因任务曾有 assignment 推导成个人范围 | Quality 聚合状态、NCR/record 强 ID；返工处置用稳定意图键和 accepted receipt，详情读到 MES 创建状态 `created` + 系统工单强 ID 才确认成功 |
| Maintenance PDA/PC 工单 | 工单队列/详情和当前生命周期命令 | Self 队列必须由服务端 principal 与持久 assignment 绑定；管理表面另按当前权限/范围实现核对 | 详情 `allowedActions/blockReasons/lifecycle`（存在时）、工单版本/幂等与公开详情回读 |
| Equipment alarms PDA/PC | 报警列表、acknowledge/shelve/unshelve | 当前报警集合主要是 Organization 范围；没有自动推导的人员责任范围 | `alarmEventId`、报警 lifecycle、命令 409/幂等语义和公开 GET 回读；无统一 `allowedActions` 时前端 fail closed |
| 设备详情/遥测历史/可靠性 | 设备、历史、健康/可靠性查询 | Organization + 设备/时间等业务过滤，不等于人员任务 scope | 原始数据 freshness、样本/历史可用性与服务返回；空历史不能解释成“健康/无故障” |

## 页面消费纪律

1. 页面必须把 Organization 队列、Self inbox、Team/work-pool 等范围按服务端真实含义展示，不能根据客户端过滤自行改名。
2. 未知状态、缺少强 ID、缺少所需 assignment/scope 或服务端未授权的动作默认 fail closed。
3. `accepted`、HTTP 200、toast、乐观状态和页面按钮都不是最终业务成功；按 operation 定义回读权威实体。
4. 当前应用表面以仓库真实 app/route 为准；不存在的工位机/车间机不能由 PDA、PC 响应式页或 `apps/screen` 冒充。
5. 项目缺口、未来表面和 owner 只在对应 Product / GitHub / Linear 跟踪，不在本 Reference 维护路线图。

## 何时更新

公开 operation/schema、Gateway scope/permission、服务端 assignment、`allowedActions`、回执/强 ID、页面实际消费任一发生变化时，更新本页受影响行；一次性调查、完成批次与历史结论写入冻结 Report，而不是追加“已完成”章节。
