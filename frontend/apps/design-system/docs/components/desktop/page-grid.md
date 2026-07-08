---
title: PageGrid 卡片网格
---

<script setup>
import { NvPageGrid } from '@nerv-iip/ui'
</script>

# PageGrid 卡片网格

等高卡片的响应式网格（参考 Nuxt UI）。移动端 1 列、`sm` 2 列，`cols` 控制大屏列数。

## 三列

<Demo>
  <NvPageGrid class="w-full">
    <div v-for="n in 6" :key="n" class="rounded-xl border border-border bg-card p-4">
      <p class="font-medium">模块 {{ n }}</p>
      <p class="mt-1 text-sm text-muted-foreground">等高卡片，自动按列排布。</p>
    </div>
  </NvPageGrid>
</Demo>

```vue
<NvPageGrid :cols="3">
  <div v-for="m in modules" :key="m.id">…</div>
</NvPageGrid>
```

## 四列

<Demo>
  <NvPageGrid :cols="4" class="w-full">
    <div v-for="n in 4" :key="n" class="rounded-xl border border-border bg-card p-4 text-center text-sm text-muted-foreground">{{ n }}</div>
  </NvPageGrid>
</Demo>

## 属性

| 属性   | 说明                               | 类型          | 默认 |
| ------ | ---------------------------------- | ------------- | ---- |
| `cols` | 大屏列数（移动 1 列、sm 2 列固定） | `2 \| 3 \| 4` | `3`  |
