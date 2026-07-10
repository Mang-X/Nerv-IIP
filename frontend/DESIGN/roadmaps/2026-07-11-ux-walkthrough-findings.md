# 实机全景 UX 走查发现（console + PDA）

> 2026-07-11。MAN-461 / #815（前端第二波 W2 独立走查）。
> 依据：`frontend/DESIGN/patterns/interaction-patterns.md`（A1 交互规范 v1，#785）的六项标准。
> 方法：`nerv.ps1 dev` 起整栈（真实后端 + PostgreSQL 种子数据），以种子管理员登录，用 Playwright（Chromium）实机逐页导航；console 桌面视口 1440×900、PDA 移动视口 390×844。**本任务只读走查 + 写文档，不改任何业务页面代码。**
> 定位：后续 W2/W3 交互重设计的事实来源。每条问题给复现路径 + 违反条款 + 证据（运行时/源码 `路径:行号`）+ 建议 + 严重度（阻断作业 / 低效 / 观感）。

## 0. 覆盖范围与走查结论

- **console**：10 个业务域共 74 条路由全部实机打开（MES/质量/库存/WMS/计划/工程/设备/维护/审批/主数据，另附 ERP/条码）。全部渲染成功，登录、路由守卫、真实数据贯通正常。
- **PDA**：13 个作业页全部实机打开（WMS 收货/上架/拣货/复核/盘点、MES 领料/工序/报工/完工、设备 报警/点检/维修）。登录与鉴权正常，多页有真实种子数据或规范空态。
- **数据真实性**：种子数据丰富（MES 17 条工序任务、真实工单号 WO-20260608-_、完工入库 FGR-_、盘点 CNT-\*、SKU 智能网关G200 等），足以观察真实密度下的交互表现。
- **一句话结论**：视觉基线（dashboard-01 黑/亮主题、导航、密度）已相当成熟；**系统性欠账集中在交互层**——"一切皆 Dialog"、破坏性动作无原因、重对象塞抽屉、筛选状态不进 URL、批量能力零使用、PDA 步骤/数字键盘未接入。此外实机暴露 1 个门禁未拦住的真实契约 bug（见 §4）。

## 1. 起栈与方法学要点（供后续走查复用）

