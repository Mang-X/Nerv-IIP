---
title: Radio 单选框
---

<script setup>
import { NvRadioGroup, NvRadioGroupItem } from '@nerv-iip/ui'
import { ref } from 'vue'

const radioValue = ref('std')
const priority = ref('normal')
</script>

# Radio 单选框

在一组互斥选项中单选。`NvRadioGroup` 承载选中值，`NvRadioGroupItem` 为每个选项。

## 基础用法

<Demo>
  <NvRadioGroup v-model="radioValue">
    <NvRadioGroupItem value="std">标准全检</NvRadioGroupItem>
    <NvRadioGroupItem value="sample">抽样检验</NvRadioGroupItem>
    <NvRadioGroupItem value="exempt">免检放行</NvRadioGroupItem>
  </NvRadioGroup>
</Demo>

```vue
<NvRadioGroup v-model="radioValue">
  <NvRadioGroupItem value="std">标准全检</NvRadioGroupItem>
  <NvRadioGroupItem value="sample">抽样检验</NvRadioGroupItem>
  <NvRadioGroupItem value="exempt">免检放行</NvRadioGroupItem>
</NvRadioGroup>
```

## 含禁用项

<Demo>
  <NvRadioGroup v-model="priority">
    <NvRadioGroupItem value="low">低优先级</NvRadioGroupItem>
    <NvRadioGroupItem value="normal">普通</NvRadioGroupItem>
    <NvRadioGroupItem value="high">高优先级</NvRadioGroupItem>
    <NvRadioGroupItem value="urgent" disabled>加急（需主管授权）</NvRadioGroupItem>
  </NvRadioGroup>
</Demo>

```vue
<NvRadioGroup v-model="priority">
  <NvRadioGroupItem value="normal">普通</NvRadioGroupItem>
  <NvRadioGroupItem value="high">高优先级</NvRadioGroupItem>
  <NvRadioGroupItem value="urgent" disabled>加急（需主管授权）</NvRadioGroupItem>
</NvRadioGroup>
```

## 属性

| 属性       | 说明                               | 类型      | 默认    |
| ---------- | ---------------------------------- | --------- | ------- |
| `v-model`  | 绑定选中值（`NvRadioGroup`）       | `string`  | —       |
| `value`    | 选项值（`NvRadioGroupItem`）       | `string`  | —       |
| `disabled` | 是否禁用该项（`NvRadioGroupItem`） | `boolean` | `false` |
