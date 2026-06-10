# 移动端（PDA 优先）设计方案

> 类型：设计 spec（brainstorming 产出）。日期：2026-06-09。
> 事实依据：`docs/architecture/implementation-readiness.md`、`docs/architecture/frontend-navigation-map.md`、
> `frontend/apps/business-console/AGENTS.md`，以及 2026-06-09 对 `backend/gateway/BusinessGateway` facade 的代码审计。
> 状态：待用户复核 → 转 writing-plans。

## 1. 背景与目标

Nerv-IIP 已交付 PC 端 Business Console（`frontend/apps/business-console`，shadcn-vue + Tailwind + Vite+），
后端业务服务与 `BusinessGateway` facade 大量就绪。导航地图（`frontend-navigation-map.md` §8、角色矩阵）
已把 PDA/移动端定为**独立任务范式**并明确**当前后置**。本方案把"移动端"从规划态推进到可实施的设计基线。

**目标**
1. 选定移动端 UI 组件库与打包路线（裁决见 §3）。
2. 规划三种移动/触摸形态的目录结构与共享包边界（§4）。
3. 给出移动端组件契约表与 UI/UX 硬标准（§5、§6）。
4. 定义 PDA v1 页面与功能范围（§7）。
5. 定义与 PC 同源的业务 SOP 模型（§8）。
6. 基于真实审计列出后端 facade 缺口与 consolidated issue（§9）。
7. 列出必须同步的文档（§12）。

**同源原则**：业务契约的单一来源是 `@nerv-iip/api-client`（由 BusinessGateway OpenAPI codegen）；
领域类型、SOP/状态机、字典（CodeSet）抽取到新包 `@nerv-iip/business-core`，供 PC 与移动端共用。
移动端**不得**直连业务服务 URL，也不得 deep import generated client——只经 BusinessGateway facade。

## 2. 范围

**v1 在范围内**
- **PDA 手持机 app（`business-pda`）**：WMS 与 MES 一线作业 + 轻量设备巡检/报修。键盘楔入扫码。
- 移动端共享层：`@nerv-iip/ui-mobile`（触摸组件）+ `@nerv-iip/business-core`（同源域逻辑）。
- Capacitor APK 打包基线（离线资源、私有化、无应用商店）。

**路线图保留位（v1 不实现，仅预留目录与边界）**
- **工位机/平板触摸操作台（`business-workstation`）**：车间固定触摸终端。
- **车间/产线/仓库/工厂大屏看板（`business-board`）**：只读展示态，Chrome kiosk URL，不打包。
- 审批移动端：后置（用户确认延后）。

**明确不做**
- 微信小程序 / uni-app 多端（无诉求 → 不引入 uni-app 体系）。
- 摄像头扫码原生插件（扫码=键盘楔入，纯 Web 即可；如后续平板需要再增量）。

## 3. 技术路线裁决

### 3.1 组件库：shadcn-vue（Reka UI 无头）+ Tailwind + 自建 `ui-mobile`（裁决：采纳）

候选对比：

| 方案 | 复用度 | 优势 | 代价 |
| --- | --- | --- | --- |
| **A. shadcn-vue + Tailwind 自建触摸区块**（采纳） | ★★★★★ | 同 tokens/暗色/动态色；同 api-client；契合"原版零改、复制重建"doctrine；真正同源 | 需自建一批 PDA 触摸组件 |
| B. TDesign Mobile Vue | ★★ | 开箱组件多、上手快 | 第二套设计系统，与统一 token、`@nerv-iip/ui` 边界冲突，视觉割裂 |
| C. uni-app + Sard/TDesign uni | ★ | 唯一能顺带出小程序/多端 | 放弃 shadcn/`@nerv-iip/ui`/composable 全部 Web 资产 |

**触摸支持核实（基于 `reka-ui ^2.9.7` 源码，非记忆）**
- 行为层 **触摸就绪**：普遍使用 Pointer Events；`Slider` 明确用 `pointerdown/move/up`；`SelectTrigger`
  对 touch 特判；`Toast` 内建 swipe-to-dismiss（`touch-action:none`）；`ContextMenu` 有 touch/pen 长按；
  `useBodyScrollLock` 处理 iOS touchmove 滚动锁；`usePointerDownOutside` 处理 touch ~350ms 点击延迟与
  滚动/长按取消。
