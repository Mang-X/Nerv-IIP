---
title: Sidebar 侧栏
---

<script setup>
import { ref } from 'vue'
import {
  SidebarProvider, Sidebar, SidebarHeader, SidebarContent, SidebarFooter,
  SidebarGroup, SidebarGroupLabel, SidebarGroupContent, SidebarGroupAction,
  SidebarMenu, SidebarMenuItem, SidebarMenuButton, SidebarMenuAction, SidebarMenuBadge,
  SidebarMenuSub, SidebarMenuSubItem, SidebarMenuSubButton,
  SidebarSeparator, SidebarInput, SidebarRail, SidebarTrigger,
  TooltipProvider,
} from '@nerv-iip/ui'
import {
  LayoutDashboardIcon, BoxesIcon, ClipboardCheckIcon, WrenchIcon, SettingsIcon,
  FactoryIcon, ChevronRightIcon, PlusIcon, BellIcon, SearchIcon, GaugeIcon,
} from 'lucide-vue-next'

// collapsible submenu open state (atomic Sidebar has no built-in accordion —
// drive SidebarMenuSub visibility with your own state)
const press = ref(true)
const weld = ref(false)
</script>

# Sidebar 侧栏

可折叠的控制台侧栏**原子件**：`SidebarProvider` 提供上下文，`Sidebar` 承载分组导航，配合 `SidebarRail` / `SidebarTrigger` 折叠。需要开箱即用的整页外壳时用 [AppShellInset](/components/desktop/dashboard)，需要自定义结构时用这些原子件自行拼装。

## 完整侧栏

品牌头 + 分组菜单（激活态）+ 底部用户，`collapsible="icon"` 折叠为图标条；折叠后悬停图标显示 Tooltip，标题文本隐藏而非压缩。点击底部折叠按钮或拖拽右侧 Rail 切换。

<Demo>
<div class="ds-sb ds-sb-collapse">
  <TooltipProvider :delay-duration="0">
  <SidebarProvider>
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <div class="flex items-center gap-2 px-1 py-1 group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:px-0">
          <div class="flex size-7 shrink-0 items-center justify-center rounded-md bg-brand text-sm font-bold text-brand-foreground">N</div>
          <span class="truncate text-sm font-semibold group-data-[collapsible=icon]:hidden">Nerv-IIP</span>
        </div>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>生产</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem><SidebarMenuButton :is-active="true" tooltip="总览"><LayoutDashboardIcon /><span>总览</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton tooltip="工单"><BoxesIcon /><span>工单</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton tooltip="质检"><ClipboardCheckIcon /><span>质检</span></SidebarMenuButton></SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
        <SidebarGroup>
          <SidebarGroupLabel>资源</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem><SidebarMenuButton tooltip="设备"><WrenchIcon /><span>设备</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton tooltip="设置"><SettingsIcon /><span>设置</span></SidebarMenuButton></SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton tooltip="班长 · 张伟">
              <div class="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted text-[11px] font-medium">张</div>
              <span>班长 · 张伟</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
    <main class="ds-sb-main">
      <SidebarTrigger />
      <span class="text-sm text-muted-foreground">点击折叠按钮在 展开 / 图标 间切换</span>
    </main>
  </SidebarProvider>
  </TooltipProvider>
</div>
</Demo>

```vue
<SidebarProvider>
  <Sidebar collapsible="icon">
    <SidebarHeader><!-- 品牌，折叠时隐藏文字 --></SidebarHeader>
    <SidebarContent>
      <SidebarGroup>
        <SidebarGroupLabel>生产</SidebarGroupLabel>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton :is-active="true" tooltip="总览">
              <LayoutDashboardIcon /><span>总览</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarGroup>
    </SidebarContent>
    <SidebarFooter><!-- 用户 --></SidebarFooter>
    <SidebarRail />
  </Sidebar>
  <main><SidebarTrigger /> …</main>
</SidebarProvider>
```

## 子菜单 · 徽标 · 操作

