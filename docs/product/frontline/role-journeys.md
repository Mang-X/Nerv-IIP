# 现场作业多端角色旅程与验收语义

> 来源：M2-C 从原 `docs/architecture/frontline-role-journey-acceptance-matrix.md` 拆分。
> 本页保留角色旅程、异常支线、账号/主数据前置与产品验收语义；通用真实运行证据模板已独立到 [`../../reference/frontline/acceptance-evidence.md`](../../reference/frontline/acceptance-evidence.md)。

本文覆盖 PDA、工位机、班组/车间终端和 Business Console。它不新增权限、范围、页面、业务命令或种子事实；当前实现仍须以代码、公开 Gateway 契约和测试核实。

## 1. 裁决口径

| 状态 | 含义 |
| --- | --- |
| 可验 | 当前代码已有公开 Gateway、页面和账号前置，可以在真实栈执行；仍须以本次 run 的业务回执判定通过。 |
| 部分可验 | 当前链路可观察，但角色范围、分页、终态或退出等至少一项不完整；不得写成整条旅程通过。 |
| 阻塞 | 缺真实账号、主数据关系、公开 Gateway 或页面；只记录补齐项，不用 admin、mock 或相邻页面替代。 |

通用证据格式、强 ID、公开回读、manifest 字段和“演示数量不是固定断言”的规则统一见[真实账号验收证据模板](../../reference/frontline/acceptance-evidence.md)。

### 1.1 生产缺口归属

#1156 / #1157 是上游事实盘点与事实矩阵，不是生产修复 owner。当前旅程引用以下 follow-up：

| 缺口 | 实现归属 |
| --- | --- |
| 主体作业上下文、MES 任务归属与范围 | #1163、#1164、#1165 |
| 全端状态动作门禁、PDA MES 引导式执行 | #1160、#1174 |
| PDA WMS 终态与作业闭环 | #1160、#1176 |
| PDA Quality 个人/团队待检与记录结果入口 | #1177 |
| 维修主体范围、派工筛选与生命周期 | #1164、#1168 |

## 2. 账号与主数据前置

领导演示只有设置进程级 `NERV_IIP_LEADER_DEMO_WORKER_PASSWORD` 后，以下四个账号才可登录；口令不得写入仓库、命令行、截图或证据包。

| 登录名 | 主体 / 工号 | 当前登录角色 | 当前可验范围 | 结论 |
| --- | --- | --- | --- | --- |
| `emp010` | `user-emp-010` / `EMP-010` | `role-pda-operator` | 组织内客户端 assignment 过滤；MES 组织范围执行页 | 部分可验 |
| `emp012` | `user-emp-012` / `EMP-012` | `role-pda-operator` | 组织内客户端 assignment 过滤；MES 前序/物料门禁 | 部分可验 |
| `emp034` | `user-emp-034` / `EMP-034` | `role-pda-inspector` | Self 待检、领取和检验提交 | 可验 |
| `emp049` | `user-emp-049` / `EMP-049` | `role-pda-warehouse` | 组织范围收货/上架/拣货/复核 | 部分可验 |

以下人员已有员工目录，但不是本基线的可登录验收账号：

| 目标角色 | 已有人员事实 | 缺失清单 |
| --- | --- | --- |
| 维修技师 | `EMP-043..046` 岗位为维修技师，工单已有 `assignedTechnicianUserId` | 缺维修 PDA 角色、最小权限集、登录成员资格、服务端权威当前技师任务读面和维修生命周期页面。 |
| 班组长 | `EMP-004..009` 为六名班组长，MasterData 有车间级班组与成员关系 | 缺可登录成员资格、班组长角色、Team scope、班组任务/异常聚合读面和代操作审计。 |
| 车间组长 | 当前人员岗位没有字面“车间组长”；`EMP-001..003` 是车间主任 | 缺术语裁决、人员到 Workshop 的权威绑定、可登录角色、Workshop scope 和车间终端工作台。 |
| PC 计划/管理 | `EMP-029` 为计划主管，`EMP-030..032` 为计划员 | 缺业务角色、登录成员资格、角色化导航验收账号和对应任务数据；admin 只能做诊断。 |

## 3. 总览矩阵

