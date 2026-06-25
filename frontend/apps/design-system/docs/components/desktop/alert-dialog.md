---
title: AlertDialog 警告对话框
---

<script setup>
import {
  AlertDialogPro,
  AlertDialogProTrigger,
  AlertDialogProContent,
  AlertDialogProHeader,
  AlertDialogProTitle,
  AlertDialogProDescription,
  AlertDialogProFooter,
  AlertDialogProAction,
  AlertDialogProCancel,
  ButtonPro,
} from '@nerv-iip/ui'
import { Trash2Icon } from 'lucide-vue-next'
import { ref } from 'vue'

const alertOpen = ref(false)
</script>

# AlertDialog 警告对话框

拦截不可逆的破坏性操作，强制用户在继续前作出明确选择。`AlertDialogPro` 没有右上角关闭按钮，只能经 Cancel / Action 退出，确保确认动作不被误触跳过。Action 内部即 `ButtonPro`，可通过 `variant="destructive"` 渲染为危险色。

## 基础用法

通过 `AlertDialogProTrigger` 触发，`v-model:open` 双向绑定开关状态；点击「删除」执行确认逻辑后关闭。

<Demo>
  <AlertDialogPro v-model:open="alertOpen">
    <AlertDialogProTrigger as-child>
      <ButtonPro variant="outline">
        <template #leading><Trash2Icon aria-hidden="true" /></template>
        删除工单
      </ButtonPro>
    </AlertDialogProTrigger>
    <AlertDialogProContent>
      <AlertDialogProHeader>
        <AlertDialogProTitle>确认删除工单</AlertDialogProTitle>
        <AlertDialogProDescription>
          WO-2406-0431 删除后不可恢复，关联的领料单与排程记录将一并失效。请确认是否继续。
        </AlertDialogProDescription>
      </AlertDialogProHeader>
      <AlertDialogProFooter>
        <AlertDialogProCancel>取消</AlertDialogProCancel>
        <AlertDialogProAction variant="destructive" @click="alertOpen = false">删除</AlertDialogProAction>
      </AlertDialogProFooter>
    </AlertDialogProContent>
  </AlertDialogPro>
</Demo>

```vue
<script setup>
import {
  AlertDialogPro, AlertDialogProTrigger, AlertDialogProContent, AlertDialogProHeader,
  AlertDialogProTitle, AlertDialogProDescription, AlertDialogProFooter,
  AlertDialogProAction, AlertDialogProCancel, ButtonPro,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const alertOpen = ref(false)
</script>

<template>
  <AlertDialogPro v-model:open="alertOpen">
    <AlertDialogProTrigger as-child>
      <ButtonPro variant="outline">删除工单</ButtonPro>
    </AlertDialogProTrigger>
    <AlertDialogProContent>
      <AlertDialogProHeader>
        <AlertDialogProTitle>确认删除工单</AlertDialogProTitle>
        <AlertDialogProDescription>WO-2406-0431 删除后不可恢复，关联的领料单与排程记录将一并失效。请确认是否继续。</AlertDialogProDescription>
      </AlertDialogProHeader>
      <AlertDialogProFooter>
        <AlertDialogProCancel>取消</AlertDialogProCancel>
        <AlertDialogProAction variant="destructive" @click="alertOpen = false">删除</AlertDialogProAction>
      </AlertDialogProFooter>
    </AlertDialogProContent>
  </AlertDialogPro>
</template>
```

## 组成

| 组件 | 说明 |
|---|---|
| `AlertDialogPro` | 根容器，支持 `v-model:open` |
| `AlertDialogProTrigger` | 触发器，配合 `as-child` 包裹自定义按钮 |
| `AlertDialogProContent` | 内容卡片（模糊遮罩 + 缩放入场，无关闭按钮） |
| `AlertDialogProHeader` / `AlertDialogProTitle` / `AlertDialogProDescription` | 头部、标题与描述 |
| `AlertDialogProFooter` | 底部操作区 |
| `AlertDialogProCancel` | 取消并关闭，内部为 `ButtonPro`（默认 `outline`） |
| `AlertDialogProAction` | 确认操作，内部为 `ButtonPro`（默认 `default`，可设 `destructive`） |

## 属性

| 属性 | 所属 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `open` | `AlertDialogPro` | 受控开关状态（`v-model:open`） | `boolean` | `false` |
| `as-child` | `AlertDialogProTrigger` | 将渲染合并到子元素 | `boolean` | `false` |
| `variant` | `AlertDialogProAction` / `AlertDialogProCancel` | 按钮样式，破坏性操作用 `destructive` | `ButtonPro['variant']` | `default` / `outline` |
| `size` | `AlertDialogProAction` / `AlertDialogProCancel` | 按钮尺寸 | `ButtonPro['size']` | `default` |
