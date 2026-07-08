---
layout: page
title: SearchBar 搜索栏
---

<script setup>
import { NvSearchBar } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const keyword = ref('')
const keyword2 = ref('')
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <NvSearchBar v-model="keyword" placeholder="搜索工单 / 物料 / 设备" />
  </section>
  <section>
    <p class="ds-mdoc-label">可取消</p>
    <NvSearchBar v-model="keyword2" cancelable placeholder="搜索工单 / 物料 / 设备" />
  </section>
</template>

# SearchBar 搜索栏

圆角胶囊搜索框（Vant / tdesign-mobile 风格）。聚焦时「取消」按钮滑入、输入框平滑收缩；有文本时淡入清除按钮。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

`v-model` 绑定关键词，回车触发 `@search`。

```vue
<NvSearchBar v-model="keyword" placeholder="搜索工单 / 物料 / 设备" />
```

## 可取消

传 `cancelable`，聚焦时滑入「取消」按钮，点击触发 `@cancel`。

```vue
<NvSearchBar
  v-model="keyword"
  cancelable
  placeholder="搜索工单 / 物料 / 设备"
  @search="onSearch"
  @cancel="onCancel"
/>
```

## 属性

| 属性          | 说明               | 类型      | 默认     |
| ------------- | ------------------ | --------- | -------- |
| `v-model`     | 搜索关键词         | `string`  | `''`     |
| `placeholder` | 占位文本           | `string`  | `'搜索'` |
| `cancelable`  | 聚焦时显示取消按钮 | `boolean` | `false`  |

| 事件     | 说明         | 回调参数          |
| -------- | ------------ | ----------------- |
| `search` | 回车确认搜索 | `(value: string)` |
| `cancel` | 点击取消     | —                 |

</MobileDoc>
