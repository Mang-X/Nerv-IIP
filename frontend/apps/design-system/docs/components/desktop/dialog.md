---
title: NvDialog 对话框
---

<script setup>
import {
  NvDialog,
  NvDialogTrigger,
  NvDialogContent,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogClose,
  NvButton,
} from '@nerv-iip/ui'
import { GaugeIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const dialogOpen = ref(false)
</script>

# NvDialog 对话框

承载需要用户聚焦确认的关键操作。`NvDialog` 提供模糊遮罩与缩放入场，由 Header / Title / Description / Footer 组合内容。

## 基础用法

通过 `NvDialogTrigger` 触发，`v-model:open` 双向绑定开关状态。

<Demo>
  <NvDialog v-model:open="dialogOpen">
    <NvDialogTrigger as-child>
      <NvButton variant="brand">
        <template #leading><GaugeIcon aria-hidden="true" /></template>
        派发工单
      </NvButton>
    </NvDialogTrigger>
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>确认派发工单</NvDialogTitle>
        <NvDialogDescription>
          WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。
        </NvDialogDescription>
      </NvDialogHeader>
      <div class="rounded-lg border border-border bg-muted/40 p-4 text-sm">
        <div class="flex justify-between">
          <span class="text-muted-foreground">数量</span>
          <span class="tabular-nums">480</span>
        </div>
        <div class="mt-2 flex justify-between">
          <span class="text-muted-foreground">预计工时</span>
          <span class="tabular-nums">14.5 h</span>
        </div>
      </div>
      <NvDialogFooter>
        <NvDialogClose as-child>
          <NvButton variant="ghost">取消</NvButton>
        </NvDialogClose>
        <NvButton variant="brand" @click="dialogOpen = false">确认派发</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</Demo>

```vue
<script setup>
import {
  NvDialog,
  NvDialogTrigger,
  NvDialogContent,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogClose,
  NvButton,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const dialogOpen = ref(false)
</script>

<template>
  <NvDialog v-model:open="dialogOpen">
    <NvDialogTrigger as-child>
      <NvButton variant="brand">派发工单</NvButton>
    </NvDialogTrigger>
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>确认派发工单</NvDialogTitle>
        <NvDialogDescription
          >WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。</NvDialogDescription
        >
      </NvDialogHeader>
      <NvDialogFooter>
        <NvDialogClose as-child><NvButton variant="ghost">取消</NvButton></NvDialogClose>
        <NvButton variant="brand" @click="dialogOpen = false">确认派发</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
```

## 组成

| 组件                                                       | 说明                                   |
| ---------------------------------------------------------- | -------------------------------------- |
| `NvDialog`                                                 | 根容器，支持 `v-model:open`            |
| `NvDialogTrigger`                                          | 触发器，配合 `as-child` 包裹自定义按钮 |
| `NvDialogContent`                                          | 内容卡片（模糊遮罩 + 缩放入场）        |
| `NvDialogHeader` / `NvDialogTitle` / `NvDialogDescription` | 头部、标题与描述                       |
| `NvDialogFooter`                                           | 底部操作区                             |
| `NvDialogClose`                                            | 关闭触发器                             |

## 属性

| 属性       | 所属                                | 说明                           | 类型      | 默认    |
| ---------- | ----------------------------------- | ------------------------------ | --------- | ------- |
| `open`     | `NvDialog`                          | 受控开关状态（`v-model:open`） | `boolean` | `false` |
| `as-child` | `NvDialogTrigger` / `NvDialogClose` | 将渲染合并到子元素             | `boolean` | `false` |
