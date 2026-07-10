---
title: NvScreenSwitch 开关
---

<script setup>
import { ref } from 'vue'
import { NvScreenSwitch } from '@nerv-iip/ui'

const auto = ref(true)
const alarm = ref(false)
</script>

# NvScreenSwitch 开关

大屏开关:关态是一口下沉的细线轨道,开态填青色带柔光并把手柄滑到另一端。按下手柄微微压扁(无回弹)。状态同时由填充与手柄位置承载 —— 绝不只靠颜色,`aria-checked` 对辅助技术保持诚实。通过 `v-model`(布尔)绑定,基于独立的 `--nv-scr-*` 令牌。

## 基础用法

`v-model` 绑定布尔值。

<ScreenDemo>
  <div style="display:flex;align-items:center;gap:24px">
    <label style="display:flex;align-items:center;gap:10px;color:var(--nv-scr-text-2);font-size:14px">
      <NvScreenSwitch v-model="auto" /> 自动排产
    </label>
    <label style="display:flex;align-items:center;gap:10px;color:var(--nv-scr-text-2);font-size:14px">
      <NvScreenSwitch v-model="alarm" /> 声光报警
    </label>
  </div>
</ScreenDemo>

```vue
<script setup>
const auto = ref(true)
</script>

<template><NvScreenSwitch v-model="auto" /> 自动排产</template>
```

## 禁用态

`disabled` 整体淡出并禁止切换,开、关两态均可禁用。

<ScreenDemo>
  <div style="display:flex;align-items:center;gap:24px">
    <NvScreenSwitch :model-value="true" disabled />
    <NvScreenSwitch :model-value="false" disabled />
  </div>
</ScreenDemo>

```vue
<NvScreenSwitch :model-value="true" disabled />
<NvScreenSwitch :model-value="false" disabled />
```

## 属性

| 属性       | 说明     | 类型      | 默认    |
| ---------- | -------- | --------- | ------- |
| `v-model`  | 开关状态 | `boolean` | `false` |
| `disabled` | 禁用     | `boolean` | `false` |
