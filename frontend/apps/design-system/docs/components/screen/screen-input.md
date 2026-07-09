---
title: NvScreenInput 输入框
---

<script setup>
import { ref } from 'vue'
import { NvScreenInput } from '@nerv-iip/ui'

const wo = ref('WO-2406-0312')
const qty = ref('1240')
const bad = ref('')
</script>

# NvScreenInput 输入框

大屏文本输入:一口下沉的暗井,聚焦点亮青色环;`error` 态把细线与环换成红色(并标记 `aria-invalid`),状态不只靠颜色传达。可选 `suffix` 在右侧钉一个单位(件 / kWh / %)。通过 `v-model`(字符串)绑定,基于独立的 `--sb-*` 令牌。

## 基础用法

`v-model` 绑定字符串值;`suffix` 固定一个单位。

<ScreenDemo>
  <div style="display:flex;flex-direction:column;gap:12px;width:280px">
    <NvScreenInput v-model="wo" placeholder="工单号" />
    <NvScreenInput v-model="qty" suffix="件" placeholder="计划产量" />
  </div>
</ScreenDemo>

```vue
<script setup>
const wo = ref('WO-2406-0312')
const qty = ref('1240')
</script>

<template>
  <NvScreenInput v-model="wo" placeholder="工单号" />
  <NvScreenInput v-model="qty" suffix="件" placeholder="计划产量" />
</template>
```

## 错误与禁用

`error` 标红提示校验失败;`disabled` 整体淡出并禁止输入。

<ScreenDemo>
  <div style="display:flex;flex-direction:column;gap:12px;width:280px">
    <NvScreenInput v-model="bad" error placeholder="工单号必填" />
    <NvScreenInput model-value="装配线 B" disabled />
  </div>
</ScreenDemo>

```vue
<NvScreenInput v-model="bad" error placeholder="工单号必填" />
<NvScreenInput model-value="装配线 B" disabled />
```

## 属性

| 属性          | 说明                          | 类型      | 默认       |
| ------------- | ----------------------------- | --------- | ---------- |
| `v-model`     | 输入值                        | `string`  | `''`       |
| `error`       | 错误态(标红 + `aria-invalid`) | `boolean` | `false`    |
| `placeholder` | 占位文本                      | `string`  | `'请输入'` |
| `suffix`      | 右侧单位                      | `string`  | —          |
| `disabled`    | 禁用                          | `boolean` | `false`    |
