# SCR M1 一期核心屏 — 设计

- 日期：2026-07-06
- 状态：设计已与用户确认，待 review → writing-plans
- 关联：Linear 里程碑「M1 一期核心屏」（项目「工业数据大屏 · 生产指挥中心」）
  - MAN-314 SCR-1 工厂总览 `/factory`（GitHub #564）
  - MAN-317 SCR-3 设备监控 `/equipment`（GitHub #567）
  - MAN-316 SCR-2 产线监控 `/line`、`/line/[id]`（GitHub #566）
  - 地基：MAN-313 SCR-0（GitHub #563，已 Done，见 `2026-06-26-screen-foundation-design.md`）
  - Epic：MAN-312 / GitHub #562；横切支撑：MAN-320 后端聚合+SSE（#570，M3，未开工）、MAN-322 安灯闭环（#573，M3）、MAN-321 组件上提 `@nerv-iip/ui`（SCR-7）

## 背景与目标

在 SCR-0 地基（独立 `apps/screen`、`screen-kit`、`@nerv-iip/ui` screen 层 28 组件、`--sb-*` 深色令牌、`useScreenData` 轮询）之上，落地数据成熟度最高的三块**挂墙/指挥中心大屏**。三屏均为**明细驱动的作业/状态/告警实时屏**，一期主打 🟡 前端聚合 + 诚实标注；🟠 聚合项全部归 MAN-320（#570，M3）。

**数据现实（贯穿全设计的硬约束）**：

- 全平台**无车间(Workshop)/产线(Line)聚合维度**，最细到 `WorkCenter`/`Device`。车间/产线归并必须前端用 MasterData 映射（`WorkCenter.WorkshopCode`、`DeviceAsset.LineCode`）建字典后聚合。
- **几乎无聚合 API**（达成率/合格率/不良率/OEE/趋势/帕累托）。
- **OEE 半成品**：仅可用率(availability)真实，性能率/质量率写死=1，综合 OEE≈可用率——**必须诚实标注，不得伪装真 OEE**。
- 设备 `overview` 须传 `deviceAssetIds`(≤50)，无全厂状态批量端点；MTBF/MTTR 真实（单设备，无样本返 null）；报警↔工单已闭环。
- **无安灯呼叫-响应闭环实体**（MAN-322）；产线屏一期诚实定位「监控屏（含异常醒目提示）」，非真安灯。

## 范围与非目标

**M1 范围**：薄共享地基 + 三块 bespoke 大屏 + 权限驱动的大屏选择页。

**非目标（M1 边界）**：

- 真实后端接入（业务数据仍走 mock seam；#570 就绪后逐屏换 fetcher）。
- SSE 推送（一期仅 `useScreenData` 轮询）。
- 🟠 聚合项（车间维度聚合、设备状态计数/在线率、产出/良率聚合、质量 KPI、OEE 深化、停机帕累托、备件库存联动）→ 全部 MAN-320。
- 真安灯呼叫-响应闭环 → MAN-322。
- 组件上提 `@nerv-iip/ui` → MAN-321（SCR-7）；M1 业务组合件先建在 `apps/screen` 本地。
- 真实 IAM 登录守卫与真实权限 claims（沿用 mock scope；seam 预留，随 #570 或单独鉴权任务接入）。
- 车间下钻 `/workshop/[id]`（MAN-315，M2）、仓储 `/warehouse`（M2）、质量 `/quality`（M2）。

## 一、薄共享地基（先落一次，之后三屏独立并行）

> 三屏是**独立可并行**的交付；唯一的共享是一层极薄的 mock 数据管道与访问上下文。把它单独落一次，避免屏间先后依赖，也保证跨屏一致（工厂屏钻到的车间/产线/设备，与设备屏、产线屏是同一套 fixture）。

### 1.1 类型化 fetcher seam

