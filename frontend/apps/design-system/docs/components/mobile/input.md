---
title: Input 输入框
---

<script setup>
import { MobileInput } from '@nerv-iip/ui-mobile'
import { ScanLineIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const search = ref('')
const code = ref('WO-2406-0413')
</script>

# Input 输入框

移动端单行文本输入。44px 触摸高度、15px 字号（避免 iOS 聚焦缩放），支持前后缀插槽与品牌聚焦环。

## 基础用法

<Demo mobile>
  <MobileInput v-model="code" placeholder="请输入工单号" />
</Demo>

```vue
<MobileInput v-model="value" placeholder="请输入工单号" />
```

## 前缀插槽

<Demo mobile>
  <MobileInput v-model="search" placeholder="搜索工单 / 物料">
    <template #leading><ScanLineIcon aria-hidden="true" /></template>
  </MobileInput>
</Demo>

```vue
<MobileInput v-model="search" placeholder="搜索工单 / 物料">
  <template #leading><ScanLineIcon aria-hidden="true" /></template>
</MobileInput>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定值 | `string \| number` | — |
| `defaultValue` | 非受控默认值 | `string \| number` | — |
| `placeholder` | 占位文本（原生属性透传） | `string` | — |

| 插槽 | 说明 |
|---|---|
| `leading` | 输入框前缀（图标等） |
| `trailing` | 输入框后缀（图标等） |
