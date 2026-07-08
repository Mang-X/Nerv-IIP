---
title: App & Header 应用外壳
---

<script setup>
import { NvAppHeader, NvButton } from '@nerv-iip/ui'
import { BellIcon, SearchIcon } from 'lucide-vue-next'
</script>

# NvApp & Header 应用外壳

`NvApp` 是根级提供者(参考 Nuxt UI `NvApp`):挂载全局 reka ConfigProvider + 统一 TooltipProvider + Toast 出口,并设定基底表面。`NvAppHeader` 是顶部应用栏。

## NvApp 根容器

整个应用包一层 `NvApp`,Tooltip / Toast / 阅读方向 / 滚动锁等全局能力即就位。

```vue
<script setup>
import { NvApp } from '@nerv-iip/ui'
</script>

<template>
  <NvApp>
    <NvAppHeader>…</NvAppHeader>
    <main>…</main>
  </NvApp>
</template>
```

| 属性           | 说明                      | 类型     | 默认  |
| -------------- | ------------------------- | -------- | ----- |
| `tooltipDelay` | 全局 Tooltip 打开延迟(ms) | `number` | `200` |

## Header 顶部栏

`#leading` 放品牌 / 菜单按钮,默认插槽放标题或居中导航,`#trailing` 放操作。默认吸顶 + 玻璃质感。

<Demo>
  <NvAppHeader :sticky="false" class="w-full rounded-xl border border-border">
    <template #leading>
      <div class="flex size-7 items-center justify-center rounded-md bg-brand text-sm font-bold text-brand-foreground">N</div>
      <span class="text-sm font-semibold">Nerv-IIP</span>
    </template>
    <nav class="ms-2 hidden items-center gap-1 text-sm sm:flex">
      <a class="rounded-md px-2 py-1 font-medium text-brand-strong">控制台</a>
      <a class="rounded-md px-2 py-1 text-muted-foreground">工单</a>
      <a class="rounded-md px-2 py-1 text-muted-foreground">设备</a>
    </nav>
    <template #trailing>
      <NvButton variant="ghost" size="icon" aria-label="搜索"><SearchIcon class="size-4" /></NvButton>
      <NvButton variant="ghost" size="icon" aria-label="通知"><BellIcon class="size-4" /></NvButton>
      <NvButton variant="brand" size="sm">新建工单</NvButton>
    </template>
  </NvAppHeader>
</Demo>

```vue
<NvAppHeader>
  <template #leading><Logo /> Nerv-IIP</template>
  <nav><!-- 居中导航 --></nav>
  <template #trailing><!-- 操作 --></template>
</NvAppHeader>
```

| 属性     | 说明            | 类型      | 默认   |
| -------- | --------------- | --------- | ------ |
| `sticky` | 吸顶 + 玻璃背景 | `boolean` | `true` |
