---
title: ScreenTabs 标签页
---

<script setup>
import { ref } from 'vue'
import { NvScreenTabs } from '@nerv-iip/ui'

const tab = ref('output')
const items = [
  { label: '产量趋势', value: 'output' },
  { label: '质量分析', value: 'quality' },
  { label: '能耗监控', value: 'energy' },
  { label: '设备状态', value: 'device' },
]
</script>

# ScreenTabs 标签页

大屏标签页:一排标签压在细基线上,激活项发青光,底部一道下划线随之平滑滑动(按索引驱动)。方向键在标签间移动。通过 `v-model` 绑定当前项 `value`,基于独立的 `--sb-*` 令牌。

## 基础用法

`items` 传 `{ label, value }`,`v-model` 持有激活的 `value`;未绑定时默认首项。

<ScreenDemo>
  <div style="width:480px">
    <NvScreenTabs v-model="tab" :items="items" />
    <div style="margin-top:16px;color:var(--sb-text-2);font-size:14px">
      当前视图:{{ items.find(i => i.value === tab)?.label }}
    </div>
  </div>
</ScreenDemo>

```vue
<script setup>
const tab = ref('output')
const items = [
  { label: '产量趋势', value: 'output' },
  { label: '质量分析', value: 'quality' },
  { label: '能耗监控', value: 'energy' },
  { label: '设备状态', value: 'device' },
]
</script>

<template>
  <NvScreenTabs v-model="tab" :items="items" />
</template>
```

## 属性

| 属性      | 说明                                | 类型                                           | 默认         |
| --------- | ----------------------------------- | ---------------------------------------------- | ------------ |
| `v-model` | 激活项的 `value`,未绑定时自动取首项 | `string \| number`                             | —            |
| `items`   | 标签列表                            | `{ label: string; value: string \| number }[]` | 内置示例视图 |
