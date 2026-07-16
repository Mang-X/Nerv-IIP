# Block Library v2（FE-2 区块库）

`@nerv-iip/ui` 里复制重建的可复用区块组件（`packages/ui/src/components/blocks/`）。
它们组合零改动的原版 shadcn-vue primitives + FE-1 token，**绝不修改任何原版件**。
一律从稳定边界 `@nerv-iip/ui` 导入；页面必须用区块拼装，不得内联同等布局。

> 命名沿革：本库最初以无前缀名交付（`Toolbar`、`PageHeader`…），#787/#789 统一改为
> `Nv*` 品牌名。历史文档/评审记录里的无前缀区块名即指下列 `Nv*` 件。
> 其中两件已被 pc 层取代（见「已被取代的区块」），不要再找旧名。

## 规则

1. **区块优先**：页面出现「页头/工具条/KPI 卡/行操作菜单」时导入区块，不内联布局。
2. **只用语义 token 类**：所有区块自动支持亮/暗 + 动态 `--brand` 强调色；扩展区块时同样
   只允许语义 token，不写死颜色。
3. **不改原版**：要改区块观感，调整区块自身或 token，绝不动 `components/ui/` 原版件。
4. **`NvDataTable` 不承载业务/取数逻辑**：表格只渲染行 + 派发排序/分页事件，数据归页面。
5. 新增/改动区块同步文档站对应页（`frontend/apps/design-system/docs/`），并过包内
   contract tests（`nvui-naming` / `blocks` 等）。

## 判定

- 「这段模板是不是在手搓某个区块已覆盖的形态（裸 `<table>`、裸页头、裸 metric `<div>`、
  自制下拉行菜单）？」是 → 换区块（gold-standard contract test 会拦裸 `<Table>`）。
- 「要的能力区块缺吗？」→ 先补区块（或走 app 侧反哺上提，见 `packages/ui/AGENTS.md`），
  不要在页面里长出一次性实现。

## 区块清单（现役导出名）

### NvAppShellInset

dashboard-01 `variant="inset"` 浮动面板外壳骨架（导航属 FE-3 的 `AppShellT`，见
`blocks/app-shell.md`）。

| Slot             | 用途                                        |
| ---------------- | ------------------------------------------- |
| `sidebar-header` | 侧栏品牌/Logo 行                            |
| `sidebar`        | 侧栏导航内容（`SidebarGroup`/`SidebarMenu`) |
| `sidebar-footer` | 用户/页脚区                                 |
| `header`         | 顶栏内容（内建 `SidebarTrigger` 旁）        |
| _default_        | 主内容（带 padding，`gap-4 md:gap-6`）      |

Prop：`collapsible`（`'offcanvas' | 'icon' | 'none'`，默认 `'icon'`）。

### NvPageHeader

紧凑的「面包屑即标题」页头（面包屑 + count + 行内动作），取代旧的大标题+描述块。
sticky（`top-14`）贴在内容区顶部。

| Prop          | 类型                 | 说明                                       |
| ------------- | -------------------- | ------------------------------------------ |
| `title`       | `string`             | 渲染为最后一级（当前）面包屑               |
| `breadcrumbs` | `{ label, href? }[]` | 祖先层级；SPA 链接用 `#breadcrumbs` 槽覆盖 |
| `count`       | `number \| string`   | 标题旁的弱化计数                           |

Slots：`actions`（右对齐）、`breadcrumbs`（覆盖面包屑）。详见 `blocks/page-header.md`。

### NvSectionCard / NvSectionCards

渐变 KPI 卡（`bg-gradient-to-t from-primary/5 to-card`）：description → 大号
`tabular-nums` 值 → 趋势 pill → 脚注。`NvSectionCards` 是响应式网格。

`NvSectionCard` props：`description`、`value`、
`trend?: { value, direction?: 'up'|'down'|'flat' }`、`footnote?`、`hint?`。
趋势色：up→success、down→destructive、flat→muted。`NvSectionCards` prop：`columns?`
（2–4，默认 4）。何时该放 KPI 卡的判定见 `pages/master-data-templates.md` §0
（默认不放，只放能驱动决策的业务指标）。

### NvToolbar

单行 筛选/动作 条。内建搜索（`v-model:search`）+ `#filters` + `#actions` 槽。
Props：`showSearch`、`searchPlaceholder`、`searchLabel`。搜索框 `sm:max-w-xs`，
动作靠右。详见 `blocks/toolbar.md`。

### NvRowActions

行操作 `MoreHorizontal` ghost 触发器 + 下拉菜单；默认槽放 `NvDropdownMenuItem`。
Props：`disabled?`、`label?`、`align?`、`contentClass?`。哪些动作可进菜单/必须提行内，
见 `interaction-patterns.md` §2（行操作分级）。

### NvThemeToggle / NvThemePicker

`NvThemeToggle` —— 亮/暗切换（`useColorMode`）。`NvThemePicker` —— 运行时强调色选择器，
基于 `ACCENT_PRESETS`（`useThemeAccent`，写 `--brand`）。均为 app 顶栏图标按钮。

## 已被取代的区块（废弃指引）

| 旧区块（FE-2 首发）                 | 现役替代（pc 层）                                                                                                                                                                   |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DataTable` + `DataTablePagination` | **`NvDataTable`**（完整表格体验，内建工具条/选择/分页；服务端分页走 `manual` 模式）。非表格分页面用独立 **`NvPagination`**。见 `blocks/data-table.md`、`blocks/pagination-bar.md`。 |
| `StatusBadge`                       | **`NvStatusBadge`**（ADR 0020 §1.3；#789 收口时移除区块版）。`resolveStatus()` 与状态类型仍从 blocks 导出——它们是共享工具，不是组件。                                               |

## 正例

现网全量在用：`NvPageHeader` 101 个文件、`NvToolbar` 81、`NvSectionCard` 61、
`NvRowActions` 22（`grep -rl` 于 `frontend/apps`，2026-07）。集中演示页：
`apps/business-console/src/pages/design-system/blocks.vue`（`/design-system/blocks`，
全区块同屏渲染供目检）。黄金标准列表页拼装示范：
`apps/business-console/src/pages/mes/operation-tasks.vue`。

<!-- 反例：本文各"规则"的违规形态（手搓裸表/裸页头等）由 goldStandardPages.contract.test.ts 机器拦截，现网黄金标准页无存量违例可引。 -->
