---
title: NvField 表单字段
---

<script setup>
import {
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvFieldDescription,
  NvFieldError,
  NvInput,
} from '@nerv-iip/ui'
import { ref } from 'vue'

const orderNo = ref('WO-2406-0431')
const planQty = ref('')
</script>

# NvField 表单字段

为表单控件提供语义与布局骨架。`NvField` 串联标签、描述与错误提示，`NvFieldGroup` 在纵向上把多个字段对齐成统一的表单节奏。

## 基础用法

`NvFieldGroup` 内放置多个 `NvField`。每个字段由 `NvFieldLabel`（通过 `for` 关联控件）、控件本体与 `NvFieldDescription` / `NvFieldError` 组成。

<Demo>
  <div style="width: 360px; max-width: 100%">
    <NvFieldGroup>
      <NvField>
        <NvFieldLabel for="field-order-no">工单号</NvFieldLabel>
        <NvInput id="field-order-no" v-model="orderNo" placeholder="请输入工单号" />
        <NvFieldDescription>派工后将以此编号锁定物料并生成领料单。</NvFieldDescription>
      </NvField>
      <NvField data-invalid="true">
        <NvFieldLabel for="field-plan-qty">计划数量</NvFieldLabel>
        <NvInput id="field-plan-qty" v-model="planQty" invalid placeholder="请输入计划数量" />
        <NvFieldError>计划数量不能为空。</NvFieldError>
      </NvField>
    </NvFieldGroup>
  </div>
</Demo>

```vue
<script setup>
import {
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvFieldDescription,
  NvFieldError,
  NvInput,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const orderNo = ref('WO-2406-0431')
const planQty = ref('')
</script>

<template>
  <NvFieldGroup>
    <NvField>
      <NvFieldLabel for="field-order-no">工单号</NvFieldLabel>
      <NvInput id="field-order-no" v-model="orderNo" placeholder="请输入工单号" />
      <NvFieldDescription>派工后将以此编号锁定物料并生成领料单。</NvFieldDescription>
    </NvField>
    <NvField data-invalid="true">
      <NvFieldLabel for="field-plan-qty">计划数量</NvFieldLabel>
      <NvInput id="field-plan-qty" v-model="planQty" invalid placeholder="请输入计划数量" />
      <NvFieldError>计划数量不能为空。</NvFieldError>
    </NvField>
  </NvFieldGroup>
</template>
```

## 组成

| 组件                           | 说明                                                     |
| ------------------------------ | -------------------------------------------------------- |
| `NvFieldGroup`                 | 字段组容器，纵向堆叠并统一字段间距                       |
| `NvField`                      | 单个字段根容器，支持 `orientation` 切换横纵布局          |
| `NvFieldLabel`                 | 字段标签（基于原版 `Label`，可经 `for` 关联控件）        |
| `NvFieldContent`               | 字段内容区，包裹控件并撑满剩余空间                       |
| `NvFieldDescription`           | 辅助说明文案                                             |
| `NvFieldError`                 | 错误提示，可经默认插槽或 `errors` 传入                   |
| `NvFieldSet` / `NvFieldLegend` | `fieldset` / `legend`，用于多控件分组（如复选 / 单选组） |
| `NvFieldTitle`                 | 分组标题                                                 |
| `NvFieldSeparator`             | 字段间分隔线，可带内嵌文案                               |

## 属性

| 属性          | 所属            | 说明                               | 类型                                         | 默认         |
| ------------- | --------------- | ---------------------------------- | -------------------------------------------- | ------------ |
| `orientation` | `NvField`       | 字段排布方向                       | `'vertical' \| 'horizontal' \| 'responsive'` | `'vertical'` |
| `for`         | `NvFieldLabel`  | 关联控件的 `id`（原生属性透传）    | `string`                                     | —            |
| `errors`      | `NvFieldError`  | 错误信息列表；省略时取默认插槽内容 | `Array<string \| { message?: string }>`      | —            |
| `variant`     | `NvFieldLegend` | 图例样式                           | `'legend' \| 'label'`                        | `'legend'`   |
