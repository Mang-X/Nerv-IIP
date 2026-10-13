# #4400 APS 时间编辑真实运行证据

固定代码 `bc205af1f3e3724e16371a69355ee729973be0c3`；2026-10-13 本地 fullstack 会话 `nerv-4400-bc205b`。正文与证明边界见[冻结报告](../../aps-timezone-editing-2026-10-13.md)。本目录为报告的持久附件，`SHA256SUMS` 固定除自身和本 README 之外的原始截图、请求投影与服务日志投影。

- `roundtrip.json`：同一 `WO-20261013-000001-OP-10`、同一方案 `plan-01a135df9bde769cbc91b1f1b12b5373`，在 `Asia/Shanghai` 与 `UTC` 浏览器中的原值、无意图编辑、显式 `Z` 改时、公开工作草案读回、重开表格、方案基线对照及网络 correlation。
- `naive-local.json`、`naive-utc.json`：同一对象继续输入不带偏移的当地时间与显式偏移对照。`naive-utc.json` 的 `utc-naive-local-wall-time` 是八小时错误的关键记录；公开 GET 和重开值均为 `+08:00`，PUT 为 200。
- `service-logs.json`：真实 BusinessGateway/Scheduling 日志按 trace/correlation 提取的最小投影，共 88 条 GET/PUT 服务记录，不含请求正文、凭据或个人数据。
- `Asia-Shanghai-*`、`UTC-*`：各浏览器的表格打开/重开和甘特重开截图；DHTMLX 评估浮层遮住部分甘特条，精确时刻以 JSON 公开读回为准。
- `naive-local-wall-time-before-reload.png`、`explicit-shanghai-offset-before-reload.png`、`utc-naive-local-wall-time-before-reload.png`、`utc-explicit-offset-before-reload.png`：四个输入对照的真实页面截图。页面截图在输入后、重载前；重载与持久化结果以对应 JSON 为准。

复现路径：用管理员登录业务控制台、选择组织 `org-001` / 环境 `env-dev`，打开「需求与计划 → 排产工作台」，对上述未发布方案的 OP10 切到「表格编辑」；查看开始/结束原始字符串，依次在对应 IANA 时区浏览器中输入 JSON 中的值并等待草案自动保存，再整页重载；通过 BusinessGateway 的 `GET /api/business-console/v1/scheduling/working-drafts?organizationId=org-001&environmentId=env-dev` 读取同一 `taskId`。服务日志 correlation 可与 JSON 中 PUT 对照。本目录只保留投影与截图，不保留登录口令、Authorization/Cookie、Playwright profile 或全量服务日志。