- **Reka 无内置 BottomSheet/Drawer** → 需引 `vaul-vue`（shadcn-vue 官方 Drawer 依赖）或基于 Reka Dialog +
  `@vueuse/useSwipe` 自建。
- 已知坑：`NavigationMenu` 的 hover 逻辑是 mouse-only → 移动端不用它做导航，改 tap 式 TabBar/Drawer；
  滚动 vs 拖拽用 `touch-action` 管控。
- 结论：**行为内核触摸良好（有源码佐证）；缺的是"移动密度 + 扫码条 + 抽屉/手势"层，归 `ui-mobile` 自建**。
  现有 `vue-sonner`(Toast)、`Sheet`、`Dialog`、`date-picker`、`@vueuse/core` 已装，摊薄净新建量。

### 3.2 打包：Capacitor → Android APK（裁决：采纳）

- 键盘楔入扫码**无需原生扫码插件**。
- Capacitor 提供：离线资源打包、`StatusBar`（色/明暗/overlay）、`Keyboard`（键盘避让 resize）、
  `@capacitor/app`（返回键）、`Preferences`/SQLite（离线队列基础）、常亮/锁屏方向。
- 大屏看板不打包：Chrome kiosk 跑 URL。

## 4. 目录结构与共享包边界

```
frontend/
  apps/
    console/                # 现有：平台管理台
    business-console/       # 现有：PC 业务操作台
    business-pda/           # 新增（v1）：手持 PDA — WMS/MES 一线扫码任务
    business-workstation/   # 预留（roadmap）：工位机/平板 触摸操作台
    business-board/         # 预留（roadmap）：车间/产线/仓库/工厂 大屏看板（只读）
  packages/
    api-client/             # 现有：generated（勿手改）— 三端共用业务契约单一来源
    ui/                     # 现有：shadcn 原版 + PC FE-2 区块（不动）
    ui-mobile/              # 新增：触摸/PDA 区块层 = Reka UI + Tailwind + 同 tokens（复制重建，原版零改）
    business-core/          # 新增：同源内核 — 领域类型 + SOP/状态机 + 字典(CodeSet) + 命令构造器
    app-shell/              # 现有：PC T 型壳
    app-shell-mobile/       # 可选/后置：PDA 壳 — 我的任务首页 + 应用墙 + 扫码条 + 底部导航
```

**边界规则**
- `business-core` 是"同源"的落点：把当前散在 `business-console/src/data/*.ts` 的字典/受控值、SOP/状态机、
  命令构造逻辑抽出，PC 与移动端共用。这是有界抽取，不是大重构；PC 端逐步迁移消费，不一次性改写。
- `ui-mobile` 复用 `@nerv-iip/ui` 导出的设计 token / Tailwind 预设，保证暗色+动态色+亮暗一致；
  组件实现独立（不 import PC FE-2 区块，避免桌面密度污染）。
- `business-board` 复用 `ui` 的图表 + token，但不依赖 `ui-mobile`（展示态非触摸操作态）。
- 本地 dev 端口（建议，待 `nerv.ps1 ports` 矩阵确认）：`business-pda` 5126、`business-workstation` 5127、
  `business-board` 5128（现有 console 5105、business-console 5125）。

## 5. 移动端组件契约表

图例：🟢 复用现有原版（Reka，触摸 OK，放大触控区） · 🔵 Tailwind 重建为移动密度（复制重建，原版零改） ·
🟠 需新建触摸组件（库无内置） · ⚙️ 壳层/系统适配。

**A. 系统适配与壳层**
- ⚙️ 顶部安全区 / ⚙️ 底部安全区 / ⚙️ 左右安全区（横屏 PDA）
- ⚙️ StatusBar 适配 · ⚙️ 键盘避让 · ⚙️ 屏幕常亮 / 横竖屏锁定 · ⚙️ 全局离线/网络态条
- ⚙️ `AppShellMobile`：固定顶栏 + 可滚内容 + 固定底栏，三段安全区统一注入

**B. 导航**
- 🔵 NavBar 标题栏（返回/标题/右操作，可选大标题折叠） · 🟠 TabBar 底部主导航（图标+文字+角标，含安全区）
- 🔵 Tabs/分段控件（基于现有 `Tabs`） · 🟠 AppWall 快捷应用墙 · 🔵 Drawer 侧滑菜单（基于 `Sheet`）
- 🟠 边缘滑动返回 · 🟢 Breadcrumb（移动端弱化）

