---
title: Select 选择器
---

<script setup>
import {
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
} from '@nerv-iip/ui'
import { ref } from 'vue'

const lineValue = ref('line-a')
const statusValue = ref()
</script>

# Select 选择器

从预设选项中单选。`SelectPro` 由触发器、值占位与下拉内容组合而成，保持品牌聚焦与一致的浮层质感。

## 基础用法

<Demo>
  <div style="max-width: 280px">
    <SelectPro v-model="lineValue">
      <SelectProTrigger><SelectProValue placeholder="选择产线" /></SelectProTrigger>
      <SelectProContent>
        <SelectProItem value="line-a">A 线 · 精密加工</SelectProItem>
        <SelectProItem value="line-b">B 线 · 锻压</SelectProItem>
        <SelectProItem value="line-c">C 线 · 总装</SelectProItem>
      </SelectProContent>
    </SelectPro>
  </div>
</Demo>

```vue
<SelectPro v-model="lineValue">
  <SelectProTrigger><SelectProValue placeholder="选择产线" /></SelectProTrigger>
  <SelectProContent>
    <SelectProItem value="line-a">A 线 · 精密加工</SelectProItem>
    <SelectProItem value="line-b">B 线 · 锻压</SelectProItem>
    <SelectProItem value="line-c">C 线 · 总装</SelectProItem>
  </SelectProContent>
</SelectPro>
```

## 占位与未选中

<Demo>
  <div style="max-width: 280px">
    <SelectPro v-model="statusValue">
      <SelectProTrigger><SelectProValue placeholder="选择工单状态" /></SelectProTrigger>
      <SelectProContent>
        <SelectProItem value="running">执行中</SelectProItem>
        <SelectProItem value="ready">可开工</SelectProItem>
        <SelectProItem value="completed">已完成</SelectProItem>
        <SelectProItem value="blocked">阻塞</SelectProItem>
      </SelectProContent>
    </SelectPro>
  </div>
</Demo>

```vue
<SelectPro v-model="statusValue">
  <SelectProTrigger><SelectProValue placeholder="选择工单状态" /></SelectProTrigger>
  <SelectProContent>
    <SelectProItem value="running">执行中</SelectProItem>
    <SelectProItem value="completed">已完成</SelectProItem>
  </SelectProContent>
</SelectPro>
```

## 属性

| 组件 | 说明 |
|---|---|
| `SelectPro` | 根容器，承载 `v-model` 绑定选中值 |
| `SelectProTrigger` | 触发器，点击展开下拉 |
| `SelectProValue` | 值展示，`placeholder` 为未选中占位文本 |
| `SelectProContent` | 下拉浮层容器 |
| `SelectProItem` | 选项，`value` 为选项值 |

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `v-model` | 绑定选中值（`SelectPro`） | `string` | — |
| `placeholder` | 未选中占位（`SelectProValue`） | `string` | — |
| `value` | 选项值（`SelectProItem`） | `string` | — |
