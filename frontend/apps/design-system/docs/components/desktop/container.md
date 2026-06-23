---
title: Container 容器
---

<script setup>
import { Container } from '@nerv-iip/ui'
</script>

# Container 容器

居中并约束内容宽度，带响应式留白（参考 Nuxt UI）。`size` 控制最大宽度，从 `sm` 到 `full`。

## 基础用法

<Demo>
  <Container size="md" class="rounded-xl border border-border bg-card py-8 text-center text-sm text-muted-foreground">
    居中容器 · size="md" · 最大宽度约束 + 响应式 padding
  </Container>
</Demo>

```vue
<Container size="md">
  <YourContent />
</Container>
```

## 宽度档位

<Demo>
  <div class="w-full space-y-3">
    <Container size="sm" class="rounded-lg border border-border bg-muted py-2 text-center text-xs">sm · max-w-3xl</Container>
    <Container size="md" class="rounded-lg border border-border bg-muted py-2 text-center text-xs">md · max-w-5xl</Container>
    <Container size="lg" class="rounded-lg border border-border bg-muted py-2 text-center text-xs">lg · max-w-6xl（默认）</Container>
    <Container size="xl" class="rounded-lg border border-border bg-muted py-2 text-center text-xs">xl · max-w-7xl</Container>
  </div>
</Demo>

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `size` | 最大宽度档位 | `sm \| md \| lg \| xl \| full` | `lg` |
| `as` | 渲染的元素 / 组件 | `string \| Component` | `div` |
