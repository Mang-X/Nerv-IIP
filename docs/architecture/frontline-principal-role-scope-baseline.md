# 现场作业主体、角色与范围事实基线

本文档记录 MAN-619 / GitHub #1156 在提交
`af8f2c4ac92f3b5ce04c685e7e737538e61f0edc` 上核实的代码与公开契约事实，
并给出后续现场作业契约统一使用的范围语义。它不是已交付 API 清单：凡标记为
“缺口”或“待裁决”的内容，都不能被前端、验收或其它文档当成现有字段、端点、
角色或种子事实。

## 1. 结论摘要

1. 登录主体由 IAM `User + Membership` 确定；一个 membership 可以关联多个
   `Role`。当前有效 `permissionCodes` 是所有关联角色权限码的去重并集，
   `roleIds` 是关联角色 ID 的有序列表。因此一个主体获得多角色能力时不需要、
   也不应手工切换角色；执行授权看 `permissionCodes`，`roleIds` 只用于展示和审计。
   证据：
   [Membership.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/MembershipAggregate/Membership.cs)、
   [IamRepositories.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs)、
   [IamAuthService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs)。
2. “人”的业务权威是 MasterData `Worker`，IAM `User` 只负责登录身份。
   两者以稳定 `userId` 相连；当前没有 Position/Job 聚合或岗位稳定 ID，
   只有 Worker 上可空的 `JobTitle` 文本。`JobTitle`、`TeamMember.IsLeader`
   和 IAM `roleIds` 是三个不同事实，不能互相推导。
   证据：
   [Worker.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkerAggregate/Worker.cs)、
   [WorldBibleWorkerSpec.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/WorldBibleWorkerSpec.cs)、
   [Role.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/RoleAggregate/Role.cs)。
3. MAN-627 / #1164 已把 IAM data scope 扩展为 `self`、`team`、`work-center`、
   `workshop`、`organization`、`site`、`production-line`，并在实时授权检查按
   permission 返回带 role/membership 来源的 `ScopeGrants`。空 data scopes 只保留
   legacy `DataScope` 兼容，不生成 grant，也绝不等价于 Organization；Organization
   必须显式持久化且匹配当前 organization。
   证据：
   [Role.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/RoleAggregate/Role.cs)、
   [AuthorizationContracts.cs](../../backend/common/Contracts/Nerv.IIP.Contracts.Iam/AuthorizationContracts.cs)。
4. 当前班组是车间级班次班组。人员候选查询的既有链路是
   `WorkCenter.WorkshopCode -> Team.WorkshopCode -> TeamMember.UserId`，不是
   Team 与 WorkCenter 的一对一绑定。因此 Team 和 WorkCenter 是两种相交的作业范围，
   不能简单排成父子层级。
   证据：
   [Team.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/TeamAggregate/Team.cs)、
   [ListWorkerDirectoryQuery.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListWorkerDirectoryQuery.cs)。
5. 代码只开通了四类代表性 PDA 演示身份：`emp010`、`emp012`、`emp034`、
   `emp049`。维修人员、班组长、车间主任和最小权限 PC 业务管理角色目前都没有可直接
   验收的登录身份；`admin` 是拥有全部权限的 Platform Administrator，不能代替这些
   角色的最小权限验收。
   证据：
   [WorldBiblePdaDemoAccountSeedService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/WorldBiblePdaDemoAccountSeedService.cs)、
   [IamSeedService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs)。

## 2. 权威边界与当前事实形状

### 2.1 主体、角色与权限

