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

### 1.3 演示数量不是固定断言

演示 seed、背景历史截止日和现场动作都会改变首页及列表数量。没有可复现的 run ID、manifest、
commit、时间戳和证据路径时，不在本基线引用历史精确计数。验收只记录当次 run 返回的强 ID、
动态数量、业务状态和回执，不把任何会话观察写成固定种子断言。

### 1.4 生产缺口归属

#1156 / #1157 是上游事实盘点与事实矩阵，不是生产修复 owner。本矩阵引用以下开放 follow-up：

| 缺口                                             | 实现归属                                                                                                                                                                  |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 伪“我的任务”、主体作业上下文、MES 任务归属与范围 | [#1163](https://github.com/Mang-X/Nerv-IIP/issues/1163)、[#1164](https://github.com/Mang-X/Nerv-IIP/issues/1164)、[#1165](https://github.com/Mang-X/Nerv-IIP/issues/1165) |
| 全端状态动作门禁、PDA MES 引导式执行             | [#1160](https://github.com/Mang-X/Nerv-IIP/issues/1160)、[#1174](https://github.com/Mang-X/Nerv-IIP/issues/1174)                                                          |
| PDA WMS 终态与作业闭环                           | #1160、[#1176](https://github.com/Mang-X/Nerv-IIP/issues/1176)                                                                                                            |
| PDA Quality 个人/团队待检与记录结果入口          | [#1177](https://github.com/Mang-X/Nerv-IIP/issues/1177)                                                                                                                   |
| 维修主体范围、派工筛选与生命周期                 | #1164、[#1168](https://github.com/Mang-X/Nerv-IIP/issues/1168)                                                                                                            |

## 2. 账号与主数据前置

领导演示只有设置进程级 `NERV_IIP_LEADER_DEMO_WORKER_PASSWORD` 后，以下四个账号才可登录。
未设置时，58 名员工都只是人员目录事实。口令不得写入仓库、命令行、截图或证据包。

| 登录名   | 主体 / 工号                | 当前登录角色         | 当前可验范围                                     | 结论     |
| -------- | -------------------------- | -------------------- | ------------------------------------------------ | -------- |
| `emp010` | `user-emp-010` / `EMP-010` | `role-pda-operator`  | 组织内客户端 assignment 过滤；MES 组织范围执行页 | 部分可验 |
| `emp012` | `user-emp-012` / `EMP-012` | `role-pda-operator`  | 组织内客户端 assignment 过滤；MES 前序/物料门禁  | 部分可验 |
| `emp034` | `user-emp-034` / `EMP-034` | `role-pda-inspector` | Self 待检、领取和检验提交                        | 可验     |
| `emp049` | `user-emp-049` / `EMP-049` | `role-pda-warehouse` | 组织范围收货/上架/拣货/复核                      | 部分可验 |

以下人员已有员工目录，但不是本基线的可登录验收账号：

| 目标角色     | 已有人员事实                                                                                          | 缺失清单                                                                                                                                     |
| ------------ | ----------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| 维修技师     | `EMP-043..046` 的岗位为维修技师；工单已有 `assignedTechnicianUserId`，L1 world-history 可生成指派样本 | 缺维修 PDA 角色、最小权限集、登录成员资格、服务端权威当前技师任务读面和维修生命周期页面。不能用 `emp010` 所带报修权限冒充维修技师。          |
| 班组长       | `EMP-004..009` 为六名班组长，MasterData 有车间级班组与成员关系                                        | 缺可登录成员资格、班组长角色、Team scope、班组任务/异常聚合读面和班组代操作审计。                                                            |
| 车间组长     | 当前人员岗位没有字面为“车间组长”的记录；`EMP-001..003` 是车间主任                                     | 缺术语裁决、人员到具体 Workshop 的权威绑定、可登录角色、Workshop scope 和车间终端工作台。不得把车间主任静默改名为车间组长。                  |
| PC 计划/管理 | `EMP-029` 为计划主管，`EMP-030..032` 为计划员；其他部门也有主管岗位                                   | 缺业务角色、登录成员资格、角色化导航验收账号和角色对应任务数据。`admin` / `role-platform-admin` 只能做全权限诊断，不能证明计划员或管理角色。 |

## 3. 总览矩阵

| 旅程                   | 端       | 账号       | 当前数据范围                               | 公开入口                      | 当前结果       |
| ---------------------- | -------- | ---------- | ------------------------------------------ | ----------------------------- | -------------- |
| O1 在制工序继续执行    | PDA      | `emp010`   | 首页伪“我的任务”；执行页 Organization      | MES dispatch/operation facade | 部分可验       |
| O2 可开工任务启动      | PDA      | `emp010`   | 首页伪“我的任务”；执行页 Organization      | MES start facade              | 阻塞状态映射   |
| O3 前序未完阻断        | PDA      | `emp012`   | 首页伪“我的任务”；命令按 org/env + task ID | MES start facade              | 可验服务端阻断 |
| O4 物料未齐套阻断      | PDA      | `emp012`   | 首页伪“我的任务”；命令按 org/env + task ID | MES start facade              | 可验服务端阻断 |
| W1 收货并进入上架观察  | PDA      | `emp049`   | Organization                               | WMS inbound/putaway facade    | 阻塞终态守卫   |
| W2 拣货并复核发货      | PDA      | `emp049`   | Organization                               | WMS picking/outbound facade   | 阻塞终态守卫   |
| Q1 待检执行与 NCR 支线 | PDA      | `emp034`   | Self（当前 principal）                     | Quality task/record facade    | 可验           |
| M1 维修人员处理工单    | PDA      | 无         | 无 Technician scope                        | Maintenance facade 存在       | 阻塞           |
| S1 工人固定工位执行    | 工位机   | 无         | 无                                         | `business-workstation` 未实现 | 阻塞           |
| T1 班组/车间当班处置   | 车间终端 | 无         | 无 Team/Workshop scope                     | 专用工作台未实现              | 阻塞           |
| P1 计划员/管理者工作台 | PC       | 无代表账号 | admin 仅 Organization                      | Business Console              | 阻塞角色终验   |

## 4. PDA 操作工：`emp010` / `emp012`

### 4.1 共用主旅程

| 环节           | 预期结果与证据                                                                                                                                                                                                                                                                                     |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录           | 从 PDA 登录页调用 `loginConsoleUser`；`getConsolePrincipal` 必须返回对应 `user-emp-*`、`role-pda-operator`、org/env 与 MES 最小权限。                                                                                                                                                              |
| 默认工作台     | 权限聚合首页只出现允许的 MES、报警、报修入口。任务页已移除客户端可控 `assignedUserId` 形成的伪个人 MES 列表，只提供按当前主体授权范围进入工序执行的入口；首页既有 dispatch 摘要仍不能作为权威本人任务证据。                                                                                        |
| 范围           | `/mes/operation` 使用 MAN-627 permission-aware work-context 的已验证选择查询，任务页不再声称只显示本人任务；首页既有 dispatch assignment 过滤仍不等于服务端绑定当前主体。MES 归属查询的后续收口继续由 #1163/#1165 跟进。                                                                           |
| 筛选/分页/刷新 | 工序页扫码只写 `keyword`；请求固定 `skip=0,take=100`，没有加载更多/分页控件。错误面可手动刷新。超过 100 条时不能证明全量，登记为 P1。                                                                                                                                                              |
| 详情/强 ID     | 任务行保存 `operationTaskId`、`workOrderId`、`operationCode`、`workCenterId`、`assignedUserId`、当前 `status`；显示名不能替代 ID。任务入口深链同时携带并精确匹配 `workOrderId + operationTaskId`，单边或不在当前授权范围的组合不打开动作面板。                                                     |
| 动作           | 服务端真实生命周期是 Queued 可开始、InProgress 可暂停/完成、Paused 可恢复/完成；开始前校验前序、质量、设备与物料。PDA `actionsFor` 却只给 `Ready` 开始动作，而公开列表序列化的是 `Queued`，因此当前无法从 PDA 启动真实任务。状态动作门禁由 #1160 跟进，引导式执行流由 #1174 跟进；这不是数据前置。 |
| 权威回执       | start/pause/resume/complete 响应中的 `operationTaskId`、`status`、`changedAtUtc`，随后同 ID 公开列表回读同一状态。PDA 自己生成的幂等键不是业务回执。                                                                                                                                               |
| 终态只读       | Completed/Cancelled 等状态刷新后 `actionsFor` 返回空，页面显示“当前状态无可执行动作”；再次提交非法状态必须由服务端拒绝且状态不变。                                                                                                                                                                 |
| 退出           | PDA 个人中心已有可见退出入口：先清本地认证会话，再有界调用 `logoutConsoleSession`；成功、网络失败与超时均回登录页，后两者显示远端撤销状态。自动化已覆盖本地 fail-safe，真实账号终验仍需保存 revoke 请求与受保护路由回登录证据。                                                                    |

### 4.2 正常与异常支线

| 编号 | 数据前置                                                                                                         | 操作                                                   | 权威期望                                                                                                                                                                                                        |
| ---- | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| O1   | 公开列表返回一条 `assignedUserId=user-emp-010` 的 InProgress/Paused 任务                                         | 打开同一 `operationTaskId`，暂停或恢复，再刷新         | 回执和列表状态一致；读面仍报告 `assignedUserId=user-emp-010`，但客户端可控过滤不能证明当前主体有权处理，故 #1163/#1164/#1165 收口前只算部分可验。                                                               |
| O2   | 公开列表返回一条 `assignedUserId=user-emp-010` 的 Queued 任务，且前序完成、质量/设备/物料均放行                  | 从 PDA 开始任务                                        | 服务端支持 Queued → InProgress，但当前 PDA 因错误匹配 `Ready` 不显示开始动作，场景应判阻塞并关联 #1160/#1174。修复后须返回同一 `operationTaskId` 的 InProgress 回执并公开回读；不得要求不存在的 Ready fixture。 |
| O3   | 公开列表返回一条 `assignedUserId=user-emp-012` 的 Queued 后序任务，至少一个更小 `operationSequence` 未 Completed | 通过公开 Gateway 尝试开始；PDA 路径待 #1160/#1174 修复 | 服务端返回“前序工序尚未完成”并列出 blocking operation task IDs；目标任务状态不变。当前只能证明服务端门禁，不能写成当前主体授权或 PDA 端到端通过。                                                               |
| O4   | 公开列表返回一条 `assignedUserId=user-emp-012`、有真实物料需求且存在 shortage 的 Queued 任务                     | 通过公开 Gateway 尝试开始；PDA 路径待 #1160/#1174 修复 | 服务端返回“物料齐套未满足”及 shortage 原因；目标任务和工单状态不变。不得用空需求或缺快照伪装 shortage；当前只能证明服务端门禁。                                                                                 |

## 5. PDA 仓储：`emp049`

### 5.1 共用主旅程

| 环节           | 预期结果与证据                                                                                                                                                                                                                                           |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录/工作台    | `getConsolePrincipal` 返回 `role-pda-warehouse`；首页按权限展示待收货、待上架、待拣货、待盘点总量和 WMS 快捷入口。                                                                                                                                       |
| 范围           | MAN-629 后 receipts/shipments/counts 公开目录只提供可信 `self/work-pool/site`，实际列表按当前选择服务端过滤；个人中心分别展示各目录授权选项与共享当前选择。任何范围都不称为“我的仓储任务”，真实账号终验仍须保存目录、选择和列表三者一致的证据。          |
| 筛选/分页/刷新 | 收货/复核扫码使用 `keyword`，上架/拣货扫码使用 `locationCode`。列表固定 `skip=0,take=100`，无分页控件；错误面可刷新。超过 100 条为 P1 缺口。                                                                                                             |
| 详情/强 ID     | 收货保存 `inboundOrderId`/`inboundOrderNo` 与逐行 `inboundOrderLineId`；上架/拣货保存 `warehouseTaskId`/`taskNo`/`sourceOrderNo`；复核保存 `outboundOrderId`/`outboundOrderNo`。                                                                         |
| 终态只读       | 当前 inbound/outbound 查询没有默认 `status=Open`，两个页面也会让任意返回行进入完成抽屉，没有终态状态守卫。后端拒绝非法转换不能替代只读体验；状态门禁由 #1160 跟进，WMS 作业流由 #1176 跟进。终态行可以留在列表供回读，但不得打开完成抽屉或暴露完成动作。 |
| 退出           | 与操作工相同，已有本地 fail-safe + 有界远端 revoke；真实账号终验仍需保存网络与路由证据。                                                                                                                                                                 |

### 5.2 收货 → 质检门禁 → 上架

1. 验收 fixture 必须从 `listBusinessConsoleWmsInboundOrders` 选择 Open 单；当前查询不默认过滤 Open，
   页面选行也不校验状态，因此 #1160/#1176 完成前整条 UI 完成旅程保持阻塞。按精确 `inboundOrderNo`
   拉取全部 receiving-quality-gates 行，未完整取回时 fail closed。
2. 扫码/录入批号和效期后调用 `completeBusinessConsoleWmsInboundOrder`。权威命令回执是
   `requestId` / `inventoryMovementId`，并须公开回读 inbound 状态和逐行批效期。
3. 待检/不合格单不得出现上架引导；`isReleasedForPutaway=true` 后才允许进入上架观察。
4. `/wms/putaway` 当前是只读任务清单，没有逐任务 complete。终态以父收货单和库存移动公开回执为准，
   不把浏览到 `warehouseTaskId` 或 HTTP 200 当作上架完成。
5. Completed/Cancelled 等终态入库单可以继续显示用于公开回读，但选中后只能打开只读详情，
   不得进入批效期采集或“确认完成”抽屉；当前页面尚未满足。

### 5.3 拣货 → 复核发货

1. `listBusinessConsoleWmsPickingTasks` 只读展示 `warehouseTaskId`、库位流向、计划/执行量和状态；
   当前没有逐任务 complete。
2. 选对应 `outboundOrderId`，填写非空 `packReviewNo` 和复核结论，调用
   `completeBusinessConsoleWmsOutboundOrder`。
3. 权威命令回执是 `requestId` / `inventoryMovementId`；随后公开回读 outbound
   `status`、`inventoryPostingStatus`、逐行 `issuedQuantity` 和失败信息。
4. Completed/Cancelled 等终态出库单可以继续显示用于公开回读，但不得进入复核单号和“确认完成”
   抽屉。当前页面没有状态守卫，终态行仍能进入完成流，因此本项阻塞并由 #1160/#1176 跟进。
5. 对 Open 单重复同一意图只能复用原幂等键，不能生成新键后盲重放。

## 6. PDA 质量：`emp034`

| 环节           | 预期结果与证据                                                                                                                                                                                                                                                                                                                                  |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 登录/工作台    | `getConsolePrincipal` 返回 `role-pda-inspector`；首页和 `/quality/tasks` 显示当前 `principalId` 的 Self pending 待检，不再把组织总量称为个人任务。                                                                                                                                                                                              |
| 范围           | `listBusinessConsoleQualityInspectionTasks` 显式发送 `scopeKind=self + scopeId=user-emp-034`；Gateway 以认证主体重新解析，Quality 按 `assigned_user_id` 服务端过滤。世界观历史任务按事实检验员归属回填。                                                                                                                                        |
| 筛选/分页/刷新 | 默认服务端 `status=pending`；来源类型/服务、来源编号或物料关键字、超期均在 Quality 端先过滤再计算 `total`，按完成态、到期、创建时间、强 ID 确定性 offset 排序；页面继续以受限页大小加载更多并支持错误刷新。                                                                                                                                     |
| 详情/强 ID     | 强 ID 详情返回来源、SKU、批次、数量、到期、assignment/version、`allowedActions/blockReasons`，并包含检验方案类别、抽样规则与特性；Self/Team/Organization 与列表使用同一服务端范围裁决。                                                                                                                                                         |
| 动作/门禁      | Pending 任务先以 `expectedVersion + idempotencyKey` 原子领取；只有权威动作包含 `submit` 才进入录入。必检特性完整、每行有效，不合格时处置原因必填；提交 `createBusinessConsoleQualityInspectionRecordFromTask` 时 inspector 由认证 principal 注入。无权、生命周期冲突、被他人领取分别 403/409/422，Completed 只读。                              |
| 权威回执       | 契约与提交 composable 已保留 `inspectionRecordId`、`result` 及可选 NCR ID/code；当前结果组件只消费结论和 NCR 标识，只提供 `openNcr`，没有检验记录 ID 展示或记录详情入口。当前验收须从浏览器网络响应保存回执，再手工调用公开 `getBusinessConsoleQualityInspectionRecord` 按同一 ID 回读；结果页记录跳转由 #1177 跟进，完成前 UI 端到端仍是缺口。 |
| 终态只读       | 提交后任务转 completed 并退出 pending 列表；检验记录与 NCR 详情作为只读证据，不能再次创建第二条首检。                                                                                                                                                                                                                                           |
| 退出           | 与操作工相同，已有本地 fail-safe + 有界远端 revoke；真实账号终验仍需保存网络与路由证据。                                                                                                                                                                                                                                                        |

MAN-630 已新增真实 inspector/team assignment、Self/Team 范围、领取/转派状态机和耐久审计回执。
目标人员与班组从 MasterData 权威目录校验；转派保留原因；领取和提交共享任务锁及版本裁决。

## 7. 维修、工位机与班组/车间终端

### 7.1 维修 PDA

Maintenance 的公开 list/create/inspection facade、PDA 报修/点检页和工单
`assignedTechnicianUserId` 已存在；该字段已持久化并出现在 BusinessGateway 工单读模型。
在 Development 同时启用 `LeaderDemo:Seed:Enabled` 与 `LeaderDemo:History:Enabled` 时，
L1 world-history 会为 `user-emp-043..046` 生成指派样本；leader-demo profile 默认开启该历史层，
但固定 L2 `MWO-DEMO-001` 本身未指派技师，关闭 history 的最小 seed 也不能提供这些样本。

真实缺口是维修技师账号/角色/登录成员资格，以及服务端权威当前技师任务和生命周期：当前工单列表只支持
org/env、设备 ID、skip/take，不支持 `assignedTechnicianUserId=principalId` 或当前技师过滤；
PDA 也没有受理、开始、备件、完工页面。“近期维修工单”只是 org/env 前 100 条，创建报修只记录
`openedBy=loginName`；创建回执 `workOrderId` 只能证明报修，不证明维修受理、执行或完工。
账号/授权与主体范围由 #1164 跟进，列表过滤和 lifecycle UI 由 #1168 跟进。

补齐后旅程必须覆盖：维修技师登录 → current-subject/team 工单工作台 → 状态/设备/优先级筛选与分页 →
`workOrderId` 详情 → 受理/开始/备件/完工或明确 blocker → 每步权威回执 →
Completed/Cancelled 只读 → 退出。目前整条旅程为阻塞。

### 7.2 工人固定工位

`frontend/apps/business-workstation` 仅在架构文档中作为 roadmap，仓库当前没有应用、登录、工作台、
设备/工位绑定、任务详情、动作回执、终态或退出。不得用 PDA 响应式页面或 `screen` 大屏代替。

补齐清单：受管终端身份、登录/换班、工作中心或设备绑定、current-subject/WorkCenter scope、工序/物料/SOP
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

| 环节           | 当前事实 / 终验要求                                                                                                       |
| -------------- | ------------------------------------------------------------------------------------------------------------------------- |
| 登录           | 当前只能用 admin 做连通性诊断；终验必须给每种目标 PC 角色独立账号，并用 `/auth/me` 证明 role/permissions。                |
| 默认工作台     | 当前按权限裁剪域和工作台来源；终验须证明每个角色只见职责内待办、消息、预警和 KPI，不用全权限 admin 截图代替。             |
| 范围           | 当前业务上下文来自 principal 的 org/env。计划/管理角色的 Team/WorkCenter/Workshop 范围尚未交付。                          |
| 筛选/分页/刷新 | 各业务页已有独立筛选分页事实；终验以 #1157 的事实矩阵为输入，按后续角色实现逐页核实，本文件不把“页面存在”扩写成角色可用。 |
| 详情/动作/回执 | 以页面实体强 ID、公开 Gateway command response 和同一公开详情回读为准；没有角色账号和样本前均是 gap。                     |
| 终态只读       | 每个业务终态必须隐藏/禁用非法动作，直接调用仍由 Gateway/服务端拒绝；需要目标角色实测。                                    |
| 退出           | shell“退出登录”调用 `logoutConsoleSession`，清本地状态并跳 `/login`；目标角色账号补齐后可验。                             |

## 9. 正式通过条件

1. #1160/#1174 先统一 MES 服务 `Queued` 与 PDA 开始动作映射；O2 必须从 PDA 对真实 Queued 任务完成
   Queued → InProgress，O3/O4 才能在同一 UI 旅程证明服务端 blocker。
2. WMS inbound/outbound 终态行可以显示供回读，但不得进入完成抽屉或暴露完成动作；两页状态守卫补齐前
   W1/W2 均保持阻塞。
3. 四个现有 PDA 账号分别完成其正常与异常支线；每条证据含当次强 ID、命令回执和公开回读。
   Quality 当前可用网络回执 + 手工公开详情回读，结果页记录入口未补齐时必须标为 UI gap。
4. PDA 可见退出与 session revoke 已有自动化覆盖；真实账号终验补网络与受保护路由证据。角色化列表范围和分页缺口关闭，或明确保持“部分可验”。
5. 维修、班组长、车间角色和 PC 计划/管理账号、角色、范围与数据前置全部由公开事实补齐。
6. 工位机、班组/车间终端若仍未实现，相关旅程保持阻塞；不得用 mock screen、PDA 或 admin 代验。
7. 全部证据来自同一 commit 的真实 PostgreSQL/Redis run，资源被精确清理，且没有直接 DB 写读证据。