```
apps/screen/src/data/
├── contracts/        # 纯 TS 类型：FactoryOverview / EquipmentBoard / LineBoard
│                     #   字段严格对齐 @nerv-iip/api-client business-console types.gen.ts
├── mock/             # 真实感 seed + 前端聚合逻辑（镜像"先前端聚合"的真实算法）
│   ├── masterdata.ts #   工厂→车间→产线→工作中心→设备 映射字典（WorkCenter.WorkshopCode / DeviceAsset.LineCode）
│   ├── fixtures.ts   #   稳定 seed（受控随机抖动；真实人读编号 WO-/WC-/DEV-/NCR-）
│   ├── alarms.ts  downtime.ts  workorders.ts  telemetry.ts  maintenance.ts
│   └── scope.ts      #   mock persona → 可见工厂/车间/产线/大屏
└── fetchers/         # 一屏一 fetcher；useScreenData(fetcher) 挂接；换真实 = 换这一个文件
    ├── factory.ts  equipment.ts  line.ts
```

**契约漂移防线**（既往教训）：mock 数据形状必须与 `@nerv-iip/api-client` 生成类型一致，**禁止用 `as`/内联标注绕过**契约。实现时对齐真实 DTO 字段名；无对应真实端点的 🟠 字段，在契约中显式标注 `// 🟠 待 #570` 并走占位口径，不伪造。

### 1.2 访问上下文 `useAccessScope`（mock，IAM-ready）

```ts
interface AccessScope {
  factories: FactoryRef[]          // 账号 scope 内工厂（支持多工厂）
  currentFactoryId: string
  visibleWorkshops: string[]       // 车间收窄
  visibleLines: string[]           // 产线收窄
  allowedScreens: ScreenKey[]      // 'factory' | 'equipment' | 'line' | ...
  switchFactory(id: string): void
}
```

- **多工厂**：M1 用「全局工厂 scope + 切换器」（路由不带 factoryId，墙面 URL 稳定）；将来需每工厂独立 URL 再演进到 `/factory/[id]`。
- **权限切分未定**：只搭 seam + 造 2 个演示 persona 证明 gating 生效，**不写死策略**：
  1. `plant-admin`：全部工厂/车间/产线 + 全部大屏。
  2. `workshop-lead`：单工厂、单车间、仅本车间产线，`allowedScreens` 仅 `line`（演示"某工厂车间账号只能看本车间产线"）。
- 等 IAM 真实接入，`useAccessScope` 从真实 claims 派生，选择页与各屏无需改动。

### 1.3 `/` 大屏选择页（Launcher）

- **权限驱动**：按 `allowedScreens` 展示"可进入的大屏卡片"（工厂总览/设备监控/产线监控），点击进入全屏大屏。
- **工厂切换**：`factories.length > 1` 时提供切换器。
- 自身是一块设计过的深色墙面页（非跳转）；`prefers-reduced-motion` 兜底。
- 现有 `pages/index.vue`（当前是工厂总览 demo）→ 内容迁入 `/factory`；`/` 改为 Launcher。

### 1.4 组件策略（复用优先，缺则新建，风格统一）

- **复用优先，以文档站实际落地件为基准**：`@nerv-iip/ui` screen 层 28 组件（`ScreenPanel`/`OeeHero`/`RingGauge`/`StatusCard`/`AlarmTable`/`TrendChart`/`DigitalFlop`/`StatusLight`/`ScrollBoard` 等）作为原子件。**真实 props/用法/视觉以 design-system 文档站「大屏」分区 + 组件源码为准**（见 §1.6），不凭记忆或二手摘要选件。
- **缺则新建，不受现有组件局限**：特殊场景（如超大产线红绿灯、设备状态全景墙、大屏选择卡）现有件不合适时，**直接按大屏风格新建**，不硬套/不改造现有件凑合。业务组合件建在各屏本地 `components/screen-blocks/`，**针对该屏内容单独设计版式**，不套统一模板。
- **设计哲学/风格必须统一**（新建件的硬门禁，依据 `frontend/packages/ui/src/components/screen/product.md` 设计宪法 + `tokens.css`）：
  1. 只用 `--sb-*` 令牌，**不混入共享 `--*`**，无亮色模式；
  2. **克制发光**——辉光只给活数据（实时值/运行态），标题/坐标轴/静态文字不发光；远距可读（关键数字大字号 + 克制 text-shadow，次要信息降噪）；
  3. **动效只用两条缓动**（`--sb-ease` / `--sb-ease-emphasized`），press 收缩（`scale`）**绝不回弹/膨胀/位移**，每个会动元素都有 `prefers-reduced-motion` 降级；
  4. **数据驱动**——零 props 可渲染示例，接真实数据只传值，不写死业务文案当占位；
  5. 遵守宪法 Don't 铁律：不堆叠 `backdrop-filter`/大面积高斯模糊、不用大数字模板/侧边色条/渐变文字、shadcn/现有原版零改动（定制靠新建）。
