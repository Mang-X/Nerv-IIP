---
layout: page
title: NvMobileRadioGroup 单选框
---

<script setup>
import { NvMobileRadioGroup, NvMobileRadioItem } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const inspect = ref('std')
const priority = ref('normal')
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <NvMobileRadioGroup v-model="inspect">
      <NvMobileRadioItem value="std">标准全检</NvMobileRadioItem>
      <NvMobileRadioItem value="sample">抽样检验</NvMobileRadioItem>
      <NvMobileRadioItem value="exempt">免检放行</NvMobileRadioItem>
    </NvMobileRadioGroup>
  </section>
  <section>
    <p class="ds-mdoc-label">含禁用项</p>
    <NvMobileRadioGroup v-model="priority">
      <NvMobileRadioItem value="low">低优先级</NvMobileRadioItem>
      <NvMobileRadioItem value="normal">普通</NvMobileRadioItem>
      <NvMobileRadioItem value="high">高优先级</NvMobileRadioItem>
      <NvMobileRadioItem value="urgent" disabled>加急（需主管授权）</NvMobileRadioItem>
    </NvMobileRadioGroup>
  </section>
</template>

# NvMobileRadioGroup 单选框

在一组互斥选项中单选，呈 iOS 设置项样式：标签居左，选中项右侧显示品牌勾选。`NvMobileRadioGroup` 承载选中值，`NvMobileRadioItem` 为每个选项。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`NvMobileRadioGroup` 的 `v-model` 绑定当前选中项的 `value`。

```vue
<NvMobileRadioGroup v-model="inspect">
  <NvMobileRadioItem value="std">标准全检</NvMobileRadioItem>
  <NvMobileRadioItem value="sample">抽样检验</NvMobileRadioItem>
  <NvMobileRadioItem value="exempt">免检放行</NvMobileRadioItem>
</NvMobileRadioGroup>
```

## 含禁用项

单个 `NvMobileRadioItem` 传 `disabled` 即可禁止选择。

```vue
<NvMobileRadioGroup v-model="priority">
  <NvMobileRadioItem value="normal">普通</NvMobileRadioItem>
  <NvMobileRadioItem value="high">高优先级</NvMobileRadioItem>
  <NvMobileRadioItem value="urgent" disabled>加急（需主管授权）</NvMobileRadioItem>
</NvMobileRadioGroup>
```

## 属性

| 属性       | 说明                                | 类型      | 默认    |
| ---------- | ----------------------------------- | --------- | ------- |
| `v-model`  | 绑定选中值（`NvMobileRadioGroup`）  | `string`  | —       |
| `value`    | 选项值（`NvMobileRadioItem`）       | `string`  | —       |
| `disabled` | 是否禁用该项（`NvMobileRadioItem`） | `boolean` | `false` |

| 插槽                             | 说明         |
| -------------------------------- | ------------ |
| `default`（`NvMobileRadioItem`） | 选项标签内容 |

</MobileDoc>
