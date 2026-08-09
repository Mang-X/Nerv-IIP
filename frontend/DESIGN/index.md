---
# Nerv-IIP Console——设计系统
# AI 编码代理：处理任何 UI 任务时先读本文件。
---

## 系统摘要

这是一个基于 **Vue 3 + Tailwind CSS v4 + shadcn-vue（reka-nova style）**、品牌名为
**NvUI** 的冷静、专业企业控制平面。四类界面（PC / mobile PDA / touch 一体机 /
screen 大屏，ADR 0020）共享同一设计理念，并通过 token 按界面隔离。`--primary` 是
**近黑色**（dashboard-01 基线）；品牌蓝是**可在运行时切换的强调色**（`--nv-brand`）；
亮色与暗色同为一等模式。所有组件都位于 `packages/ui` / `packages/ui-mobile`，并通过
裸 barrel 以 **`Nv*` 品牌名称**消费。拒绝无意义装饰；UI 始终保持高信息密度。

## 设计价值观（规范没覆盖到的场景，用这五条判断）

1. **信息密度优先，克制装饰** —— 数据是主角。动效传达状态而非装饰，辉光只给活数据，
   没有理由的视觉元素一律不加。
2. **确定性** —— 同一事实同一数据源同一呈现；状态语义走 `NvStatus*` 与语义令牌，
   不即兴造色；同类操作在所有页面长得一样。
3. **真实感** —— 用真实业务数据的形状做设计（`WO-` 单号、产线名、真实数量级）；
   UI 文案永远说业务的语言，绝不暴露开发者语言（见下节）。
4. **说人话，给出路** —— 工程术语翻译成业务语言；空态、失败态、无权限态必须
   给出下一步动作，不许死胡同。
5. **诚实** —— 不做假绿：数据缺失、失联、占位、能力未就绪都显式标注；
   宁可示弱，不可误导。

组件形态不存在时：**按价值观 + 业务场景大胆新建**（选件阶梯与新组件 DoD 见
`governance.md`），成熟后上提组件库。

---

## 面向用户的文案规则

业务页面是面向计划员、操作员、检验员、仓库人员、采购员、会计和管理者的产品界面，
不是实施备注、测试面板、seed 数据查看器或 PR 验收证据。

可见页面文案必须帮助用户决策、行动或理解业务状态。不得在标题、描述、空状态、说明文字、
badge、表格摘要、表单帮助、toast 或菜单中放置开发、验证或脚手架用语。

产品 UI 中的禁止示例：

1. `样例数据`, `内置样例`, `用于验证`, `便于联动测试`, `当前页面`, `demo`, `mock`, `fallback`, `seed`.
2. 技术所有权或 gateway 用语，例如 `业务网关契约`、`接口`、`API`、`operationId`、`source service`、`organization`、`environment`、`context`。
3. `汽车减振器制造场景下...用于验证...` 之类的场景免责声明。行业上下文可以塑造数据和标签，但页面应以用户正在使用的真实业务系统口吻表达。

允许的替代表达：

1. 使用简洁的业务名词：`销售订单`、`采购订单`、`生产计划`、`工单`、`物料`、`工艺路线`、`应收`、`应付`、`成本归集`。
2. 使用操作性摘要：`今日待排产订单`、`待齐套工单`、`待检来料`、`本班待报工任务`。
3. 使用与下一步操作相关的空状态指引：`暂无待派工工单，请先确认生产计划并下达到车间。`

如果数据只用于演示（demo）或尚不完整，应把该事实留在开发文档、PR 说明或测试夹具中，
不得在应用 UI 中呈现。

---

## 组件速查（PC；名称即 `@nerv-iip/ui` 导出真名）

