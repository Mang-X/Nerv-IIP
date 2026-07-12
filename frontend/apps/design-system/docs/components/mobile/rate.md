---
layout: page
title: NvMobileRate 评分
---

<script setup>
import { ref } from 'vue'
import { NvMobileRate } from '@nerv-iip/ui-mobile'

const score = ref(4)
const halfScore = ref(3.5)
const quality = ref(0)
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">基础（点按选择，再点当前星清零）</p>
    <NvMobileRate v-model="score" />
    <p class="mt-1 text-sm text-muted-foreground">当前评分：{{ score }} 星</p>
  </section>
  <section>
    <p class="nv-mdoc-label">半星</p>
    <NvMobileRate v-model="halfScore" allow-half />
    <p class="mt-1 text-sm text-muted-foreground">当前评分：{{ halfScore }} 星</p>
  </section>
  <section>
    <p class="nv-mdoc-label">只读</p>
    <NvMobileRate :model-value="4" readonly />
  </section>
  <section>
    <p class="nv-mdoc-label">质检评级</p>
    <div class="rounded-xl border border-border bg-card p-3">
      <div class="mb-2 flex items-center justify-between text-sm">
        <span class="font-medium text-foreground">QC-20260617-118</span>
        <span class="text-muted-foreground">外观工序</span>
      </div>
      <NvMobileRate v-model="quality" />
      <p class="mt-1 text-sm text-muted-foreground">
        {{ quality ? `已评 ${quality} 星` : '请为本批次外观评级' }}
      </p>
    </div>
  </section>
</template>

# NvMobileRate 评分

星级评分。每颗星 ≥44px 可点，激活态为琥珀色填充，点选时被点中的星有轻微弹动（已适配 `prefers-reduced-motion`）。用于满意度、质检评级等录入。右侧手机模拟器为实时组件。

## 基础

`v-model` 绑定数字。再次点击当前最高星可清零。

```vue
<NvMobileRate v-model="score" />
```

## 半星

加 `allow-half`：点击星的左半为半星，右半为整星。

```vue
<NvMobileRate v-model="halfScore" allow-half />
```

## 只读

`readonly` 用于展示既有评级，禁用交互。

```vue
<NvMobileRate :model-value="4" readonly />
```

## 属性

| 属性        | 说明     | 类型      | 默认    |
| ----------- | -------- | --------- | ------- |
| `v-model`   | 当前评分 | `number`  | `0`     |
| `count`     | 星星总数 | `number`  | `5`     |
| `readonly`  | 只读     | `boolean` | `false` |
| `allowHalf` | 允许半星 | `boolean` | `false` |

</MobileDoc>
