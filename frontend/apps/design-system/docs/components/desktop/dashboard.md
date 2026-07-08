---
title: NvAppShellInset 应用外壳
---

<script setup>
import {
  NvAppShellInset,
  SidebarFooter, SidebarGroup, SidebarGroupLabel, SidebarMenu, SidebarMenuItem, SidebarMenuButton,
  NvSidebarBrand, NvSidebarDot, NvSidebarUser,
  Breadcrumb, BreadcrumbList, BreadcrumbItem, BreadcrumbLink, BreadcrumbPage, BreadcrumbSeparator,
} from '@nerv-iip/ui'
import { LayoutDashboardIcon, BoxesIcon, ClipboardCheckIcon, WrenchIcon, SettingsIcon } from 'lucide-vue-next'
</script>

# NvAppShellInset 应用外壳

控制台页面的标准外壳 **block**:组合 `@nerv-iip/ui` 稳定导出的 Sidebar 系统(可折叠 / Rail / 移动抽屉)+ inset 内容区 + 顶部栏。搭一个控制台页面从它起步,而不是手拼分区件。侧栏导航用 `SidebarGroup` / `SidebarMenu` / `SidebarMenuButton` 组织。

## 完整外壳

<Demo>
<div class="ds-shell-demo w-full">
  <NvAppShellInset collapsible="icon">
    <template #sidebar-header>
      <NvSidebarBrand name="Nerv-IIP" sub="总装一厂 · 早班" logo="N" />
    </template>
    <template #sidebar>
      <SidebarGroup>
        <SidebarGroupLabel>生产</SidebarGroupLabel>
        <SidebarMenu>
          <SidebarMenuItem><SidebarMenuButton :is-active="true"><LayoutDashboardIcon /><span>总览</span></SidebarMenuButton></SidebarMenuItem>
          <SidebarMenuItem><SidebarMenuButton><BoxesIcon /><span>工单</span></SidebarMenuButton></SidebarMenuItem>
          <SidebarMenuItem><SidebarMenuButton><ClipboardCheckIcon /><span>质检</span></SidebarMenuButton></SidebarMenuItem>
        </SidebarMenu>
      </SidebarGroup>
      <SidebarGroup>
        <SidebarGroupLabel>资源</SidebarGroupLabel>
        <SidebarMenu>
          <SidebarMenuItem><SidebarMenuButton><WrenchIcon /><span>设备</span><NvSidebarDot tone="ok" /></SidebarMenuButton></SidebarMenuItem>
          <SidebarMenuItem><SidebarMenuButton><SettingsIcon /><span>设置</span></SidebarMenuButton></SidebarMenuItem>
        </SidebarMenu>
      </SidebarGroup>
    </template>
    <template #sidebar-footer>
      <SidebarMenu>
        <SidebarMenuItem>
          <SidebarMenuButton size="lg">
            <NvSidebarUser name="张伟" role="班长 · 早班" initials="张" />
          </SidebarMenuButton>
        </SidebarMenuItem>
      </SidebarMenu>
    </template>
    <template #header>
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem><BreadcrumbLink href="#">控制台</BreadcrumbLink></BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem><BreadcrumbPage>工单总览</BreadcrumbPage></BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
    </template>
    <div class="grid gap-4 sm:grid-cols-3">
      <div class="rounded-xl border border-border bg-card p-4"><div class="text-xs text-muted-foreground">今日产出</div><div class="mt-1 text-2xl font-semibold tabular-nums">412 件</div></div>
      <div class="rounded-xl border border-border bg-card p-4"><div class="text-xs text-muted-foreground">在制工单</div><div class="mt-1 text-2xl font-semibold tabular-nums">18</div></div>
      <div class="rounded-xl border border-border bg-card p-4"><div class="text-xs text-muted-foreground">设备 OEE</div><div class="mt-1 text-2xl font-semibold tabular-nums">78.6%</div></div>
    </div>
  </NvAppShellInset>
</div>
</Demo>

<style>
/* 文档内把全屏外壳收进一个有界容器预览（真实使用时占满视口）。
   transform 让本容器成为 fixed 定位的包含块——Sidebar 的 `position:fixed` 容器
   于是锚定到这个预览框而非视口，icon 折叠交互即可在文档内正常工作。 */
.ds-shell-demo {
  height: 480px;
  overflow: hidden;
  border-radius: 12px;
  border: 1px solid var(--border);
  transform: translateZ(0);
}
.ds-shell-demo [data-slot='sidebar-wrapper'],
.ds-shell-demo .group\/sidebar-wrapper { min-height: 0 !important; height: 100% !important; }
/* 固定定位的侧栏容器原为 h-svh（整屏高），收进预览框高度 */
.ds-shell-demo [data-slot='sidebar-container'] { height: 100% !important; }
</style>

```vue
<NvAppShellInset collapsible="icon">
  <template #sidebar-header><NvSidebarBrand name="Nerv-IIP" sub="总装一厂 · 早班" logo="N" /></template>
  <template #sidebar>
    <SidebarGroup>
      <SidebarGroupLabel>生产</SidebarGroupLabel>
      <SidebarMenu>
        <SidebarMenuItem><SidebarMenuButton :is-active="true"><LayoutDashboardIcon /><span>总览</span></SidebarMenuButton></SidebarMenuItem>
        <SidebarMenuItem><SidebarMenuButton><WrenchIcon /><span>设备</span><NvSidebarDot tone="ok" /></SidebarMenuButton></SidebarMenuItem>
      </SidebarMenu>
    </SidebarGroup>
  </template>
  <template #sidebar-footer>
    <SidebarMenu>
      <SidebarMenuItem>
        <SidebarMenuButton size="lg">
          <NvSidebarUser name="张伟" role="班长 · 早班" initials="张" />
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  </template>
  <template #header><Breadcrumb>…</Breadcrumb></template>

  <!-- 主内容（default slot） -->
</NvAppShellInset>
```

## 插槽 / 属性

| 名称              | 说明                                                       |
| ----------------- | ---------------------------------------------------------- |
| `#sidebar-header` | 侧栏顶部（品牌 / 切换器）                                  |
| `#sidebar`        | 侧栏主体(用 `SidebarGroup` + `SidebarMenu` 组织导航)       |
| `#sidebar-footer` | 侧栏底部（用户 / 操作）                                    |
| `#header`         | 顶部栏内容（面包屑等），自带 `SidebarTrigger` 折叠按钮     |
| `default`         | inset 主内容区                                             |
| `collapsible`     | `offcanvas` \| `icon` \| `none`，侧栏折叠方式(默认 `icon`) |

> 底层通过 `@nerv-iip/ui` 稳定导出完整 Sidebar 系统(`SidebarProvider` / `Sidebar` / `SidebarRail` / `SidebarInset` / `SidebarTrigger` / `SidebarMenu*`),需要更深定制时也从组件库导入。