| 旅程 | 端 | 账号 | 当前数据范围 | 公开入口 | 当前结果 |
| --- | --- | --- | --- | --- | --- |
| O1 在制工序继续执行 | PDA | `emp010` | 当前主体授权作业范围；双强 ID 精确打开 | MES operation facade | 部分可验 |
| O2 可开工任务启动 | PDA | `emp010` | 当前主体授权作业范围；双强 ID 精确打开 | MES start facade | 阻塞状态映射 |
| O3 前序未完阻断 | PDA | `emp012` | 当前主体授权作业范围 + task ID | MES start facade | 可验服务端阻断 |
| O4 物料未齐套阻断 | PDA | `emp012` | 当前主体授权作业范围 + task ID | MES start facade | 可验服务端阻断 |
| W1 收货并进入上架观察 | PDA | `emp049` | Organization | WMS inbound/putaway facade | 阻塞终态守卫 |
| W2 拣货并复核发货 | PDA | `emp049` | Organization | WMS picking/outbound facade | 阻塞终态守卫 |
| Q1 待检执行与 NCR 支线 | PDA | `emp034` | Self（当前 principal） | Quality task/record facade | 可验 |
| M1 维修人员处理工单 | PDA | 无 | 无 Technician scope | Maintenance facade 存在 | 阻塞 |
| S1 工人固定工位执行 | 工位机 | 无 | 无 | `business-workstation` 未实现 | 阻塞 |
| T1 班组/车间当班处置 | 车间终端 | 无 | 无 Team/Workshop scope | 专用工作台未实现 | 阻塞 |
| P1 计划员/管理者工作台 | PC | 无代表账号 | admin 仅 Organization | Business Console | 阻塞角色终验 |

## 4. PDA 操作工：`emp010` / `emp012`

### 4.1 共用主旅程

- 登录：`loginConsoleUser` 后由 `getConsolePrincipal` 返回主体、角色、org/env 与 MES 最小权限。
- 默认工作台：只出现允许的 MES、报警、报修入口；没有 principal-bound 事实时不得写“我的任务”。
- 范围：`/mes/operation` 使用 permission-aware work-context 的已验证选择查询；范围未核验时 fail closed。
- 详情：入口必须同时携带并精确匹配 `workOrderId + operationTaskId`；显示名不能替代强 ID。
- 动作：服务端真实生命周期为 Queued 可开始、InProgress 可暂停/完成、Paused 可恢复/完成；开始前校验前序、质量、设备与物料。页面动作必须消费服务端权威门禁。
- 权威回执：写动作回执中的 `operationTaskId/status/changedAtUtc` 之后用同 ID 公开读面回读。
- 终态：Completed/Cancelled 等状态只读，不再提供非法动作。
- 退出：本地会话先清理，远端 revoke 有界执行；远端失败不阻止返回登录页，但必须明确撤销状态。

### 4.2 正常与异常支线

| 编号 | 数据前置 | 操作 | 权威期望 |
| --- | --- | --- | --- |
| O1 | `emp010` 的 InProgress/Paused 任务 | 打开同一 task，暂停或恢复并刷新 | 回执和列表状态一致；客户端过滤本身不能证明主体授权。 |
| O2 | `emp010` 的 Queued 任务，前序/质量/设备/物料均放行 | 从 PDA 开始任务 | 服务端支持 Queued → InProgress；若 PDA 仍只匹配 `Ready`，场景保持阻塞并关联 #1160/#1174。 |
| O3 | `emp012` 的 Queued 后序任务且更小 sequence 未 Completed | 通过公开 Gateway 尝试开始 | 返回可读“前序工序尚未完成”，不泄露 raw task ID；目标状态不变。 |
| O4 | `emp012` 的 Queued 任务且存在真实物料 shortage | 通过公开 Gateway 尝试开始 | 返回“物料齐套未满足”及 shortage 原因；不得用空需求或缺快照伪装 shortage。 |

## 5. PDA 仓储：`emp049`

- 登录后只显示其 WMS 快捷入口；范围由服务端可信 `self/work-pool/site` 目录与当前选择共同裁决。
- 收货/复核扫码使用 `keyword`，上架/拣货使用 `locationCode`；列表覆盖不足时必须明确，不伪装全量。
- 收货使用 `inboundOrderId`，上架/拣货使用 `warehouseTaskId`，复核使用 `outboundOrderId`。
- W1：只从 Open 入库单进入完成流；质检放行后才允许进入上架观察。`completeBusinessConsoleWmsInboundOrder` 的 `requestId/inventoryMovementId` 必须再由公开读面回读。
- W2：拣货任务本身只读；出库复核由 `completeBusinessConsoleWmsOutboundOrder` 完成并回读 `status/inventoryPostingStatus/issuedQuantity`。
- Completed/Cancelled 等终态可用于证据回读，但不得再次进入完成抽屉；守卫未补齐时 W1/W2 仍保持阻塞。
- 同一写意图重试复用原幂等键，不能生成新键盲重放。

