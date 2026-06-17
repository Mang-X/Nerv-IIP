---
title: Card 卡片
---

<script setup>
import { CardPro, BadgePro, StatusBadgePro } from '@nerv-iip/ui'
</script>

# Card 卡片

承载分组信息的基础容器。`CardPro` 在 shadcn 之上复制重建，发丝级描边 + 顶部内嵌高光读作单一清晰表面，`interactive` 为可点击卡片叠加克制的悬浮上浮。

## 基础

<Demo>
  <CardPro class="w-80 p-6">
    <div class="flex items-center justify-between">
      <h3 class="text-sm font-semibold">前桥壳体 A2</h3>
      <StatusBadgePro value="running" pulse />
    </div>
    <p class="mt-1 font-mono text-xs text-muted-foreground">WO-2406-0413 · WC-CNC-07</p>
    <p class="mt-4 text-sm text-muted-foreground">计划数量 480 件，已完成 312 件，节拍 42s/件。</p>
  </CardPro>
</Demo>

```vue
<CardPro class="p-6">
  <div class="flex items-center justify-between">
    <h3 class="text-sm font-semibold">前桥壳体 A2</h3>
    <StatusBadgePro value="running" pulse />
  </div>
  <p class="mt-1 font-mono text-xs text-muted-foreground">WO-2406-0413 · WC-CNC-07</p>
</CardPro>
```

## 可交互

<Demo>
  <CardPro interactive class="w-80 p-6">
    <h3 class="text-sm font-semibold">齿轮箱端盖</h3>
    <p class="mt-1 font-mono text-xs text-muted-foreground">WO-2406-0421</p>
    <div class="mt-3">
      <BadgePro variant="warning">待开工</BadgePro>
    </div>
  </CardPro>
</Demo>

```vue
<CardPro interactive class="p-6">
  <h3 class="text-sm font-semibold">齿轮箱端盖</h3>
  <p class="mt-1 font-mono text-xs text-muted-foreground">WO-2406-0421</p>
</CardPro>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `interactive` | 开启悬浮上浮，用于可点击卡片 | `boolean` | `false` |
| `class` | 透传类名（内边距、宽度等由调用方控制） | `string` | — |
