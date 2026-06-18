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
  SidebarSeparator, SidebarInput, SidebarRail, SidebarTrigger, SidebarInset,
  TooltipProvider,
} from '@nerv-iip/ui'
import {
  LayoutDashboardIcon, BoxesIcon, ClipboardCheckIcon, WrenchIcon, SettingsIcon,
  FactoryIcon, ChevronRightIcon, ChevronsUpDownIcon, PlusIcon, BellIcon, SearchIcon,
  GaugeIcon, ActivityIcon,
} from 'lucide-vue-next'

// collapsible submenu open state (atomic Sidebar has no built-in accordion —
// drive SidebarMenuSub visibility with your own state)
const press = ref(true)
const weld = ref(false)
</script>

# Sidebar 侧栏

可折叠的控制台侧栏**原子件**：`SidebarProvider` 提供上下文，`Sidebar` 承载分组导航，配合 `SidebarRail` / `SidebarTrigger` 折叠。需要开箱即用的整页外壳时用 [AppShellInset](/components/desktop/dashboard)，需要自定义结构时用这些原子件自行拼装。

## 完整控制台侧栏

工作区品牌头 + 分组导航（计数徽标、在线状态点、激活强调条）+ 用户区，`collapsible="icon"` 折叠为图标条：折叠后标题文本隐藏（非压缩），悬停图标显示 Tooltip。拖右缘 Rail 或点顶部按钮切换。

<Demo>
<div class="ds-sb ds-sb-collapse ds-sb-tall">
  <TooltipProvider :delay-duration="0">
  <SidebarProvider>
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <button type="button" class="ds-sb-brand group-data-[collapsible=icon]:justify-center">
          <span class="ds-sb-logo">N</span>
          <span class="ds-sb-brand-text group-data-[collapsible=icon]:hidden">
            <span class="ds-sb-brand-name">Nerv-IIP</span>
            <span class="ds-sb-brand-sub">总装一厂 · 早班</span>
          </span>
          <ChevronsUpDownIcon class="ds-sb-brand-caret group-data-[collapsible=icon]:hidden" />
        </button>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>生产</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem><SidebarMenuButton :is-active="true" tooltip="总览"><LayoutDashboardIcon /><span>总览</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem>
              <SidebarMenuButton tooltip="工单"><BoxesIcon /><span>工单</span></SidebarMenuButton>
              <SidebarMenuBadge>24</SidebarMenuBadge>
            </SidebarMenuItem>
            <SidebarMenuItem>
              <SidebarMenuButton tooltip="质检"><ClipboardCheckIcon /><span>质检</span></SidebarMenuButton>
              <SidebarMenuBadge>3</SidebarMenuBadge>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
        <SidebarGroup>
          <SidebarGroupLabel>资源</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton tooltip="设备 · 在线">
                <WrenchIcon /><span>设备</span>
                <span class="ds-sb-dot ds-sb-dot-ok" aria-hidden="true" />
              </SidebarMenuButton>
            </SidebarMenuItem>
            <SidebarMenuItem>
              <SidebarMenuButton tooltip="告警 · 2 条未读">
                <BellIcon /><span>告警</span>
                <span class="ds-sb-dot ds-sb-dot-warn" aria-hidden="true" />
              </SidebarMenuButton>
            </SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton tooltip="设置"><SettingsIcon /><span>设置</span></SidebarMenuButton></SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" tooltip="张伟 · 班长">
              <span class="ds-sb-avatar">张<span class="ds-sb-avatar-status" aria-hidden="true" /></span>
              <span class="ds-sb-user group-data-[collapsible=icon]:hidden">
                <span class="ds-sb-user-name">张伟</span>
                <span class="ds-sb-user-role">班长 · 早班</span>
              </span>
              <ChevronsUpDownIcon class="ds-sb-brand-caret group-data-[collapsible=icon]:hidden" />
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
    <SidebarInset class="ds-sb-inset">
      <header class="ds-sb-topbar">
        <SidebarTrigger />
        <span class="ds-sb-topbar-divider" aria-hidden="true" />
        <span class="text-sm text-muted-foreground">控制台</span>
        <ChevronRightIcon class="size-3.5 text-muted-foreground/50" />
        <span class="text-sm font-medium">工单总览</span>
      </header>
      <div class="ds-sb-body">
        <div class="ds-sb-tiles">
          <div class="ds-sb-tile"><span class="ds-sb-tile-label">今日产出</span><span class="ds-sb-tile-value">412 <small>件</small></span></div>
          <div class="ds-sb-tile"><span class="ds-sb-tile-label">在制工单</span><span class="ds-sb-tile-value">18</span></div>
          <div class="ds-sb-tile"><span class="ds-sb-tile-label">设备 OEE</span><span class="ds-sb-tile-value ds-sb-tile-ok">78.6 <small>%</small></span></div>
        </div>
      </div>
    </SidebarInset>
  </SidebarProvider>
  </TooltipProvider>
