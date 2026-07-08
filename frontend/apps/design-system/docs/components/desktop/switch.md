---
title: NvSwitch 开关
---

<script setup>
import { NvSwitch } from '@nerv-iip/ui'
import { ref } from 'vue'

const switchOn = ref(true)
const autoDispatch = ref(false)
</script>

# NvSwitch 开关

切换单个布尔状态，即时生效。`NvSwitch` 选中态走品牌色，含平滑滑块过渡。

## 基础用法

<Demo>
  <div style="display:flex;align-items:center;justify-content:space-between;max-width:280px">
    <span style="font-size:14px">加急插单</span>
    <NvSwitch v-model="switchOn" />
  </div>
</Demo>

```vue
<div style="display:flex;align-items:center;justify-content:space-between">
  <span>加急插单</span>
  <NvSwitch v-model="switchOn" />
</div>
```

## 多项与禁用

<Demo>
  <div style="display:flex;flex-direction:column;gap:12px;max-width:280px;font-size:14px">
    <div style="display:flex;align-items:center;justify-content:space-between">
      <span>自动派工</span>
      <NvSwitch v-model="autoDispatch" />
    </div>
    <div style="display:flex;align-items:center;justify-content:space-between;opacity:.6">
      <span>夜班自动报工（暂不可用）</span>
      <NvSwitch :disabled="true" />
    </div>
  </div>
</Demo>

```vue
<NvSwitch v-model="autoDispatch" />
<NvSwitch disabled />
```

## 属性

| 属性       | 说明     | 类型      | 默认    |
| ---------- | -------- | --------- | ------- |
| `v-model`  | 是否开启 | `boolean` | `false` |
| `disabled` | 是否禁用 | `boolean` | `false` |
