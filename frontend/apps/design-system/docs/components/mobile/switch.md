---
title: Switch 开关
---

<script setup>
import { Cell, CellGroup, MobileSwitch } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const rush = ref(true)
const autoStock = ref(false)
const locked = ref(true)
</script>

# Switch 开关

iOS 比例（51×31）的开关，拨动带弹性滑动，触摸端读起来如原生控件。

## 基础用法

<Demo mobile>
  <div style="display:flex;align-items:center;gap:16px">
    <MobileSwitch v-model="rush" />
    <MobileSwitch v-model="autoStock" />
  </div>
</Demo>

```vue
<MobileSwitch v-model="rush" />
```

## 配合单元格

<Demo mobile>
  <CellGroup>
    <Cell title="加急插单">
      <template #value><MobileSwitch v-model="rush" /></template>
    </Cell>
    <Cell title="完工自动入库">
      <template #value><MobileSwitch v-model="autoStock" /></template>
    </Cell>
  </CellGroup>
</Demo>

```vue
<CellGroup>
  <Cell title="加急插单">
    <template #value><MobileSwitch v-model="rush" /></template>
  </Cell>
</CellGroup>
```

## 禁用

<Demo mobile>
  <MobileSwitch v-model="locked" disabled />
</Demo>

```vue
<MobileSwitch v-model="value" disabled />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 开关状态 | `boolean` | `false` |
| `disabled` | 是否禁用 | `boolean` | `false` |
