---
title: PageColumns 瀑布分栏
---

<script setup>
import { PageColumns } from '@nerv-iip/ui'
</script>

# PageColumns 瀑布分栏

基于 CSS columns 的瀑布流多列布局（参考 Nuxt UI），适合高度不一的卡片；子项不会跨列断开。

## 瀑布流

<Demo>
  <PageColumns class="w-full">
    <div class="rounded-xl border border-border bg-card p-4">
      <p class="font-medium">短卡片</p>
      <p class="mt-1 text-sm text-muted-foreground">一行描述。</p>
    </div>
    <div class="rounded-xl border border-border bg-card p-4">
      <p class="font-medium">长卡片</p>
      <p class="mt-1 text-sm text-muted-foreground">高度不一的内容会自动错落排布，填满每一列，常用于看板备注、日志、动态等流式内容。</p>
    </div>
    <div class="rounded-xl border border-border bg-card p-4">
      <p class="font-medium">中等卡片</p>
      <p class="mt-1 text-sm text-muted-foreground">子项 break-inside-avoid，不会被拆到两列。</p>
    </div>
    <div class="rounded-xl border border-border bg-card p-4">
      <p class="font-medium">又一张</p>
      <p class="mt-1 text-sm text-muted-foreground">瀑布流。</p>
    </div>
  </PageColumns>
</Demo>

```vue
<PageColumns>
  <div v-for="note in notes" :key="note.id">…</div>
</PageColumns>
```

> 默认 `sm` 2 列、`lg` 3 列。需要其他列数时用 `class` 覆盖 `columns-*`。
