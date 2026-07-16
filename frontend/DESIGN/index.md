---
# Nerv-IIP Console — Design System
# AI coding agents: read this file first on any UI task.
---

## System Summary

A calm, professional enterprise control plane built on **Vue 3 + Tailwind CSS v4 +
shadcn-vue (reka-nova style)**, branded as **NvUI**. Four surfaces (PC / mobile PDA /
touch 一体机 / screen 大屏, ADR 0020) share one design philosophy with
surface-isolated tokens. `--primary` is **near-black** (dashboard-01 baseline);
brand blue is a **runtime-switchable emphasis accent** (`--nv-brand`); light + dark
are both first-class. All components live in `packages/ui` / `packages/ui-mobile`
and are consumed via the bare barrel with **`Nv*` brand names**. No decoration for
its own sake — the UI is always information-dense.

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

## User-Facing Copy Rule

Business pages are product surfaces for planners, operators, inspectors, warehouse users, buyers, accountants and managers. They are not implementation notes, test panels, seed-data viewers or PR acceptance evidence.

Visible page copy must help the user decide, act, or understand a business state. Do not put development, validation or scaffolding language in headings, descriptions, empty states, captions, badges, table summaries, form help, toasts or menus.

Forbidden examples in product UI:

1. `样例数据`, `内置样例`, `用于验证`, `便于联动测试`, `当前页面`, `demo`, `mock`, `fallback`, `seed`.
2. Technical ownership or gateway language such as `业务网关契约`, `接口`, `API`, `operationId`, `source service`, `organization`, `environment`, `context`.
3. Scenario disclaimers such as `汽车减振器制造场景下...用于验证...`. Industry context can shape the data and labels, but the page should speak as the user's live business system.

Allowed alternatives:

1. Use concise business nouns: `销售订单`, `采购订单`, `生产计划`, `工单`, `物料`, `工艺路线`, `应收`, `应付`, `成本归集`.
2. Use operational summaries: `今日待排产订单`, `待齐套工单`, `待检来料`, `本班待报工任务`.
3. Use empty-state guidance tied to the next action: `暂无待派工工单，请先确认生产计划并下达到车间。`

If data is demo-only or incomplete, keep that fact in developer docs, PR notes or test fixtures. Do not surface it in the application UI.

---

## Component Quick Reference（PC；名称即 `@nerv-iip/ui` 导出真名）

