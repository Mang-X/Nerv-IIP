---
layout: page
title: Picker 滚轮选择
---

<script setup>
import { NvCell, NvCellGroup, NvPicker } from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'

const pickerOpen = ref(false)
const pickerLine = ref('line-a')
const pickerOptions = [
  { label: 'A 线 · 精密加工', value: 'line-a' },
  { label: 'B 线 · 锻压', value: 'line-b' },
  { label: 'C 线 · 总装', value: 'line-c' },
  { label: 'D 线 · 热处理', value: 'line-d' },
  { label: 'E 线 · 喷涂', value: 'line-e' },
]
const pickerLabel = computed(
  () => pickerOptions.find((o) => o.value === pickerLine.value)?.label ?? '请选择',
)
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <NvCellGroup>
      <NvCell title="目标产线" :value="pickerLabel" arrow @click="pickerOpen = true" />
    </NvCellGroup>
    <NvPicker
      v-model:open="pickerOpen"
      v-model="pickerLine"
      :options="pickerOptions"
      title="选择产线"
    />
  </section>
</template>

# Picker 滚轮选择

单列滚轮选择器（Vant / tdesign-mobile 风格），承载于底部抽屉。带中央高亮带的滚动吸附；取消 / 确定提交。`v-model:open` 控制显隐，`v-model` 绑定选中值，需配合触发器与 open ref 使用。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

点击单元格打开抽屉，滚动选择后确定提交。

```vue
<script setup>
const pickerOpen = ref(false)
const pickerLine = ref('line-a')
const pickerOptions = [
  { label: 'A 线 · 精密加工', value: 'line-a' },
  { label: 'B 线 · 锻压', value: 'line-b' },
  { label: 'C 线 · 总装', value: 'line-c' },
]
const pickerLabel = computed(
  () => pickerOptions.find((o) => o.value === pickerLine.value)?.label ?? '请选择',
)
</script>

<template>
  <NvCellGroup>
    <NvCell title="目标产线" :value="pickerLabel" arrow @click="pickerOpen = true" />
  </NvCellGroup>
  <NvPicker
    v-model:open="pickerOpen"
    v-model="pickerLine"
    :options="pickerOptions"
    title="选择产线"
  />
</template>
```

## 属性

| 属性           | 说明             | 类型             | 默认       |
| -------------- | ---------------- | ---------------- | ---------- |
| `v-model:open` | 抽屉显隐         | `boolean`        | `false`    |
| `v-model`      | 选中项的 `value` | `string`         | —          |
| `options`      | 选项列表         | `PickerOption[]` | —          |
| `title`        | 标题             | `string`         | `'请选择'` |

`PickerOption`：`{ label: string; value: string }`

</MobileDoc>
