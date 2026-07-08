---
title: AlertDialog 警告对话框
---

<script setup>
import {
  NvAlertDialog,
  NvAlertDialogTrigger,
  NvAlertDialogContent,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvButton,
} from '@nerv-iip/ui'
import { Trash2Icon } from 'lucide-vue-next'
import { ref } from 'vue'

const alertOpen = ref(false)
</script>

# AlertDialog 警告对话框

拦截不可逆的破坏性操作，强制用户在继续前作出明确选择。`NvAlertDialog` 没有右上角关闭按钮，只能经 Cancel / Action 退出，确保确认动作不被误触跳过。Action 内部即 `NvButton`，可通过 `variant="destructive"` 渲染为危险色。

## 基础用法

通过 `NvAlertDialogTrigger` 触发，`v-model:open` 双向绑定开关状态；点击「删除」执行确认逻辑后关闭。

<Demo>
  <NvAlertDialog v-model:open="alertOpen">
    <NvAlertDialogTrigger as-child>
      <NvButton variant="outline">
        <template #leading><Trash2Icon aria-hidden="true" /></template>
        删除工单
      </NvButton>
    </NvAlertDialogTrigger>
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>确认删除工单</NvAlertDialogTitle>
        <NvAlertDialogDescription>
          WO-2406-0431 删除后不可恢复，关联的领料单与排程记录将一并失效。请确认是否继续。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <NvAlertDialogAction variant="destructive" @click="alertOpen = false">删除</NvAlertDialogAction>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</Demo>

```vue
<script setup>
import {
  NvAlertDialog,
  NvAlertDialogTrigger,
  NvAlertDialogContent,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvButton,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const alertOpen = ref(false)
</script>

<template>
  <NvAlertDialog v-model:open="alertOpen">
    <NvAlertDialogTrigger as-child>
      <NvButton variant="outline">删除工单</NvButton>
    </NvAlertDialogTrigger>
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>确认删除工单</NvAlertDialogTitle>
        <NvAlertDialogDescription
          >WO-2406-0431
          删除后不可恢复，关联的领料单与排程记录将一并失效。请确认是否继续。</NvAlertDialogDescription
        >
      </NvAlertDialogHeader>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <NvAlertDialogAction variant="destructive" @click="alertOpen = false"
          >删除</NvAlertDialogAction
        >
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
```

## 组成

| 组件                                                                      | 说明                                                              |
| ------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `NvAlertDialog`                                                           | 根容器，支持 `v-model:open`                                       |
| `NvAlertDialogTrigger`                                                    | 触发器，配合 `as-child` 包裹自定义按钮                            |
| `NvAlertDialogContent`                                                    | 内容卡片（模糊遮罩 + 缩放入场，无关闭按钮）                       |
| `NvAlertDialogHeader` / `NvAlertDialogTitle` / `NvAlertDialogDescription` | 头部、标题与描述                                                  |
| `NvAlertDialogFooter`                                                     | 底部操作区                                                        |
| `NvAlertDialogCancel`                                                     | 取消并关闭，内部为 `NvButton`（默认 `outline`）                   |
| `NvAlertDialogAction`                                                     | 确认操作，内部为 `NvButton`（默认 `default`，可设 `destructive`） |

## 属性

| 属性       | 所属                                          | 说明                                 | 类型                   | 默认                  |
| ---------- | --------------------------------------------- | ------------------------------------ | ---------------------- | --------------------- |
| `open`     | `NvAlertDialog`                               | 受控开关状态（`v-model:open`）       | `boolean`              | `false`               |
| `as-child` | `NvAlertDialogTrigger`                        | 将渲染合并到子元素                   | `boolean`              | `false`               |
| `variant`  | `NvAlertDialogAction` / `NvAlertDialogCancel` | 按钮样式，破坏性操作用 `destructive` | `ButtonPro['variant']` | `default` / `outline` |
| `size`     | `NvAlertDialogAction` / `NvAlertDialogCancel` | 按钮尺寸                             | `ButtonPro['size']`    | `default`             |