- **治理**：新建组合件先落本地，接口稳定后由 MAN-321（SCR-7）上提 `@nerv-iip/ui` 并补 design-system 文档站 demo（`/components/screen/`）。

### 1.5 诚实标注约定

- 占位指标（OEE 性能率/质量率=1、综合 OEE≈可用率）统一走 `PlaceholderBadge`/tooltip：屏上明确标注「≈可用率」「待 #570」，不伪装真 OEE。
- 数据新鲜度 `IsSourceFresh` 驱动"失联灰条/角标"，防假绿。
- 无对应闭环的能力（安灯呼叫-响应）显式标注「闭环待 MAN-322」，不假装有。

### 1.6 参考资料（source of truth，实现前必读）

设计与实现都以**项目中实际落地的组件**为基准，不凭记忆/二手摘要：

- **实际落地组件（首要基准）**：design-system 文档站「大屏 / 控制室」分区 `frontend/apps/design-system/docs/components/screen/`——每组件 usage/props + 可交互 `ScreenDemo` 示例 + `gallery` 总览（`ScreenGallery`）。选件、比对视觉、给新建件定风格都以此为准；本地可 `pnpm -C frontend --filter @nerv-iip/design-system dev` 实机查阅。
- **组件源码（API 权威）**：`frontend/packages/ui/src/components/screen/*.vue`（props/slots/emits 以源码为准）+ `index.ts` 导出边界。
- **设计宪法**：`frontend/packages/ui/src/components/screen/product.md`。
- **令牌**：`frontend/packages/ui/src/components/screen/tokens.css`。
- **地基**：`apps/screen/src/screen-kit/`（`ScreenScaler`/`ScrollBoard`/`useScreenData`）+ `docs/superpowers/specs/2026-06-26-screen-foundation-design.md`。
- **数据契约**：`@nerv-iip/api-client` business-console `types.gen.ts`（mock 契约对齐目标）。

> 落地纪律：每屏实现子代理动手前，先读上述文档站对应组件页 + 源码，确认真实 props 再用；新建件先在文档站找最接近的落地件对齐风格。

## 二、屏 A · 工厂总览 `/factory`（MAN-314）

> 受众：厂长/管理层/访客；大堂或作战指挥中心墙。**全厂健康度仪表盘——一眼判断今天能不能按时交付、哪个车间掉链子、有没有正在烧的火。** 下钻交给 MES 驾驶舱/设备屏。

**布局分区（bespoke）**：

- **顶部全厂 KPI 带**：今日全厂达成率（大号 + 进度环，🟡）· 在产工单总数（✅ Total）· 超期/风险工单数（🟡）· 未恢复告警数（✅ 总 / 🟡 分级）· Open 停机数（✅）· Open NCR 数（✅）。
- **主体：车间状态矩阵**（厂长 3 秒判绿/黄/红）——每车间一张 `StatusCard`：车间名+主管（`Workshop.Name/ManagerUserId` ✅）· 健康度色（下列指标合成 🟡）· 在产工单/工序数（🟡）· 达成率（完成/计划，工单按车间归并 🟡）· 超期工单数（DueUtc 过期未完成 🟡）· 未恢复 critical 告警数（设备→车间映射 🟠 占位）· Open 停机数（downtime-events 按 workCenter 🟡）。红卡置顶/闪烁。
  - 健康度色**默认合成规则**（可调，写在一处便于统一）：红 = 存在未恢复 critical 告警 **或** 超期工单 > 0；黄 = 存在 Open 停机 **或** 达成率 < 80%；否则绿。阈值集中为常量，方便逐屏确认时调整。
