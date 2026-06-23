---
layout: page
title: Checkbox 复选框
---

<script setup>
import { MobileCheckbox } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const checkA = ref(true)
const checkB = ref(false)
const checkC = ref(false)
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <MobileCheckbox v-model="checkA">首检合格后转批量</MobileCheckbox>
    <MobileCheckbox v-model="checkB">完工自动生成入库单</MobileCheckbox>
  </section>
  <section>
    <p class="ds-mdoc-label">禁用</p>
    <MobileCheckbox v-model="checkC" disabled>需主管授权（暂不可选）</MobileCheckbox>
  </section>
</template>

# Checkbox 复选框

可点击的整行复选项（盒子 + 标签），≥44px 触摸行高。选中时品牌色填充并显示勾选，`v-model` 为布尔值。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

整行可点击，`v-model` 绑定布尔值。

```vue
<MobileCheckbox v-model="checkA">首检合格后转批量</MobileCheckbox>
<MobileCheckbox v-model="checkB">完工自动生成入库单</MobileCheckbox>
```

## 禁用

传 `disabled` 禁止选择。

```vue
<MobileCheckbox v-model="value" disabled>需主管授权（暂不可选）</MobileCheckbox>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 是否选中 | `boolean` | `false` |
| `disabled` | 是否禁用 | `boolean` | `false` |

| 插槽 | 说明 |
|---|---|
| `default` | 选项标签内容 |

</MobileDoc>
