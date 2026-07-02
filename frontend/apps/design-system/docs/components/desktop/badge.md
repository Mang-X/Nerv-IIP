---
title: Badge 徽标
---

<script setup>
import { BadgePro } from '@nerv-iip/ui'
</script>

# Badge 徽标

用克制的语义色块标注状态、分类或数量。`BadgePro` 采用柔色填充 + 同色描边，高度与 `ButtonPro` 协调。

## 变体

<Demo>
  <BadgePro variant="brand">品牌</BadgePro>
  <BadgePro variant="success">已完成</BadgePro>
  <BadgePro variant="warning">待处理</BadgePro>
  <BadgePro variant="danger">阻塞</BadgePro>
  <BadgePro variant="solid">主要</BadgePro>
  <BadgePro>中性</BadgePro>
</Demo>

```vue
<BadgePro variant="brand">品牌</BadgePro>
<BadgePro variant="success">已完成</BadgePro>
<BadgePro variant="danger">阻塞</BadgePro>
<BadgePro>中性</BadgePro>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `variant` | 视觉变体 | `neutral \| solid \| brand \| success \| warning \| danger` | `neutral` |
