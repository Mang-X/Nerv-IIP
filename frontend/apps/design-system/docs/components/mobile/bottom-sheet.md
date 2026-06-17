---
layout: page
title: BottomSheet 底部抽屉
---

<script setup>
import { BottomSheet, MobileButton } from '@nerv-iip/ui-mobile'
import { PrinterIcon, SplitIcon, WrenchIcon, XCircleIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const sheetOpen = ref(false)
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <MobileButton variant="primary" size="md" block @click="sheetOpen = true">
      打开底部抽屉
    </MobileButton>
    <BottomSheet v-model:open="sheetOpen" title="更多操作">
      <div class="space-y-2 py-1">
        <MobileButton variant="default" size="lg" block class="justify-start gap-3" @click="sheetOpen = false">
          <SplitIcon class="size-5" aria-hidden="true" />拆分工单
        </MobileButton>
        <MobileButton variant="default" size="lg" block class="justify-start gap-3" @click="sheetOpen = false">
          <PrinterIcon class="size-5" aria-hidden="true" />补打标签
        </MobileButton>
        <MobileButton variant="default" size="lg" block class="justify-start gap-3" @click="sheetOpen = false">
          <WrenchIcon class="size-5" aria-hidden="true" />设备维护
        </MobileButton>
        <MobileButton variant="danger" size="lg" block class="justify-start gap-3" @click="sheetOpen = false">
          <XCircleIcon class="size-5" aria-hidden="true" />报告异常
        </MobileButton>
      </div>
    </BottomSheet>
  </section>
</template>

# BottomSheet 底部抽屉

从屏幕底部滑入的承载面板，可向下拖拽关闭，适合放置一组次级操作或表单内容。

## 基础用法

由触发按钮控制 `open`，默认插槽承载任意内容；抓住顶部把手向下拖拽即可关闭。

```vue
<script setup>
import { BottomSheet, MobileButton } from '@nerv-iip/ui-mobile'
import { PrinterIcon, SplitIcon, WrenchIcon, XCircleIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const sheetOpen = ref(false)
</script>

<template>
  <MobileButton variant="primary" size="md" block @click="sheetOpen = true">
    打开底部抽屉
  </MobileButton>
  <BottomSheet v-model:open="sheetOpen" title="更多操作">
    <div class="space-y-2 py-1">
      <MobileButton variant="default" size="lg" block class="justify-start gap-3" @click="sheetOpen = false">
        <SplitIcon class="size-5" aria-hidden="true" />拆分工单
      </MobileButton>
      <MobileButton variant="danger" size="lg" block class="justify-start gap-3" @click="sheetOpen = false">
        <XCircleIcon class="size-5" aria-hidden="true" />报告异常
      </MobileButton>
    </div>
  </BottomSheet>
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `open` | 是否打开（`v-model:open`） | `boolean` | `false` |
| `title` | 标题 | `string` | — |
| `description` | 描述 | `string` | — |

## 事件

| 事件 | 说明 | 回调参数 |
|---|---|---|
| `update:open` | 开关状态变化（含拖拽关闭） | `(value: boolean)` |

</MobileDoc>
