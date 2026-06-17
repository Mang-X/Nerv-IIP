---
title: Card 卡片
---

<script setup>
import { CardPro, BadgePro, StatusBadgePro } from '@nerv-iip/ui'
</script>

# Card 卡片

承载分组信息的基础容器。`CardPro` 在 shadcn 之上复制重建——发丝级描边 + 顶部内嵌高光读作单一清晰表面；内边距、宽度、栅格由调用方控制（通常 `p-6` + 放进 `grid`）。`interactive` 为可点击卡片叠加克制的悬浮上浮。

## 基础

惯用结构：头部（标题 + 副标题 + 可选状态）+ 内容，放进栅格自适应。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2">
    <CardPro class="p-6">
      <div class="flex items-start justify-between">
        <div>
          <h3 class="text-sm font-semibold">前桥壳体 A2</h3>
          <p class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0413 · WC-CNC-07</p>
        </div>
        <StatusBadgePro value="running" pulse />
      </div>
      <p class="mt-4 text-sm text-muted-foreground">计划 480 件 · 已完成 312 件 · 节拍 42s/件。</p>
    </CardPro>
    <CardPro class="p-6">
      <div class="flex items-start justify-between">
        <div>
          <h3 class="text-sm font-semibold">齿轮箱端盖</h3>
          <p class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0421 · WC-CNC-03</p>
        </div>
        <BadgePro variant="warning">待开工</BadgePro>
      </div>
      <p class="mt-4 text-sm text-muted-foreground">计划 320 件 · 排程于 14:30 开工。</p>
    </CardPro>
  </div>
</Demo>

```vue
<div class="grid gap-4 sm:grid-cols-2">
  <CardPro class="p-6">
    <div class="flex items-start justify-between">
      <div>
        <h3 class="text-sm font-semibold">前桥壳体 A2</h3>
        <p class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0413 · WC-CNC-07</p>
      </div>
      <StatusBadgePro value="running" pulse />
    </div>
    <p class="mt-4 text-sm text-muted-foreground">计划 480 件 · 已完成 312 件。</p>
  </CardPro>
</div>
```

## 可交互

加 `interactive`，悬停时整卡轻微上浮，用于可点击的卡片入口。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2">
    <CardPro interactive class="p-6">
      <div class="flex items-start justify-between">
        <div>
          <h3 class="text-sm font-semibold">液压阀体 V3</h3>
          <p class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0426</p>
        </div>
        <BadgePro variant="brand">进行中</BadgePro>
      </div>
      <p class="mt-4 text-sm text-muted-foreground">点击进入工单详情 →</p>
    </CardPro>
    <CardPro interactive class="p-6">
      <div class="flex items-start justify-between">
        <div>
          <h3 class="text-sm font-semibold">电机定子叠片</h3>
          <p class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0430</p>
        </div>
        <BadgePro variant="success">已完成</BadgePro>
      </div>
      <p class="mt-4 text-sm text-muted-foreground">5000 件 · 良率 99.2%。</p>
    </CardPro>
  </div>
</Demo>

```vue
<CardPro interactive class="p-6">
  <!-- 悬停上浮 -->
</CardPro>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `interactive` | 开启悬停上浮，用于可点击卡片 | `boolean` | `false` |
| `class` | 透传类名（内边距 `p-6`、宽度、栅格由调用方控制） | `string` | — |

> 卡片本身不含内边距与排版——它是「表面」原语。需要带标题/底部的结构化卡片可用 blocks 里的 `SectionCard`，需要指标卡用 `MetricCardPro`。
