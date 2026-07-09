---
title: NvScreenSegmented 分段控制
---

<script setup>
import { ref } from 'vue'
import { NvScreenSegmented } from '@nerv-iip/ui'

const range = ref('today')
const shift = ref('a')
const shifts = [
  { label: '早班', value: 'a' },
  { label: '中班', value: 'b' },
  { label: '夜班', value: 'c' },
]
</script>

# NvScreenSegmented 分段控制

大屏分段控制(今日 / 近7天 / 近30天):细线框出的轨道,激活项填青色带淡辉光,段间骑一道内嵌分隔线。选择由键盘驱动(← / →),并以 radio 组语义暴露。通过 `v-model` 绑定,基于独立的 `--sb-*` 令牌。

## 基础用法

`options` 传 `{ label, value }`,`v-model` 持有选中值;未绑定时默认首项。

<ScreenDemo>
  <NvScreenSegmented v-model="range" />
</ScreenDemo>

```vue
<script setup>
const range = ref('today')
</script>

<template>
  <!-- 默认选项:今日 / 近7天 / 近30天 -->
  <NvScreenSegmented v-model="range" />
</template>
```

## 自定义选项

传入 `options` 覆盖默认时间段,例如班次切换。

<ScreenDemo>
  <NvScreenSegmented v-model="shift" :options="shifts" />
</ScreenDemo>

```vue
<script setup>
const shift = ref('a')
const shifts = [
  { label: '早班', value: 'a' },
  { label: '中班', value: 'b' },
  { label: '夜班', value: 'c' },
]
</script>

<template>
  <NvScreenSegmented v-model="shift" :options="shifts" />
</template>
```

## 属性

| 属性      | 说明                                | 类型                                           | 默认                    |
| --------- | ----------------------------------- | ---------------------------------------------- | ----------------------- |
| `v-model` | 选中项的 `value`,未绑定时自动取首项 | `string \| number`                             | —                       |
| `options` | 分段选项                            | `{ label: string; value: string \| number }[]` | `今日 / 近7天 / 近30天` |
