---
layout: page
title: NvMobileEmpty 空状态
---

<script setup>
import { NvMobileEmpty, NvMobileButton } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">基础用法</p>
    <div class="w-full rounded-xl border border-border bg-card">
      <NvMobileEmpty description="暂无待处理工单">
        <NvMobileButton variant="primary" size="sm">去接单</NvMobileButton>
      </NvMobileEmpty>
    </div>
  </section>
</template>

# NvMobileEmpty 空状态

无数据时的占位提示，弱化的图标配说明文字，可在默认插槽放置引导操作。

## 基础用法

`description` 设置说明文字，默认插槽承载引导按钮。

```vue
<script setup>
import { NvMobileEmpty, NvMobileButton } from '@nerv-iip/ui-mobile'
</script>

<template>
  <NvMobileEmpty description="暂无待处理工单">
    <NvMobileButton variant="primary" size="sm">去接单</NvMobileButton>
  </NvMobileEmpty>
</template>
```

## 属性

| 属性          | 说明     | 类型     | 默认 |
| ------------- | -------- | -------- | ---- |
| `description` | 说明文字 | `string` | —    |

## 插槽

| 插槽      | 说明                         |
| --------- | ---------------------------- |
| `default` | 说明下方的操作区             |
| `icon`    | 自定义图标（默认收件箱图标） |

</MobileDoc>
