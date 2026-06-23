---
title: Radio 单选框
---

<script setup>
import { RadioGroupPro, RadioGroupProItem } from '@nerv-iip/ui'
import { ref } from 'vue'

const radioValue = ref('std')
const priority = ref('normal')
</script>

# Radio 单选框

在一组互斥选项中单选。`RadioGroupPro` 承载选中值，`RadioGroupProItem` 为每个选项。

## 基础用法

<Demo>
  <RadioGroupPro v-model="radioValue">
    <RadioGroupProItem value="std">标准全检</RadioGroupProItem>
    <RadioGroupProItem value="sample">抽样检验</RadioGroupProItem>
    <RadioGroupProItem value="exempt">免检放行</RadioGroupProItem>
  </RadioGroupPro>
</Demo>

```vue
<RadioGroupPro v-model="radioValue">
  <RadioGroupProItem value="std">标准全检</RadioGroupProItem>
  <RadioGroupProItem value="sample">抽样检验</RadioGroupProItem>
  <RadioGroupProItem value="exempt">免检放行</RadioGroupProItem>
</RadioGroupPro>
```

## 含禁用项

<Demo>
  <RadioGroupPro v-model="priority">
    <RadioGroupProItem value="low">低优先级</RadioGroupProItem>
    <RadioGroupProItem value="normal">普通</RadioGroupProItem>
    <RadioGroupProItem value="high">高优先级</RadioGroupProItem>
    <RadioGroupProItem value="urgent" disabled>加急（需主管授权）</RadioGroupProItem>
  </RadioGroupPro>
</Demo>

```vue
<RadioGroupPro v-model="priority">
  <RadioGroupProItem value="normal">普通</RadioGroupProItem>
  <RadioGroupProItem value="high">高优先级</RadioGroupProItem>
  <RadioGroupProItem value="urgent" disabled>加急（需主管授权）</RadioGroupProItem>
</RadioGroupPro>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定选中值（`RadioGroupPro`） | `string` | — |
| `value` | 选项值（`RadioGroupProItem`） | `string` | — |
| `disabled` | 是否禁用该项（`RadioGroupProItem`） | `boolean` | `false` |
