---
title: Dialog 对话框
---

<script setup>
import {
  DialogPro,
  DialogProTrigger,
  DialogProContent,
  DialogProHeader,
  DialogProTitle,
  DialogProDescription,
  DialogProFooter,
  DialogProClose,
  ButtonPro,
} from '@nerv-iip/ui'
import { GaugeIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const dialogOpen = ref(false)
</script>

# Dialog 对话框

承载需要用户聚焦确认的关键操作。`DialogPro` 提供模糊遮罩与缩放入场，由 Header / Title / Description / Footer 组合内容。

## 基础用法

通过 `DialogProTrigger` 触发，`v-model:open` 双向绑定开关状态。

<Demo>
  <DialogPro v-model:open="dialogOpen">
    <DialogProTrigger as-child>
      <ButtonPro variant="brand">
        <template #leading><GaugeIcon aria-hidden="true" /></template>
        派发工单
      </ButtonPro>
    </DialogProTrigger>
    <DialogProContent>
      <DialogProHeader>
        <DialogProTitle>确认派发工单</DialogProTitle>
        <DialogProDescription>
          WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。
        </DialogProDescription>
      </DialogProHeader>
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
      <DialogProFooter>
        <DialogProClose as-child>
          <ButtonPro variant="ghost">取消</ButtonPro>
        </DialogProClose>
        <ButtonPro variant="brand" @click="dialogOpen = false">确认派发</ButtonPro>
      </DialogProFooter>
    </DialogProContent>
  </DialogPro>
</Demo>

```vue
<script setup>
import {
  DialogPro, DialogProTrigger, DialogProContent, DialogProHeader,
  DialogProTitle, DialogProDescription, DialogProFooter, DialogProClose, ButtonPro,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const dialogOpen = ref(false)
</script>

<template>
  <DialogPro v-model:open="dialogOpen">
    <DialogProTrigger as-child>
      <ButtonPro variant="brand">派发工单</ButtonPro>
    </DialogProTrigger>
    <DialogProContent>
      <DialogProHeader>
        <DialogProTitle>确认派发工单</DialogProTitle>
        <DialogProDescription>WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。</DialogProDescription>
      </DialogProHeader>
      <DialogProFooter>
        <DialogProClose as-child><ButtonPro variant="ghost">取消</ButtonPro></DialogProClose>
        <ButtonPro variant="brand" @click="dialogOpen = false">确认派发</ButtonPro>
      </DialogProFooter>
    </DialogProContent>
  </DialogPro>
</template>
```

## 组成

| 组件 | 说明 |
|---|---|
| `DialogPro` | 根容器，支持 `v-model:open` |
| `DialogProTrigger` | 触发器，配合 `as-child` 包裹自定义按钮 |
| `DialogProContent` | 内容卡片（模糊遮罩 + 缩放入场） |
| `DialogProHeader` / `DialogProTitle` / `DialogProDescription` | 头部、标题与描述 |
| `DialogProFooter` | 底部操作区 |
| `DialogProClose` | 关闭触发器 |

## 属性

| 属性 | 所属 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `open` | `DialogPro` | 受控开关状态（`v-model:open`） | `boolean` | `false` |
| `as-child` | `DialogProTrigger` / `DialogProClose` | 将渲染合并到子元素 | `boolean` | `false` |
