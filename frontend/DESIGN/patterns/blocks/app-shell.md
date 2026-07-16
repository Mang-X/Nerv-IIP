# Block: App Shell（应用外壳）

应用级 chrome，来自 `@nerv-iip/app-shell`。**`AppShellT`** 是现役 T 形外壳（FE-3）——
`apps/console` 的 `DefaultLayout.vue` 与 `apps/business-console` 的 `BusinessLayout.vue`
均已切换到它。旧的两级侧栏 **`AppShell`** 仅作迁移兼容保留（见文末"Legacy"）。

## 规则

1. **页面只填内容槽**：一切导航/顶栏/用户菜单由外壳承载，页面组件内不得内联 shell chrome。
2. **消费方 app 拥有导航模型与 RBAC 过滤**（如 business-console `src/navigation.ts`）以及
   路由→域的解析；外壳**不做任何权限强制**——`requiredPermissions` 只是消费方过滤的输入。
   Business Console 菜单分域见 `docs/architecture/frontend-navigation-map.md`。
3. **每个导航项必须带 `icon`**：顶部 `NavDomain` 与侧栏每个叶子 `NavLink` 都必须设置
   `icon`（@lucide/vue 组件）。侧栏 rail/折叠态只渲染图标；漏 icon 会退化成首字竖排
   （生 / 工 / 派 …），视觉即坏——**漏 icon 当 bug 处理，不是风格选择**（历史上最常被遗忘的导航缺陷）。
4. **icon 只用 app 内已 import 过的名字**（证明存在于锁定的 lucide 版本）——不要凭记忆猜
   改名后的图标名（如 `AlertTriangleIcon` / `CheckSquareIcon`）。快速盘点现有图标：
   `grep -rhoE '\b[A-Z][a-zA-Z0-9]+Icon\b' apps packages | sort -u`。
5. **不放业务逻辑进外壳**：`AppShellT` 纯结构；每页布局微调用槽位或另建 layout，不改外壳源码。
6. **单一外壳**：一个 app 只有一个 shell 实例，不加第二条侧栏。

## 判定

- 「这个入口是**能力域**（顶部）还是**域内页面**（侧栏叶子）？」——顶部放权限过滤后的能力域
  （溢出收进「更多」，`maxVisibleDomains` 默认 7）；左侧放当前域的 side nav。
- 「新增的每个 `NavDomain` / `NavLink` 都设了 `icon` 吗？」——折叠侧栏看一眼：出现首字
  glyph 即打回。
- 「权限过滤发生在消费方 `navigation.ts` 而不是指望外壳吗？」

## 正例

现网接入（两个 app 都是真实用法）：

- `apps/business-console/src/layouts/BusinessLayout.vue`（`<AppShellT>` 于 70–106 行，
  导航模型在 `src/navigation.ts`，含 RBAC 过滤与逐项 icon 的完整示范——`master-data`、
  `engineering` 两域可作照抄样板）。
- `apps/console/src/layouts/DefaultLayout.vue`（`<AppShellT>` 于 79–90 行）。

接口（导出自 `@nerv-iip/app-shell`，权威定义 `packages/app-shell/src/types.ts`）：

```ts
interface NavDomain { id: string, title: string, icon?: Component, to?: RouteLocationRaw, requiredPermissions?: string[] }
interface NavLink { title: string, to: RouteLocationRaw, icon?: Component, requiredPermissions?: string[] }
interface NavGroup { label?: string, items: NavLink[] }
type SideNav = NavGroup[]

// AppShellT props
{ title, topDomains: NavDomain[], currentDomainId?, sideNav?: SideNav,
  maxVisibleDomains? = 7, user?: ShellUser, signOutLabel?, searchLabel?, recent?, starred? }
// emits: signOut, openSearch   // slots: #header-actions, #default
```

结构：基于 FE-2 的 `NvAppShellInset`（dashboard-01 `variant="inset"`）。顶栏 = 权限过滤后的
能力域（溢出 →「更多」）；左侧 = 当前域 side nav；右上 = 命令搜索（⌘/Ctrl+K，FE-13 前为
占位）、`#header-actions` 槽（主题控件）与用户菜单。

<!-- 反例：暂无现网证据（两个 app 的现役 navigation.ts 均已逐项配 icon）；漏 icon 属高频回归项，发现即按规则 3 打回。 -->

---

## Legacy: AppShell（两级折叠侧栏，已废弃）

> **废弃声明**：`AppShell`（sidebar-07 折叠侧栏 + sidebar-01 顶栏组合）仍从
> `@nerv-iip/app-shell` 导出以保兼容，但**现网 app 已零使用**（两个 console 均用
> `AppShellT`）。新布局一律用 `AppShellT`；本节仅存档旧接口供读旧代码时查阅。

```ts
interface NavSubItem {
  title: string
  to: RouteLocationRaw
}
interface NavItem {
  title: string
  to?: RouteLocationRaw // 叶子项（直链）
  icon?: Component
  isActive?: boolean // 折叠组默认展开
  items?: NavSubItem[] // 组（有 items 即可折叠；组不带 to）
}
// props: { title, navItems: NavItem[], user?: { name, email? } }
// emits: signOut   // slots: #header（顶栏分隔线后的内容，如面包屑）、#default
```

- 布局：侧栏 16rem 展开 / 3rem 图标模式，状态持久化到 `localStorage` 键 `sidebar_state`；
  顶栏 `h-16` `border-b`（图标模式缩到 `h-12`）；内容区 `p-4`；移动端（<768px）侧栏隐藏、
  用 SidebarTrigger 唤出。
- 组件文件：`packages/app-shell/src/AppShell.vue` / `NavMain.vue` / `NavUser.vue`。
- 已知约束：`SidebarProvider` 自带 `TooltipProvider`，不要再包一层。
