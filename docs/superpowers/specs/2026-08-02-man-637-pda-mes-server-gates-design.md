# MAN-637 PDA MES 服务端门禁演示闭环设计

## 目标与边界

在现有 BusinessGateway/MES operation-task list 路由上完成 PDA 当前工序演示旅程，不新增后端路由或数据库。为保证完整 deep link 在分页前精确命中双强 ID，BusinessGateway operation list 增加向后兼容的可选 `operationTaskId` query，并按正式链路导出 OpenAPI、重新生成 client、经 stable barrel 暴露；facade classification 保持 `exposed`。`reportable-operation-tasks` 继续使用独立 public request type，不暴露该 exact filter。入口仍由服务端按当前 principal 与所选授权作业范围过滤；PDA 只消费返回的 `workOrderId`、`operationTaskId`、`allowedActions`、`blockReasons` 与 `evaluatedAtUtc`。

非范围保持为完整报工字段、耗料批次、领料、完工入库、扫码解析、测量判定和离线队列。

## 方案选择

采用“既有 list 路由 + exact query”方案。备选的“新增 MES 详情端点”会重复现有 operation-task list 行事实；“抽取跨域通用门禁组件”会把单域演示切片扩大为平台重构，均不采用。Gateway 的 operation/reportable public request types 保持隔离，只将共同 scope resolution 与下游 DTO mapping 收敛到 private helper，且只有 operation list adapter 传递 `OperationTaskId`。

## 组件与数据流

- `src/composables/useBusinessMes.ts`：保持 principal/scope-bound 列表查询；完整 deep link 使用独立 `operationTaskId` filter，query identity、首屏与后续分页均携带 exact 参数且清空 fuzzy keyword。动作 API 接收完整 `workOrderId + operationTaskId`，写前精确回读同一 pair，并只在回读行的 `allowedActions` 包含目标动作时调用 mutation。初次动作同时冻结 principal、org/env、manage scope kind/id、pair 与 operation type；未确认重试先在 composable 边界比较当前 identity，漂移时在 acquire/mutation 前 fail closed。composable 同时暴露不含动作/pair 的 manage-action identity 与当前 context 判定，供页面隔离异步结果。
- `src/pages/mes/operation.vue`：只保留 route/query/action orchestration、稳定幂等键和冻结 context snapshot；列表继续绑定所选 pair。首次动作与重试调用前冻结由 route pair、principal/org/env、读范围和 manage scope 组成的页面 identity 及单调 generation；普通列表选中 pair 另有独立 generation/identity，只有打开不同 pair 才推进，内部关闭不推进。await 后写入 success/error 前必须同时仍是同一页面代与选择代，否则只清除仍属于旧 context 的结果/意图并刷新当前上下文，不能清除新的选择。完整 deep link 的打开 identity 同时包含 manage-action identity，并仅在写范围 ready 后打开精确 pair。
- `src/pages/mes/components/MesOperationExecutionPanel.vue` 与 `operationPresentation.ts`：作为 `routesFolder.exclude` 覆盖的 page-private presentation 层，以 typed props/events 展示详情、服务端门禁、SOP 与结果。工序任务实例只显示 `operationTaskNo` 或明确“工序任务信息未提供”；`operationCode` 仅出现在工序/SOP 上下文。
- `e2e/fixtures.ts` 与 `e2e/mes.spec.ts`：生产形态 operation-task fixture 的可选 `operationTaskNo` 保持 `null`；以 375×812 Chromium 覆盖显式缺失文案、前序 `operationSequence` 可读引用、当前/前序 raw ID 不出现、服务端动作门禁、双强 ID、409 刷新、未确认回执、完成态只读和 history 快速切换。非空 `operationTaskNo` 只保留在明确命名的单元/组件覆盖中。
- 产品/架构文档：同步说明 PDA 工序页不从本地状态推断动作，且只有 confirmed 或权威回读确认才显示成功。

## 错误与并发

- 409/lifecycle conflict：关闭旧 sheet、清除旧动作上下文和幂等键、刷新当前 scope 的权威列表；旧按钮不会继续可用。
- `accepted`/缺失/畸形回执或权威回读未确认：保留原幂等键并显示“结果不确定”错误，不显示成功。
- 未确认后 principal、组织、环境或 manage scope 漂移：保留旧 pair/key 的历史事实但禁止在新 context 重放；页面只显示可读冲突提示，不泄露 raw ID。
- 点击重试前先验证 route 与 action identity：存在任一 deep-link query 时，必须同时具备且精确匹配 result/frozen context 的 `workOrderId + operationTaskId`；其它 pair 或不完整 query 在 mutation 前转成可恢复的 route conflict，保留 result/context/key，回到原 pair 且 watcher 落稳后才允许 same-key retry。principal、组织、环境或 manage scope 漂移则转成 identity conflict；identity 恢复并经过 watcher 与 deep-link auto-open 落稳后仍保留安全重试入口。只有请求调用期间才发生的漂移进入 stale discard；身份类冲突 route 换 pair 或用户返回后改选任务会清理，普通 determinate error 不跨 identity 保留，无 route pair 的普通列表选择不受影响。
- route query、scope 或 principal identity 变化：立即关闭旧详情，只能由新 identity 的成功响应重新打开精确 pair。
- 首次动作或重试 await 期间发生 route、principal、组织/环境、读范围或 manage scope 漂移：旧 success/error 都静默丢弃，清除旧结果/意图并刷新当前上下文；上下文变化本身不显示“操作失败”。
- 首次动作或重试 await 期间从普通列表 pair A 改选 pair B：A 的旧 success/error 都静默丢弃并刷新当前列表，只清理仍属于 A 的意图；B 的 sheet、选择和结果不被关闭或覆盖。
- 完整 deep link 的任务数据先于 manage scope 到达：未 ready 时不打开；manage identity 就绪后按新的打开 identity 重新评估并只打开精确 pair。
- 前序阻塞：MES readiness 与 complete command 都投影权威 `operationSequence`，以“工序 N”及截断后的“等 N 道”返回，不把当前或前序 `operationTaskId` 放入面向操作员的说明。
- 未知 `allowedActions` 值不渲染按钮；空数组只读，不从 status 推导兜底动作。

## 测试策略

先在 composable、page-private component 与页面单元测试中新增失败断言，再做最小实现；route contract 直接检查 generated route table 不包含 `components`。页面延迟 mutation 表格测试覆盖 route/principal/org-env/manage-scope × success/error × initial/retry，并覆盖普通列表选择 A→B 的 initial/retry × success/error；另以任务数据先到、manage scope 后到的确定性用例验证 deep-link 打开顺序。未知结果重试另以真实 principal、organization、environment、manage-scope identity 分别漂移，断言 mutation 未调用、冲突可读且原 key/context 仍在。BottomSheet 黑盒断言使用真实 Portal 契约 `[data-slot="mobile-sheet-content"][data-state="open"]`，关闭动画保留的 `data-state="closed"` 节点不算打开。readiness evaluator、complete command 与 service contract 直接断言“工序 N / 等 N 道”且 current/predecessor raw ID 缺席。375×812 Playwright 增加同工单 20 个以上 substring collisions 的 deep-link 场景，断言请求携带 exact `operationTaskId` 且不携带 keyword，并测量页头返回按钮不低于 44 px；同时覆盖生产形态缺号与可读前序引用。最终运行 Gateway/MES/Facade、api-client/business-core/PC/PDA、PDA typecheck/build、MES 与完整 PDA e2e、逐文件格式和 OpenAPI drift；仅在存在受管场景时通过 `nerv.ps1 fullstack run` 留存公开 BusinessGateway 证据。
