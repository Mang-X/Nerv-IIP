# APS 时间编辑语义调查（2026-10-13）

## 结论与定级

[#4400](https://github.com/Mang-X/Nerv-IIP/issues/4400) 的一日时间盒调查在固定 `main` SHA `bc205af1f3e3724e16371a69355ee729973be0c3` 上完成。原 U14 有两层事实：甘特以浏览器本地时间显示、表格直接展示带偏移的原始 API 时间，是**展示与编辑语言不一致**；UTC 浏览器在表格输入不带时区的本地时间后，真实服务将其保存为 `+08:00`，造成**实际时刻偏移 8 小时**。因此不能把 U14 仅按格式统一结项。显式输入 `Z` 或 `+08:00` 的控制组可以正确往返，但计划员无法从当前表格知道应使用哪种格式与时区。

调查已验证现有工作草案持久化能力可复用；无生产代码、API 或数据库修改。后续修复在[独立 `scope:M` 票 #4405](https://github.com/Mang-X/Nerv-IIP/issues/4405) 跟踪，待产品裁决时区口径后实施。本报告没有完成 #4272 的 U14 业务验收，父票仍负责修复后在固定组合中重验。

## 基线、身份和取证边界

- 代码、正式 spec 与来源：上述 SHA；[#4272 正式 r1 spec](https://github.com/Mang-X/Nerv-IIP/issues/4272)、[原 U14 走查](https://github.com/Mang-X/Nerv-IIP/blob/51a408a9c3a323a868d1cd6b31dbd36b239c99e2/docs/reports/audits/2026-10-10-mes-aps-ui-ux-walkthrough.md)和[28 条故事结项核对](https://github.com/Mang-X/Nerv-IIP/issues/4272#issuecomment-6147438906)。原记录是甘特本地 16:00 与编辑 `08:00+00:00` 并列，并非当时已证实排程算法错位。
- 真实隔离 fullstack 会话：`nerv-4400-bc205b`，本地 Business Console `http://localhost:54890`、BusinessGateway `http://localhost:54866`；真实 PostgreSQL/Redis、BusinessGateway 与 Scheduling。T3 preview 状态不可用，改用本地 Chromium/Playwright 浏览器上下文，分别设置 IANA `Asia/Shanghai`、`UTC`，宽 1440、高 900。DHTMLX Gantt 是本地评估版，截图有许可浮层遮住部分任务条；甘特轴与其它截图、公开读回交叉核验，不把遮住的图形作为独立精确时间证明。
- 种子管理员 `user-admin` / `role-platform-admin`，组织 `org-001`、环境 `env-dev`，同一身份在两个浏览器上下文完成计划读取和草案写入，实际具备 `business.scheduling.plans.read/manage` 所需访问。未以最小权限角色核验，也未触发发布权限。
- 真实 MES 急单 `WO-20261013-000001`，SKU `SF-ROD-01`，生产版本 `01a135db-b36d-72b7-9405-5777995e8678`，OP10/20/30。工单发布因真实 `RM-BAR-01` 缺料 1.4 被拒绝；APS 候选池仍提供该工单并生成未发布方案 `plan-01a135df9bde769cbc91b1f1b12b5373`，含三道工序。核验对象为 `WO-20261013-000001-OP-10` / 草案 task `assign-WO-20261013-000001-WO-20261013-000001-OP-10` / 资源 `WC-TUB-01`。方案基线 OP10 开始 `2026-10-13T15:25:29.452321+00:00`，结束 `15:32:29.452321+00:00`。

此处验证的是已生成方案的**私有、未锁定工作草案**。工作草案 API 的公开读回是编辑后的权威结果；方案详情 GET 仍保留生成基线，直到锁定重预览，不能误判为保存失败。没有发布 MES 排程，也没有真实生产执行。

## 时间入口、字段生产与转换

| 面 | 当前来源与实际规则 | 可观察结果 |
| --- | --- | --- |
| 排程窗口 | `SchedulingHorizonFields` 的 `datetime-local` 经 `schedulingHorizon.ts` 用浏览器本地 `Date` 转 `toISOString()` | 此入口已有本地墙上时间到 UTC 的明确转换，未作为本次缺陷点 |
| 甘特 / 详情 | 方案 `DateTimeOffset` 字符串经 `Date`、本地格式化与 Gantt 时间轴 | 上海、UTC 浏览器对同一瞬间显示不同当地时钟；页面未标明 IANA 区域 |
| 表格编辑 | `SchedulingDraftBoard.vue` 的开始/结束 `NvInput` 直接以 `task.startUtc/endUtc` 为值，将输入字符串原样 emit | 原始 `+00:00` 字符串与本地甘特并列，字段没有格式/时区提示；不带偏移的字符串可以进入保存路径 |
| 草案 / API | `useWorkingScheduleDraft.updateTask` 保存字符串，`useSchedulingDraftPersistence` 调用 BusinessGateway 草案 PUT；公开 GET 从 Scheduling 返回 `DateTimeOffset` | 服务端可保留带偏移输入的同一瞬间；不带偏移输入由运行端按 `+08:00` 解释，本次 UTC 浏览器发生偏移 |

对应固定 SHA 的[表格输入](https://github.com/Mang-X/Nerv-IIP/blob/bc205af1f3e3724e16371a69355ee729973be0c3/frontend/apps/business-console/src/components/scheduling/SchedulingDraftBoard.vue#L406)、[草案更新](https://github.com/Mang-X/Nerv-IIP/blob/bc205af1f3e3724e16371a69355ee729973be0c3/frontend/apps/business-console/src/composables/useWorkingScheduleDraft.ts#L129)、[持久化](https://github.com/Mang-X/Nerv-IIP/blob/bc205af1f3e3724e16371a69355ee729973be0c3/frontend/apps/business-console/src/composables/useSchedulingDraftPersistence.ts)、[本地窗口转换](https://github.com/Mang-X/Nerv-IIP/blob/bc205af1f3e3724e16371a69355ee729973be0c3/frontend/apps/business-console/src/composables/schedulingHorizon.ts#L44)及[调度公开契约](https://github.com/Mang-X/Nerv-IIP/blob/bc205af1f3e3724e16371a69355ee729973be0c3/backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs)支持上述静态链路。当前[排程 Product](../../product/scheduling/design.md)规定日排程单按本地日期、历史方案可按 UTC 发布日筛选，没有裁定表格编辑应采用浏览器时区还是工厂/方案时区；方案/草案对象本次读回亦无可供编辑器使用的 IANA 时区字段。运行结果证明错误存在，服务端解析路径的具体代码因果仍需修复票定位。

## 同一工序的真实往返

两组浏览器使用相同管理员、范围、方案和 OP10。每次打开表格、重新输入原字符串并读取草案；随后明确将开始/结束同时后移一小时，等待自动 PUT，重新打开表格及甘特，并从公开 `GET /api/business-console/v1/scheduling/working-drafts?organizationId=org-001&environmentId=env-dev` 读回。完整逐请求状态、关联 ID 和字段见[往返记录](assets/issue-4400/roundtrip.json)。

| 浏览器 | 同值输入后草案 GET | 明确输入 | PUT 后公开草案 GET / 重开表格 | 甘特 |
| --- | --- | --- | --- | --- |
| `Asia/Shanghai` | 原始 `15:25:29.452321+00:00` 读回仍为同一瞬间；未证实保存请求 | `16:25:29.452Z` / `16:32:29.452Z` | `16:25:29.452+00:00` / `16:32:29.452+00:00` | 以浏览器当地时间绘制，见截图 |
| `UTC` | 接续前次 `16:25:29.452+00:00` 读回仍为同一瞬间；未证实保存请求 | `17:25:29.452Z` / `17:32:29.452Z` | `17:25:29.452+00:00` / `17:32:29.452+00:00` | 以 UTC 浏览器当地时间绘制，见截图 |

**证据口径纠正（2026-10-14）：** 初稿把同值输入阶段写成已发出并完成 HTTP 200 草案 PUT，超出了归档证据。复核 `roundtrip.json` 与关联日志，每个时区只记录两个 PUT，均与后续显式修改开始/结束时间同秒；`noop` / `noopDraftTask` 只保存同值输入后的页面值和公开草案 GET，缺少可定位的独立保存 PUT。因此“无操作保存→重新打开且实际时刻不变”**尚未独立验证**，由 #4405 修复票与父 #4272 后续验收承接；不得把显式编辑的 PUT 转用于此分支。显式改时后的时间比较仍按瞬间进行，微秒精度被输入的毫秒值覆盖；两个带偏移控制组的保存与重开无漂移。该纠正不改变 UTC 不带偏移输入的八小时错位事实。

进一步在同一 OP10、同一两个浏览器分别输入不带偏移的当地墙上时间，并以显式偏移作对照：

| 浏览器与输入 | 用户意图对应 UTC | 公开 GET / 重开实际值 | 结论 |
| --- | --- | --- | --- |
| 上海 `2026-10-14T02:25:29` | `2026-10-13T18:25:29Z` | `2026-10-14T02:25:29+08:00` | 本次恰好符合当地时间；显式 `+08:00` 对照相同 |
| UTC `2026-10-13T19:25:29` | `2026-10-13T19:25:29Z` | `2026-10-13T19:25:29+08:00`，即 `11:25:29Z` | **实际时刻提前 8 小时**；PUT HTTP 200，重开仍在 |
| UTC `2026-10-13T19:25:29Z` | `2026-10-13T19:25:29Z` | `2026-10-13T19:25:29+00:00` | 显式偏移对照正确 |

结束时间同步设置为每行开始后七分钟。两个浏览器的[上海记录](assets/issue-4400/naive-local.json)、[UTC 记录](assets/issue-4400/naive-utc.json)保存了输入、PUT 200 correlation、保存前后公开 GET 和重开表格字段。UTC 不带偏移输入保存前页面有暂态「请输入有效起止时间」提示；实际 PUT 与重开证明它并未阻止错误时刻持久化。此现象未用 fixture、请求注入故障或直接数据库写入复现。

证据截图：[上海表格原值](assets/issue-4400/Asia-Shanghai-table-before.png)、[上海重开](assets/issue-4400/Asia-Shanghai-table-reopened.png)、[上海甘特](assets/issue-4400/Asia-Shanghai-gantt-reopened.png)、[UTC 表格原值](assets/issue-4400/UTC-table-before.png)、[UTC 重开](assets/issue-4400/UTC-table-reopened.png)、[UTC 甘特](assets/issue-4400/UTC-gantt-reopened.png)、[UTC 不带偏移输入](assets/issue-4400/utc-naive-local-wall-time-before-reload.png)和[UTC 显式 `Z` 输入](assets/issue-4400/utc-explicit-offset-before-reload.png)。其余上海对照截图及逐文件 SHA256 见[证据目录](assets/issue-4400/README.md)。

相关服务日志仅保存路径、方法、状态、时间戳、trace/correlation 投影：[BusinessGateway 与 Scheduling 日志](assets/issue-4400/service-logs.json)。例如上海显式编辑 PUT `630bd731a3764846bf58feff9a8ee797`、UTC 显式编辑 PUT `6185641f61594ddcbe9d0b881e91ba23`、UTC 不带偏移输入的首个 PUT `f87625b513b9421ca6a7324f7bb8bbad`；各记录可用 correlation 对照真实 Gateway/Scheduling 路径和 HTTP 200。未保存 Authorization、Cookie、口令或 token。

## 复用、产品裁决与最小交付

- [#3905](https://github.com/Mang-X/Nerv-IIP/issues/3905) 只覆盖 14 个 date-only 字段，明确排除排程/MES 时间控件；本票不复用其交付结论。[#4286](https://github.com/Mang-X/Nerv-IIP/issues/4286)及[#4331](https://github.com/Mang-X/Nerv-IIP/issues/4331)覆盖 APS 布局与编辑操作可达，**部分复用入口**，没有裁定时间语义。[#4144](https://github.com/Mang-X/Nerv-IIP/issues/4144)与[#3624](https://github.com/Mang-X/Nerv-IIP/issues/3624)的工作草案持久化**完全复用基础能力**，无需另建存储。[#3633](https://github.com/Mang-X/Nerv-IIP/issues/3633)是日排程单/CSV，[#4068](https://github.com/Mang-X/Nerv-IIP/issues/4068)是布局，均不承接本次编辑语义；[#2362](https://github.com/Mang-X/Nerv-IIP/issues/2362)是主数据调度页重构，不是此缺陷归属。上述 CLOSED 状态不代替当前 U14 往返验收。
- 产品需明确一个口径：**计划员编辑浏览器当地时区**（与当前甘特、窗口输入一致），或**编辑指定工厂/方案时区**。前者可在现有前端边界做 `scope:M`；后者若需要方案时区字段/授权范围映射，须先补当前权威来源并重新定级，不能把本机 `+08:00` 当产品默认。
- 若裁定浏览器当地时区，建议独立 `scope:M` 修复只覆盖 APS 表格开始/结束输入及其与现有甘特/详情的时区提示：输入展示明确当地日期时间和 IANA 区域/UTC 偏移；编辑提交前转换为带偏移的 UTC 瞬间，拒绝含糊不带时区的字符串直通现有草案 API。复用已有 `schedulingHorizon` 本地转换语义与工作草案通道，不加全仓日期控件、服务端存储、排程算法、物理跨午夜或发布流程变更。
- 修复票最小验收：在上海与 UTC 浏览器对同一 OP10 打开→无操作保存→重开，公开草案 GET 的瞬间不变；各明确改动一小时后，表格、甘特和草案公开 GET 指向同一瞬间；计划员在输入处能识别时区；UTC 本地墙上时间输入不会再被保存成 `+08:00`。父 #4272 在修复合入后的固定组合重验 U14，不能由此报告或本票 CLOSED 代替。

本次没有改生产代码，也没有运行新的单元测试、正式 provider/FullChain、PR CI 或 main CI。测试截图和 API 属真实本地运行；静态链路单列；没有故障注入或 fixture。报告门禁及 PR head 证据在 PR 中登记。

取证后关闭自有 Playwright 浏览器与辅助 shell；`pwsh -NoProfile -File nerv.ps1 fullstack stop -SessionId nerv-4400-bc205b` 返回 `state=Stopped remaining=0`，随后同 ID `fullstack status` 回读 `state=Stopped containers=0 recordedContainers=4 unresolved=0`。未清理当前工作树，截图、请求/日志投影和 SHA256 清单保留在本报告链接的证据目录。
