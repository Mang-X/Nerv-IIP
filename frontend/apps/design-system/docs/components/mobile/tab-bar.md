---
layout: page
title: TabBar 标签栏
---

<script setup>
import { NvTabBar } from '@nerv-iip/ui-mobile'
import { ClipboardListIcon, ScanLineIcon, UserIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const tab = ref('orders')
const tabs = [
  { value: 'orders', label: '工单', icon: ClipboardListIcon },
  { value: 'scan', label: '扫码', icon: ScanLineIcon },
  { value: 'me', label: '我的', icon: UserIcon },
]
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="mx-3 overflow-hidden rounded-xl border border-border bg-card">
      <NvTabBar v-model="tab" :items="tabs" />
    </div>
    <p class="mt-2 px-4 text-sm text-muted-foreground">当前：{{ tab }}</p>
  </section>
</template>

# NvTabBar 标签栏

底部主导航（tdesign-mobile 风格）：每个标签为图标 + 文字，选中态使用品牌色，触控区 ≥ 48px。

## 基础用法

底部固定主导航，点击切换选中态。

```vue
<script setup lang="ts">
import { NvTabBar } from '@nerv-iip/ui-mobile'
import { ClipboardListIcon, ScanLineIcon, UserIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const tab = ref('orders')
const tabs = [
  { value: 'orders', label: '工单', icon: ClipboardListIcon },
  { value: 'scan', label: '扫码', icon: ScanLineIcon },
  { value: 'me', label: '我的', icon: UserIcon },
]
</script>

<template>
  <NvTabBar v-model="tab" :items="tabs" />
</template>
```

## 属性

| 属性         | 说明                            | 类型        | 默认 |
| ------------ | ------------------------------- | ----------- | ---- |
| `modelValue` | 当前选中的 `value`（`v-model`） | `string`    | —    |
| `items`      | 标签项数组                      | `TabItem[]` | —    |

`TabItem`：`{ value: string; label: string; icon?: Component }`。事件：`@update:modelValue`。

</MobileDoc>