1. **MAX_PATH 构建阻断（已定位并绕过）**：本 worktree 路径含中文且极深（前缀 89 字符）。`Nerv.IIP.Business.IndustrialTelemetry.Infrastructure` 是解决方案里名字最长的工程，其 `obj\Debug\net10.0\*.dll` 路径达 **272 字符 > MAX_PATH(260)**，且机器 `LongPathsEnabled=0` → 该工程稳定 `MSB3030：找不到该文件`（DLL 已编译出但拷贝步骤存在性检查失败）。**绕过方式**：`subst N: <worktree>` 建短盘符（前缀降到 3 字符），经 `N:\` 构建与 `N:\nerv.ps1 dev`。构建即通过。后续在此机器起栈务必走短盘符。
2. **dev-server 抖动辨识（勿误判为 bug）**：首次访问页面的 Vite 冷编译会偶发 `page.goto` 45s 超时（`/wms/inbound`、`/barcode/print-batches` 命中，重试即正常）；PDA 首启出现 `504 Outdated Optimize Dep`（依赖预打包过程态）。二者均为开发服务器瞬态，**非产品缺陷**。
3. **误报排除**：正文只保留可复现的真实问题；机器初判的 `/planning`、`/erp/sales/orders` "错误文案"经核实是种子数据里的数字 `500 EA` / `500 台`，非 HTTP 500，已剔除。
4. **PDA 未纳入 AppHost**：`business-pda` 不在平台 AppHost 的 `AddViteApp` 列表（仅 console/business-console/screen）。走查时单独起 `vp dev --port 5126` 并注入真实网关 URL（`NERV_IIP_BUSINESS_GATEWAY_URL` / `NERV_IIP_PLATFORM_GATEWAY_URL`）。

## 2. 逐域打分表

评分：✅ 合规 / ⚠️ 部分合规或轻问题 / ❌ 系统性不合规。§6 仅适用 PDA（console 域记 —）。"批量(§5.2)"全平台 `selectable` 零使用（`grep` 证实），故各域 §5 若无批量诉求以筛选/空态为主评。

| 域          | §1 表单承载           | §2 行操作                    | §3 列表-详情        | §4 引导+失效         | §5 空态/批量/筛选            | 关键证据                                                                     |
| ----------- | --------------------- | ---------------------------- | ------------------- | -------------------- | ---------------------------- | ---------------------------------------------------------------------------- |
| **MES**     | ❌ 6/7 字段塞Dialog   | ✅ 工序执行金标准            | ✅ 工单独立页+速览  | ❌ 死文案+失效漏库存 | ⚠️ 筛选部分进URL/批量缺      | 运行时确认：receipts 弹窗 6 输入；operation-tasks 选筛选 URL 不变            |
| **质量**    | ⚠️ Sheet容器对但超载  | ❌ 唯一动作收菜单+关闭无原因 | ❌ NCR重对象塞Sheet | ⚠️                   | ⚠️ 筛选进URL/无批量          | `quality/ncrs.vue:259/267/341`；运行时 NCR 空态无 CTA 链接                   |
| **库存**    | ⚠️ 1 Dialog           | ⚠️                           | ✅ 轻对象           | ⚠️                   | ✅ 筛选进URL/批量缺          | availability/counts/lots/movements 均用 `route.query`                        |
| **WMS**     | ⚠️ 1 Dialog           | ⚠️                           | ⚠️                  | ⚠️                   | ⚠️ 仅 picking 进URL/批量缺   | 收货/上架/拣货/盘点均渲染真实单据                                            |
| **计划**    | ✅ 工作台无重表单     | ⚠️ 转工单行内✅              | ⚠️                  | ⚠️                   | ✅ plans 进URL               | `mes/plans.vue` 转工单按钮行内直达                                           |
| **工程**    | ❌ ECO动态行塞Dialog  | ⚠️                           | ❌ ECO无详情载体    | ⚠️                   | ✅ bom-analysis 进URL(正例)  | `engineering/eco.vue:312`；7 Dialog/6 Sheet 混用                             |
| **设备**    | ⚠️ 1 Dialog           | ⚠️ 报警批量确认缺            | ✅ 设备独立页       | ⚠️                   | ⚠️ history/oee进URL/批量缺   | `equipment/[deviceAssetId].vue` 独立页合规                                   |
| **维护**    | ✅ 0 重表单           | ⚠️                           | ⚠️                  | ⚠️                   | ✅ work-orders等进URL        | 工单/计划/点检/备件/可靠性/可用窗口全渲染                                    |
| **审批**    | ✅ 单页               | ⚠️                           | ⚠️ 运行时空态       | ⚠️                   | ⚠️                           | 空态，无待办可深入交互                                                       |
| **主数据**  | ❌ 5–6字段塞Dialog×多 | ❌ 三动作全下拉+停用无原因   | ✅ 轻详情Dialog合规 | ⚠️                   | ✅ 空态带CTA/批量缺          | `MasterDataRowActions.vue:80/120`；`units.vue:471/565`；组织空态"新建部门"✅ |
| **PDA(§6)** | ⚠️ Sheet字段可能超3   | —                            | —                   | —                    | —（§6.1数字键盘/§6.3步骤缺） | 运行时确认：报工"第 1/4 步"裸文案；报工列表"未知状态"                        |

## 3. 问题清单（三级严重度）

### 3.1 阻断作业 / 高优

- **P0-1 主数据人员目录接口 400，人员选择器全空**（门禁未拦，见 §4）。复现：登录 → `/master-data/organization`（或 `/master-data/skills`、`/mes/dispatch`）。前端 `useBusinessMasterData.ts:600` 发 `pageIndex: 0`，后端校验 `pageIndex 必须大于 0`（1-based）→ **HTTP 400**，人员/班组选择器静默空。违反：功能正确性（非 A1 条款，但直接阻断派工/技能登记）。建议：前端改 1-based（`pageIndex: 1`）或后端接受 0-based；补一条真机集成用例覆盖分页边界。**证据：运行时抓包 body `{"errors":{"pageIndex":["'pageIndex' 必须大于 '0'"]}}`。**
- **P0-2 破坏性动作无原因、原因不入审计**。复现：主数据行操作 → 停用；质量 → 关闭不合格品。确认框仅说明文案、无原因输入即可提交。违反 **§2 破坏性条款**（`AlertDialogPro` + 原因必填 + 随请求进审计）。证据：`components/masterData/MasterDataRowActions.vue:120`、`quality/ncrs.vue:341`。建议：按 §2 目标写法补原因输入 + `disabled` 门禁 + 提交入审计。

### 3.2 低效 / 中优

- **P1-1 完工入库 6 字段塞居中 Dialog**（运行时确认弹窗 6 个输入）。复现：`/mes/receipts?workOrderId=…` → "登记完工入库"。违反 **§1**（4~8 字段应 Sheet 侧滑）。证据：运行时 inputCount=6；`mes/receipts.vue:290`（开关变量已叫 `receiptSheetOpen`）。建议：改 `SheetPro`。
- **P1-2 工单报工 7 字段塞 Dialog**。复现：`/mes/work-orders` → 报工。违反 §1。证据：`mes/work-orders/index.vue:558`（变量 `reportSheetOpen`）。建议：改 Sheet。
- **P1-3 工程变更 ECO：动态行塞 Dialog，且重对象无详情载体**。复现：`/engineering/eco` → 发布工程变更（受影响版本动态行组）。违反 **§1**（动态行至少 Sheet）+ **§3**（ECO 有状态机/审批链/多段，应独立页 `/engineering/eco/[id]`）。证据：`engineering/eco.vue:312`、`:509`。建议：创建/编辑改独立页或 Sheet；补 ECO 详情独立页。
- **P1-4 NCR 重对象塞 Sheet、唯一动作收菜单**。复现：`/quality/ncrs`（当前种子为空，源码为准）。违反 **§3**（NCR 有状态机+处置+关闭+审批链+附件，应独立页 `/quality/ncrs/[id]`）+ **§2**（整行唯一"打开处置"仍收 `RowActions`）。证据：`quality/ncrs.vue:267`、`:259`。建议：NCR 独立页；"打开处置"提为行内按钮或由 ID 列承担。
- **P1-5 完工入库成功态是"死文案"，跨域失效漏库存**。违反 **§4.1**（创建成功须给"继续/查看/返回"至少两项）+ **§4.2**（入库改库存，`onSuccess` 未失效库存键）。证据：`mes/receipts.vue:298` 常驻 `<p>`；`useBusinessMes.ts:856` 只失效 MES 两键。建议：容器内成功态 + 出路按钮；失效链补 `getBusinessConsoleInventoryAvailability` 等库存 operationId。
- **P1-6 MES 工序执行筛选状态不进 URL**（运行时确认）。复现：`/mes/operation-tasks` → 选"状态=待开工" → URL 仍为 `/mes/operation-tasks`（无 `?status`）；进详情返回即丢。违反 **§5.3**。证据：运行时 `URL_CHANGED=false`；`mes/operation-tasks.vue:64` 筛选存 `ref`。建议：筛选/搜索/页码 `router.replace({query})` 双向同步（库存/质量/条码域已有先例可抄）。
- **P1-7 批量能力全平台零使用**。`DataTablePro` 已内建 `selectable` + `#bulk-actions`，但 `grep` 全 business-console **0 处**接入。违反 **§5.2**。需批量的场景（工单批量下达、报警批量确认、主数据批量停用）现均逐行操作。建议：按 §5.2 形态接入，破坏性批量带条数复述 + 原因。

