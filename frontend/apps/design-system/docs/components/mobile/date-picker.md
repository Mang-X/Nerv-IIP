---
title: DatePicker 日期选择
---

<script setup>
import { Cell, CellGroup, MobileDatePicker } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const dateOpen = ref(false)
const dateVal = ref('2026-06-18')
</script>

# DatePicker 日期选择

年 / 月 / 日三列滚轮日期选择器（Vant / tdesign-mobile 风格），承载于底部抽屉。`v-model:open` 控制显隐，`v-model` 绑定 `YYYY-MM-DD` 字符串，需配合触发器与 open ref 使用。

## 基础用法

<Demo mobile>
  <CellGroup>
    <Cell title="计划日期" :value="dateVal" arrow @click="dateOpen = true" />
  </CellGroup>
  <MobileDatePicker v-model:open="dateOpen" v-model="dateVal" title="计划日期" />
</Demo>

```vue
<script setup>
const dateOpen = ref(false)
const dateVal = ref('2026-06-18')
</script>

<template>
  <CellGroup>
    <Cell title="计划日期" :value="dateVal" arrow @click="dateOpen = true" />
  </CellGroup>
  <MobileDatePicker v-model:open="dateOpen" v-model="dateVal" title="计划日期" />
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model:open` | 抽屉显隐 | `boolean` | `false` |
| `v-model` | 选中日期（`YYYY-MM-DD`） | `string` | — |
| `title` | 标题 | `string` | `'选择日期'` |
| `minYear` | 最小年份 | `number` | 当前年 − 10 |
| `maxYear` | 最大年份 | `number` | 当前年 + 5 |
