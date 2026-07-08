---
layout: page
title: NvMobileSwitch 开关
---

<script setup>
import { NvCell, NvCellGroup, NvMobileSwitch } from '@nerv-iip/ui-mobile'
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
      <NvMobileSwitch v-model="rush" />
      <NvMobileSwitch v-model="autoStock" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">配合单元格</p>
    <NvCellGroup>
      <NvCell title="加急插单">
        <template #value><NvMobileSwitch v-model="rush" /></template>
      </NvCell>
      <NvCell title="完工自动入库">
        <template #value><NvMobileSwitch v-model="autoStock" /></template>
      </NvCell>
    </NvCellGroup>
  </section>
  <section>
    <p class="ds-mdoc-label">禁用</p>
    <NvMobileSwitch v-model="locked" disabled />
  </section>
</template>

# NvMobileSwitch 开关

iOS 比例（51×31）的开关，拨动带弹性滑动，触摸端读起来如原生控件。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`v-model` 绑定布尔状态。

```vue
<NvMobileSwitch v-model="rush" />
```

## 配合单元格

嵌入 `NvCell` 的 `#value` 插槽，构成设置项行。

```vue
<NvCellGroup>
  <NvCell title="加急插单">
    <template #value><NvMobileSwitch v-model="rush" /></template>
  </NvCell>
</NvCellGroup>
```

## 禁用

传 `disabled` 禁止拨动。

```vue
<NvMobileSwitch v-model="value" disabled />
```

## 属性

| 属性       | 说明     | 类型      | 默认    |
| ---------- | -------- | --------- | ------- |
| `v-model`  | 开关状态 | `boolean` | `false` |
| `disabled` | 是否禁用 | `boolean` | `false` |

</MobileDoc>
