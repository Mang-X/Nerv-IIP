---
title: Descriptions 描述列表
pageClass: ds-wide
aside: false
---

<script setup>
import { DescriptionsPro, StatusBadgePro } from '@nerv-iip/ui'

const items = [
  { key: 'code', label: '工单号', value: 'WO-2406-0413' },
  { label: '产品', value: '前桥壳体 A2' },
  { label: '工作中心', value: 'WC-CNC-07' },
  { label: '负责人', value: '张伟' },
  { label: '计划数量', value: '480 件' },
  { label: '已完成', value: '312 件' },
  { key: 'status', label: '状态', value: 'running' },
  { label: '优先级', value: '高' },
  { label: '交期', value: '2026-06-22' },
  { label: '备注', value: '首件需三坐标全尺寸检测，合格后方可批量。', span: 3 },
]

const clampItems = [
  { label: '首件检验工艺要求', value: '三坐标全尺寸' },
  { label: '关联物料批次号', value: 'MTL-7782-0034' },
  { label: '客户与交付地点', value: '一汽解放 · 长春' },
  { label: '首检报告存储路径', value: 'first-article-v3.pdf' },
]
</script>

# Descriptions 描述列表

工单 / 设备等实体详情页的键值列表。`DescriptionsPro` 支持多列网格、单元格跨列、`#<key>` 自定义值插槽，以及带边框的正式记录样式。

## 网格

<Demo block>
  <DescriptionsPro :items="items" :columns="3">
    <template #code="{ item }">
      <span class="font-mono text-xs">{{ item.value }}</span>
    </template>
    <template #status="{ item }">
      <StatusBadgePro :value="String(item.value)" :pulse="item.value === 'running'" />
    </template>
  </DescriptionsPro>
</Demo>

```vue
<DescriptionsPro :items="items" :columns="3">
  <template #code="{ item }">
    <span class="font-mono text-xs">{{ item.value }}</span>
  </template>
  <template #status="{ item }">
    <StatusBadgePro :value="String(item.value)" :pulse="item.value === 'running'" />
  </template>
</DescriptionsPro>
```

## 带边框

<Demo block>
  <DescriptionsPro :items="items" :columns="3" bordered>
    <template #code="{ item }">
      <span class="font-mono text-xs">{{ item.value }}</span>
    </template>
    <template #status="{ item }">
      <StatusBadgePro :value="String(item.value)" :pulse="item.value === 'running'" />
    </template>
  </DescriptionsPro>
</Demo>

```vue
<DescriptionsPro :items="items" :columns="3" bordered />
```

## 标题超长省略

`ellipsis` + `label-width`：标题单行截断，悬停弹 Tooltip 看全文。

<Demo block>
  <DescriptionsPro :items="clampItems" :columns="2" bordered ellipsis label-width="6rem" />
</Demo>

```vue
<DescriptionsPro :items="items" :columns="2" bordered ellipsis label-width="6rem" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `items` | 条目数组（`label` / `value` / `span` / `key`） | `DescriptionItem[]` | — |
| `columns` | 每行键值对数（≤640px 折叠为 1） | `number` | `2` |
| `bordered` | 带边框正式记录样式 | `boolean` | `false` |
| `layout` | 标签布局：横排或上下 | `horizontal \| vertical` | `horizontal` |
| `labelWidth` | 固定标签列宽（如 `6rem`） | `string` | — |
| `ellipsis` | 标签单行截断 + 悬停 Tooltip | `boolean` | `false` |
| `size` | 密度 | `default \| compact` | `default` |
