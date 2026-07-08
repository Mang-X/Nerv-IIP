---
title: Badge 徽标
---

<script setup>
import { NvBadge } from '@nerv-iip/ui'
</script>

# Badge 徽标

用克制的语义色块标注状态、分类或数量。`NvBadge` 采用柔色填充 + 同色描边，高度与 `NvButton` 协调。

## 变体

<Demo>
  <NvBadge variant="brand">品牌</NvBadge>
  <NvBadge variant="success">已完成</NvBadge>
  <NvBadge variant="warning">待处理</NvBadge>
  <NvBadge variant="danger">阻塞</NvBadge>
  <NvBadge variant="solid">主要</NvBadge>
  <NvBadge>中性</NvBadge>
</Demo>

```vue
<NvBadge variant="brand">品牌</NvBadge>
<NvBadge variant="success">已完成</NvBadge>
<NvBadge variant="danger">阻塞</NvBadge>
<NvBadge>中性</NvBadge>
```

## 属性

| 属性      | 说明     | 类型                                                        | 默认      |
| --------- | -------- | ----------------------------------------------------------- | --------- |
| `variant` | 视觉变体 | `neutral \| solid \| brand \| success \| warning \| danger` | `neutral` |
