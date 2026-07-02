---
title: Status 状态
---

<script setup>
import { StatusDot, StatusBadgePro } from '@nerv-iip/ui'
</script>

# Status 状态

表达实体的实时状态。`StatusDot` 是一个带可选脉冲环的状态点；`StatusBadgePro` 在其前置色点基础上叠加色调胶囊与本地化文案，并复用共享状态映射，原始值（如 `running`）会自动解析为标签与色调。

## 状态点 StatusDot

<Demo>
  <span class="flex items-center gap-4">
    <StatusDot tone="info" pulse />
    <StatusDot tone="success" />
    <StatusDot tone="warning" />
    <StatusDot tone="danger" />
    <StatusDot tone="neutral" />
  </span>
</Demo>

```vue
<StatusDot tone="info" pulse />
<StatusDot tone="success" />
<StatusDot tone="danger" />
```

## 状态徽标 StatusBadgePro

按原始状态值自动解析文案与色调，`pulse` 标记进行中状态。

<Demo>
  <span class="flex flex-wrap items-center gap-2">
    <StatusBadgePro value="running" pulse />
    <StatusBadgePro value="ready" />
    <StatusBadgePro value="completed" />
    <StatusBadgePro value="pending" />
    <StatusBadgePro value="blocked" />
  </span>
</Demo>

```vue
<StatusBadgePro value="running" pulse />
<StatusBadgePro value="ready" />
<StatusBadgePro value="completed" />
<StatusBadgePro value="blocked" />
```

## 属性

### StatusDot

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `tone` | 色调 | `success \| warning \| danger \| info \| neutral` | `neutral` |
| `pulse` | 显示脉冲环（活跃/在线态） | `boolean` | `false` |
| `size` | 尺寸 | `sm \| default` | `default` |

### StatusBadgePro

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `value` | 原始状态值，自动映射为标签 + 色调 | `string \| null` | — |
| `label` | 覆盖解析出的文案 | `string` | — |
| `tone` | 覆盖解析出的色调 | `success \| warning \| danger \| info \| neutral` | — |
| `pulse` | 状态点脉冲 | `boolean` | `false` |
