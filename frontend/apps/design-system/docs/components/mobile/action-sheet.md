---
title: ActionSheet 动作面板
---

<script setup>
import { ActionSheet, MobileButton } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const actionOpen = ref(false)
const actions = [
  { label: '拆分工单', value: 'split' },
  { label: '补打标签', value: 'reprint' },
  { label: '报告异常', value: 'fault', danger: true },
]
const picked = ref('')
function onAction(value) {
  picked.value = value
}
</script>

# ActionSheet 动作面板

从底部升起的动作列表，堆叠备选操作并单独分隔「取消」，点击后回传所选值。

## 基础用法

由触发按钮控制 `open`，`select` 事件回传 `value`。`danger` 项以危险色呈现。

<Demo mobile>
  <MobileButton variant="default" size="md" block @click="actionOpen = true">
    打开动作面板
  </MobileButton>
  <ActionSheet
    v-model:open="actionOpen"
    title="工单操作"
    :actions="actions"
    @select="onAction"
  />
</Demo>

```vue
<script setup>
import { ActionSheet, MobileButton } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const actionOpen = ref(false)
const actions = [
  { label: '拆分工单', value: 'split' },
  { label: '补打标签', value: 'reprint' },
  { label: '报告异常', value: 'fault', danger: true },
]
function onAction(value) {
  console.log('操作：', value)
}
</script>

<template>
  <MobileButton variant="default" size="md" block @click="actionOpen = true">
    打开动作面板
  </MobileButton>
  <ActionSheet
    v-model:open="actionOpen"
    title="工单操作"
    :actions="actions"
    @select="onAction"
  />
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `open` | 是否打开（`v-model:open`） | `boolean` | `false` |
| `actions` | 动作列表 | `ActionItem[]` | — |
| `title` | 标题 | `string` | — |
| `description` | 描述 | `string` | — |
| `cancelText` | 取消按钮文案 | `string` | `取消` |

`ActionItem`：`{ label: string; value: string; danger?: boolean }`

## 事件

| 事件 | 说明 | 回调参数 |
|---|---|---|
| `select` | 选择某项时触发 | `(value: string)` |