**C. 录入（PDA 核心）**
- 🟠 ScanBar 扫码焦点条（常驻/自动重聚焦/键盘楔入捕获/声光振反馈/防抖）—— PDA 命脉
- 🟠 NumericKeypad 大数字键盘 · 🟠 Stepper 数量±器 · 🔵 Input/Textarea/SearchBar（基于现有 `Input`）
- 🟠 Picker 选择器（全屏/底部 Sheet 列表替代下拉 `Select`） · 🔵 Date/TimePicker（现有 `date-picker` 改形态）
- 🟢 Switch/Checkbox/Radio（放大触控） · 🔵 Field/Form 表单（基于现有 `Field` 体系）

**D. 展示**
- 🟠 ListRow/Cell 大行列表 · 🟠 CellGroup 设置项组 · 🟢 Card/SectionCard · 🔵 KeyValue 详情键值
- 🟢 Badge/StatusBadge/Tag（token 直接复用） · 🟢 Avatar/Image（补懒加载） · 🟢 Empty 空态 · 🔵 Skeleton 骨架屏
- 🔵 Steps 流程步骤条 · 🟠 Result 结果页（大成功/失败闭环） · 🔵 Progress · 🟠 长列表虚拟滚动 · 🟠 宽表→卡片降级

**E. 反馈/浮层**
- 🟢 Toast（现有 `vue-sonner`） · 🟢 Dialog/AlertDialog（现有 Reka） · 🟠 ActionSheet 底部动作面板
- 🟠 BottomSheet 拖拽抽屉（vaul-vue/自建） · 🔵 Sheet 侧滑（现有重建） · 🟢 Loading + 全屏遮罩
- 🟠 PullToRefresh 下拉刷新/上拉加载 · 🟠 SwipeAction 滑动操作 · 🔵 NoticeBar 通告栏

**F. 业务复合（落 `business-core` + `ui-mobile`）**
- 🟠 TaskCard 我的任务卡 · 🟠 StepFlow 扫一步确认一步容器 · 🟠 QtyConfirm 数量确认块
- 🟠 BatchPicker/LocationPicker 批次/库位选择 · 🟠 ScanResultRouter 扫码结果分流

## 6. 移动端 UI/UX 硬标准

延续设计方向 v2（黑主题 + 动态色 + 亮暗），叠加 PDA 专属硬规范：

1. **安全区**：`<meta viewport-fit=cover>` 开启；统一工具类
   `pt-safe = max(12px, env(safe-area-inset-top))`、`pb-safe = max(8px, env(safe-area-inset-bottom))`、
   `px-safe = env(safe-area-inset-left/right)`；顶栏 `pt-safe`、底部 TabBar/主操作条 `pb-safe`、
   横屏内容 `px-safe`，全部收口在 `AppShellMobile`，业务页不各写一套。
2. **触控尺寸**：主操作目标 ≥ 48×48px；列表行 ≥ 56px；行间命中区不重叠；主操作按钮置于单手拇指可达区（底部）。
3. **扫码即焦点**：进入作业页 ScanBar 自动聚焦并在失焦后自动重聚焦；扫码成功有声/光/振三态反馈；
   连扫防抖与重复扫码去重。
4. **强反馈闭环**：每个写操作有明确成功/失败结果态（Result 或大 Toast）；失败给可读原因与重试入口。
5. **误操作防护**：危险/不可逆动作（过账、完工、关闭）走 AlertDialog 二次确认；数量类输入用 Stepper/数字键盘
   并做范围校验。
6. **离线/弱网态**：全局网络态条；写操作失败进入可重试队列（见 §10）；不伪造成功。
7. **不暴露工程语言**：operationId/sourceSystem/code/policy/demo/seed/mock/issue 号不进界面（同 PC 金标准）。
8. **字典/配置驱动**：业务取值走 `business-core` 的 CodeSet/常量，不写死客户/产品专名。

## 7. PDA v1 页面与功能范围

**首页（任务范式，非菜单树）**
- 顶部：常驻 ScanBar（扫码直达）。
- 我的任务：跨 WMS/MES 的个人待办卡片（TaskCard）。
- 快捷应用墙：收货/上架/拣货/复核发货/盘点/报工/领料/完工入库/工序执行/巡检点检/报修 入口九宫格。
- 扫码结果分流（ScanResultRouter）：扫码串 → 识别对象（工单/库位/批次/SKU/设备/容器）→ 进对应作业页。

