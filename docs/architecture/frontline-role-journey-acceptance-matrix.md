# 现场作业多端角色旅程与真实账号验收矩阵

> 适用范围：MAN-621 / GitHub
> [#1158](https://github.com/Mang-X/Nerv-IIP/issues/1158) 的 M0 事实验收。
> 角色、权限、主体与范围模型以
> [#1156](https://github.com/Mang-X/Nerv-IIP/issues/1156) 为后续权威输入，契约、页面、任务范围与
> allowed action 以 [#1157](https://github.com/Mang-X/Nerv-IIP/issues/1157)
> 为后续权威输入；两项尚未合入时，本文件只引用 issue，不复制并行分支结论。

本文固定 PDA、工位机、班组/车间终端和 Business Console 的角色旅程、异常支线、真实账号前置与证据口径。
它是验收清单，不新增权限、范围、页面、业务命令或种子事实。

## 1. 裁决口径

### 1.1 状态

| 状态     | 含义                                                                                            |
| -------- | ----------------------------------------------------------------------------------------------- |
| 可验     | 当前代码已有公开 Gateway、页面和账号前置，可以在真实栈执行；仍须以本次 run 的业务回执判定通过。 |
| 部分可验 | 当前链路可观察，但角色范围、分页、终态或退出等至少一项不完整；不得写成整条旅程通过。            |
| 阻塞     | 缺真实账号、主数据关系、公开 Gateway 或页面；只记录补齐项，不用 admin、mock 或相邻页面替代。    |

### 1.2 最终证据

1. 最终证据运行在 PostgreSQL + Redis profile，认证走 PlatformGateway，业务读写只走
   BusinessGateway `/api/business-console/v1/**`。浏览器网络记录、公开响应和同一公开读面的状态回读组成证据链。
2. 登录证据必须包含 `loginConsoleUser` 成功以及 `getConsolePrincipal` 返回的
   `principalId`、`organizationId`、`environmentId`、`roleIds` 和 `permissionCodes`；不得记录口令或 token。
3. 业务动作必须保存请求前实体强 ID、命令回执和请求后公开读面。强 ID 是
   `operationTaskId`、`inboundOrderId`、`warehouseTaskId`、`outboundOrderId`、
   `inspectionTaskId`、`inspectionRecordId`、`nonconformanceReportId` 或
   `workOrderId`，不能用页面行号、显示顺序或 UI 默认值替代。
4. seed 数量、首页计数、HTTP 200、服务启动成功和直接数据库查询都不是业务成功证据。
   数据库只作为真实持久化基础设施，不作为验收读面。
5. 写动作结果不确定时先用公开列表/详情核实，不盲重放非幂等命令。终态必须刷新公开读面，
   验证状态稳定且 UI 不再提供非法动作。
6. 每次 evidence manifest 至少记录 commit、run/session ID、PostgreSQL/Redis profile、
   账号、组织/环境、UTC 时间、公开 operationId、强 ID、前后状态、响应摘要和清理结果。
7. 本任务不重复记录 `name` → `displayName` 的独立缺陷或修复。离线 outbox、相机、
   实体扫码枪、打印机和其他专用硬件属于 P2，不进入本矩阵。

### 1.3 当前快照不是固定断言

合入主线的真实栈走查曾观察到：`emp010` 首页进行中 2、待开工 7，`emp012` 待开工 10，
`emp049` 仓储摘要 4/3/6/0，`emp034` 待检 100。它们只证明账号和公开读面曾产生角色差异，
不作为以后 run 的固定数量断言。验收必须记录当次返回的强 ID，并以业务状态和回执裁决。

## 2. 账号与主数据前置

领导演示只有设置进程级 `NERV_IIP_LEADER_DEMO_WORKER_PASSWORD` 后，以下四个账号才可登录。
未设置时，58 名员工都只是人员目录事实。口令不得写入仓库、命令行、截图或证据包。

| 登录名   | 主体 / 工号                | 当前登录角色         | 当前可验范围                     | 结论     |
| -------- | -------------------------- | -------------------- | -------------------------------- | -------- |
| `emp010` | `user-emp-010` / `EMP-010` | `role-pda-operator`  | 本人派工首页；MES 组织范围执行页 | 部分可验 |
| `emp012` | `user-emp-012` / `EMP-012` | `role-pda-operator`  | 本人派工首页；MES 前序/物料门禁  | 部分可验 |
| `emp034` | `user-emp-034` / `EMP-034` | `role-pda-inspector` | 组织范围待检和检验提交           | 部分可验 |
| `emp049` | `user-emp-049` / `EMP-049` | `role-pda-warehouse` | 组织范围收货/上架/拣货/复核      | 部分可验 |

以下人员已有员工目录，但不是本基线的可登录验收账号：

| 目标角色     | 已有人员事实                                                              | 缺失清单                                                                                                                                     |
| ------------ | ------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| 维修技师     | `EMP-043..046` 的岗位为维修技师；`EMP-042` 为设备主管，`EMP-047` 为点检员 | 缺维修 PDA 角色、最小权限集、登录成员资格、维修工单指派字段/样本和本人任务读面。不能用 `emp010` 所带报修权限冒充维修技师。                   |
| 班组长       | `EMP-004..009` 为六名班组长，MasterData 有车间级班组与成员关系            | 缺可登录成员资格、班组长角色、Team scope、班组任务/异常聚合读面和班组代操作审计。                                                            |
| 车间组长     | 当前人员岗位没有字面为“车间组长”的记录；`EMP-001..003` 是车间主任         | 缺术语裁决、人员到具体 Workshop 的权威绑定、可登录角色、Workshop scope 和车间终端工作台。不得把车间主任静默改名为车间组长。                  |
| PC 计划/管理 | `EMP-029` 为计划主管，`EMP-030..032` 为计划员；其他部门也有主管岗位       | 缺业务角色、登录成员资格、角色化导航验收账号和角色对应任务数据。`admin` / `role-platform-admin` 只能做全权限诊断，不能证明计划员或管理角色。 |

## 3. 总览矩阵

| 旅程                   | 端       | 账号       | 当前数据范围                        | 公开入口                      | 当前结果       |
| ---------------------- | -------- | ---------- | ----------------------------------- | ----------------------------- | -------------- |
| O1 在制工序继续执行    | PDA      | `emp010`   | 首页 Self；执行页 Organization      | MES dispatch/operation facade | 部分可验       |
| O2 可开工任务启动      | PDA      | `emp010`   | 首页 Self；执行页 Organization      | MES start facade              | 部分可验       |
| O3 前序未完阻断        | PDA      | `emp012`   | 首页 Self；命令按 org/env + task ID | MES start facade              | 可验服务端阻断 |
| O4 物料未齐套阻断      | PDA      | `emp012`   | 首页 Self；命令按 org/env + task ID | MES start facade              | 可验服务端阻断 |
| W1 收货并进入上架观察  | PDA      | `emp049`   | Organization                        | WMS inbound/putaway facade    | 部分可验       |
| W2 拣货并复核发货      | PDA      | `emp049`   | Organization                        | WMS picking/outbound facade   | 部分可验       |
| Q1 待检执行与 NCR 支线 | PDA      | `emp034`   | Organization                        | Quality task/record facade    | 部分可验       |
| M1 维修人员处理工单    | PDA      | 无         | 无 Technician scope                 | Maintenance facade 存在       | 阻塞           |
| S1 工人固定工位执行    | 工位机   | 无         | 无                                  | `business-workstation` 未实现 | 阻塞           |
| T1 班组/车间当班处置   | 车间终端 | 无         | 无 Team/Workshop scope              | 专用工作台未实现              | 阻塞           |
| P1 计划员/管理者工作台 | PC       | 无代表账号 | admin 仅 Organization               | Business Console              | 阻塞角色终验   |

## 4. PDA 操作工：`emp010` / `emp012`

### 4.1 共用主旅程

| 环节           | 预期结果与证据                                                                                                                                                                                              |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录           | 从 PDA 登录页调用 `loginConsoleUser`；`getConsolePrincipal` 必须返回对应 `user-emp-*`、`role-pda-operator`、org/env 与 MES 最小权限。                                                                       |
| 默认工作台     | 权限聚合首页只出现允许的 MES、报警、报修入口。“我的任务”用 `listBusinessConsoleMesDispatchTasks` 按 `assignedUserId=principalId` 分别读取 Queued/InProgress/Paused，行级再次核对 `assignedUserId`。         |
| 范围           | 首页是 Self；`/mes/operation` 当前调用 `listBusinessConsoleMesOperationTasks` 时没有 `assignedUserId`，实际是 Organization scope。正式角色终验前必须由 #1156/#1157 收口，当前不得声称执行页只显示本人任务。 |
| 筛选/分页/刷新 | 工序页扫码只写 `keyword`；请求固定 `skip=0,take=100`，没有加载更多/分页控件。错误面可手动刷新。超过 100 条时不能证明全量，登记为 P1。                                                                       |
| 详情/强 ID     | 任务行至少保存 `operationTaskId`、`workOrderId`、`operationCode`、`workCenterId`、`assignedUserId`、当前 `status`；显示名不能替代 ID。                                                                      |
| 动作           | Ready 可开始；InProgress 可暂停/完成；Paused 可恢复/完成。服务端开始前校验前序、质量、设备与物料；完成前再校验前序。                                                                                        |
| 权威回执       | start/pause/resume/complete 响应中的 `operationTaskId`、`status`、`changedAtUtc`，随后同 ID 公开列表回读同一状态。PDA 自己生成的幂等键不是业务回执。                                                        |
| 终态只读       | Completed/Cancelled 等状态刷新后 `actionsFor` 返回空，页面显示“当前状态无可执行动作”；再次提交非法状态必须由服务端拒绝且状态不变。                                                                          |
| 退出           | `@nerv-iip/auth` store 支持 revoke，但 PDA 当前没有可见退出入口。该项阻塞整条角色旅程通过，需补 UI、调用 `logoutConsoleSession`、清本地会话并验证受保护路由回到登录页。                                     |

### 4.2 正常与异常支线

| 编号 | 数据前置                                                                 | 操作                                           | 权威期望                                                                                                                                                                    |
| ---- | ------------------------------------------------------------------------ | ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| O1   | `emp010` 名下至少一条 InProgress/Paused 任务                             | 打开同一 `operationTaskId`，暂停或恢复，再刷新 | 回执和列表状态一致；任务仍属 `user-emp-010`；不得只截“操作成功”页。                                                                                                         |
| O2   | `emp010` 名下一条 Ready 且前序完成、质量/设备/物料均放行的任务           | 开始任务                                       | 返回同一 `operationTaskId` 的进行中状态；工单/任务公开读面同步。只有 Queued 时 UI 不提供开始动作，须记录为场景数据前置未满足，不能把首页“待开工”计数当作可启动证明。        |
| O3   | `emp012` 名下一条后序任务，至少一个更小 `operationSequence` 未 Completed | 尝试开始                                       | 服务端返回“前序工序尚未完成”并列出 blocking operation task IDs；目标任务状态不变。若当前状态使 UI 隐藏开始按钮，只能证明公开 Gateway 的服务端门禁，端到端旅程仍为部分可验。 |
| O4   | `emp012` 名下一条有真实物料需求且存在 shortage 的可见任务                | 尝试开始                                       | 服务端返回“物料齐套未满足”及 shortage 原因；目标任务和工单状态不变。不得用空需求或缺快照伪装 shortage；若 UI 没有开始动作，处理方式与 O3 相同。                             |

## 5. PDA 仓储：`emp049`

### 5.1 共用主旅程

| 环节           | 预期结果与证据                                                                                                                                                                                           |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录/工作台    | `getConsolePrincipal` 返回 `role-pda-warehouse`；首页按权限展示待收货、待上架、待拣货、待盘点总量和 WMS 快捷入口。                                                                                       |
| 范围           | 当前 inbound/outbound/putaway/picking 都仅按 org/env 查询。WMS 单据和任务没有 assigned operator 持久字段，历史里的 `user-emp-049..052` 也未落到单据；因此这些是 Organization scope，不是“我的仓储任务”。 |
| 筛选/分页/刷新 | 收货/复核扫码使用 `keyword`，上架/拣货扫码使用 `locationCode`。列表固定 `skip=0,take=100`，无分页控件；错误面可刷新。超过 100 条为 P1 缺口。                                                             |
| 详情/强 ID     | 收货保存 `inboundOrderId`/`inboundOrderNo` 与逐行 `inboundOrderLineId`；上架/拣货保存 `warehouseTaskId`/`taskNo`/`sourceOrderNo`；复核保存 `outboundOrderId`/`outboundOrderNo`。                         |
| 退出           | 与操作工相同，PDA 缺可见退出入口，当前阻塞。                                                                                                                                                             |

### 5.2 收货 → 质检门禁 → 上架

1. 用 `listBusinessConsoleWmsInboundOrders` 选择 Open 单；按精确 `inboundOrderNo`
   拉取全部 receiving-quality-gates 行，未完整取回时 fail closed。
2. 扫码/录入批号和效期后调用 `completeBusinessConsoleWmsInboundOrder`。权威命令回执是
   `requestId` / `inventoryMovementId`，并须公开回读 inbound 状态和逐行批效期。
3. 待检/不合格单不得出现上架引导；`isReleasedForPutaway=true` 后才允许进入上架观察。
4. `/wms/putaway` 当前是只读任务清单，没有逐任务 complete。终态以父收货单和库存移动公开回执为准，
   不把浏览到 `warehouseTaskId` 或 HTTP 200 当作上架完成。

### 5.3 拣货 → 复核发货

1. `listBusinessConsoleWmsPickingTasks` 只读展示 `warehouseTaskId`、库位流向、计划/执行量和状态；
   当前没有逐任务 complete。
2. 选对应 `outboundOrderId`，填写非空 `packReviewNo` 和复核结论，调用
   `completeBusinessConsoleWmsOutboundOrder`。
3. 权威命令回执是 `requestId` / `inventoryMovementId`；随后公开回读 outbound
   `status`、`inventoryPostingStatus`、逐行 `issuedQuantity` 和失败信息。
4. 已完成出库单保持只读；重复同一意图只能复用原幂等键，不能生成新键后盲重放。

## 6. PDA 质量：`emp034`

| 环节           | 预期结果与证据                                                                                                                                                                                                              |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录/工作台    | `getConsolePrincipal` 返回 `role-pda-inspector`；首页显示组织范围 pending 待检总量和检验入口。                                                                                                                              |
| 范围           | `listBusinessConsoleQualityInspectionTasks` 当前只带 org/env/status，没有 inspector assignment/filter；因此不是“我的待检”。提交时 `inspectorUserId` 后置注入 `user-emp-034`，只能证明实际执行人，不能证明任务预先派给该人。 |
| 筛选/分页/刷新 | 默认服务端 `status=pending`；来源类型、超期排序和扫码匹配是客户端逻辑。页面按不超过 200 的页迭代“加载更多/加载全部”，错误面可刷新。                                                                                         |
| 详情/强 ID     | 保存 `inspectionTaskId`、`inspectionPlanId`、`sourceDocumentId`、SKU、批次、数量、到期时间；计划特性来自公开 characteristics 读面。                                                                                         |
| 动作/门禁      | 必检特性完整、每行有效，不合格时处置原因必填；提交 `createBusinessConsoleQualityInspectionRecordFromTask`。已 completed 任务重放返回同一记录。                                                                              |
| 权威回执       | 后端返回 `inspectionRecordId`、`result`，不合格时还返回 `nonconformanceReportId` / `nonconformanceReportCode`。结果页必须使用这些字段，随后从记录/NCR 公开详情回读。                                                        |
| 终态只读       | 提交后任务转 completed 并退出 pending 列表；检验记录与 NCR 详情作为只读证据，不能再次创建第二条首检。                                                                                                                       |
| 退出           | PDA 缺可见退出入口，当前阻塞。                                                                                                                                                                                              |

P1 必须新增真实 inspector assignment：任务持久保存被派检验员强 ID，公开列表支持
`assignedInspectorUserId=principalId` 或等价 Self/Team scope，Gateway 校验当前主体能处理该任务，
并保留 assignment/代检审计。仅把 `inspectorUserId` 写进最终记录不满足该要求。

## 7. 维修、工位机与班组/车间终端

### 7.1 维修 PDA

Maintenance 的公开 list/create/inspection facade 和 PDA 报修、点检页已存在，但当前没有维修技师账号或
technician assignment。“近期维修工单”也是 org/env 前 100 条，创建报修只记录
`openedBy=loginName`；创建回执 `workOrderId` 只能证明报修，不证明维修受理、执行或完工。

补齐后旅程必须覆盖：维修技师登录 → 本人/团队工单工作台 → 状态/设备/优先级筛选与分页 →
`workOrderId` 详情 → 受理/开始/备件/完工或明确 blocker → 每步权威回执 →
Completed/Cancelled 只读 → 退出。目前整条旅程为阻塞。

### 7.2 工人固定工位

`frontend/apps/business-workstation` 仅在架构文档中作为 roadmap，仓库当前没有应用、登录、工作台、
设备/工位绑定、任务详情、动作回执、终态或退出。不得用 PDA 响应式页面或 `screen` 大屏代替。

补齐清单：受管终端身份、登录/换班、工作中心或设备绑定、Self/WorkCenter scope、工序/物料/SOP
聚合工作台、扫码/手输降级、公开 Gateway、强 ID 回执、终态只读和安全退出。

### 7.3 班组长/车间终端

当前 `frontend/apps/screen` 是挂墙大屏：默认 build 使用 mock；real 模式仅部分设备/仓储取数走
BusinessGateway，工厂/车间/产线/质量等 fetcher 仍有 mock seam，且没有可见退出入口。
它是只读展示面，不是班组长或车间组长的处置终端。

补齐后旅程必须覆盖：真实班组长/车间角色登录 → Team/Workshop 默认工作台 →
班次、产线、工作中心、人员、状态筛选和服务端分页/刷新 → 强 ID 详情 →
放行/转派/异常确认等受权动作或明确 blocker → 审计回执 → 终态只读 → 退出。
当前账号、范围、公开聚合和专用页面均缺失，整条旅程为阻塞。

## 8. Business Console 计划/管理角色

Business Console 已有真实登录、按 `permissionCodes` 裁剪的顶部域/侧栏、组织/环境上下文、
工作台 summary、各域筛选分页与 shell 退出。当前唯一受治理的演示登录代表是
`admin` / `role-platform-admin`，它不能替代计划员、计划主管、质量主管、设备主管、仓储主管或车间管理角色。

| 环节           | 当前事实 / 终验要求                                                                                           |
| -------------- | ------------------------------------------------------------------------------------------------------------- |
| 登录           | 当前只能用 admin 做连通性诊断；终验必须给每种目标 PC 角色独立账号，并用 `/auth/me` 证明 role/permissions。    |
| 默认工作台     | 当前按权限裁剪域和工作台来源；终验须证明每个角色只见职责内待办、消息、预警和 KPI，不用全权限 admin 截图代替。 |
| 范围           | 当前业务上下文来自 principal 的 org/env。计划/管理角色的 Team/WorkCenter/Workshop 范围尚未交付。              |
| 筛选/分页/刷新 | 各业务页已有独立筛选分页事实，但必须按 #1157 对目标旅程逐页固定；本文件不把“页面存在”扩写成角色可用。         |
| 详情/动作/回执 | 以页面实体强 ID、公开 Gateway command response 和同一公开详情回读为准；没有角色账号和样本前均是 gap。         |
| 终态只读       | 每个业务终态必须隐藏/禁用非法动作，直接调用仍由 Gateway/服务端拒绝；需要目标角色实测。                        |
| 退出           | shell“退出登录”调用 `logoutConsoleSession`，清本地状态并跳 `/login`；目标角色账号补齐后可验。                 |

## 9. 正式通过条件

1. 四个现有 PDA 账号分别完成其正常与异常支线；每条证据含当次强 ID、命令回执和公开回读。
2. PDA 增加可见退出并完成 session revoke；角色化列表范围和分页缺口关闭，或明确保持“部分可验”。
3. 维修、班组长、车间角色和 PC 计划/管理账号、角色、范围与数据前置全部由公开事实补齐。
4. 工位机、班组/车间终端若仍未实现，相关旅程保持阻塞；不得用 mock screen、PDA 或 admin 代验。
5. 全部证据来自同一 commit 的真实 PostgreSQL/Redis run，资源被精确清理，且没有直接 DB 写读证据。