| 组件                                                          | 使用时机                                                                      | 禁用场景                                                      |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `NvButton`                                                    | 任何可点击操作（内建 `loading`；页面主 CTA 用 `variant="brand"`）             | 导航到其他路由（使用 `RouterLink`）                           |
| `NvBadge`                                                     | 类别 chip、计数                                                               | 状态语义（使用 `NvStatusBadge`）                              |
| `NvStatusBadge` / `NvStatusDot`                               | 业务状态呈现（`tone`: success/warning/danger/info/neutral，`value` 自动解析） | 非状态的普通标签                                              |
| `NvCard` + 各部分                                             | 分组内容区、表单卡片                                                          | 包裹数据表                                                    |
| `NvDataTable`                                                 | 表格式实体列表（内建加载 / 空状态 / 分页 / `#cell-*` 插槽）                   | 单项详情视图（使用 `NvDescriptions`）                         |
| `NvDescriptions`                                              | 详情字段的键值对呈现                                                          | 可编辑表单（使用 `NvField`）                                  |
| `NvPageHeader`                                                | 页头（标题 + 描述 + 操作）                                                    | 卡片内小节标题                                                |
| `NvToolbar` / `NvFilterBar`                                   | 搜索 + 筛选 + 主操作栏                                                        | 表单内部布局                                                  |
| `NvDialog` + 各部分                                           | ≤3 字段的轻量新建/编辑（见 interaction-patterns §1）                          | 破坏性确认（使用 `NvAlertDialog`）；4+ 字段（使用 `NvSheet`） |
| `NvSheet` + 各部分                                            | 保持列表上下文的详情/编辑侧滑                                                 | 全页面工作流                                                  |
| `NvAlertDialog` + 各部分                                      | 确认不可逆操作                                                                | 信息提示                                                      |
| `NvPopconfirm`                                                | 行内轻量二次确认（低风险）                                                    | 不可逆/高风险动作（使用 `NvAlertDialog`）                     |
| `NvField` + 各部分                                            | 带标签和校验的表单字段                                                        | 简单行内输入                                                  |
| `NvFormSection`                                               | 表单分节（标题 + 描述 + 字段组）                                              | 单字段表单                                                    |
| `NvInput`                                                     | 文本输入                                                                      | 固定选项选择（使用 `NvSelect`）                               |
| `NvSelect` + 各部分                                           | 固定选项选择（选项 ≲15）                                                      | 大数据集搜索（使用 `NvSearchSelect`/`NvCombobox`）            |
| `NvSearchSelect` / `NvCombobox`                               | 可搜索选择（设备/技师/SKU 等主数据）                                          | 固定短列表（使用 `NvSelect`）                                 |
| `NvCheckbox` / `NvRadioGroup` / `NvSwitch`                    | 多选 / 互斥单选 / 即时生效开关                                                | 需提交才生效的开关（用表单 + 保存）                           |
| `NvTabs` + 各部分                                             | 详情对象内的同级区段                                                          | 应用主导航                                                    |
| `NvDatePicker` / `NvDateRangePicker` / `NvTimePicker`         | 业务日期/区间/时间选择                                                        | 特定时区（timezone）的时间戳                                  |
| `NvAreaChart` / `NvLineChart` / `NvBarChart` / `NvDonutChart` | 业务仪表板（语义图表令牌）                                                    | 一次性装饰性可视化                                            |
| `NvMetricCard` / `NvStatTile` / `NvSectionCard`               | 语义 KPI（见 list-workbench：只放帮助行动的指标）                             | 机械计数（本页 X 行）                                         |
| `NvDropdownMenu` + 各部分 / `NvRowActions`                    | 上下文行操作（高频动作行内直达，其余收菜单，见 interaction-patterns §2）      | 主导航                                                        |
| `NvPagination`                                                | 独立分页（`NvDataTable` 已内建）                                              | 客户端筛选列表                                                |
| `NvTimeline`                                                  | 审计/生命周期时间线                                                           | 平铺列表                                                      |
| `NvKanban`                                                    | 看板式任务分列                                                                | 普通列表页                                                    |
| `NvLoader`                                                    | 加载四形态（页面/区块/行内/按钮内建）                                         | —                                                             |
| `NvTooltip` + 各部分                                          | 纯图标按钮标签、状态说明                                                      | 长篇帮助文本（使用 `Popover`）                                |
| `NvNavigationMenu` / `NvAppHeader` / `NvPage*`                | 应用外壳与页面骨架                                                            | —                                                             |

**无 `Nv` 版的现役原版件**（Appendix A 未列品牌版，直接从 `@nerv-iip/ui` 用原名，
合法且过门禁）：`Alert` `Avatar` `Empty` `Skeleton` `Spinner` `Progress` `ScrollArea`
`Separator` `Toaster`/`toast` `Breadcrumb` `Popover` `FileUpload` 等。深路径、
`reka-ui`、`shadcn-vue` 直引仍然全部禁止。

还缺什么组件：先查 `component-coverage.md` 四场景矩阵的缺口列与
`components/install-backlog.md`，再按 `governance.md` 的选件阶梯决定装原版还是新建。

---

## 交互模式速查