**WMS 作业页**（窄流程，扫一步确认一步）
- 收货入库、上架、拣货、复核发货、盘点；页内就地嵌入库存可用量/批次/库位上下文（融合 Inventory，见导航图 §10）。

**MES 作业页**
- 报工、领料/齐套、完工入库、工序执行（start/pause/resume/complete）、工单查看/详情。

**设备（轻量）**
- 故障报修（创建维修工单）、点检/巡检（创建点检记录）、设备报警查看。

对象详情、动作表单、页内 Tabs 不作为常驻菜单项（同 PC 导航硬约束）。

## 8. 同源业务 SOP 模型

每个 PDA 任务流程是一个 **StepFlow 状态机**，定义集中在 `business-core`，绑定到 BusinessGateway facade
**已有命令/事件**，与 PC 同源、配置驱动（AGENTS.md §6：SOP 充分设计后固化）。

示例（绑定真实端点）：
- **收货 SOP**：扫到货单 → 扫物料/SKU → 扫批次 → 扫库位 → 数量确认 →
  `POST /api/business-console/v1/wms/inbound-orders/{id}/complete`（幂等键）→ 上架任务
  `POST .../inbound-orders/{id}/putaway-tasks`。Inventory 过账经公共 `Inventory` 集成事件异步闭环。
- **报工 SOP**：扫工单 → 选工序 → 数量/良次品 → `POST /api/business-console/v1/mes/production-reports`；
  领料 `POST .../work-orders/{id}/material-issue-requests` + 线边接收
  `POST .../material-issue-requests/{id}/line-side-receipts`。
- **报修 SOP**：扫设备/选设备 → 故障描述 → `POST /api/business-console/v1/maintenance/work-orders`；
  点检 `POST .../maintenance/inspections`。

状态机、字段可见性、可选项字典均为 `business-core` 中的数据驱动配置，避免流程逻辑散落多处。

## 9. 后端 facade 缺口与 consolidated issue

基于 2026-06-09 对 `backend/gateway/BusinessGateway` 的审计（文件级证据）。**已满足**：MES 工单/工序执行/报工/
领料/完工入库（`BusinessConsoleMesEndpoints.cs`）、Maintenance 维修工单/点检（`BusinessConsoleMaintenanceEndpoints.cs`）、
Telemetry 报警 list（`BusinessConsoleTelemetryEndpoints.cs`）、WMS 收货/出库 list+create+complete
（`BusinessConsoleWmsEndpoints.cs`）。

**缺口（需整批发 consolidated issue 给后端，并在模块文档回填 issue 号）**

| # | 缺口 | 严重度 | 现状 | 建议补救 |
| --- | --- | --- | --- | --- |
| 1 | WMS 拣货无独立 list | 🔴 | 仅 `POST outbound-orders/{id}/picking-tasks` | 新增 `GET /wms/picking-tasks`（含状态/库位/操作员过滤） |
| 2 | WMS 上架无独立 list | 🔴 | 仅 `POST inbound-orders/{id}/putaway-tasks` | 新增 `GET /wms/putaway-tasks` |
| 3 | WMS 盘点执行无 list | 🔴 | 有 create+complete，无 GET list | 新增 `GET /wms/count-executions` |
| 4 | "我的任务"个人过滤缺失 | 🟡 | `workbench/summary` 仅 KPI/待办/通知；list 端点无 `assignedToUserId` | 加操作员/工作中心 scope 过滤参数，或新增 PDA 任务收件箱 facade |
| 5 | 扫码解析端点缺失 | 🟡 | 仅 `POST barcode/scans`(记录) + GET list；`search` 不支持 inventoryBatch/Lot/equipmentDevice | 新增 `POST /barcode/resolve` → `{objectType, objectId, target}`；并补 search 批次/库位/设备类型 |
| 6 | WMS 出库无独立复核端点 | 🟠 | 复核混在 picking + complete | v1 可绕开；如需独立复核再补 |
| 7 | WCS 任务权限为 Automation | 🟠 | `WmsAutomationManage`，非一线操作员 | PDA v1 不纳入 WCS；如纳入需拆低权限 dispatch |

**v1 兜底**：缺口 1-3 落地前，对应 PDA 作业页保持 disabled/feature-flag hidden 或以父单进入，不做半截/空跳转
（同导航图 §2 上下文穿透验证口径）；"我的任务"v1 先按工作中心/状态在客户端聚合现有 list，缺口 4 落地后切换。

