---
title: ScreenSearch 搜索框
---

<script setup>
import { ref } from 'vue'
import { NvScreenSearch } from '@nerv-iip/ui'

const kw = ref('WO-2406-0312')
const empty = ref('')
</script>

# ScreenSearch 搜索框

大屏搜索框:lucide 放大镜锚定左侧,聚焦时井口点亮青色环;一旦有文本,右侧浮出清除按钮(×)一键抹除。通过 `v-model`(字符串)绑定,基于独立的 `--sb-*` 令牌。

## 基础用法

`v-model` 绑定关键词;有内容时自动显示清除按钮。

<ScreenDemo>
  <div style="display:flex;flex-direction:column;gap:12px;width:320px">
    <NvScreenSearch v-model="kw" placeholder="搜索工单号或产品" />
    <NvScreenSearch v-model="empty" placeholder="搜索工单号或产品" />
  </div>
</ScreenDemo>

```vue
<script setup>
const kw = ref('WO-2406-0312')
</script>

<template>
  <NvScreenSearch v-model="kw" placeholder="搜索工单号或产品" />
</template>
```

## 禁用态

`disabled` 整体淡出并禁止输入。

<ScreenDemo>
  <div style="width:320px">
    <NvScreenSearch model-value="" disabled placeholder="检索已锁定" />
  </div>
</ScreenDemo>

```vue
<NvScreenSearch disabled placeholder="检索已锁定" />
```

## 属性

| 属性          | 说明       | 类型      | 默认                         |
| ------------- | ---------- | --------- | ---------------------------- |
| `v-model`     | 搜索关键词 | `string`  | `''`                         |
| `placeholder` | 占位文本   | `string`  | `'搜索工单号 / 产线 / 设备'` |
| `disabled`    | 禁用       | `boolean` | `false`                      |
