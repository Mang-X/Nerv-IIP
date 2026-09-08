# 现场作业主体、角色与 scope 当前架构

本文只描述现场作业的**当前身份事实所有权、角色/权限组合、Worker 与作业资源关系以及授权 scope 语义**。M2-L 拆分前含 commit 时点证据、交付缺口、演示账号与验收记录的混合正文冻结于 [`../../reports/m2-l-frontline-principal-role-scope-baseline-pre-split-2026-09-07.md`](../../reports/m2-l-frontline-principal-role-scope-baseline-pre-split-2026-09-07.md)。精确字段和 API 以当前 IAM/MasterData/Gateway 代码与公开契约为准。

## 权威边界

- 登录主体由 IAM `User + Membership` 确定；Membership 绑定当前 organization/environment，可关联多个 Role 并持有 membership data scopes。
- 动作授权以当前 `permissionCodes` 和服务端逐请求校验为边界；`roleIds` 用于展示/审计，不作为业务代码分支，也不要求用户手工切换角色。
- “人”的业务权威是 BusinessMasterData `Worker`；IAM `User` 只拥有登录身份。二者以稳定 `userId` 关联。
- `Worker.JobTitle`、`TeamMember.IsLeader` 与 IAM `Role` 是不同事实，互相不能推导；岗位文本不是 permission 或 role ID。
- Team 是车间级班次班组；WorkCenter 是资源范围。当前人员候选关系经 `WorkCenter.WorkshopCode -> Team.WorkshopCode -> TeamMember.UserId` 相交，Team 与 WorkCenter 不是一对一父子关系。

## 多角色与 permission-aware scope

```text
effectivePermissionCodes = distinct(union(each membership role.permissionCodes))
effectiveRoleIds         = all membership roleIds
permissionScopeGrants    = scopes(roles granting checked permission) ∪ membership scopes
```

角色 A 提供本次动作权限时，不得自动借用角色 B 的更宽 role scope；membership scope 是主体在当前组织环境中的显式公共边界。客户端不得自行组合 role、permission 与 scope 并把组合结果当成授权事实。

空 data scopes 只保留 legacy 兼容语义，不生成 grant，也绝不等价于 organization scope；organization 必须显式持久化并匹配当前 organization。

## Worker 与作业资源事实

| 事实 | 当前所有权/关系 | 关键限制 |
| --- | --- | --- |
| Worker | MasterData 拥有员工业务身份、稳定 `userId`、部门/岗位文本、状态等 | Worker 可以没有 IAM 登录；IAM 展示字段不能反推 Worker 事实 |
| Team / TeamMember | MasterData 拥有班组、班次/车间归属及当前有效成员关系 | `isLeader` 是班组关系，不自动授予权限；一个 Worker 可有多个有效 Team |
| WorkCenter | MasterData 拥有稳定资源 code、plant/line/workshop 归属和容量等静态事实 | 缺失 workshop 关系时按 WorkCenter 求人员不得扩大为全厂 |
| Workshop | MasterData 拥有稳定 code、site 归属及可选 manager 引用 | manager 引用与 IAM role/permission 是不同事实 |
| Shift | MasterData 拥有班次定义；Worker 通过当前 Team 的 `shiftCode` 间接关联 | 没有当前 Team 就没有可推导的当前班次 |

代码事实可从 [`../../../backend/services/Business/MasterData/`](../../../backend/services/Business/MasterData/) 与 [`../../../backend/services/Iam/`](../../../backend/services/Iam/) 追溯；Gateway 聚合不能取得这些 owner 的写入权。

## 授权 scope 语义

当前 IAM scope kind 包括 `self`、`team`、`work-center`、`workshop`、`organization`、`site`、`production-line`。它们是授权范围，不是页面筛选标签；客户端提交 scope kind/code 只能表达请求，服务端必须从当前 principal、Worker、资源层级和 permission-aware grants 解析并校验。

| Scope | 语义 |
| --- | --- |
| `self` | 只覆盖明确记录当前主体/Worker 的业务对象；没有 assignee/owner/subject 事实时不得伪造“我的” |
| `team` | 覆盖服务端确认的当前有效 Team；跨成员动作仍需独立 permission |
| `work-center` | 覆盖绑定一个或多个稳定 WorkCenter code 的对象 |
| `workshop` | 覆盖稳定 Workshop 及其经当前资源关系可解析的对象，不能因为层级缺失而扩大 |
| `organization` | 覆盖当前显式 organization grant；空 scope 不得退化为 organization |
| `site` | 覆盖明确 Site 及其经当前资源层级可验证的对象 |
| `production-line` | 覆盖明确 ProductionLine/Line code 及其可验证资源关系 |

## Fail-closed 不变量

1. scope 候选由当前 principal、Worker 和资源关系产生，再与本次 permission 的 grants 求交；任何缺失、重复、孤立或冲突关系不得扩大候选范围。
2. 前端可以用 permission/scope 信息裁剪交互，但最终授权必须在 Gateway/IAM 与领域服务侧验证。
3. 业务读模型可聚合 Worker/Team/Shift/Workshop/Site/WorkCenter，但不因此成为这些事实的 owner。
4. 新增 scope kind、权限组合规则或 owner 变更属于长期边界变化时，应同步 IAM 契约、业务 owner 文档和相关 ADR/Governance，而不是仅改页面。

授权规则进一步见 [`../../governance/security/authorization.md`](../../governance/security/authorization.md)，API/Gateway 运行时关系见 [`../integration/api-contracts.md`](../integration/api-contracts.md)。