## 10. 离线与扫码策略

- **扫码**：键盘楔入，ScanBar 捕获键盘序列 + 结束符解析；不依赖原生相机。
- **v1 在线优先**：写操作走 facade，失败用**幂等键**保护重试（后端 #188 numbering + idempotency keys 已具备
  幂等创建基线）。
- **离线队列（phase 2）**：弱网下写操作入本地队列（Capacitor Preferences/SQLite），恢复后按幂等键重放；
  v1 不承诺完整离线，仅做失败重试与清晰降级。

## 11. 分期与里程碑

1. **M0 `ui-mobile` 地基**（先行）：`AppShellMobile`（含三段安全区）+ ScanBar + ListRow + BottomSheet + Result
   五个先跑通；建 `business-core` 骨架与 token 接入。
2. **M1 PDA 骨架**：`business-pda` app + Capacitor APK 基线 + 首页（我的任务/应用墙/扫码分流）+ 登录/会话复用。
3. **M2 WMS 一线闭环**：收货/上架/拣货/复核/盘点（依赖缺口 1-3，未落地处保持降级）。
4. **M3 MES 一线闭环**：报工/领料/完工入库/工序执行。
5. **M4 设备轻量**：报修/点检/报警查看。
6. **M5 扫码解析增强**：接入缺口 5 的 resolve 端点，强化扫码直达。
7. **roadmap**：`business-workstation`、`business-board`、审批移动端。

## 12. 文档更新清单（"别忘了文档"）

- **本 spec**：`docs/superpowers/specs/2026-06-09-mobile-pda-design.md`（本文件）。
- 新建 **模块产品业务文档**：`docs/architecture/mobile-pda-module-product-design.md`（PDA 域的产品/IA/UX/分期/验收，
  business-console AGENTS.md「新域开工先立此文档」）。
- 更新 `docs/architecture/frontend-navigation-map.md`：把 PDA 范式由"预留/后置"细化为 v1 任务地图与角色入口。
- 更新 `docs/architecture/frontend-structure.md`：新增 apps（business-pda 等）与 packages（ui-mobile/business-core）边界。
- 更新 `docs/architecture/implementation-readiness.md`：新增"移动端/PDA"实施轨与端口。
- 后端缺口：按 §9 整批发 consolidated issue，并在模块文档「后端缺口」回填 issue 号。

以上文档更新在实施计划（writing-plans）中按"先文档后代码"铁律排入；本 spec 是上游设计真相源。

## 13. 验收/门禁

- 新增 app/包遵循前端门禁：`pnpm -C frontend --filter <pkg> typecheck/test/build`。
- PDA 页面参照 PC「金标准」治理级别：组件契约表 + UI/UX 硬标准作为评审清单；UI 无工程语言、无假分页/假数据。
- 触及跨域穿透/缺口降级的页面补 smoke 测试，未具备 facade 处保持 disabled/hidden。
- 除 typecheck/test/build 外，新增 **Playwright e2e**（移动视口真实浏览器：登录→首页流程 +
  ui-mobile 组件交互 + 视觉/布局 smoke——无横向溢出、触控 ≥44px、安全区 fallback 最小内边距、暗色 token），
  网关全程 Mock（`page.route`，无需后端）。运行与浏览器不可用时的降级口径（最低 `playwright test --list`
  + typecheck/build）见 `docs/architecture/mobile-pda-testing-and-smoke.md`。
- **像素级视觉快照**（仓库无基线，PDA 设计稿未定稿）与**真机扫码/真实 `env(safe-area-inset-*)`/Capacitor
  APK 内 WebView** 不在 e2e 内，进同文档的「真机手动冒烟清单」，每次发版前在目标 PDA 上勾验。

## 14. 风险与取舍

- 🟠 新建触摸组件十余项是真实一次性工作量 → 用 M0 先行阶段集中消化，避免边做业务边补组件。
- 🟡 WMS 拣货/上架/盘点缺 list 是 v1 闭环的硬阻塞 → 依赖后端 consolidated issue；未落地处不上半截入口。
- 🟡 "我的任务"在缺口 4 前是客户端聚合，存在权限/范围近似 → 以 Gateway per-request enforcement 为权威，
  客户端聚合只作展示。
- 🟢 同源靠 `api-client` + `business-core`，不靠复制粘贴；PC 端渐进迁移消费 `business-core`，不阻塞 PDA。
