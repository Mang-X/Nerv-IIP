---
title: ScreenSelect 下拉选择
---

<script setup>
import { ref } from 'vue'
import { ScreenSelect } from '@nerv-iip/ui'

const line = ref('line-a')
const shift = ref('')

const lines = [
  { label: '焊接线 A', value: 'line-a' },
  { label: '装配线 B', value: 'line-b' },
  { label: 'CNC 线 C', value: 'line-c' },
  { label: '涂装线 D', value: 'line-d' },
]
const shifts = [
  { label: '早班 06:00 - 14:00', value: 'a' },
  { label: '中班 14:00 - 22:00', value: 'b' },
  { label: '夜班 22:00 - 06:00', value: 'c' },
]
</script>

# ScreenSelect 下拉选择

大屏下拉:暗色触发器落下一块发光选项面板,选中行显青色并带勾,面板浮在青色阴影上。键盘可驱动(↑ / ↓ / Enter / Esc),具备 listbox 语义,点击外部即收起。通过 `v-model` 绑定选项 `value`,基于独立的 `--sb-*` 令牌。

## 基础用法

`options` 传 `{ label, value }` 列表,`v-model` 持有选中的 `value`。

<ScreenDemo>
  <div style="width:260px">
    <ScreenSelect v-model="line" :options="lines" />
  </div>
</ScreenDemo>

```vue
<script setup>
const line = ref('line-a')
const lines = [
  { label: '焊接线 A', value: 'line-a' },
  { label: '装配线 B', value: 'line-b' },
  { label: 'CNC 线 C', value: 'line-c' },
  { label: '涂装线 D', value: 'line-d' },
]
</script>

<template>
  <ScreenSelect v-model="line" :options="lines" />
</template>
```

## 占位与禁用

未选中时显示 `placeholder`;`disabled` 整体淡出。

<ScreenDemo>
  <div style="display:flex;gap:16px">
    <div style="width:240px">
      <ScreenSelect v-model="shift" :options="shifts" placeholder="请选择班次" />
    </div>
    <div style="width:200px">
      <ScreenSelect :options="lines" disabled placeholder="产线锁定" />
    </div>
  </div>
</ScreenDemo>

```vue
<ScreenSelect v-model="shift" :options="shifts" placeholder="请选择班次" />
<ScreenSelect :options="lines" disabled placeholder="产线锁定" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 选中项的 `value` | `string \| number` | — |
| `options` | 选项列表 | `{ label: string; value: string \| number }[]` | 内置示例产线 |
| `placeholder` | 未选中时的占位 | `string` | `'请选择产线'` |
| `disabled` | 禁用 | `boolean` | `false` |
