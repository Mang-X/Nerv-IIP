---
title: NvSelect 选择器
---

<script setup>
import {
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import { ref } from 'vue'

const lineValue = ref('line-a')
const statusValue = ref()
</script>

# NvSelect 选择器

从预设选项中单选。`NvSelect` 由触发器、值占位与下拉内容组合而成，保持品牌聚焦与一致的浮层质感。

## 基础用法

<Demo>
  <div style="max-width: 280px">
    <NvSelect v-model="lineValue">
      <NvSelectTrigger><NvSelectValue placeholder="选择产线" /></NvSelectTrigger>
      <NvSelectContent>
        <NvSelectItem value="line-a">A 线 · 精密加工</NvSelectItem>
        <NvSelectItem value="line-b">B 线 · 锻压</NvSelectItem>
        <NvSelectItem value="line-c">C 线 · 总装</NvSelectItem>
      </NvSelectContent>
    </NvSelect>
  </div>
</Demo>

```vue
<NvSelect v-model="lineValue">
  <NvSelectTrigger><NvSelectValue placeholder="选择产线" /></NvSelectTrigger>
  <NvSelectContent>
    <NvSelectItem value="line-a">A 线 · 精密加工</NvSelectItem>
    <NvSelectItem value="line-b">B 线 · 锻压</NvSelectItem>
    <NvSelectItem value="line-c">C 线 · 总装</NvSelectItem>
  </NvSelectContent>
</NvSelect>
```

## 占位与未选中

<Demo>
  <div style="max-width: 280px">
    <NvSelect v-model="statusValue">
      <NvSelectTrigger><NvSelectValue placeholder="选择工单状态" /></NvSelectTrigger>
      <NvSelectContent>
        <NvSelectItem value="running">执行中</NvSelectItem>
        <NvSelectItem value="ready">可开工</NvSelectItem>
        <NvSelectItem value="completed">已完成</NvSelectItem>
        <NvSelectItem value="blocked">阻塞</NvSelectItem>
      </NvSelectContent>
    </NvSelect>
  </div>
</Demo>

```vue
<NvSelect v-model="statusValue">
  <NvSelectTrigger><NvSelectValue placeholder="选择工单状态" /></NvSelectTrigger>
  <NvSelectContent>
    <NvSelectItem value="running">执行中</NvSelectItem>
    <NvSelectItem value="completed">已完成</NvSelectItem>
  </NvSelectContent>
</NvSelect>
```

## 属性

| 组件              | 说明                                   |
| ----------------- | -------------------------------------- |
| `NvSelect`        | 根容器，承载 `v-model` 绑定选中值      |
| `NvSelectTrigger` | 触发器，点击展开下拉                   |
| `NvSelectValue`   | 值展示，`placeholder` 为未选中占位文本 |
| `NvSelectContent` | 下拉浮层容器                           |
| `NvSelectItem`    | 选项，`value` 为选项值                 |

| 属性          | 说明                          | 类型     | 默认 |
| ------------- | ----------------------------- | -------- | ---- |
| `v-model`     | 绑定选中值（`NvSelect`）      | `string` | —    |
| `placeholder` | 未选中占位（`NvSelectValue`） | `string` | —    |
| `value`       | 选项值（`NvSelectItem`）      | `string` | —    |
