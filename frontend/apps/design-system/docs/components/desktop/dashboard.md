---
title: Dashboard 仪表盘外壳
---

<script setup>
import {
  DashboardGroup, DashboardSidebar, DashboardPanel, DashboardNavbar, DashboardToolbar, ButtonPro,
} from '@nerv-iip/ui'
import {
  LayoutDashboardIcon, BoxesIcon, WrenchIcon, SettingsIcon, PanelLeftIcon, BellIcon,
} from 'lucide-vue-next'
</script>

# Dashboard 仪表盘外壳

可组合的控制台外壳分区件（参考 Nuxt UI Pro）。比一体式 app-shell 更细：`DashboardGroup` 作为根行容器，内含 `DashboardSidebar` 与一个或多个 `DashboardPanel`；每个面板用 `DashboardNavbar` + `DashboardToolbar` 吸顶，下方主体可滚动。

## 完整组合

<Demo>
  <DashboardGroup class="!min-h-0 h-[460px] w-full overflow-hidden rounded-xl border border-border">
    <DashboardSidebar class="w-52">
      <template #header>
        <div class="flex size-6 items-center justify-center rounded bg-brand text-xs font-bold text-brand-foreground">N</div>
        <span class="text-sm font-semibold">Nerv-IIP</span>
      </template>
      <nav class="space-y-1 text-sm">
        <a class="flex items-center gap-2 rounded-md bg-accent px-2 py-1.5 font-medium text-brand-strong"><LayoutDashboardIcon class="size-4" />总览</a>
        <a class="flex items-center gap-2 rounded-md px-2 py-1.5 text-muted-foreground"><BoxesIcon class="size-4" />工单</a>
        <a class="flex items-center gap-2 rounded-md px-2 py-1.5 text-muted-foreground"><WrenchIcon class="size-4" />设备</a>
        <a class="flex items-center gap-2 rounded-md px-2 py-1.5 text-muted-foreground"><SettingsIcon class="size-4" />设置</a>
      </nav>
      <template #footer>
        <div class="flex items-center gap-2 text-sm"><div class="size-7 rounded-full bg-muted" />张伟 · 早班</div>
      </template>
    </DashboardSidebar>
    <DashboardPanel>
      <template #header>
        <DashboardNavbar title="工单 WO-2406-0413">
          <template #leading>
            <ButtonPro variant="ghost" size="icon" aria-label="折叠侧栏"><PanelLeftIcon class="size-4" /></ButtonPro>
          </template>
          <template #right>
            <ButtonPro variant="ghost" size="icon" aria-label="通知"><BellIcon class="size-4" /></ButtonPro>
            <ButtonPro variant="brand" size="sm">新建工单</ButtonPro>
          </template>
        </DashboardNavbar>
        <DashboardToolbar>
          <span class="rounded-md bg-accent px-2 py-1 text-xs font-medium text-brand-strong">进行中</span>
          <span class="px-2 py-1 text-xs text-muted-foreground">已完成</span>
          <span class="px-2 py-1 text-xs text-muted-foreground">全部</span>
        </DashboardToolbar>
      </template>
      <div class="space-y-2 p-4">
        <p class="text-sm text-muted-foreground">主内容区可滚动；导航栏与工具栏吸顶固定。</p>
        <div v-for="n in 8" :key="n" class="rounded-lg border border-border bg-card px-3 py-2.5 text-sm">
          工序 OP-{{ String(n).padStart(2, '0') }} · 前桥壳体 A2
        </div>
      </div>
    </DashboardPanel>
  </DashboardGroup>
</Demo>

```vue
<DashboardGroup>
  <DashboardSidebar>
    <template #header><!-- 品牌 / 切换器 --></template>
    <nav><!-- 导航 --></nav>
    <template #footer><!-- 用户 --></template>
  </DashboardSidebar>

  <DashboardPanel>
    <template #header>
      <DashboardNavbar title="工单 WO-2406-0413">
        <template #leading><!-- 折叠按钮 --></template>
        <template #right><!-- 操作 --></template>
      </DashboardNavbar>
      <DashboardToolbar><!-- 标签 / 筛选 --></DashboardToolbar>
    </template>

    <!-- 主体（可滚动） -->
  </DashboardPanel>
</DashboardGroup>
```

## 分区件

| 组件 | 作用 | 关键插槽 |
|---|---|---|
| `DashboardGroup` | 根行容器，撑满高度 | `default`（侧栏 + 面板）|
| `DashboardSidebar` | 固定宽度左侧栏（默认 `w-64`）| `header` / `default`（导航）/ `footer` |
| `DashboardPanel` | 主内容区，`#header` 吸顶、主体滚动 | `header` / `default` |
| `DashboardNavbar` | 面板顶栏（玻璃吸顶）| `leading` / `title`（或 `title` 属性）/ `right` |
| `DashboardToolbar` | 顶栏下的次级条（标签 / 筛选）| `default` / `right` |

> 这些是**细粒度分区件**，用于手工拼装控制台布局；需要开箱即用的整体外壳时仍可用 `blocks` 里的 app-shell。
