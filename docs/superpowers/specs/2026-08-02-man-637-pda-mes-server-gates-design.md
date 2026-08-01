# MAN-637 PDA MES 服务端门禁演示闭环设计

## 目标与边界

在现有 BusinessGateway/MES 公开契约上完成 PDA 当前工序演示旅程，不新增后端路由、数据库或 generated client。入口继续由服务端按当前 principal 与所选授权作业范围过滤；PDA 只消费返回的 `workOrderId`、`operationTaskId`、`allowedActions`、`blockReasons` 与 `evaluatedAtUtc`。

非范围保持为完整报工字段、耗料批次、领料、完工入库、扫码解析、测量判定和离线队列。

## 方案选择

采用纯 PDA 消费方案。备选的“新增 MES 详情端点”会重复现有 operation-task list 行事实；“抽取跨域通用门禁组件”会把单域演示切片扩大为平台重构，均不采用。

## 组件与数据流

- `src/composables/useBusinessMes.ts`：保持 principal/scope-bound 列表查询；动作 API 改为接收完整 `workOrderId + operationTaskId`，写前精确回读同一 pair，并只在回读行的 `allowedActions` 包含目标动作时调用 mutation。权威回执未确认时抛出既有不确定错误并保留同一幂等意图。
- `src/pages/mes/operation.vue`：列表、标题、详情、动作与结果都绑定所选 pair；详情展示工单、工序、设备、SOP、服务端门禁评估时间和阻塞原因。按钮直接来自 `allowedActions`，完成态和无动作态只读。
- `e2e/fixtures.ts` 与 `e2e/mes.spec.ts`：以 375×812 Chromium 覆盖服务端动作门禁、阻塞原因、双强 ID、409 刷新、未确认回执、完成态只读和 history 快速切换。
- 产品/架构文档：同步说明 PDA 工序页不从本地状态推断动作，且只有 confirmed 或权威回读确认才显示成功。

## 错误与并发

- 409/lifecycle conflict：关闭旧 sheet、清除旧动作上下文和幂等键、刷新当前 scope 的权威列表；旧按钮不会继续可用。
- `accepted`/缺失/畸形回执或权威回读未确认：保留原幂等键并显示“结果不确定”错误，不显示成功。
- route query、scope 或 principal identity 变化：立即关闭旧详情，只能由新 identity 的成功响应重新打开精确 pair。
- 未知 `allowedActions` 值不渲染按钮；空数组只读，不从 status 推导兜底动作。

## 测试策略

先在 composable 与页面单元测试中新增失败断言，再做最小实现；随后扩展 375×812 Playwright。最终运行 PDA typecheck/test/build、完整 PDA e2e、每个触及文件的逐文件格式检查，并在环境允许时通过 `nerv.ps1 fullstack run` 留存公开 BusinessGateway 证据。