`SidebarMenuSub` 承载二级菜单（展开状态用你自己的 `ref` 驱动）；`SidebarMenuBadge` 显示计数；`SidebarMenuAction` 是仅在悬停时浮现的行内操作；`SidebarGroupAction` 是分组级操作。

<Demo>
<div class="ds-sb">
  <SidebarProvider>
    <Sidebar collapsible="none">
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>车间</SidebarGroupLabel>
          <SidebarGroupAction title="新增车间"><PlusIcon /></SidebarGroupAction>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton @click="press = !press">
                <FactoryIcon /><span>冲压车间</span>
                <ChevronRightIcon class="ds-sb-chevron ml-auto" :class="press && 'rotate-90'" />
              </SidebarMenuButton>
              <SidebarMenuSub v-show="press">
                <SidebarMenuSubItem><SidebarMenuSubButton :is-active="true">L01 产线</SidebarMenuSubButton></SidebarMenuSubItem>
                <SidebarMenuSubItem><SidebarMenuSubButton>L02 产线</SidebarMenuSubButton></SidebarMenuSubItem>
                <SidebarMenuSubItem><SidebarMenuSubButton>L03 产线</SidebarMenuSubButton></SidebarMenuSubItem>
              </SidebarMenuSub>
            </SidebarMenuItem>
            <SidebarMenuItem>
              <SidebarMenuButton @click="weld = !weld">
                <FactoryIcon /><span>焊接车间</span>
                <ChevronRightIcon class="ds-sb-chevron ml-auto" :class="weld && 'rotate-90'" />
              </SidebarMenuButton>
              <SidebarMenuSub v-show="weld">
                <SidebarMenuSubItem><SidebarMenuSubButton>W01 工位</SidebarMenuSubButton></SidebarMenuSubItem>
                <SidebarMenuSubItem><SidebarMenuSubButton>W02 工位</SidebarMenuSubButton></SidebarMenuSubItem>
              </SidebarMenuSub>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
        <SidebarSeparator />
        <SidebarGroup>
          <SidebarGroupLabel>待办</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton><BoxesIcon /><span>待派工单</span></SidebarMenuButton>
              <SidebarMenuBadge>12</SidebarMenuBadge>
            </SidebarMenuItem>
            <SidebarMenuItem>
              <SidebarMenuButton><GaugeIcon /><span>OEE 异常</span></SidebarMenuButton>
              <SidebarMenuBadge>3</SidebarMenuBadge>
            </SidebarMenuItem>
            <SidebarMenuItem>
              <SidebarMenuButton><BellIcon /><span>设备告警</span></SidebarMenuButton>
              <SidebarMenuAction title="全部标记已读" show-on-hover><PlusIcon /></SidebarMenuAction>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
    </Sidebar>
    <main class="ds-sb-main">
      <span class="text-sm text-muted-foreground">点击车间展开 / 收起二级产线</span>
    </main>
  </SidebarProvider>
</div>
</Demo>

```vue
<SidebarMenuItem>
  <SidebarMenuButton @click="open = !open">
    <FactoryIcon /><span>冲压车间</span>
    <ChevronRightIcon class="ml-auto transition-transform" :class="open && 'rotate-90'" />
  </SidebarMenuButton>
  <SidebarMenuSub v-show="open">
    <SidebarMenuSubItem><SidebarMenuSubButton>L01 产线</SidebarMenuSubButton></SidebarMenuSubItem>
  </SidebarMenuSub>
</SidebarMenuItem>

<SidebarMenuItem>
  <SidebarMenuButton><BoxesIcon /><span>待派工单</span></SidebarMenuButton>
  <SidebarMenuBadge>12</SidebarMenuBadge>
</SidebarMenuItem>
```

## 搜索头

`SidebarInput` 是侧栏内的输入框样式，常置于 `SidebarHeader` 做快速过滤。

<Demo>
<div class="ds-sb ds-sb-short">
  <SidebarProvider>
    <Sidebar collapsible="none">
      <SidebarHeader>
        <div class="relative">
          <SearchIcon class="pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2 text-muted-foreground" />
          <SidebarInput placeholder="搜索工单 / 工位…" class="pl-8" />
        </div>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarMenu>
            <SidebarMenuItem><SidebarMenuButton><BoxesIcon /><span>WO-2406-0413</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton><BoxesIcon /><span>WO-2406-0421</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton><WrenchIcon /><span>工位 CNC-07</span></SidebarMenuButton></SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
    </Sidebar>
    <main class="ds-sb-main"></main>
  </SidebarProvider>
