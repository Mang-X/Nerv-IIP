---
title: SwipeCell 侧滑操作
---

<script setup>
import { SwipeCell } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const swipeActions = [
  { label: '完工', value: 'finish', tone: 'brand' },
  { label: '暂停', value: 'pause' },
  { label: '删除', value: 'remove', tone: 'danger' },
]
const swipeRows = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖' },
  { code: 'WO-2406-0426', product: '液压阀体 V3' },
]
const last = ref('')
function onSwipe(value) {
  last.value = value
}
</script>

# SwipeCell 侧滑操作

向左滑动行以露出右侧操作按钮。指针驱动（触摸与鼠标皆可），自动吸附开合，仅在水平拖拽时生效以保留纵向滚动。

## 基础用法

`actions` 提供右侧按钮，`tone` 控制色调；`select` 回传所选 `value`。默认插槽承载行内容。

<Demo mobile>
  <div class="w-full overflow-hidden rounded-xl border border-border">
    <SwipeCell
      v-for="row in swipeRows"
      :key="row.code"
      :actions="swipeActions"
      class="border-b border-border last:border-0"
      @select="onSwipe"
    >
      <div class="flex min-h-touch items-center gap-3 px-4 py-3">
        <div class="min-w-0 flex-1">
          <div class="text-[15px]">{{ row.product }}</div>
          <div class="text-sm text-muted-foreground">{{ row.code }}</div>
        </div>
        <span class="shrink-0 text-xs text-muted-foreground">← 左滑</span>
      </div>
    </SwipeCell>
  </div>
</Demo>

```vue
<script setup>
import { SwipeCell } from '@nerv-iip/ui-mobile'

const swipeActions = [
  { label: '完工', value: 'finish', tone: 'brand' },
  { label: '暂停', value: 'pause' },
  { label: '删除', value: 'remove', tone: 'danger' },
]
function onSwipe(value) {
  console.log('侧滑操作：', value)
}
</script>

<template>
  <SwipeCell :actions="swipeActions" @select="onSwipe">
    <div class="flex items-center gap-3 px-4 py-3">
      <div class="flex-1">
        <div class="text-[15px]">齿轮箱端盖</div>
        <div class="text-sm text-muted-foreground">WO-2406-0421</div>
      </div>
    </div>
  </SwipeCell>
</template>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `actions` | 右侧操作按钮 | `SwipeAction[]` | — |

`SwipeAction`：`{ label: string; value: string; tone?: 'default' \| 'brand' \| 'danger' }`

## 事件

| 事件 | 说明 | 回调参数 |
|---|---|---|
| `select` | 点击某个操作 | `(value: string)` |