### 3.3 观感 / 低优

- **P2-1 主数据 5–6 字段建档普遍塞 Dialog**。`units.vue:471`（新建计量单位 5 字段）、`:565`（换算 6 字段）、`quality/reason-codes.vue:241`（5 字段）。违反 §1（主数据域整改以 A1 §1 取代 `master-data-templates.md` §5 的"Dialog 默认"）。
- **P2-2 主数据行操作三动作全收下拉、行内零按钮**。`MasterDataRowActions.vue:80`（查看/编辑/停用全下拉）。违反 §2（查看详情由 ID 列承担，编辑/停用按频率提行内）。
- **P2-3 NCR 空态无 CTA 出路**。运行时：`/quality/ncrs` 空态"未返回不合格报告…"仅解释、无"去检验 →"链接。违反 **§5.1**（只读生成页应给"去 {上游页} 维护 →"）。建议：补跳检验记录的链接。
- **P2-4 PDA 报工步骤裸文案"第 1/4 步"**（运行时确认，截图在案）。违反 **§6.3**（应挂 `Steps` 组件，全程可见步骤名 + 当前位置）。证据：`business-pda mes/report.vue:245`、`mes/receipt.vue:323`。
- **P2-5 PDA 数量/测量值用原生 `<input type=number>`**。违反 **§6.1**（应用 `NumberKeyboard`，已导出零接入）。证据：`business-pda wms/count.vue:211`、`mes/report.vue:365`。

## 4. 门禁绿 ≠ 真机无 bug（运行时专有发现）

教训复盘：单测桩掉了真实 HTTP，导致以下问题不被门禁拦截，只有实机能暴露——