</div>
</Demo>

## 组成

- `SidebarProvider` — 提供开合状态（`useSidebar()`）、快捷键与 Cookie 持久化；外层容器。
- `Sidebar` — 侧栏本体；`side` 左/右、`variant` `sidebar`/`floating`/`inset`、`collapsible` `offcanvas`/`icon`/`none`。
- `SidebarHeader` / `SidebarFooter` — 顶部 / 底部固定区。
- `SidebarContent` — 可滚动主体。
- `SidebarGroup` / `SidebarGroupLabel` / `SidebarGroupContent` / `SidebarGroupAction` — 分组、分组标题与分组级操作。
- `SidebarMenu` / `SidebarMenuItem` / `SidebarMenuButton` — 列表、单项与可点按钮（`isActive`、`tooltip` 折叠时悬浮显示、`as` 可换 `a` / router-link）。
- `SidebarMenuAction` / `SidebarMenuBadge` — 行内操作（`showOnHover`）与计数徽标。
- `SidebarMenuSub` / `SidebarMenuSubItem` / `SidebarMenuSubButton` — 二级菜单（展开状态自管理）。
- `SidebarSeparator` / `SidebarInput` — 分隔线与侧栏输入框。
- `SidebarRail` — 右缘可点/可拖的折叠条；`SidebarTrigger` — 折叠按钮（放任意处）。

## 属性

| 组件 | 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `Sidebar` | `side` | 停靠侧 | `'left' \| 'right'` | `'left'` |
| `Sidebar` | `variant` | 形态 | `'sidebar' \| 'floating' \| 'inset'` | `'sidebar'` |
| `Sidebar` | `collapsible` | 折叠方式 | `'offcanvas' \| 'icon' \| 'none'` | `'offcanvas'` |
| `SidebarMenuButton` | `isActive` | 激活态 | `boolean` | `false` |
| `SidebarMenuButton` | `tooltip` | 折叠时悬浮标签 | `string \| Component` | — |
| `SidebarMenuButton` | `size` | 尺寸 | `'default' \| 'sm' \| 'lg'` | `'default'` |
| `SidebarMenuAction` | `showOnHover` | 仅悬停浮现 | `boolean` | `false` |
| `SidebarProvider` | `defaultOpen` | 初始展开 | `boolean` | `true` |

> 折叠为图标条时，`SidebarMenuButton` 会自动只留图标；放在 Header / Footer 的**自定义内容**需自行加 `group-data-[collapsible=icon]:hidden` 隐藏文字，避免被压缩。

<style>
/* 有界预览框收纳整页外壳。 */
.ds-sb {
  height: 400px;
  overflow: hidden;
  border-radius: 12px;
  border: 1px solid var(--border);
  background: var(--background);
}
.ds-sb.ds-sb-short { height: 300px; }
.ds-sb [data-slot='sidebar-wrapper'],
.ds-sb .group\/sidebar-wrapper { min-height: 0 !important; height: 100% !important; }
/* only the collapsible="icon" demo uses the Sidebar's fixed `h-svh` container, so
   only it needs a containing block (transform) + height cap to stay in-frame. */
.ds-sb-collapse { transform: translateZ(0); }
.ds-sb-collapse [data-slot='sidebar-container'] { height: 100% !important; }
.ds-sb-main {
  display: flex;
  flex: 1 1 0;
  min-width: 0;
  align-items: flex-start;
  gap: 0.5rem;
  padding: 0.875rem 1rem;
}
.ds-sb-chevron {
  width: 1rem;
  height: 1rem;
  transition: transform 0.2s var(--ease-out-quart, cubic-bezier(0.25, 1, 0.5, 1));
}
@media (prefers-reduced-motion: reduce) {
  .ds-sb-chevron { transition: none; }
}
</style>
