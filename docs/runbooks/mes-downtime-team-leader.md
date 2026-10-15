# 停机班组长账号准备

适用于 Business Console 的班组长停机登记与恢复。权限模板 producer 为 IAM `IamFacts.cs` 的 `MesDowntimeTeamLeader`，由 `IamSeedService` 的基线 seed/启动引导补缺；不创建默认业务账号、不授予默认组织全权。生产启动引导须已完成[数据库发布前置](database-release.md)。

## 角色与账号

由具备 IAM 用户/角色管理权限的管理员执行准备，业务动作必须使用新登录的非管理员账号：

1. 在平台控制台角色列表确认固定 ID `role-mes-downtime-team-leader`。已有同 ID 角色若被运营调整过，重启不会恢复默认授权；先核对实际权限，不能以默认模板名代替核验。
2. 通过现有用户管理创建独立账号，分配该角色到目标组织/环境；不叠加平台管理员、PDA 操作员或组织全权角色。公开 facade 为 `POST /api/console/v1/iam/users` 与 `PUT /api/console/v1/iam/users/{userId}/membership`，后者请求体为 `{"roleIds":["role-mes-downtime-team-leader"]}`。创建口令通过受控通道交付，首次登录按现有改密流程完成。
3. 通过 IAM `PATCH /api/iam/v1/users/{userId}/membership-data-scopes` 配置成员范围，示例请求体如下；把示例工作中心替换为实际授权值。角色本身保留空范围，不能再叠加 Organization/Workshop 等更宽范围。

```json
{
  "organizationId": "org-001",
  "environmentId": "env-dev",
  "dataScopes": [{ "scopeType": "work-center", "scopeCode": "WC-ROD-01" }]
}
```

4. 新账号退出后重新登录，用公开 `GET /api/console/v1/auth/me` 核实主体、组织/环境、角色与六项权限：`business.masterdata.resources.read`（工作中心/设备显示与范围）、`business.mes.work-orders.read`（工单及返回目标）、`business.mes.operations.read`（真实工序上下文）、`business.mes.downtime.read`（事件列表）、`business.mes.downtime.manage`（登记/恢复）、`business.maintenance.downtime-reasons.read`（Maintenance 原因目录）。以实际 producer 为准，不另授交接、质检或维修写权限。
5. 分别用上述 MES 权限调用 `GET /api/business-console/v1/me/work-context?organizationId=...&environmentId=...&permissionCode=...`，核实真实 work-center grant 和可选范围。界面选择该 WC；只有权限码、但没有实际 scope 的账号不能作为就绪账号。

## 员工、班组及设备

岗位角色和 MasterData 员工/班组是不同事实。由主数据准备者使用现有公开 workers、teams 和 team members 接口，把员工的 `userId` 绑定到上述账号，保持员工在岗，按真实班组及生效日期登记成员/班组长。不能用班组名称推导 IAM scope，也不能靠 IAM 角色伪造员工任职。

在同一隔离运行中选取合法 WO/OP 和所属 WC。通过公开设备主数据读取或创建合法设备，再由有派工管理权的准备者调用 `POST /api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign`，按现有合同提交 `assignedUserId`、`shiftId` 与 `deviceAssetId`。执行人员须满足现行派工条件。随后公开读回工序的 `deviceAssetId`、WO/OP/WC；不直接写库或新增设备绑定接口。准备账号的主数据/派工权限不分配给停机班组长。

## 验证及停止条件

- 班组长从同 WO/OP 进入停机页，读取既有原因目录，选择同一真实工序并登记。页面使用现行 v2 登记合同；旧 v1 POST 不支持工作中心，不作为替代路径。
- 用公开事件列表读回事件强 ID、WO/OP/WC/device 和开始时间。再由同账号在 UI 恢复该事件，公开读回恢复事实，并通过「返回工单」回到同来源工序。
- 使用未授权 WC 验证停机读写拒绝；分别缺停机管理权、缺原因目录读权时核实入口/动作失败关闭。失败后停止依赖性动作，先核实公开身份、范围、工序/设备和目录，不临时补组织全权。
- 保存固定 SHA、隔离 session、主体与范围、强 ID、关键请求/公开读回、关联日志和截图；密码/token/连接串不进入证据。沿[本地启动规则](local-development.md)及[证据规则](testing/evidence.md)运行，交接前官方停止自有栈并 inspect 残留。

撤销账号可通过现有 IAM 移除成员角色/范围或禁用账号并撤销会话；已创建的业务停机事件仍按 MES 合同保留，不通过删除账号回滚事件。