- **实时流**：最新告警流（✅ 明细，设备名经 MasterData 补）· 停机事件流（✅ 明细）——`ScrollBoard` 无缝滚动。
- **🟠 占位区**：不良率/设备在线率/产出良率/趋势——「待 #570」占位，不硬造。

**一眼焦点**：①今日全厂达成率（大号+进度环）②车间状态矩阵（绿/黄/红卡）③未恢复告警+Open 停机（红色大数字）④超期/风险工单数 ⑤实时告警/停机流。

**验收**：6 个 🟡 车间卡字段 + KPI 带 + 告警/停机流以纯明细聚合渲染；🟠 项诚实占位；健康度色合成规则可解释；`useScreenData` 轮询生效。

## 三、屏 B · 设备监控 `/equipment`（MAN-317）

> 受众：设备部/维修班/TPM 工程师。**设备健康 + 维修执行作战图。** 后端数据最成熟。

**布局分区（bespoke）**：

- **主体：设备状态全景墙** `DeviceStatusWall`——运行绿/待机黄/停机灰/报警红闪 + 断线灰条（`IsSourceFresh` 防假绿）；顶部状态分类计数 运行N/待机N/停机N/报警N（🟡）；活动阻塞原因（✅ overview `activeBlocks`）。⚠️ state 为自由小写字符串，一期与连接器约定标准词表。
- **未恢复报警实时表** `AlarmTable`：级别 · 未恢复时长（🟡）· 已触发工单（报警↔工单已闭环 ✅）。
- **维修工单进度**：工单 + 进度（✅）· 未关闭/超时（🟡）· 报警已恢复待确认（✅）。
- **可靠性区**：时间稼动率仪表（`RingGauge`，**标注「≈可用率，非完整 OEE」**）+ MTBF/MTTR（✅，无样本 null 显「—」）+ 故障/修复次数（✅）。
- **今日 PM 到期（🟡）+ 点检台账（✅）**。
- **🟠 占位**：OEE 深化（质量率接 MES 报工/性能率需节拍）· 停机原因帕累托 · 备件库存联动（跨 Inventory）· 全厂设备状态批量端点 · PM 达成率/点检完成率。

**一眼焦点**：①设备状态全景墙 ②未恢复报警实时表 ③维修工单进度 ④时间稼动率（标注非完整 OEE）+ MTBF/MTTR ⑤今日 PM 到期 + 点检台账。

**验收**：状态墙五态（含断线）+ 计数正确；OEE 仅呈现可用率并显式标注；MTBF/MTTR 无样本 null 正确显示；报警→工单联动可见；`deviceAssetIds`≤50 分批约束在 mock 契约中体现。

## 四、屏 C · 产线监控 `/line` + `/line/[id]`（MAN-316）

> 受众：线长/班组长/操作工；产线正上方挂屏，远距可读。**一期诚实定位「产线监控屏（含异常醒目提示）」，非真安灯**（后端无安灯闭环，见 MAN-322）。

**入口**：`/line` = 产线选择器（本身是迷你监控板，按 scope 收窄产线）；`/line/[id]` = 单线详情大屏。

**布局分区（bespoke，现场远距可读）**：

- **超大设备状态红绿灯** `LineAndonHero` + 失联角标（远距一眼判断正常/出事/失联）。
- **当班产量 vs 计划 + 达成率环**：当班良品/报废/返修（🟡）· 计划量（🟡）· 达成率（🟡）· 节拍达成（`StandardOperation` 标准工时反推，落后变红 🟡）· 小时趋势（🟡 进阶）。
- **即时停机/报警红色横幅**：设备报警含级别（按设备 🟡）· 即时不良（🟡）· 停机（✅）。
- **当前工单 & 工序**：当前工单（✅ 按 workCenter）· 工序进度状态机（✅）· 工单完成进度（✅）· WIP（✅）· 线边齐套（单工单 🟡）。
- **倒计时**：距交付（✅ 工单 DueUtc）· 当班剩余（🟡 班次）。
- **诚实**：综合 OEE 的 P/Q 占位标注；「安灯呼叫-响应」区显式标注「闭环待 MAN-322」；换型时点无数据不做。