| 场景                                                                            | 交互模式                            | 文件                                                                   |
| ------------------------------------------------------------------------------- | ----------------------------------- | ---------------------------------------------------------------------- |
| 表单承载/行操作/列表-详情/操作后引导/空态·批量·筛选 + PDA（W2/W3 交互验收依据） | 交互模式 v1                         | `patterns/interaction-patterns.md`                                     |
| 操作反馈：toast 与内联校验                                                      | 反馈与通知                          | `patterns/feedback-and-notifications.md`                               |
| Business Console 列表工作台基线                                                 | 列表工作台                          | `patterns/pages/list-workbench.md`                                     |
| 主数据六类页型模板                                                              | 主数据模板                          | `patterns/pages/master-data-templates.md`                              |
| 身份验证 / 登录                                                                 | 登录页                              | `patterns/pages/login-page.md`                                         |
| 带搜索 / 筛选的 CRUD 列表页                                                     | 列表页                              | `patterns/pages/list-page.md`                                          |
| 行内创建实体                                                                    | 新建对话框                          | `patterns/flows/create-dialog.md`                                      |
| 确认破坏性操作                                                                  | 破坏性操作确认                      | `patterns/flows/confirm-destroy.md`                                    |
| 应用框架（侧边栏 + 顶栏）                                                       | 应用外壳                            | `patterns/blocks/app-shell.md`                                         |
| 带标题和描述的页头                                                              | 页头                                | `patterns/blocks/page-header.md`                                       |
| 搜索 + 筛选 + 主操作栏                                                          | 工具栏                              | `patterns/blocks/toolbar.md`                                           |
| 带加载 / 空状态的数据表                                                         | 数据表                              | `patterns/blocks/data-table.md`                                        |
| 分页表格页脚                                                                    | 分页栏                              | `patterns/blocks/pagination-bar.md`                                    |
| 工单 / 资源排程可视化                                                           | GanttChart / ResourceSchedulerBoard | `components/gantt-chart.md` / `components/resource-scheduler-board.md` |

> **排程可视化组件**（工单甘特图 `GanttChart` / 资源甘特图 `ResourceSchedulerBoard`）来自独立包 **`@nerv-iip/scheduling`**（非 `@nerv-iip/ui`）：引擎无关契约 + DHTMLX 适配器（试用开发 / 正式手动分发），无本地引擎时优雅占位。组件契约见 `components/gantt-chart.md`、`components/resource-scheduler-board.md`；引擎接缝见包 `README.md`。

## 路线图

| 场景                                                | 文件                                             |
| --------------------------------------------------- | ------------------------------------------------ |
| Business Console 组件就绪（#143）                   | `roadmaps/business-console-readiness.md`         |
| Business Console MES PC 工作台                      | `roadmaps/business-console-mes-pc-workbench.md`  |
| UX 走查发现 console + PDA（#815 / A1 验收事实来源） | `roadmaps/2026-07-11-ux-walkthrough-findings.md` |

---

## 所有 AI 代理必须遵守的规则

1. **面向用户的文案优先**：页面服务于业务用户，而不是开发者。绝不得在 UI 文案中暴露演示（demo）/ 测试 / 脚手架 / 网关（gateway）/ 上下文（context）用语。
2. **导入边界**：只能使用裸 `@nerv-iip/ui` / `@nerv-iip/ui-mobile` 和 `Nv*` 品牌名称（无 Nv 版原版件见上节清单）。绝不得使用深路径，也不得直接导入 `reka-ui`、`shadcn-vue`。
3. **禁止原始调色板 CSS 类**：禁止 `bg-blue-600`、`text-gray-500`、`border-zinc-*`。应使用语义工具类（`bg-primary`、`text-muted-foreground`、`border-border`、`bg-brand`）。
4. **模板中禁止原始十六进制值**：使用 token 工具类。
5. **新组件中禁止 `--legacy-color-*`**。
6. **状态使用 `NvStatusBadge`/`NvStatusDot` 的 `tone`**（`success`/`warning`/`danger`/`info`/`neutral`）；绝不得手工拼装颜色。
7. **破坏性确认使用 `NvAlertDialog`**：绝不得使用 `window.confirm` 或普通 `NvDialog`。
8. **`<script setup lang="ts">`** 搭配 Composition API；不使用 Options API。
9. **图标规则**：默认 `size-4`；装饰性图标添加 `aria-hidden="true"`；纯图标按钮添加 `aria-label`。
10. **新组件**遵循 `governance.md` 中的选件阶梯与 DoD（选件阶梯 / 新组件六件套）。
11. **作用域 CSS 例外**：只有登录页（`login.vue`）可用 `<style scoped>` 实现流式 `clamp()` 标题。其他所有新组件只能使用 Tailwind 工具类。
12. **交互验收口径**：`patterns/interaction-patterns.md` 的"规则/判定/正例/反例"是评审打回依据，写页面前先过一遍对应章节。