| 事实 | 当前形状与关系 | 权威来源 |
| --- | --- | --- |
| Principal | `principalId/userId`、`principalType`、`loginName`、`email`、`organizationId`、`environmentId`、`permissionVersion`、`permissionCodes[]`、`roleIds[]`；当前 Console user principal 来自一个组织/环境 membership。 | [IamAuthModels.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthModels.cs)、[ConsoleAuthModels.cs](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/ConsoleAuthModels.cs) |
| Membership | 以 `userId + organizationId + environmentId` 表达主体在当前租户环境中的成员资格；可关联多个 role，也可持有 membership data scopes。 | [Membership.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/MembershipAggregate/Membership.cs) |
| Role | 聚合 `permissionCodes`，并可持有 role data scopes；角色名是管理/展示文本，不参与业务代码分支。 | [Role.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/RoleAggregate/Role.cs) |
| `permissionCodes` | 当前 membership 下全部未删除角色权限的 `Distinct + OrderBy` 结果；前端可用来裁剪入口，Gateway/IAM 逐请求校验仍是最终边界。 | [IamRepositories.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs)、[AuthorizedBusinessProxyEndpoint.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs) |
| `roleIds` | 当前 membership 的全部 role ID；`/auth/me` 返回它们供角色 catalog 展示和审计，不作为动作授权条件。 | [IamAuthService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs)、[API 契约与代码生成规范](api-contract-and-codegen.md#console-iam-admin-api) |
| 当前有效 data scope | legacy `DataScope` 仍是 membership scopes 与 role scopes 的兼容并集；MAN-627 新增的 permission-aware `ScopeGrants` 只取真正授予本次 permission 的 role scopes，并附加当前 membership scopes，保留 `sourceKind/sourceId` 审计来源。 | [IamRepositories.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs) |

当前实现的多角色算法可概括为：

```text
effectivePermissionCodes = distinct(union(each membership role.permissionCodes))
effectiveRoleIds         = all membership roleIds
permissionScopeGrants    = scopes(roles granting checked permission) ∪ membership scopes
```

这证明多角色主体无需角色切换。角色 A 提供动作权限时，不再自动拼接角色 B 的更宽
role scope；membership scope 是主体在当前组织环境中的显式公共边界，会随本次 permission
返回并保留 membership 来源。客户端不能自行组合 role、permission 和 scope。

### 2.2 Worker、岗位、班组、工作中心、车间与班次

| 事实 | 当前形状与关系 | 明确限制 |
| --- | --- | --- |
| Worker | `organizationId`、`environmentId`、`employeeNo(Code)`、`name`、稳定 `userId`、可空 `departmentCode`、可空 `jobTitle`、`employmentStatus`、`phone`、`disabled`；只有 enabled 且 `active` 才可派工。 | Worker 可存在而没有可登录 IAM 账号；业务页面不能从 IAM 展示冗余反推 Worker。 |
| Position/Job | 当前没有 Position/Job 聚合、目录或稳定岗位 ID；World Bible 的 `RoleName` 最终写入 Worker `JobTitle` 文本。 | 岗位文本不能用于授权，也不能当成 IAM role ID。岗位目录化是否需要落地仍待产品裁决。 |
| Team | `code`、`departmentCode`、`shiftCode`、可空 `workshopCode`；班组是车间级班次班组。 | Team 不直接保存 work center；不能从 Team 自动得到唯一 WorkCenter。 |
| TeamMember | `teamCode + userId`、`isLeader`、有效起止日、`disabled`；目录查询只采用当前生效且未停用的关系。 | `isLeader=true` 是班组关系事实，不会自动授予班组长 permissionCode。模型允许一个 worker 返回多个有效团队。 |
| WorkCenter | 稳定 `code`，含 `plantCode`、`lineCode`、可空 `workshopCode`、日历与产能字段。 | WorkCenter 未挂车间时，按 WorkCenter 查人员返回空，不会降级为全厂。 |
| Workshop | 稳定 `code`，含 `siteCode`、可空 `managerUserId`。 | World Bible 车间 seed 当前把 `managerUserId` 写为 `null`；车间主任岗位没有形成运行时 workshop-manager 映射。 |
| Shift | 稳定 `code`，含起止时间、跨日、paid/break minutes；当前 worker 通过 Team `shiftCode` 间接关联班次。 | 无 Team 的 worker 没有当前班次事实；Worker 本身不保存 shift。 |

证据：

- [Worker.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkerAggregate/Worker.cs)
- [Team.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/TeamAggregate/Team.cs)
- [TeamMember.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/TeamMemberAggregate/TeamMember.cs)
- [WorkCenter.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs)
- [Workshop.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkshopAggregate/Workshop.cs)
- [Shift.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ShiftAggregate/Shift.cs)
- [WorldBibleSeedService.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/WorldBibleSeedService.cs)

公开 BusinessGateway worker directory 当前返回 worker、团队和技能的组合读面，支持
`userId`、`departmentCode`、`teamCode`、`workshopCode`、`workCenterCode`、
`skillCode`、`employmentStatus` 过滤；它不返回 IAM `roleIds` 或 `permissionCodes`。
MAN-627 新增 `GET /api/business-console/v1/me/work-context`，从实时 IAM 检查取得服务端
principal，再聚合 MasterData Worker、当前有效 Team、Shift、Workshop、Site 和车间覆盖的
WorkCenter 候选，最后与 permission-aware grants 求交集。客户端不传 userId；缺 Worker、
重复 Worker、停用 Worker、孤立/矛盾层级都显式返回 resolution/issues，且不扩张候选范围。

证据：

- [BusinessConsoleModels.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs)
- [BusinessConsoleMasterDataEndpoints.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs)
- [ListWorkerDirectoryQuery.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListWorkerDirectoryQuery.cs)

## 3. 五种作业范围的统一语义

本节冻结的是后续业务契约、页面口径和验收共同使用的**语义**，不声明已经存在同名 API
字段。范围选择必须由服务端从当前 principal、Worker 和授权事实解析并校验；客户端传入的
scope kind/code 只能是请求，不能成为授权事实。

| 范围 | 统一含义 | 当前可验证的事实锚点 | 当前交付状态与 fail-closed 规则 |
| --- | --- | --- | --- |
| Self | 只包含业务对象明确记录的当前主体本人：例如任务 `assignedUserId == principalId`，或命令 subject 是映射到 principal 的 Worker `userId`。 | Principal `principalId` 与 Worker `userId`；PDA “我的任务”当前按 `assignedUserId=principalId` 做服务端查询并再次行级校验。 | 仅部分 MES 派工读面已有实例。没有 assignee/owner/subject 字段的对象不能伪称“我的”；交由 #1157、#1163、#1165–#1168 明确各域归属。 |
| Team | 当前主体被服务端确认可管理/参与的一个或多个稳定 `teamCode` 所覆盖的对象集合。成员关系必须当前有效；跨成员动作还需独立 permissionCode。 | `TeamMember(teamCode,userId,isLeader,effectiveFrom/effectiveTo)` 和 `Team.shiftCode/workshopCode`。 | IAM 已支持显式 `team` grant；work-context 只把当前有效成员关系解析为候选，再与 grant 求交。`JobTitle=班组长` 或 `isLeader` 仍不会自动授予权限。 |
| WorkCenter | 明确绑定一个或多个稳定 WorkCenter code 的任务、工序、设备或其它对象集合。 | `WorkCenter.Code/WorkshopCode`、设备与任务已有的 WorkCenter 引用。 | IAM 已支持显式 `work-center` grant；work-context 会验证车间和产线层级，孤立或冲突 WorkCenter 不成为候选。各域列表下推仍由 #1165–#1168 交付。 |
| Workshop | 明确绑定一个或多个稳定 Workshop code，及各域契约明确声明可沿 Workshop 展开的对象集合。 | IAM `workshop` grant；MasterData Workshop、WorkCenter.WorkshopCode、Team.WorkshopCode。 | work-context 可沿已验证层级展开 Team 与 WorkCenter，但这不代表所有业务域读写都已接入范围门禁。 |
| Organization | 当前 `organizationId + environmentId` 内、经独立 Organization 级授权允许的对象集合，是最宽业务范围。 | IAM 显式 `organization` grant，且 scope id 必须匹配当前 organization。 | 空 scopes 不生成 Organization grant；管理员 seed 显式配置 Organization，PDA 演示账号 membership 显式配置各自 Self。 |

这五种范围不是可以按名称做数值比较的单链：

- Self 是主体归属过滤。
- Team 按人员组织，WorkCenter 按生产资源组织；当前班组覆盖同车间多个 WorkCenter，
  一个 WorkCenter 也可能由不同班次团队接续，因此二者相交但不互为父子。
- Workshop 可以通过明确的主数据关系展开 Team 和 WorkCenter。
- Organization 是独立授权的最宽边界，不是“其它 scope 为空”的默认别名。

当前 IAM 的实时授权检查保留 scope grant 的 role/membership 来源；存在未知 legacy
scope type 时授权结果 `DenyAll=true`。BusinessGateway 当前只在 MES 工单、遥测/设备报警、
Maintenance 工单等有限读面解析 site/workshop/production-line，再收敛 work center 和设备；
这个实现不能证明 WMS、Quality 或所有写命令都已有范围门禁。

证据：

- [IamAuthService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs)
- [AuthorizationContracts.cs](../../backend/common/Contracts/Nerv.IIP.Contracts.Iam/AuthorizationContracts.cs)
- [BusinessGatewayDataScopeFilter.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayDataScopeFilter.cs)

## 4. 菜单、数据与动作授权必须分离

| 层次 | 作用 | 当前证据 | 不能推出什么 |
| --- | --- | --- | --- |
| 菜单/入口可见性 | 用当前 principal 的 `permissionCodes` 裁剪导航、首页板块和快捷入口，减少注定 403 的请求。 | Business Console [navigation.ts](../../frontend/apps/business-console/src/navigation.ts)、[BusinessLayout.vue](../../frontend/apps/business-console/src/layouts/BusinessLayout.vue)；PDA [useWorkbenchHome.ts](../../frontend/apps/business-pda/src/composables/useWorkbenchHome.ts)。 | 看得见不代表能看到所有行或能执行动作；隐藏入口也不是安全边界。 |
| 数据范围 | 服务端只返回 actor 被授权范围内的对象；scope 解析、分页、搜索和筛选必须在服务端完成。 | IAM authorization 返回 `DataScope`，BusinessGateway 的有限读面通过 [BusinessGatewayDataScopeFilter.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayDataScopeFilter.cs) 下推。 | 有 read permission 不代表 Organization 全量；当前某个读面已过滤不代表其它域自动继承。 |
| 可执行动作授权 | 服务端逐请求校验 actor 的 permissionCode、organization/environment、资源/数据范围，并继续执行领域状态、业务不变式、冲突和幂等门禁。 | [AuthorizedBusinessProxyEndpoint.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs)、[IAM 认证与授权基线](iam-authentication-baseline.md#授权模型)。 | 行可见、菜单可见、`roleIds` 匹配或前端按钮启用都不能代替后端动作授权。 |

Business Console 路由守卫和导航当前采用“任一 required permission 命中”来允许进入；
PDA 多数页面只标记 `requiresAuth`，首页/快捷入口才按权限裁剪。因此前端入口行为本身也不能
作为权限矩阵的事实源。每个动作的 permissionCode、状态前置和 allowed action 仍须由
issue #1157 的契约—页面—范围—动作矩阵逐项核实。

证据：

- [auth.ts](../../frontend/apps/business-console/src/router/guards/auth.ts)
- [business-pda pages](../../frontend/apps/business-pda/src/pages)

## 5. 代操作与委托的 actor / subject / reason 语义

### 5.1 已确认的现状

BusinessGateway 已能从 IAM authorization 结果取得经过认证的 principal，并优先使用
`PrincipalId`、再回退 `LoginName` 形成 actor。审批链启动、审批步骤处理、委托创建和撤销会
覆盖客户端传来的 actor/delegator/createdBy/revokedBy，避免客户端冒充另一个 actor。
审批委托当前记录 delegator、delegate、有效期、可空 reason、createdBy 和 revokedBy。

证据：

- [AuthorizedBusinessProxyEndpoint.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs)
- [BusinessConsoleApprovalEndpoints.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Approval/BusinessConsoleApprovalEndpoints.cs)
- [BusinessConsoleModels.cs](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs)

当前没有跨 MES/WMS/Quality/Maintenance 统一的“主管代另一名 worker 执行现场动作”公开契约；
也没有统一 subject worker、authority/delegation reference 和必填 reason 字段。现有
BusinessApproval delegation 是审批域事实，不能自动授权业务域代报工、代检验、代维修或
跨人员再分配。

### 5.2 后续现场契约必须遵守的审计语义

以下是审计语义要求，不是当前已存在的 DTO 字段声明：

1. **Actor** 是实际登录并发起请求的认证 principal。Gateway 必须从授权结果注入，客户端
   不能覆盖；授权永远使用 actor 的 `permissionCodes` 与 actor 的服务端有效范围。
2. **Subject** 是动作归属或被代表的 Worker/业务对象主体。普通本人操作时
   `subjectWorkerUserId == actor principalId`；代操作时两者必须不同并同时保留，不能把
   subject 伪装成 actor。
3. **Reason** 解释为什么需要委托、代操作、再分配或跨人员协助。代操作 reason 应是非空、
   受长度治理的业务文本或受控原因码；现有 Approval delegation 的 reason 仍可为空，
   因而不能证明该要求已交付。
4. 代操作不能借用 subject 的权限。服务端必须验证 actor 有该动作的独立 permissionCode、
   subject 落在 actor 授权范围、业务对象允许该状态转换，并记录所依据的 delegation/
   assignment/主管权限事实。
5. 审计必须能关联 actor、subject、reason、目标强 ID、organization/environment、实际
   scope kind/code、发生时间、correlation/idempotency key 和授权依据。各域的具体字段名与
   回执由 #1157、#1162、#1165–#1168 冻结，本文不预造 DTO。

## 6. 四个现有 PDA 账号核对

四个账号只有在 `LeaderDemo:World:Enabled=true`、IAM 使用 PostgreSQL 自动迁移/seed 路径，
且当前进程注入非空 `Iam__Seed__DemoWorkerPassword` 时才会被开通；密码不在仓库。
其余 World Bible 人员虽会生成 IAM user 与 MasterData worker，但随机未知口令、
`PasswordChangeRequired=true`，且没有 role/membership，不是可登录验收账号。

证据：

- [Program.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Program.cs)
- [IamSeedOptions.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedOptions.cs)
- [WorldBibleWorkerSeedService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/WorldBibleWorkerSeedService.cs)
- [WorldBiblePdaDemoAccountSeedService.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/WorldBiblePdaDemoAccountSeedService.cs)

| 登录名 | Principal / Worker `userId` | 员工事实 | MasterData 作业关系 | `roleIds` 与有效能力 |
| --- | --- | --- | --- | --- |
| `emp010` | `user-emp-010` | `EMP-010` 吴桂芳，生产部，操作工 | `TEAM-WB-MC-A` 机加车间早班组，非 leader；Workshop `WS-01`；Shift `EARLY` | `role-pda-operator`；获得下表 operator 权限并集 |
| `emp012` | `user-emp-012` | `EMP-012` 孙明辉，生产部，操作工 | `TEAM-WB-AS-A` 装配车间早班组，非 leader；Workshop `WS-02`；Shift `EARLY` | `role-pda-operator`；获得下表 operator 权限并集 |
| `emp034` | `user-emp-034` | `EMP-034` 朱立新，质量部，检验员 | 当前无 Team，因此无可解析 Workshop/Shift | `role-pda-inspector`；获得下表 inspector 权限并集 |
| `emp049` | `user-emp-049` | `EMP-049` 周文斌，仓储部，库管 | 当前无 Team，因此无可解析 Workshop/Shift | `role-pda-warehouse`；获得下表 warehouse 权限并集 |

姓名、岗位和人员编号由两侧同一确定性 World Bible 规格生成；班组关系来自 MasterData seed，
不是 IAM role 推导。证据：
[WorldBibleWorkerSpec.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/WorldBibleWorkerSpec.cs)、
[WorldBibleSpec.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/WorldBibleSpec.cs)、
[WorldBibleSeedService.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/WorldBibleSeedService.cs)。

| PDA role | 当前 seed 的准确 `permissionCodes` |
| --- | --- |
| `role-pda-operator` | `business.mes.work-orders.read`、`business.mes.dispatch.read`、`business.mes.operations.read`、`business.mes.operations.manage`、`business.mes.reporting.read`、`business.mes.reporting.write`、`business.mes.materials.read`、`business.mes.materials.manage`、`business.mes.receipts.read`、`business.mes.receipts.manage`、`business.engineering.documents.read`、`business.iiot.alarms.read`、`business.iiot.alarms.write`、`business.maintenance.work-orders.read`、`business.maintenance.work-orders.manage`、`business.maintenance.plans.read`、`business.masterdata.resources.read` |
| `role-pda-warehouse` | `business.wms.receipts.read`、`business.wms.receipts.manage`、`business.wms.shipments.read`、`business.wms.shipments.manage`、`business.wms.counts.read`、`business.inventory.ledger.read`、`business.inventory.counts.manage`、`business.inventory.movements.create`、`business.masterdata.resources.read` |
| `role-pda-inspector` | `business.quality.inspection-records.read`、`business.quality.inspection-records.create`、`business.mes.work-orders.read`、`business.masterdata.resources.read` |

`role-pda-warehouse` 在严格匹配受管基线时写入 `site/SITE-001` role data scope；
`role-pda-operator` 与 `role-pda-inspector` 不写 role data scopes。四个 PDA membership
seed 仍显式写入各自 principal 的 Self scope，避免空 scope 被误读为全量。Platform
Administrator seed 则显式写入当前 Organization scope。MAN-627 前已落库且仍严格匹配旧
seed 基线的空 scope 身份，会由独立 seed manifest 做一次性回填：管理员需同时匹配旧默认
manifest、固定角色名和完整基线权限，PDA membership 需只含对应固定角色且该角色名/权限均未
变化。任意已自定义的 permissions、roles、scopes 均不覆盖；验收仍必须通过公开实时授权路径
读取实际结果，不能只看 seed。这些 seed 只提供本人、受管仓储角色的 `SITE-001` 或管理员
Organization 边界，不代表 Team、WorkCenter、Workshop 等五级范围已自动配置。

## 7. 缺少的验收身份与前置

| 验收角色 | 已有人员主数据 | 当前缺失 |
| --- | --- | --- |
| 维修人员 | `EMP-043`–`EMP-046` 是维修技师，`EMP-047` 是点检员；均有 `equipment-maintenance` 技能。 | 没有已知口令、可登录 membership、维修角色、准确 permissionCodes、Self/Team/WorkCenter/Workshop 范围或维修派工事实。由 #1158、#1164、#1168、#1178 补齐并验收。 |
| 班组长 | `EMP-004`–`EMP-009` 分别是六个 World Bible Team 的 `isLeader=true` 成员。 | 没有已知口令、可登录 membership、班组长角色/权限或 Team scope；`isLeader` 不能代替授权。由 #1158、#1164、#1179 补齐。 |
| 车间主任/车间组长 | `EMP-001`–`EMP-003` 的 Worker `JobTitle=车间主任`；World Bible 规格曾在构造阶段为其分配三个 workshop code。 | 持久 Worker 不保存 workshop，World Bible Workshop `managerUserId=null`，也无登录 membership、角色/权限或 Workshop scope；运行时不能证实三人分别管理哪个车间。由 #1158、#1164、#1179 补齐。 |
| PC 管理角色 | seed 可创建 `admin` / `role-platform-admin`，拥有权限 catalog 全集及显式 Organization scope。 | 这是平台超级管理员且无 Worker 映射，不代表计划员、质量主管、设备主管、仓储主管、班组长或车间主任的最小权限 PC 身份；缺各业务角色和默认工作台。由 #1158、#1173、#1182 补齐。 |

人员号段和岗位分布证据：
[WorldBibleWorkerSpec.cs](../../backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/WorldBibleWorkerSpec.cs)、
[WorldBibleSpec.cs](../../backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/WorldBibleSpec.cs)。

## 8. 能力缺口、数据缺口与待裁决项

### 8.1 已确认的能力缺口

- current principal work-context 和 permission-aware grant 已由 #1164 交付；MES、WMS、
  Quality、Maintenance 的列表/动作仍需 #1165–#1168 把已验证 scope 下推到各自查询与命令。
- 没有跨业务域统一 actor/subject/reason 代操作契约；审批委托不能替代现场动作授权：
  #1157、#1162、#1179。
- PDA 首页虽然按 `permissionCodes` 聚合入口，但 current principal 不含 Worker/范围，
  角色/范围个人中心及固定四入口尚未交付：#1170。
- Business Console 已按权限裁剪导航，但没有服务端验证的五级范围选择器和角色化默认工作台：
  #1173。

### 8.2 已知设备数据前置，不归类为前端或授权缺陷

固定 leader-demo seed 明确不创建遥测样本、报警事件或已完成维修工单；它只保证
`DEV-CNC-DEMO`、`ALARM-DEMO-001` 规则和 `MWO-DEMO-001` open 工单等前置。可选的
World Bible L1 背景历史引擎可以生成设备遥测、报警、维修和点检历史，但是否存在取决于
该 profile/开关与实际 seed 结果。因此：

- 页面出现“历史数据积累中/等数据”时，必须先核实当前 run 是否启用了对应历史或模拟器；
  不能把缺历史误报成布局、权限或范围 bug。
- 维修角色验收除登录身份外，还需要范围内设备、报警/报修、可领取维修单及相应状态前置；
  不得通过直接写库或伪造完成历史补证。
- 数据条件登记与契约矩阵归 #1157；维修生命周期归 #1168；移动闭环归 #1178；真实栈证据归
  #1182。

证据：[实施状态清单：领导演示环境基线](implementation-readiness.md)、
[实施状态清单：设备域背景历史](implementation-readiness.md)。

### 8.3 仍需产品/设计裁决

1. Team 与 WorkCenter 并非父子层级时，各角色默认 scope、可同时选择的 scope kinds 及 UI
   表达；不能用单个“范围级别”整数覆盖。
2. Worker 多个当前有效 Team、多个班次或临时支援时，默认 Team/Shift、显式切换和有效期
   冲突如何处理。
3. 是否引入 Position/Job 稳定目录，及 `JobTitle`、Team leader、Workshop manager 与 IAM
   role 的管理关系；无论如何，业务代码都不得按中文岗位名授权。
4. 主管代操作适用哪些动作、是否必须先有 delegation/assignment、reason 采用文本还是受控
   原因码，以及 subject 对回执和统计的归属。

这些裁决的实现入口是 #1164、#1169、#1170、#1173 和 #1179；在代码/公开契约落地前，
产品文档只能标注为计划能力。

## 9. 后续 issue 路由

| 范围 | 跟进 issue |
| --- | --- |
| M0 契约、身份与验收定义 | [#1157 契约—页面—任务范围—允许动作矩阵](https://github.com/Mang-X/Nerv-IIP/issues/1157)、[#1158 多端角色旅程与真实账号验收矩阵](https://github.com/Mang-X/Nerv-IIP/issues/1158) |
| P0 现场正确性底座 | [#1159 PDA MES 报工实体修复](https://github.com/Mang-X/Nerv-IIP/issues/1159)、[#1160 终态/未知态/冲突态动作门禁](https://github.com/Mang-X/Nerv-IIP/issues/1160)、[#1161 PDA 高频输入组件](https://github.com/Mang-X/Nerv-IIP/issues/1161)、[#1162 写操作防重与权威回执](https://github.com/Mang-X/Nerv-IIP/issues/1162)、[#1163 真实“我的任务”范围](https://github.com/Mang-X/Nerv-IIP/issues/1163) |
| 主体范围与各域 assignment/query | [#1164 当前主体作业上下文与授权范围 API](https://github.com/Mang-X/Nerv-IIP/issues/1164)、[#1165 MES 任务范围](https://github.com/Mang-X/Nerv-IIP/issues/1165)、[#1166 WMS 派工与范围](https://github.com/Mang-X/Nerv-IIP/issues/1166)、[#1167 Quality 派工与范围](https://github.com/Mang-X/Nerv-IIP/issues/1167)、[#1168 Maintenance 派工与生命周期](https://github.com/Mang-X/Nerv-IIP/issues/1168)、[#1169 现场可搜索目录与原因码](https://github.com/Mang-X/Nerv-IIP/issues/1169) |
| 多端共用交互底座 | [#1170 PDA 权限聚合工作台](https://github.com/Mang-X/Nerv-IIP/issues/1170)、[#1171 PDA 任务列表](https://github.com/Mang-X/Nerv-IIP/issues/1171)、[#1172 共用动作门禁与回执](https://github.com/Mang-X/Nerv-IIP/issues/1172)、[#1173 Business Console 角色化范围](https://github.com/Mang-X/Nerv-IIP/issues/1173) |
| 角色工作流 | [#1174 PDA MES](https://github.com/Mang-X/Nerv-IIP/issues/1174)、[#1175 工位机 MES](https://github.com/Mang-X/Nerv-IIP/issues/1175)、[#1176 PDA WMS](https://github.com/Mang-X/Nerv-IIP/issues/1176)、[#1177 PDA Quality](https://github.com/Mang-X/Nerv-IIP/issues/1177)、[#1178 PDA Maintenance](https://github.com/Mang-X/Nerv-IIP/issues/1178)、[#1179 班组长/车间组长工作台](https://github.com/Mang-X/Nerv-IIP/issues/1179) |
| 文档与放行证据 | [#1180 产品文档](https://github.com/Mang-X/Nerv-IIP/issues/1180)、[#1181 跨端自动化门禁](https://github.com/Mang-X/Nerv-IIP/issues/1181)、[#1182 真实栈跨角色验收](https://github.com/Mang-X/Nerv-IIP/issues/1182) |

## 10. 关联架构文档

- 当前交付与 seed/profile 事实：[实施状态清单](implementation-readiness.md)
- IAM、Gateway、MasterData 的上下文所有权：[平台上下文地图](context-map.md)
- permission、principalType 与组织/环境/资源授权基线：[统一授权矩阵](authorization-matrix.md)
- 会话、principal 与服务端授权层次：[IAM 认证与授权基线](iam-authentication-baseline.md)
- MasterData 人/时间与资源层级：[基础数据模块产品业务设计](master-data-module-product-design.md)
- Gateway facade、principal 与 worker directory 公开契约：
  [API 契约与代码生成规范](api-contract-and-codegen.md)
- 前端请求/状态/认证边界：[前端结构与命名规范](frontend-structure.md)
- Business Console 菜单与 route-ready 边界：[前端导航地图](frontend-navigation-map.md)
- PDA 当前产品与技术边界：[PDA 模块产品设计](mobile-pda-module-product-design.md)