| Component                                                     | Use when                                                                           | Do NOT use when                                                      |
| ------------------------------------------------------------- | ---------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `NvButton`                                                    | Any clickable action（内建 `loading`；页面主 CTA 用 `variant="brand"`）            | Navigating to another route (use `RouterLink`)                       |
| `NvBadge`                                                     | Category chips, counts                                                             | 状态语义（use `NvStatusBadge`）                                      |
| `NvStatusBadge` / `NvStatusDot`                               | 业务状态呈现（`tone`: success/warning/danger/info/neutral，`value` 自动解析）      | 非状态的普通标签                                                     |
| `NvCard` + parts                                              | Grouped content sections, form cards                                               | Wrapping a data table                                                |
| `NvDataTable`                                                 | Tabular entity lists（内建 loading/empty/分页/`#cell-*` 插槽）                     | Single-item detail views (use `NvDescriptions`)                      |
| `NvDescriptions`                                              | 详情字段的键值对呈现                                                               | 可编辑表单 (use `NvField`)                                           |
| `NvPageHeader`                                                | 页头（标题 + 描述 + actions）                                                      | 卡片内小节标题                                                       |
| `NvToolbar` / `NvFilterBar`                                   | Search + filter + primary action bar                                               | 表单内部布局                                                         |
| `NvDialog` + parts                                            | ≤3 字段的轻量新建/编辑（见 interaction-patterns §1）                               | Destructive confirms (use `NvAlertDialog`)；4+ 字段（use `NvSheet`） |
| `NvSheet` + parts                                             | 保持列表上下文的详情/编辑侧滑                                                      | Full-page workflows                                                  |
| `NvAlertDialog` + parts                                       | Confirm irreversible actions                                                       | Informational prompts                                                |
| `NvPopconfirm`                                                | 行内轻量二次确认（低风险）                                                         | 不可逆/高风险动作 (use `NvAlertDialog`)                              |
| `NvField` + parts                                             | Form fields with label + validation                                                | Simple inline inputs                                                 |
| `NvFormSection`                                               | 表单分节（标题 + 描述 + 字段组）                                                   | 单字段表单                                                           |
| `NvInput`                                                     | Text entry                                                                         | Fixed-option selection (use `NvSelect`)                              |
| `NvSelect` + parts                                            | Fixed-option selection（选项 ≲15）                                                 | 大数据集搜索 (use `NvSearchSelect`/`NvCombobox`)                     |
| `NvSearchSelect` / `NvCombobox`                               | 可搜索选择（设备/技师/SKU 等主数据）                                               | 固定短列表 (use `NvSelect`)                                          |
| `NvCheckbox` / `NvRadioGroup` / `NvSwitch`                    | 多选 / 互斥单选 / 即时生效开关                                                     | 需提交才生效的开关（用表单 + 保存）                                  |
| `NvTabs` + parts                                              | Peer sections inside a detail object                                               | Primary app navigation                                               |
| `NvDatePicker` / `NvDateRangePicker` / `NvTimePicker`         | 业务日期/区间/时间选择                                                             | timezone-specific timestamps                                         |
| `NvAreaChart` / `NvLineChart` / `NvBarChart` / `NvDonutChart` | Business dashboards（语义图表令牌）                                                | Decorative one-off visualizations                                    |
| `NvMetricCard` / `NvStatTile` / `NvSectionCard`               | 语义 KPI（见 list-workbench：只放帮助行动的指标）                                  | 机械计数（本页 X 行）                                                |
| `NvDropdownMenu` + parts / `NvRowActions`                     | Contextual row actions（高频动作行内直达，其余收菜单，见 interaction-patterns §2） | Primary navigation                                                   |
| `NvPagination`                                                | 独立分页（`NvDataTable` 已内建）                                                   | Client-side filtered lists                                           |
| `NvTimeline`                                                  | 审计/生命周期时间线                                                                | 平铺列表                                                             |
| `NvKanban`                                                    | 看板式任务分列                                                                     | 普通列表页                                                           |
| `NvLoader`                                                    | 加载四形态（页面/区块/行内/按钮内建）                                              | —                                                                    |
| `NvTooltip` + parts                                           | Icon-only button labels, status descriptions                                       | Long-form help text (use `Popover`)                                  |
| `NvNavigationMenu` / `NvAppHeader` / `NvPage*`                | App shell 与页面骨架                                                               | —                                                                    |

**无 `Nv` 版的现役原版件**（Appendix A 未列品牌版，直接从 `@nerv-iip/ui` 用原名，
合法且过门禁）：`Alert` `Avatar` `Empty` `Skeleton` `Spinner` `Progress` `ScrollArea`
`Separator` `Toaster`/`toast` `Breadcrumb` `Popover` `FileUpload` 等。深路径、
`reka-ui`、`shadcn-vue` 直引仍然全部禁止。

还缺什么组件：先查 `component-coverage.md` 四场景矩阵的缺口列与
`components/install-backlog.md`，再按 `governance.md` 的选件阶梯决定装原版还是新建。

---

## Pattern Quick Reference

