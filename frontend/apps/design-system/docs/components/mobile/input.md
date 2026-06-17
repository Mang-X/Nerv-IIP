---
layout: page
title: Input 输入框
---

<script setup>
import { MobileInput } from '@nerv-iip/ui-mobile'
import { ScanLineIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const search = ref('')
const code = ref('WO-2406-0413')
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <MobileInput v-model="code" placeholder="请输入工单号" />
  </section>
  <section>
    <p class="ds-mdoc-label">前缀插槽</p>
    <MobileInput v-model="search" placeholder="搜索工单 / 物料">
      <template #leading><ScanLineIcon aria-hidden="true" /></template>
    </MobileInput>
  </section>
</template>

# Input 输入框

移动端单行文本输入。44px 触摸高度、15px 字号（避免 iOS 聚焦缩放），支持前后缀插槽与品牌聚焦环。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`v-model` 双向绑定文本值。

```vue
<MobileInput v-model="value" placeholder="请输入工单号" />
```

## 前缀插槽

`#leading` 插槽嵌入前缀图标，`#trailing` 嵌入后缀图标。

```vue
<MobileInput v-model="search" placeholder="搜索工单 / 物料">
  <template #leading><ScanLineIcon aria-hidden="true" /></template>
</MobileInput>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定值 | `string \| number` | — |
| `defaultValue` | 非受控默认值 | `string \| number` | — |
| `placeholder` | 占位文本（原生属性透传） | `string` | — |

| 插槽 | 说明 |
|---|---|
| `leading` | 输入框前缀（图标等） |
| `trailing` | 输入框后缀（图标等） |

</MobileDoc>
