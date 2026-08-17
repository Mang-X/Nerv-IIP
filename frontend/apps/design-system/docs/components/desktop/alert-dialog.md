---
title: NvAlertDialog 警告对话框
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
import { Trash2Icon } from '@lucide/vue'
import { ref } from 'vue'

const alertOpen = ref(false)
</script>

# NvAlertDialog 警告对话框

拦截不可逆的破坏性操作，强制用户在继续前作出明确选择。`NvAlertDialog` 没有右上角关闭按钮，只能通过取消或确认操作退出，确保确认动作不会被误触跳过。`NvAlertDialogAction` 内部使用 `NvButton`，可通过 `variant="destructive"` 渲染为危险色。

::: warning 异步确认不要用 NvAlertDialogAction
`NvAlertDialogAction` 包的是 reka `AlertDialogAction`，直接渲染成 `DialogClose`——`@click` 里
`onOpenChange(false)` **无条件执行、不看 `event.defaultPrevented`**。所以**点击瞬间框就关**，
异步请求之后才落地：

- 「提交失败后保留输入、原地重试」做不到（框早没了）；
- 「`pending` 期间禁点确认」也做不到（用户根本看不到 disabled 那一瞬）。

**确认动作要等接口结果时，请用普通 `NvButton`**，由 handler 成功才把 `open` 置 false；
「取消」继续用 `NvAlertDialogCancel`（本就该无条件关）。本页下方示例都是**同步**关闭的场景，
故仍用 `NvAlertDialogAction`。

判定与实现见 `frontend/DESIGN/patterns/flows/confirm-destroy.md` 规则 3（#1607）。
:::

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

| 属性       | 所属                                          | 说明                                 | 类型                  | 默认                  |
| ---------- | --------------------------------------------- | ------------------------------------ | --------------------- | --------------------- |
| `open`     | `NvAlertDialog`                               | 受控开关状态（`v-model:open`）       | `boolean`             | `false`               |
| `as-child` | `NvAlertDialogTrigger`                        | 将渲染合并到子元素                   | `boolean`             | `false`               |
| `variant`  | `NvAlertDialogAction` / `NvAlertDialogCancel` | 按钮样式，破坏性操作用 `destructive` | `NvButton['variant']` | `default` / `outline` |
| `size`     | `NvAlertDialogAction` / `NvAlertDialogCancel` | 按钮尺寸                             | `NvButton['size']`    | `default`             |
