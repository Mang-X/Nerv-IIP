---
title: NvCheckbox 复选框
---

<script setup>
import { NvCheckbox } from '@nerv-iip/ui'
import { ref } from 'vue'

const agreed = ref(true)
const lockMaterial = ref(false)
const genPicking = ref(true)
</script>

# NvCheckbox 复选框

在一组选项中多选，或表示单个开关式确认。`NvCheckbox` 复用品牌选中色与聚焦环。

## 基础用法

<Demo>
  <label style="display:flex;align-items:center;gap:10px;font-size:14px">
    <NvCheckbox v-model="agreed" />
    派发后立即锁定物料并生成领料单
  </label>
</Demo>

```vue
<label style="display:flex;align-items:center;gap:10px">
  <NvCheckbox v-model="agreed" />
  派发后立即锁定物料并生成领料单
</label>
```

## 多选组

<Demo>
  <div style="display:flex;flex-direction:column;gap:10px;font-size:14px">
    <label style="display:flex;align-items:center;gap:10px">
      <NvCheckbox v-model="lockMaterial" /> 锁定物料
    </label>
    <label style="display:flex;align-items:center;gap:10px">
      <NvCheckbox v-model="genPicking" /> 生成领料单
    </label>
    <label style="display:flex;align-items:center;gap:10px;opacity:.6">
      <NvCheckbox :disabled="true" /> 自动报工（暂不可用）
    </label>
  </div>
</Demo>

```vue
<label><NvCheckbox v-model="lockMaterial" /> 锁定物料</label>
<label><NvCheckbox v-model="genPicking" /> 生成领料单</label>
<label><NvCheckbox disabled /> 自动报工（暂不可用）</label>
```

## 属性

| 属性       | 说明     | 类型      | 默认    |
| ---------- | -------- | --------- | ------- |
| `v-model`  | 是否选中 | `boolean` | `false` |
| `disabled` | 是否禁用 | `boolean` | `false` |