1. **人员目录分页 0-based/1-based 契约错位（P0-1）**：前端发 `pageIndex=0`，后端 FluentValidation 要求 `>0`，稳定 400。三处消费页（组织与班组、人员技能、派工看板）的人员选择器静默空。前端单测 mock 了 `workers` 响应，故 `pnpm test` 全绿。**这是"reka/契约类漏网"的典型**：属数据契约层，不是 reka 运行时约束，但同类"测试桩掩盖真实后端行为"。→ 建议补一条经真实 BusinessGateway 的分页边界用例。
2. **PDA 工单状态显示"未知状态"**：`business-pda mes/report.vue` 工单列表 17 条全部渲染"未知状态"，疑似状态枚举未映射/字段缺失（真机数据是 `released` 等）。观感级但影响一线判断。→ 核对 PDA 侧状态映射与 facade 字段。

> 说明：本次未发现 reka 运行时抛错类问题（如 `SelectItem` 空值崩溃）——74 + 13 页导航 console/pageerror 干净（除上述 400 与开发瞬态）。可作为 MAN-435 codemod 后运行时稳定性的一个正面数据点。

## 5. 正面确认（避免整改误伤）

- **MES 工序执行 = 行操作金标准**：高频"报工"按状态条件行内直达，其余收 `RowActions`（`mes/operation-tasks.vue:331`）。§2 正例，勿动。
- **MES 工单 = 列表-详情正例**：独立页 `work-orders/[id]` + 列表处 `WorkOrderQuickView` 只读速览带"进完整页"出口。§3 正例。
- **设备 = 独立页正例**：`equipment/[deviceAssetId].vue`。
- **筛选进 URL 已在多域落地**：库存（availability/counts/lots/movements）、质量（inspections/ncrs）、条码、设备 telemetry、维护、工程 bom-analysis 均用 `route.query`——比 A1 立标时更普及，MES operation-tasks 是明显缺口。
- **空态带 CTA 已有样板**：主数据组织与班组"还没有部门，点击创建第一条 + 新建部门"按钮；`units.vue` 文案式 CTA。
- **视觉与密度成熟**：真实 17 行工序任务下表格、分页、状态徽章、暗色主题表现良好。

## 6. Top 问题 → issue 去向

| 问题                       | 严重度 | 去向建议                                                                                                                                         |
| -------------------------- | ------ | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| P0-1 人员目录分页 400      | 阻断   | **独立新开 bug issue**（`type:bug` `area:frontend` `domain:business-platform`）；修前端 1-based 或后端契约，非交互重设计范畴，不宜并入 W2 交互链 |
| P0-2 破坏性动作无原因      | 阻断   | 并入各域 W2/W3 交互 issue 的 §2 验收；主数据域 + 质量域优先                                                                                      |
| P1-1/1-2 报工/入库塞Dialog | 低效   | 并入 **MES W2 交互重设计** issue（§1 承载分级批量整改）                                                                                          |
| P1-3 ECO 动态行/无详情     | 低效   | 并入 **工程 W2** issue（ECO 独立页 + 承载）                                                                                                      |
| P1-4 NCR 重对象塞Sheet     | 低效   | 并入 **质量 W2** issue（NCR 独立页 `/quality/ncrs/[id]`）                                                                                        |
| P1-5 入库成功态+失效漏库存 | 低效   | 并入 MES W2（§4 引导 + 跨域失效链）                                                                                                              |
| P1-6 MES 筛选不进URL       | 低效   | 并入 MES W2（§5.3，照抄库存域先例）                                                                                                              |
| P1-7 批量零使用            | 低效   | **横切 issue**（§5.2 批量形态接入，跨域样板 1 个 + 各域跟进）                                                                                    |
| P2-4/2-5 PDA 步骤/数字键盘 | 观感   | 并入 **PDA W2/W3** issue（§6.1/§6.3，`Steps` + `NumberKeyboard` 接入）                                                                           |
| PDA "未知状态"             | 观感   | 并入 PDA W2（状态映射核对）                                                                                                                      |

## 7. 覆盖边界与未走查项（如实记录）

- **创建成功态的完整闭环**未逐一提交真实写操作（走查以只读 + 打开表单为主，不改数据）。§4.1"成功后出路"多以源码 + 容器形态判定；receipts 死文案经源码确认，未真正提交入库。
- **NCR / 审批中心运行时为空**（种子无不合格品/待办），§2/§3 的 NCR 结论以源码 `路径:行号` 为准，未获运行时行级交互证据。
- **PDA 深层交互**（数字键盘弹出、多步 Sheet 字段数）未逐一进入作业详情态；§6.1/§6.2 以源码 + 列表态截图判定，§6.3 已获运行时截图证据。
- **ERP/条码**不在 #815 十域清单内，仅顺带确认可打开，未逐项按六标准打分。
