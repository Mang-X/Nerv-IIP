# 现场多端真实账号验收证据模板

本页从原 `frontline-role-journey-acceptance-matrix.md` 拆出**通用运行证据口径**。它只规定如何证明一次真实验收，不承载角色旅程、产品 IA 或项目状态。角色旅程当前住所见 [`../../product/frontline/role-journeys.md`](../../product/frontline/role-journeys.md)。

## 最终证据

1. 最终证据运行在 PostgreSQL + Redis profile，认证走 PlatformGateway，业务读写只走 BusinessGateway `/api/business-console/v1/**`。浏览器网络记录、公开响应和同一公开读面的状态回读组成证据链。
2. 登录证据必须包含 `loginConsoleUser` 成功以及 `getConsolePrincipal` 返回的 `principalId`、`organizationId`、`environmentId`、`roleIds` 和 `permissionCodes`；不得记录口令或 token。
3. 业务动作必须保存请求前实体强 ID、命令回执和请求后公开读面。强 ID 是 `operationTaskId`、`inboundOrderId`、`warehouseTaskId`、`outboundOrderId`、`inspectionTaskId`、`inspectionRecordId`、`nonconformanceReportId` 或 `workOrderId`，不能用页面行号、显示顺序或 UI 默认值替代。
4. seed 数量、首页计数、HTTP 200、服务启动成功和直接数据库查询都不是业务成功证据。数据库只作为真实持久化基础设施，不作为验收读面。
5. 写动作结果不确定时先用公开列表/详情核实，不盲重放非幂等命令。终态必须刷新公开读面，验证状态稳定且 UI 不再提供非法动作。
6. 每次 evidence manifest 至少记录 commit、run/session ID、PostgreSQL/Redis profile、账号、组织/环境、UTC 时间、公开 operationId、强 ID、前后状态、响应摘要和清理结果。
7. 离线 outbox、相机、实体扫码枪、打印机和其他专用硬件如未进入当前旅程验收范围，不得用浏览器或 mock 结果替代真机证明。

## 演示数量不是固定断言

演示 seed、背景历史截止日和现场动作都会改变首页及列表数量。没有可复现的 run ID、manifest、commit、时间戳和证据路径时，不引用历史精确计数。验收只记录当次 run 返回的强 ID、动态数量、业务状态和回执，不把任何会话观察写成固定种子断言。

## 证据边界

- 产品旅程决定“需要证明什么”；本页只决定“如何证明”。
- 自动化、真栈 e2e 与真机证据必须按各应用适用的测试分层命名，不能越级声称。
- 本页不是项目状态页；某条旅程当前是否可验、阻塞或完成，以当前产品文档、代码与对应 Issue/PR 为准。