## 6. PDA 质量：`emp034`

- 首页和 `/quality/tasks` 使用当前 principal 的 Self pending 待检，不把组织总量称为个人任务。
- `scopeKind=self` 由 Gateway 重新绑定认证主体，Quality 按 `assigned_user_id` 服务端过滤。
- Pending 任务先以 `expectedVersion + idempotencyKey` 原子领取；只有权威动作包含 `submit` 才进入录入。
- 提交检验时 inspector 由认证 principal 注入；无权、生命周期冲突、被他人领取分别由服务端拒绝，Completed 只读。
- 提交回执保留 `inspectionRecordId/result` 及可选 NCR 标识，再用公开 `getBusinessConsoleQualityInspectionRecord` 按同一 ID 回读。结果页记录详情入口未完成时应标 UI gap。

## 7. 维修、工位机与班组/车间终端

### 7.1 维修 PDA

Maintenance 已有公开工单/点检能力与 `assignedTechnicianUserId`，但当前验收需要真实维修技师账号、最小权限、principal-bound 当前技师队列和生命周期 UI。补齐后旅程为：维修技师登录 → self/team 工单工作台 → 筛选分页 → `workOrderId` 详情 → 受理/开始/备件/完工或明确 blocker → 每步公开回执 → 终态只读 → 退出。未补齐前保持阻塞。

### 7.2 工人固定工位

`frontend/apps/business-workstation` 当前未实现，不能用 PDA 响应式页面或 `screen` 大屏代替。目标需要受管终端身份、换班、工作中心/设备绑定、工序/物料/SOP 聚合工作台、扫码/手输降级、公开 Gateway、强 ID 回执、终态只读和安全退出。

### 7.3 班组长/车间终端

`frontend/apps/screen` 是只读挂墙大屏，不是班组长或车间组长处置终端。目标旅程必须有真实角色登录、Team/Workshop 默认工作台、班次/产线/工作中心/人员筛选、强 ID 详情、受权动作与审计回执、终态只读和退出；账号、范围、聚合和专用页面缺失时保持阻塞。

## 8. Business Console 计划/管理角色

Business Console 已有真实登录、按 `permissionCodes` 裁剪的域/侧栏、组织/环境上下文、工作台 summary、筛选分页与 shell 退出；当前演示代表 `admin / role-platform-admin` 不能替代计划员、计划主管、质量主管、设备主管、仓储主管或车间管理角色。

| 环节 | 当前事实 / 终验要求 |
| --- | --- |
| 登录 | admin 只做连通性诊断；终验必须给每种目标 PC 角色独立账号，并用 `/auth/me` 证明 role/permissions。 |
| 默认工作台 | 终验须证明每个角色只见职责内待办、消息、预警和 KPI，不用全权限 admin 截图代替。 |
| 范围 | 当前业务上下文来自 principal 的 org/env；Team/WorkCenter/Workshop 范围尚未交付时不能假装存在。 |
| 详情/动作/回执 | 以页面实体强 ID、公开 Gateway command response 和同一公开详情回读为准；没有角色账号和样本前均是 gap。 |
| 终态只读 | 每个业务终态必须隐藏或禁用非法动作，直接调用仍由 Gateway/服务端拒绝。 |
| 退出 | shell 退出调用 `logoutConsoleSession`，清本地状态并跳 `/login`。 |

## 9. 正式通过条件

1. MES 的 Queued 与 PDA 开始动作映射一致；O2 从 PDA 对真实 Queued 任务完成 Queued → InProgress，O3/O4 在同一 UI 旅程证明 blocker。
2. WMS inbound/outbound 终态行只用于回读，不进入完成抽屉；状态守卫未补齐前 W1/W2 保持阻塞。
3. 四个现有 PDA 账号分别完成正常与异常支线，每条证据含强 ID、命令回执和公开回读。
4. 维修、班组长、车间角色和 PC 计划/管理账号、角色、范围与数据前置由公开事实补齐。
5. 工位机、班组/车间终端若仍未实现，对应旅程保持阻塞，不用 mock screen、PDA 或 admin 代验。
6. 全部真实运行证据必须遵守[验收证据模板](../../reference/frontline/acceptance-evidence.md)，来自同一 commit 的受治理 run，并精确清理资源。