</div>
</Demo>

```vue
<Sidebar collapsible="icon">
  <SidebarHeader><!-- 工作区：logo + 名称 + 厂区，折叠时隐藏文字 --></SidebarHeader>
  <SidebarContent>
    <SidebarGroup>
      <SidebarGroupLabel>生产</SidebarGroupLabel>
      <SidebarMenu>
        <SidebarMenuItem>
          <SidebarMenuButton :is-active="true" tooltip="总览">
            <LayoutDashboardIcon /><span>总览</span>
          </SidebarMenuButton>
        </SidebarMenuItem>
        <SidebarMenuItem>
          <SidebarMenuButton tooltip="工单"><BoxesIcon /><span>工单</span></SidebarMenuButton>
          <SidebarMenuBadge>24</SidebarMenuBadge>
        </SidebarMenuItem>
      </SidebarMenu>
    </SidebarGroup>
  </SidebarContent>
  <SidebarFooter><!-- 用户：头像 + 在线点 + 姓名/角色 --></SidebarFooter>
  <SidebarRail />
</Sidebar>
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
      <span class="ds-sb-hint"><ActivityIcon class="size-4" />点击车间展开 / 收起二级产线</span>
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
          <SidebarGroupLabel>最近</SidebarGroupLabel>
          <SidebarMenu>
            <SidebarMenuItem><SidebarMenuButton><BoxesIcon /><span>WO-2406-0413</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton><BoxesIcon /><span>WO-2406-0421</span></SidebarMenuButton></SidebarMenuItem>
            <SidebarMenuItem><SidebarMenuButton><WrenchIcon /><span>工位 CNC-07</span></SidebarMenuButton></SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
    </Sidebar>
    <main class="ds-sb-main">
      <span class="ds-sb-hint"><SearchIcon class="size-4" />输入即过滤工单 / 工位</span>
    </main>
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
.ds-sb.ds-sb-tall { height: 460px; }
.ds-sb [data-slot='sidebar-wrapper'],
.ds-sb .group\/sidebar-wrapper { min-height: 0 !important; height: 100% !important; }
/* only the collapsible="icon" demo uses the Sidebar's fixed `h-svh` container, so
   only it needs a containing block (transform) + height cap to stay in-frame. */
.ds-sb-collapse { transform: translateZ(0); }
.ds-sb-collapse [data-slot='sidebar-container'] { height: 100% !important; }

/* placeholder canvas next to a non-collapsible sidebar */
.ds-sb-main {
  display: flex;
  flex: 1 1 0;
  min-width: 0;
  align-items: center;
  justify-content: center;
  padding: 1rem;
}
.ds-sb-hint {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--muted-foreground);
}

/* ── workspace brand lockup ─────────────────────────────────────────────── */
.ds-sb-brand {
  display: flex;
  width: 100%;
  align-items: center;
  gap: 0.625rem;
  border-radius: 0.625rem;
  padding: 0.375rem 0.375rem;
  text-align: left;
  transition: background-color 0.15s ease;
}
.ds-sb-brand:hover { background: var(--sidebar-accent, var(--muted)); }
.ds-sb-logo {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  flex-shrink: 0;
  border-radius: 0.5rem;
  background: linear-gradient(140deg, var(--brand), color-mix(in oklch, var(--brand) 70%, black));
  color: var(--brand-foreground);
  font-size: 0.875rem;
  font-weight: 700;
  box-shadow: inset 0 1px 0 0 color-mix(in oklch, white 22%, transparent);
}
.ds-sb-brand-text { display: flex; min-width: 0; flex: 1; flex-direction: column; line-height: 1.2; }
.ds-sb-brand-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.8125rem; font-weight: 600; }
.ds-sb-brand-sub { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.6875rem; color: var(--muted-foreground); }
.ds-sb-brand-caret { width: 1rem; height: 1rem; flex-shrink: 0; color: var(--muted-foreground); }

/* ── active accent bar on the current item ─────────────────────────────── */
.ds-sb [data-slot='sidebar-menu-button'][data-active='true'] { position: relative; font-weight: 600; }
.ds-sb [data-slot='sidebar-menu-button'][data-active='true']::before {
  content: '';
  position: absolute;
  left: 0;
  top: 50%;
  height: 1.05rem;
  width: 3px;
  transform: translateY(-50%);
  border-radius: 0 3px 3px 0;
  background: var(--brand);
}

/* ── status dots trailing a menu label ─────────────────────────────────── */
.ds-sb-dot {
  margin-left: auto;
  width: 7px;
  height: 7px;
  flex-shrink: 0;
  border-radius: 9999px;
}
.ds-sb-dot-ok {
  background: oklch(0.72 0.17 152);
  box-shadow: 0 0 0 3px color-mix(in oklch, oklch(0.72 0.17 152) 16%, transparent);
}
.ds-sb-dot-warn {
  background: oklch(0.78 0.16 75);
  box-shadow: 0 0 0 3px color-mix(in oklch, oklch(0.78 0.16 75) 16%, transparent);
}

/* ── footer user (avatar + online status) ──────────────────────────────── */
.ds-sb-avatar {
  position: relative;
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  flex-shrink: 0;
  border-radius: 9999px;
  background: var(--muted);
  font-size: 0.75rem;
  font-weight: 600;
}
.ds-sb-avatar-status {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 0.625rem;
  height: 0.625rem;
  border-radius: 9999px;
  background: oklch(0.72 0.17 152);
  border: 2px solid var(--sidebar, var(--background));
}
.ds-sb-user { display: flex; min-width: 0; flex: 1; flex-direction: column; line-height: 1.2; }
.ds-sb-user-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.8125rem; font-weight: 600; }
.ds-sb-user-role { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.6875rem; color: var(--muted-foreground); }

/* ── inset canvas (top bar + KPI tiles) ────────────────────────────────── */
.ds-sb-inset { display: flex; min-width: 0; flex: 1; flex-direction: column; }
.ds-sb-topbar {
  display: flex;
  height: 3.25rem;
  flex-shrink: 0;
  align-items: center;
  gap: 0.5rem;
  border-bottom: 1px solid var(--border);
  padding-inline: 0.875rem;
}
.ds-sb-topbar-divider { width: 1px; height: 1rem; background: var(--border); margin-inline: 0.125rem; }
.ds-sb-body { flex: 1; min-height: 0; padding: 1rem; }
.ds-sb-tiles { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0.625rem; }
.ds-sb-tile {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  border-radius: 0.75rem;
  border: 1px solid var(--border);
  background: var(--card);
  padding: 0.75rem;
}
.ds-sb-tile-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.6875rem; color: var(--muted-foreground); }
.ds-sb-tile-value { white-space: nowrap; font-size: 1.25rem; font-weight: 600; font-variant-numeric: tabular-nums; line-height: 1; }
.ds-sb-tile-value small { font-size: 0.75rem; font-weight: 500; color: var(--muted-foreground); }
.ds-sb-tile-ok { color: oklch(0.74 0.16 152); }

.ds-sb-chevron {
  width: 1rem;
  height: 1rem;
  transition: transform 0.2s var(--ease-out-quart, cubic-bezier(0.25, 1, 0.5, 1));
}
@media (prefers-reduced-motion: reduce) {
  .ds-sb-chevron { transition: none; }
}
</style>