| Scenario                                                                        | Pattern                             | File                                                                   |
| ------------------------------------------------------------------------------- | ----------------------------------- | ---------------------------------------------------------------------- |
| 表单承载/行操作/列表-详情/操作后引导/空态·批量·筛选 + PDA（W2/W3 交互验收依据） | Interaction Patterns v1             | `patterns/interaction-patterns.md`                                     |
| 操作反馈：toast vs 内联校验                                                     | Feedback & Notifications            | `patterns/feedback-and-notifications.md`                               |
| Business Console 列表工作台基线                                                 | List Workbench                      | `patterns/pages/list-workbench.md`                                     |
| 主数据六类页型模板                                                              | Master Data Templates               | `patterns/pages/master-data-templates.md`                              |
| Authentication / sign in                                                        | Login Page                          | `patterns/pages/login-page.md`                                         |
| CRUD list page with search/filter                                               | List Page                           | `patterns/pages/list-page.md`                                          |
| Inline entity creation                                                          | Create Dialog                       | `patterns/flows/create-dialog.md`                                      |
| Confirm destructive action                                                      | Confirm Destroy                     | `patterns/flows/confirm-destroy.md`                                    |
| App chrome (sidebar + topbar)                                                   | App Shell                           | `patterns/blocks/app-shell.md`                                         |
| Page heading with title + description                                           | Page Header                         | `patterns/blocks/page-header.md`                                       |
| Search + filter + primary action bar                                            | Toolbar                             | `patterns/blocks/toolbar.md`                                           |
| Data table with loading/empty states                                            | Data Table                          | `patterns/blocks/data-table.md`                                        |
| Paginated table footer                                                          | Pagination Bar                      | `patterns/blocks/pagination-bar.md`                                    |
| 工单 / 资源排程可视化                                                           | GanttChart / ResourceSchedulerBoard | `components/gantt-chart.md` / `components/resource-scheduler-board.md` |

> **排程可视化组件**（工单甘特图 `GanttChart` / 资源甘特图 `ResourceSchedulerBoard`）来自独立包 **`@nerv-iip/scheduling`**（非 `@nerv-iip/ui`）：引擎无关契约 + DHTMLX 适配器（试用开发 / 正式手动分发），无本地引擎时优雅占位。组件契约见 `components/gantt-chart.md`、`components/resource-scheduler-board.md`；引擎接缝见包 `README.md`。

## Roadmaps

| Scenario                                            | File                                             |
| --------------------------------------------------- | ------------------------------------------------ |
| Business console component readiness (#143)         | `roadmaps/business-console-readiness.md`         |
| Business Console MES PC workbench                   | `roadmaps/business-console-mes-pc-workbench.md`  |
| UX 走查发现 console + PDA（#815 / A1 验收事实来源） | `roadmaps/2026-07-11-ux-walkthrough-findings.md` |

---

## Rules All AI Agents Must Follow

1. **User-facing copy first**: pages are for business users, not developers. Never expose demo/test/scaffolding/gateway/context wording in UI copy.
2. **Import boundary**: bare `@nerv-iip/ui` / `@nerv-iip/ui-mobile` only, `Nv*` brand names（无 Nv 版原版件见上节清单）。Never deep paths, `reka-ui`, `shadcn-vue`.
3. **No raw palette classes**: `bg-blue-600`, `text-gray-500`, `border-zinc-*` are forbidden. Use semantic utilities (`bg-primary`, `text-muted-foreground`, `border-border`, `bg-brand`).
4. **No raw hex in templates**: use token utilities.
5. **No `--legacy-color-*` in new components**.
6. **Status via `NvStatusBadge`/`NvStatusDot` `tone`** (`success`/`warning`/`danger`/`info`/`neutral`) — never handcraft colors.
7. **`NvAlertDialog` for destructive confirms**: never `window.confirm` or a plain `NvDialog`.
8. **`<script setup lang="ts">`** with Composition API — Options API is not used.
9. **Icon rules**: `size-4` default, `aria-hidden="true"` on decorative, `aria-label` on icon-only buttons.
10. **New components** follow the ladder + DoD in `governance.md`（选件阶梯 / 新组件六件套）。
11. **Scoped CSS exception**: only the login page (`login.vue`) uses `<style scoped>` for the fluid `clamp()` heading. All other new components use Tailwind utilities only.
12. **交互验收口径**：`patterns/interaction-patterns.md` 的"规则/判定/正例/反例"是评审打回依据，写页面前先过一遍对应章节。
