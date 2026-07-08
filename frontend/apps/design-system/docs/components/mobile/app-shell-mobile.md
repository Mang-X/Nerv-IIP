---
layout: page
title: AppShellMobile 应用外壳
---

<script setup>
import { ref } from 'vue'
import { NvAppShellMobile, NvNavBar, NvTabBar, NvCellGroup, NvCell, NvMobileTag } from '@nerv-iip/ui-mobile'
import { LayoutGrid, ClipboardList, Bell, User } from 'lucide-vue-next'

const tab = ref('home')
const tabs = [
  { value: 'home', label: '工作台', icon: LayoutGrid },
  { value: 'tasks', label: '任务', icon: ClipboardList },
  { value: 'alerts', label: '告警', icon: Bell },
  { value: 'me', label: '我的', icon: User },
]

const kpis = [
  { label: '今日产量', value: '1,284', unit: '件' },
  { label: '稼动率', value: '92', unit: '%' },
  { label: '待处理', value: '7', unit: '单' },
]

const tasks = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖', station: '焊接 03', label: '进行中', variant: 'brand' },
  { code: 'WO-2406-0426', product: '液压阀体 V3', station: '装配 01', label: '待报工', variant: 'warning' },
  { code: 'WO-2406-0433', product: '主轴箱体', station: 'CNC 02', label: '已超时', variant: 'danger' },
]
</script>

<MobileDoc>

<template #phone>
<NvAppShellMobile class="h-full">
<template #header>
<NvNavBar title="车间工作台">
<template #right>
<button type="button" class="relative flex size-9 items-center justify-center rounded-full text-foreground active:bg-accent" aria-label="告警">
<Bell class="size-5" aria-hidden="true" />
<span class="absolute top-2 right-2 size-1.5 rounded-full bg-destructive" aria-hidden="true" />
</button>
</template>
</NvNavBar>
</template>

<div class="space-y-4 p-4">
<div class="grid grid-cols-3 gap-2">
<div v-for="k in kpis" :key="k.label" class="rounded-xl border border-border bg-card px-3 py-2.5">
<div class="text-xs text-muted-foreground">{{ k.label }}</div>
<div class="mt-1 flex items-baseline gap-0.5">
<span class="text-xl font-semibold tabular-nums">{{ k.value }}</span>
<span class="text-xs text-muted-foreground">{{ k.unit }}</span>
</div>
</div>
</div>
<NvCellGroup title="我的任务">
<NvCell v-for="t in tasks" :key="t.code" :title="t.product" :note="`${t.code} · ${t.station}`" arrow>
<template #value>
<NvMobileTag :variant="t.variant" size="sm">{{ t.label }}</NvMobileTag>
</template>
</NvCell>
</NvCellGroup>
<NvCellGroup title="快捷入口">
<NvCell title="扫码报工" arrow />
<NvCell title="设备点检" arrow />
<NvCell title="物料申请" arrow />
<NvCell title="异常上报" arrow />
</NvCellGroup>
</div>
<template #footer>
<NvTabBar v-model="tab" :items="tabs" />
</template>
</NvAppShellMobile>
</template>

# AppShellMobile 应用外壳

PDA 页面的整体骨架：吸顶的 `header`、可滚动的内容区、吸底的 `footer`，三段式布局占满整屏（`h-dvh`）。头/尾默认带毛玻璃背景与分隔线，并自动避让设备安全区（刘海、底部小白条）。内容区独立滚动、滚动条隐藏，头尾始终可见。

## 基础用法

把顶部栏放进 `#header`、底部导航放进 `#footer`，其余内容写在默认插槽即为可滚动主体。`header` / `footer` 插槽为空时对应区域不渲染。

```vue
<NvAppShellMobile>
  <template #header>
    <NvNavBar title="车间工作台" />
  </template>

  <!-- 可滚动主体 -->
  <div class="p-4">…</div>

  <template #footer>
    <NvTabBar v-model="tab" :items="tabs" />
  </template>
</NvAppShellMobile>
```

## 结构说明

| 区域      | 行为                                                              |
| --------- | ----------------------------------------------------------------- |
| `#header` | 吸顶（`sticky top-0`）、毛玻璃、底部分隔线、避让顶部安全区        |
| 默认插槽  | 主体，独立纵向滚动、隐藏滚动条、`overscroll-contain` 防止滚动穿透 |
| `#footer` | 吸底（`sticky bottom-0`）、毛玻璃、顶部分隔线、避让底部安全区     |

## 属性

| 属性    | 说明                             | 类型     | 默认 |
| ------- | -------------------------------- | -------- | ---- |
| `class` | 根容器类（默认 `h-dvh`，整屏高） | `string` | —    |

## 插槽

| 插槽      | 说明                                                    |
| --------- | ------------------------------------------------------- |
| `header`  | 顶部固定区，通常放 [NavBar](/components/mobile/nav-bar) |
| `default` | 可滚动主体内容                                          |
| `footer`  | 底部固定区，通常放 [TabBar](/components/mobile/tab-bar) |

::: tip 提示
外壳默认 `h-dvh` 占满整个视口高度，是整页应用的最外层容器。本页演示为放入手机模拟器，用 `class="h-full"` 改为填满父容器。
:::

</MobileDoc>
