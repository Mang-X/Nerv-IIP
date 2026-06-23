---
title: Input 输入框
---

<script setup>
import { InputPro } from '@nerv-iip/ui'
import { SearchIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const searchValue = ref('')
const codeValue = ref('WO-2406-0413')
</script>

# Input 输入框

接收单行文本输入。`InputPro` 在原版基础上叠加前后缀插槽、品牌聚焦环与一致的禁用 / 错误态。

## 基础用法

<Demo>
  <div style="max-width: 320px">
    <InputPro v-model="codeValue" placeholder="请输入工单号" />
  </div>
</Demo>

```vue
<InputPro v-model="value" placeholder="请输入工单号" />
```

## 前后缀插槽

<Demo>
  <div style="max-width: 320px">
    <InputPro v-model="searchValue" placeholder="搜索工单号 / 产品">
      <template #leading><SearchIcon aria-hidden="true" /></template>
      <template #trailing>
        <kbd style="border:1px solid var(--vp-c-divider);border-radius:4px;padding:1px 6px;font-family:monospace;font-size:10px">⌘K</kbd>
      </template>
    </InputPro>
  </div>
</Demo>

```vue
<InputPro v-model="searchValue" placeholder="搜索工单号 / 产品">
  <template #leading><SearchIcon aria-hidden="true" /></template>
  <template #trailing><kbd>⌘K</kbd></template>
</InputPro>
```

## 错误态与禁用

<Demo>
  <div style="display:flex;flex-direction:column;gap:12px;max-width:320px">
    <InputPro :model-value="'数量不能为空'" invalid />
    <InputPro :model-value="'WC-CNC-07'" disabled />
  </div>
</Demo>

```vue
<InputPro v-model="value" invalid />
<InputPro v-model="value" disabled />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定值 | `string \| number` | — |
| `invalid` | 是否为错误态 | `boolean` | `false` |
| `disabled` | 是否禁用（原生属性透传） | `boolean` | `false` |
| `placeholder` | 占位文本（原生属性透传） | `string` | — |

| 插槽 | 说明 |
|---|---|
| `leading` | 输入框前缀（图标等） |
| `trailing` | 输入框后缀（图标、快捷键提示等） |
