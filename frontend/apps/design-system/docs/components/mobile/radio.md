---
layout: page
title: Radio 单选框
---

<script setup>
import { MobileRadioGroup, MobileRadioItem } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const inspect = ref('std')
const priority = ref('normal')
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <MobileRadioGroup v-model="inspect">
      <MobileRadioItem value="std">标准全检</MobileRadioItem>
      <MobileRadioItem value="sample">抽样检验</MobileRadioItem>
      <MobileRadioItem value="exempt">免检放行</MobileRadioItem>
    </MobileRadioGroup>
  </section>
  <section>
    <p class="ds-mdoc-label">含禁用项</p>
    <MobileRadioGroup v-model="priority">
      <MobileRadioItem value="low">低优先级</MobileRadioItem>
      <MobileRadioItem value="normal">普通</MobileRadioItem>
      <MobileRadioItem value="high">高优先级</MobileRadioItem>
      <MobileRadioItem value="urgent" disabled>加急（需主管授权）</MobileRadioItem>
    </MobileRadioGroup>
  </section>
</template>

# Radio 单选框

在一组互斥选项中单选，呈 iOS 设置项样式：标签居左，选中项右侧显示品牌勾选。`MobileRadioGroup` 承载选中值，`MobileRadioItem` 为每个选项。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`MobileRadioGroup` 的 `v-model` 绑定当前选中项的 `value`。

```vue
<MobileRadioGroup v-model="inspect">
  <MobileRadioItem value="std">标准全检</MobileRadioItem>
  <MobileRadioItem value="sample">抽样检验</MobileRadioItem>
  <MobileRadioItem value="exempt">免检放行</MobileRadioItem>
</MobileRadioGroup>
```

## 含禁用项

单个 `MobileRadioItem` 传 `disabled` 即可禁止选择。

```vue
<MobileRadioGroup v-model="priority">
  <MobileRadioItem value="normal">普通</MobileRadioItem>
  <MobileRadioItem value="high">高优先级</MobileRadioItem>
  <MobileRadioItem value="urgent" disabled>加急（需主管授权）</MobileRadioItem>
</MobileRadioGroup>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定选中值（`MobileRadioGroup`） | `string` | — |
| `value` | 选项值（`MobileRadioItem`） | `string` | — |
| `disabled` | 是否禁用该项（`MobileRadioItem`） | `boolean` | `false` |

| 插槽 | 说明 |
|---|---|
| `default`（`MobileRadioItem`） | 选项标签内容 |

</MobileDoc>
