---
layout: page
title: NvNavBar 顶部栏
---

<script setup>
import { NvNavBar, NvMobileButton } from '@nerv-iip/ui-mobile'
import { EllipsisIcon } from '@lucide/vue'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">基础用法</p>
    <div class="border-b border-border bg-card">
      <NvNavBar title="工单详情" />
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">返回与右侧操作</p>
    <div class="border-b border-border bg-card">
      <NvNavBar title="WO-2406-0413" back>
        <template #right>
          <NvMobileButton variant="text" size="sm" aria-label="更多">
            <EllipsisIcon class="size-5" aria-hidden="true" />
          </NvMobileButton>
        </template>
      </NvNavBar>
    </div>
  </section>
</template>

# NvNavBar 顶部栏

顶部应用栏（tdesign-mobile 风格）：标题居中，左侧可选返回，右侧自由放置操作。

## 基础用法

仅标题居中的最简形态。

```vue
<NvNavBar title="工单详情" />
```

## 返回与右侧操作

`back` 显示左侧返回按钮，`#right` 插槽放置操作。

```vue
<NvNavBar title="WO-2406-0413" back @back="goBack">
  <template #right>
    <NvMobileButton variant="text" size="sm" aria-label="更多">
      <EllipsisIcon class="size-5" />
    </NvMobileButton>
  </template>
</NvNavBar>
```

## 属性

| 属性    | 说明                       | 类型      | 默认    |
| ------- | -------------------------- | --------- | ------- |
| `title` | 居中标题（也可用默认插槽） | `string`  | —       |
| `back`  | 显示左侧返回按钮           | `boolean` | `false` |

事件：`@back`（点击返回按钮）。插槽：默认（标题区）、`#left`、`#right`。

</MobileDoc>
