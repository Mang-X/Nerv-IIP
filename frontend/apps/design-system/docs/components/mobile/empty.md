---
layout: page
title: Empty 空状态
---

<script setup>
import { Empty, MobileButton } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="w-full rounded-xl border border-border bg-card">
      <Empty description="暂无待处理工单">
        <MobileButton variant="primary" size="sm">去接单</MobileButton>
      </Empty>
    </div>
  </section>
</template>

# Empty 空状态

无数据时的占位提示，弱化的图标配说明文字，可在默认插槽放置引导操作。

## 基础用法

`description` 设置说明文字，默认插槽承载引导按钮。

```vue
<script setup>
import { Empty, MobileButton } from '@nerv-iip/ui-mobile'
</script>

<template>
  <Empty description="暂无待处理工单">
    <MobileButton variant="primary" size="sm">去接单</MobileButton>
  </Empty>
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `description` | 说明文字 | `string` | — |

## 插槽

| 插槽 | 说明 |
|---|---|
| `default` | 说明下方的操作区 |
| `icon` | 自定义图标（默认收件箱图标） |

</MobileDoc>
