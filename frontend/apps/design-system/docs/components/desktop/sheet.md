---
title: Sheet 抽屉
---

<script setup>
import {
  SheetPro,
  SheetProTrigger,
  SheetProContent,
  SheetProHeader,
  SheetProTitle,
  SheetProDescription,
  SheetProFooter,
  SheetProClose,
  ButtonPro,
} from '@nerv-iip/ui'
import { SlidersHorizontalIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const sheetOpen = ref(false)
</script>

# Sheet 抽屉

从屏幕边缘滑入的侧边面板，承载详情查看、高级筛选等不打断主流程的次级操作。`SheetPro` 默认从右侧滑入，由 Header / Title / Description / Footer 组合内容。

## 基础用法

通过 `SheetProTrigger` 触发，`v-model:open` 双向绑定开关状态。`SheetProContent` 的 `side` 控制滑入方向，默认 `right`。

<Demo>
  <SheetPro v-model:open="sheetOpen">
    <SheetProTrigger as-child>
      <ButtonPro variant="outline">
        <template #leading><SlidersHorizontalIcon aria-hidden="true" /></template>
        筛选
      </ButtonPro>
    </SheetProTrigger>
    <SheetProContent side="right">
      <SheetProHeader>
        <SheetProTitle>高级筛选</SheetProTitle>
        <SheetProDescription>
          按产线、状态与时间范围筛选工单，结果实时刷新列表。
        </SheetProDescription>
      </SheetProHeader>
      <div class="flex flex-col gap-4 px-4">
        <div class="flex flex-col gap-1.5">
          <label class="text-sm text-muted-foreground">产线</label>
          <div class="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">A 线 / B 线 / C 线</div>
        </div>
        <div class="flex flex-col gap-1.5">
          <label class="text-sm text-muted-foreground">工单状态</label>
          <div class="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">待派发 · 生产中 · 已完工</div>
        </div>
        <div class="flex flex-col gap-1.5">
          <label class="text-sm text-muted-foreground">计划完工日期</label>
          <div class="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm tabular-nums">2406-01 ~ 2406-30</div>
        </div>
      </div>
      <SheetProFooter>
        <SheetProClose as-child>
          <ButtonPro variant="ghost">取消</ButtonPro>
        </SheetProClose>
        <ButtonPro variant="brand" @click="sheetOpen = false">应用</ButtonPro>
      </SheetProFooter>
    </SheetProContent>
  </SheetPro>
</Demo>

```vue
<script setup>
import {
  SheetPro, SheetProTrigger, SheetProContent, SheetProHeader,
  SheetProTitle, SheetProDescription, SheetProFooter, SheetProClose, ButtonPro,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const sheetOpen = ref(false)
</script>

<template>
  <SheetPro v-model:open="sheetOpen">
    <SheetProTrigger as-child>
      <ButtonPro variant="outline">筛选</ButtonPro>
    </SheetProTrigger>
    <SheetProContent side="right">
      <SheetProHeader>
        <SheetProTitle>高级筛选</SheetProTitle>
        <SheetProDescription>按产线、状态与时间范围筛选工单，结果实时刷新列表。</SheetProDescription>
      </SheetProHeader>
      <div class="flex flex-col gap-4 px-4">
        <!-- 表单字段 -->
      </div>
      <SheetProFooter>
        <SheetProClose as-child><ButtonPro variant="ghost">取消</ButtonPro></SheetProClose>
        <ButtonPro variant="brand" @click="sheetOpen = false">应用</ButtonPro>
      </SheetProFooter>
    </SheetProContent>
  </SheetPro>
</template>
```

## 组成

| 组件 | 说明 |
|---|---|
| `SheetPro` | 根容器，支持 `v-model:open` |
| `SheetProTrigger` | 触发器，配合 `as-child` 包裹自定义按钮 |
| `SheetProContent` | 抽屉面板（模糊遮罩 + 边缘滑入，内置关闭按钮） |
| `SheetProHeader` / `SheetProTitle` / `SheetProDescription` | 头部、标题与描述 |
| `SheetProFooter` | 底部操作区 |
| `SheetProClose` | 关闭触发器 |

## 属性

| 属性 | 所属 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `open` | `SheetPro` | 受控开关状态（`v-model:open`） | `boolean` | `false` |
| `side` | `SheetProContent` | 滑入方向：`top` 顶部 / `right` 右侧 / `bottom` 底部 / `left` 左侧 | `'top' \| 'right' \| 'bottom' \| 'left'` | `'right'` |
| `show-close-button` | `SheetProContent` | 是否显示右上角内置关闭按钮 | `boolean` | `true` |
| `as-child` | `SheetProTrigger` / `SheetProClose` | 将渲染合并到子元素 | `boolean` | `false` |
