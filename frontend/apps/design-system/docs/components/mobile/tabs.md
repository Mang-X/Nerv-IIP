---
layout: page
title: MobileTabs 顶部标签
---

<script setup>
import { MobileTabs } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const topTab = ref('doing')
const topTabs = [
  { value: 'all', label: '全部' },
  { value: 'doing', label: '进行中' },
  { value: 'done', label: '已完成' },
  { value: 'blocked', label: '阻塞' },
]
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <MobileTabs v-model="topTab" :items="topTabs" />
    <p class="mt-3 px-4 text-sm text-muted-foreground">当前分类：{{ topTab }}</p>
  </section>
</template>

# MobileTabs 顶部标签

顶部内容分类标签，品牌色下划线在标签间滑动切换（Vant / tdesign-mobile 风格），可横向滚动。

## 基础用法

顶部分类标签，下划线随选中项滑动。

```vue
<script setup lang="ts">
import { MobileTabs } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const topTab = ref('doing')
const topTabs = [
  { value: 'all', label: '全部' },
  { value: 'doing', label: '进行中' },
  { value: 'done', label: '已完成' },
  { value: 'blocked', label: '阻塞' },
]
</script>

<template>
  <MobileTabs v-model="topTab" :items="topTabs" />
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `modelValue` | 当前选中的 `value`（`v-model`，必填） | `string` | — |
| `items` | 标签项数组 | `MobileTabItem[]` | — |

`MobileTabItem`：`{ value: string; label: string }`。

</MobileDoc>
