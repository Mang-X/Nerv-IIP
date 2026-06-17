---
layout: page
title: Switch 开关
---

<script setup>
import { Cell, CellGroup, MobileSwitch } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const rush = ref(true)
const autoStock = ref(false)
const locked = ref(true)
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div style="display:flex;align-items:center;gap:16px">
      <MobileSwitch v-model="rush" />
      <MobileSwitch v-model="autoStock" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">配合单元格</p>
    <CellGroup>
      <Cell title="加急插单">
        <template #value><MobileSwitch v-model="rush" /></template>
      </Cell>
      <Cell title="完工自动入库">
        <template #value><MobileSwitch v-model="autoStock" /></template>
      </Cell>
    </CellGroup>
  </section>
  <section>
    <p class="ds-mdoc-label">禁用</p>
    <MobileSwitch v-model="locked" disabled />
  </section>
</template>

# Switch 开关

iOS 比例（51×31）的开关，拨动带弹性滑动，触摸端读起来如原生控件。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`v-model` 绑定布尔状态。

```vue
<MobileSwitch v-model="rush" />
```

## 配合单元格

嵌入 `Cell` 的 `#value` 插槽，构成设置项行。

```vue
<CellGroup>
  <Cell title="加急插单">
    <template #value><MobileSwitch v-model="rush" /></template>
  </Cell>
</CellGroup>
```

## 禁用

传 `disabled` 禁止拨动。

```vue
<MobileSwitch v-model="value" disabled />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 开关状态 | `boolean` | `false` |
| `disabled` | 是否禁用 | `boolean` | `false` |

</MobileDoc>
