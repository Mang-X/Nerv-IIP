---
title: Field 表单字段
---

<script setup>
import {
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  FieldProDescription,
  FieldProError,
  InputPro,
} from '@nerv-iip/ui'
import { ref } from 'vue'

const orderNo = ref('WO-2406-0431')
const planQty = ref('')
</script>

# Field 表单字段

为表单控件提供语义与布局骨架。`FieldPro` 串联标签、描述与错误提示，`FieldProGroup` 在纵向上把多个字段对齐成统一的表单节奏。

## 基础用法

`FieldProGroup` 内放置多个 `FieldPro`。每个字段由 `FieldProLabel`（通过 `for` 关联控件）、控件本体与 `FieldProDescription` / `FieldProError` 组成。

<Demo>
  <div style="width: 360px; max-width: 100%">
    <FieldProGroup>
      <FieldPro>
        <FieldProLabel for="field-order-no">工单号</FieldProLabel>
        <InputPro id="field-order-no" v-model="orderNo" placeholder="请输入工单号" />
        <FieldProDescription>派工后将以此编号锁定物料并生成领料单。</FieldProDescription>
      </FieldPro>
      <FieldPro data-invalid="true">
        <FieldProLabel for="field-plan-qty">计划数量</FieldProLabel>
        <InputPro id="field-plan-qty" v-model="planQty" invalid placeholder="请输入计划数量" />
        <FieldProError>计划数量不能为空。</FieldProError>
      </FieldPro>
    </FieldProGroup>
  </div>
</Demo>

```vue
<script setup>
import {
  FieldPro, FieldProGroup, FieldProLabel,
  FieldProDescription, FieldProError, InputPro,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const orderNo = ref('WO-2406-0431')
const planQty = ref('')
</script>

<template>
  <FieldProGroup>
    <FieldPro>
      <FieldProLabel for="field-order-no">工单号</FieldProLabel>
      <InputPro id="field-order-no" v-model="orderNo" placeholder="请输入工单号" />
      <FieldProDescription>派工后将以此编号锁定物料并生成领料单。</FieldProDescription>
    </FieldPro>
    <FieldPro data-invalid="true">
      <FieldProLabel for="field-plan-qty">计划数量</FieldProLabel>
      <InputPro id="field-plan-qty" v-model="planQty" invalid placeholder="请输入计划数量" />
      <FieldProError>计划数量不能为空。</FieldProError>
    </FieldPro>
  </FieldProGroup>
</template>
```

## 组成

| 组件 | 说明 |
|---|---|
| `FieldProGroup` | 字段组容器，纵向堆叠并统一字段间距 |
| `FieldPro` | 单个字段根容器，支持 `orientation` 切换横纵布局 |
| `FieldProLabel` | 字段标签（基于原版 `Label`，可经 `for` 关联控件） |
| `FieldProContent` | 字段内容区，包裹控件并撑满剩余空间 |
| `FieldProDescription` | 辅助说明文案 |
| `FieldProError` | 错误提示，可经默认插槽或 `errors` 传入 |
| `FieldProSet` / `FieldProLegend` | `fieldset` / `legend`，用于多控件分组（如复选 / 单选组） |
| `FieldProTitle` | 分组标题 |
| `FieldProSeparator` | 字段间分隔线，可带内嵌文案 |

## 属性

| 属性 | 所属 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `orientation` | `FieldPro` | 字段排布方向 | `'vertical' \| 'horizontal' \| 'responsive'` | `'vertical'` |
| `for` | `FieldProLabel` | 关联控件的 `id`（原生属性透传） | `string` | — |
| `errors` | `FieldProError` | 错误信息列表；省略时取默认插槽内容 | `Array<string \| { message?: string }>` | — |
| `variant` | `FieldProLegend` | 图例样式 | `'legend' \| 'label'` | `'legend'` |
