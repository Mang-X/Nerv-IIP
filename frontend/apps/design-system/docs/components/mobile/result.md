---
layout: page
title: NvMobileResult 结果页
---

<script setup>
import { NvMobileButton, NvMobileResult } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">成功</p>
    <div class="w-full rounded-xl border border-border bg-card">
      <NvMobileResult
        status="success"
        title="本班产出已同步"
        description="已完成 4,210 件，良率 99.2%。"
      />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">失败（带操作）</p>
    <div class="w-full rounded-xl border border-border bg-card">
      <NvMobileResult
        status="error"
        title="工单上报失败"
        description="网关连接超时，请检查网络后重试。"
      >
        <template #actions>
          <NvMobileButton variant="primary" size="md" block>重新上报</NvMobileButton>
        </template>
      </NvMobileResult>
    </div>
  </section>
</template>

# NvMobileResult 结果页

操作完成后的反馈页，居中呈现成功/失败图标、标题与说明，可在 `actions` 插槽放置后续操作。

## 成功

`status` 决定图标与色调，`title` / `description` 承载结果说明。

```vue
<script setup>
import { NvMobileResult } from '@nerv-iip/ui-mobile'
</script>

<template>
  <NvMobileResult
    status="success"
    title="本班产出已同步"
    description="已完成 4,210 件，良率 99.2%。"
  />
</template>
```

## 失败（带操作）

`status="error"` 呈现失败态；`#actions` 插槽放置重试等后续操作。

```vue
<template>
  <NvMobileResult
    status="error"
    title="工单上报失败"
    description="网关连接超时，请检查网络后重试。"
  >
    <template #actions>
      <NvMobileButton variant="primary" size="md" block>重新上报</NvMobileButton>
    </template>
  </NvMobileResult>
</template>
```

## 属性

| 属性          | 说明     | 类型               | 默认 |
| ------------- | -------- | ------------------ | ---- |
| `status`      | 结果状态 | `success \| error` | —    |
| `title`       | 标题     | `string`           | —    |
| `description` | 说明文字 | `string`           | —    |

## 插槽

| 插槽      | 说明             |
| --------- | ---------------- |
| `actions` | 标题下方的操作区 |

</MobileDoc>
