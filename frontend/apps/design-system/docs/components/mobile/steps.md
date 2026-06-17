---
layout: page
title: Steps 步骤条
---

<script setup>
import { Steps } from '@nerv-iip/ui-mobile'

const procSteps = [
  { label: '下料' },
  { label: '加工', note: '进行中' },
  { label: '质检' },
  { label: '入库' },
]
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="px-3">
      <Steps :steps="procSteps" :current="1" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">全部完成</p>
    <div class="px-3">
      <Steps :steps="procSteps" :current="4" />
    </div>
  </section>
</template>

# Steps 步骤条

横向流程指示器（Vant / tdesign-mobile 风格）：连接线随进度推进填充品牌色，已完成节点显示对勾。

## 基础用法

`current` 指定当前步骤索引，之前的节点显示对勾。

```vue
<script setup lang="ts">
import { Steps } from '@nerv-iip/ui-mobile'

const procSteps = [
  { label: '下料' },
  { label: '加工', note: '进行中' },
  { label: '质检' },
  { label: '入库' },
]
</script>

<template>
  <Steps :steps="procSteps" :current="1" />
</template>
```

## 全部完成

`current` 超过末位索引时，所有节点均为完成态。

```vue
<Steps :steps="procSteps" :current="4" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `steps` | 步骤数组 | `StepItem[]` | — |
| `current` | 当前步骤索引（从 0 开始） | `number` | `0` |

`StepItem`：`{ label: string; note?: string }`。`note` 显示在标签下方并使用品牌色。

</MobileDoc>