**一眼焦点**：①超大设备状态红绿灯 + 失联角标 ②当班产量 vs 计划 + 达成率环 ③即时停机/报警红色横幅 ④节拍达成偏差（落后变红）⑤距交付倒计时。

**验收**：`lineCode→workCenter/device` 前端聚合正确；远距可读（大字号/高对比）；OEE 仅可用率真值 + 标注；安灯区诚实标注；`/line` 选择器按 scope 收窄。

## 五、路由与鉴权

- 路由（vue-router auto-routes）：`/`（Launcher）· `/factory` · `/equipment` · `/line` · `/line/[id]` · `/login`（沿用）。`/` 不再重定向。
- 鉴权：M1 沿用 mock（`stores/auth.ts` + `useAccessScope` mock persona）；`@nerv-iip/auth` 真守卫留 seam，随后端接入。
- 各屏与 Launcher 均读 `useAccessScope`：不在 scope 的工厂/车间/产线/大屏不可见/不可进。

## 六、测试策略

- **单测（vitest）**：fetcher/前端聚合逻辑（车间归并、产线归并、健康度色合成、达成率/节拍反推、状态计数）· `useAccessScope` gating（两 persona）· 关键组合件 smoke。
- **契约对齐**：mock 形状与 api-client 类型一致的编译期校验（无 `as` 绕过）。
- **门禁（每屏）**：`pnpm -C frontend --filter @nerv-iip/screen typecheck && test && build`。
- **实机 preview**：每屏 5128 截图确认（远距可读性、五态色、诚实标注、零 console/server 错误）。

## 七、交付与流程（走 superpowers）

| 步骤 | 内容 | 分支 / 交付 |
|---|---|---|
| 地基（薄） | fetcher-seam + `useAccessScope` mock + `/` Launcher + 工厂切换 + 共享 masterdata/fixtures | 1 个 foundation 分支/PR |
| 屏 A | 工厂总览 `/factory`（MAN-314） | `man-314` 分支/PR |
| 屏 B | 设备监控 `/equipment`（MAN-317） | `man-317` 分支/PR |
| 屏 C | 产线监控 `/line` + `/line/[id]`（MAN-316） | `man-316` 分支/PR |

- 地基先落一次；随后 A/B/C **独立并行**，各由一个实现子代理按 plan 执行。
- 我（主控）用 `requesting-code-review` 逐屏审核 + preview 截图确认，再合并。
- 单屏单分支、各自 PR；每屏满足门禁 + 实机截图方算完成。

## 八、issue → 交付映射

| Linear | 屏 | 路由 | 一期落地要点 | 依赖/占位 |
|---|---|---|---|---|
| MAN-314 | 工厂总览 | `/factory` | 车间状态矩阵（6 🟡 字段）+ 全厂 KPI 带 + 告警/停机流 | 🟠 车间聚合/在线率/良率/趋势 → #570 |
| MAN-317 | 设备监控 | `/equipment` | 状态全景墙 + 未恢复报警表 + 维修进度 + 稼动率/MTBF/MTTR + PM/点检 | 🟠 OEE 深化/帕累托/备件/批量端点 → #570 |
| MAN-316 | 产线监控 | `/line`,`/line/[id]` | 状态红绿灯 + 当班产量/达成环 + 停机/报警横幅 + 节拍 + 倒计时 | 🟠 OEE 真值/当班聚合/节拍主数据；安灯 → MAN-322 |

## 九、风险与待办

- **契约漂移**：mock 与 api-client 类型漂移会导致换 fetcher 时假绿——靠编译期对齐 + 无 `as` 约束兜底。
- **诚实标注一致性**：占位指标口径散落会误导——统一 `PlaceholderBadge` 约定。
- **权限策略未定**：seam + 2 persona 演示，不写死；IAM 接入前不宣称真实权限能力。
- **换 fetcher 就绪**：#570 落地后逐屏把 `data/fetchers/*` 从 mock 切真实，契约不变。
