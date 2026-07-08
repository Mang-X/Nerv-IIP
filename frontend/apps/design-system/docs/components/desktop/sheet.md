---
title: NvSheet 抽屉
---

<script setup>
import {
  NvSheet,
  NvSheetTrigger,
  NvSheetContent,
  NvSheetHeader,
  NvSheetTitle,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetClose,
  NvButton,
} from '@nerv-iip/ui'
import { SlidersHorizontalIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const sheetOpen = ref(false)
</script>

# NvSheet 抽屉

从屏幕边缘滑入的侧边面板，承载详情查看、高级筛选等不打断主流程的次级操作。`NvSheet` 默认从右侧滑入，由 Header / Title / Description / Footer 组合内容。

## 基础用法

通过 `NvSheetTrigger` 触发，`v-model:open` 双向绑定开关状态。`NvSheetContent` 的 `side` 控制滑入方向，默认 `right`。

<Demo>
  <NvSheet v-model:open="sheetOpen">
    <NvSheetTrigger as-child>
      <NvButton variant="outline">
        <template #leading><SlidersHorizontalIcon aria-hidden="true" /></template>
        筛选
      </NvButton>
    </NvSheetTrigger>
    <NvSheetContent side="right">
      <NvSheetHeader>
        <NvSheetTitle>高级筛选</NvSheetTitle>
        <NvSheetDescription>
          按产线、状态与时间范围筛选工单，结果实时刷新列表。
        </NvSheetDescription>
      </NvSheetHeader>
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
      <NvSheetFooter>
        <NvSheetClose as-child>
          <NvButton variant="ghost">取消</NvButton>
        </NvSheetClose>
        <NvButton variant="brand" @click="sheetOpen = false">应用</NvButton>
      </NvSheetFooter>
    </NvSheetContent>
  </NvSheet>
</Demo>

```vue
<script setup>
import {
  NvSheet,
  NvSheetTrigger,
  NvSheetContent,
  NvSheetHeader,
  NvSheetTitle,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetClose,
  NvButton,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const sheetOpen = ref(false)
</script>

<template>
  <NvSheet v-model:open="sheetOpen">
    <NvSheetTrigger as-child>
      <NvButton variant="outline">筛选</NvButton>
    </NvSheetTrigger>
    <NvSheetContent side="right">
      <NvSheetHeader>
        <NvSheetTitle>高级筛选</NvSheetTitle>
        <NvSheetDescription>按产线、状态与时间范围筛选工单，结果实时刷新列表。</NvSheetDescription>
      </NvSheetHeader>
      <div class="flex flex-col gap-4 px-4">
        <!-- 表单字段 -->
      </div>
      <NvSheetFooter>
        <NvSheetClose as-child><NvButton variant="ghost">取消</NvButton></NvSheetClose>
        <NvButton variant="brand" @click="sheetOpen = false">应用</NvButton>
      </NvSheetFooter>
    </NvSheetContent>
  </NvSheet>
</template>
```

## 组成

| 组件                                                    | 说明                                          |
| ------------------------------------------------------- | --------------------------------------------- |
| `NvSheet`                                               | 根容器，支持 `v-model:open`                   |
| `NvSheetTrigger`                                        | 触发器，配合 `as-child` 包裹自定义按钮        |
| `NvSheetContent`                                        | 抽屉面板（模糊遮罩 + 边缘滑入，内置关闭按钮） |
| `NvSheetHeader` / `NvSheetTitle` / `NvSheetDescription` | 头部、标题与描述                              |
| `NvSheetFooter`                                         | 底部操作区                                    |
| `NvSheetClose`                                          | 关闭触发器                                    |

## 属性

| 属性                | 所属                              | 说明                                                              | 类型                                     | 默认      |
| ------------------- | --------------------------------- | ----------------------------------------------------------------- | ---------------------------------------- | --------- |
| `open`              | `NvSheet`                         | 受控开关状态（`v-model:open`）                                    | `boolean`                                | `false`   |
| `side`              | `NvSheetContent`                  | 滑入方向：`top` 顶部 / `right` 右侧 / `bottom` 底部 / `left` 左侧 | `'top' \| 'right' \| 'bottom' \| 'left'` | `'right'` |
| `show-close-button` | `NvSheetContent`                  | 是否显示右上角内置关闭按钮                                        | `boolean`                                | `true`    |
| `as-child`          | `NvSheetTrigger` / `NvSheetClose` | 将渲染合并到子元素                                                | `boolean`                                | `false`   |
