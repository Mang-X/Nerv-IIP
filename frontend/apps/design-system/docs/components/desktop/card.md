---
title: Card 卡片
---

<script setup>
import { NvCard, NvBadge, NvStatusBadge, Separator } from '@nerv-iip/ui'
</script>

# Card 卡片

承载分组信息的「表面」原语。`NvCard` 基于 `@nerv-iip/ui` 稳定导出的卡片基础能力复制重建——发丝级描边 + 顶部内嵌高光读作单一清晰表面。它本身不含内边距与排版，内容由调用方组织（通常 `p-6` 头部 + 内容，放进 `grid`）；`interactive` 为可点击卡片叠加克制的悬浮上浮。

## 基础

惯用结构：头部（标题 + 副标题 + 可选状态）紧凑置顶，内容在下，放进栅格自适应。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2">
    <NvCard class="p-5">
      <div class="flex items-start justify-between gap-3">
        <div>
          <div class="text-sm font-semibold text-foreground">前桥壳体 A2</div>
          <div class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0413 · WC-CNC-07</div>
        </div>
        <NvStatusBadge value="running" pulse />
      </div>
      <div class="mt-4 text-sm text-muted-foreground">计划 480 件 · 已完成 312 件 · 节拍 42s/件。</div>
    </NvCard>
    <NvCard class="p-5">
      <div class="flex items-start justify-between gap-3">
        <div>
          <div class="text-sm font-semibold text-foreground">齿轮箱端盖</div>
          <div class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0421 · WC-CNC-03</div>
        </div>
        <NvBadge variant="warning">待开工</NvBadge>
      </div>
      <div class="mt-4 text-sm text-muted-foreground">计划 320 件 · 排程 14:30 开工。</div>
    </NvCard>
  </div>
</Demo>

```vue
<NvCard class="p-5">
  <div class="flex items-start justify-between gap-3">
    <div>
      <div class="text-sm font-semibold">前桥壳体 A2</div>
      <div class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0413 · WC-CNC-07</div>
    </div>
    <NvStatusBadge value="running" pulse />
  </div>
  <div class="mt-4 text-sm text-muted-foreground">计划 480 件 · 已完成 312 件。</div>
</NvCard>
```

## 可交互

加 `interactive`，悬停时整卡轻微上浮，用于可点击的卡片入口。

<Demo>
  <div class="grid w-full gap-4 sm:grid-cols-2">
    <NvCard interactive class="p-5">
      <div class="flex items-start justify-between gap-3">
        <div>
          <div class="text-sm font-semibold text-foreground">液压阀体 V3</div>
          <div class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0426</div>
        </div>
        <NvBadge variant="brand">进行中</NvBadge>
      </div>
      <div class="mt-4 text-sm text-muted-foreground">点击进入工单详情 →</div>
    </NvCard>
    <NvCard interactive class="p-5">
      <div class="flex items-start justify-between gap-3">
        <div>
          <div class="text-sm font-semibold text-foreground">电机定子叠片</div>
          <div class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0430</div>
        </div>
        <NvBadge variant="success">已完成</NvBadge>
      </div>
      <div class="mt-4 text-sm text-muted-foreground">5000 件 · 良率 99.2%。</div>
    </NvCard>
  </div>
</Demo>

```vue
<NvCard interactive class="p-5"><!-- 悬停上浮 --></NvCard>
```

## 插槽组合

卡片是空容器，默认插槽里可自由组织头部 / 分隔 / 底部等结构（搭配 `Separator`）。

<Demo>
  <NvCard class="w-full max-w-sm overflow-hidden">
    <div class="flex items-center justify-between p-5">
      <div>
        <div class="text-sm font-semibold text-foreground">质检放行</div>
        <div class="mt-0.5 font-mono text-xs text-muted-foreground">WO-2406-0413</div>
      </div>
      <NvStatusBadge value="running" pulse />
    </div>
    <Separator />
    <div class="flex items-center justify-between p-5 text-sm">
      <span class="text-muted-foreground">首检结论</span>
      <span class="font-medium text-foreground">合格 · 可批量</span>
    </div>
  </NvCard>
</Demo>

```vue
<NvCard class="overflow-hidden">
  <div class="p-5"><!-- 头部 --></div>
  <Separator />
  <div class="p-5"><!-- 底部 --></div>
</NvCard>
```

## 属性

| 属性          | 说明                                       | 类型      | 默认    |
| ------------- | ------------------------------------------ | --------- | ------- |
| `interactive` | 开启悬停上浮，用于可点击卡片               | `boolean` | `false` |
| `class`       | 透传类名（内边距、宽度、栅格由调用方控制） | `string`  | —       |

> 需要带固定标题/底部结构的卡片可用 blocks 里的 `NvSectionCard`；指标卡用 `NvMetricCard`。
