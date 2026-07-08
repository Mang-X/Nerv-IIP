---
title: NvScreenScrollArea 滚动区
---

<script setup>
import { NvScreenScrollArea } from '@nerv-iip/ui'

const rows = Array.from({ length: 14 }, (_, i) => ({
  code: `NCR-26-${String(41 + i).padStart(3, '0')}`,
  text: ['极片对齐度超差', '隔膜厚度偏差', '焊点虚焊', '面漆橘皮'][i % 4],
  line: ['电芯线', '焊装一线', '面漆线', '总装一线'][i % 4],
}))
</script>

# NvScreenScrollArea 滚动区

shadcn / reka-ui ScrollArea 的**大屏重制版**(shadcn 原版零改动,按定制规矩复制重建):悬浮细滚动条——无轨道底色、发丝级圆角 thumb、hover / 滚动时才浮现;上下缘**渐隐遮罩 + 微呼吸箭头**提示还有内容可滑(挂墙远视距也能一眼看出列表未到底)。基于独立的 `--sb-*` 令牌。

::: warning 虚拟滚动容器不适用
`useVirtualList` 需绑定原生滚动元素(`containerProps`),这类容器请保留原生滚动并使用全局 `.sb-scroll` 细滚条样式(视觉与本组件一致)。
:::

## 基础用法

调用方用 `height` / `max-height` / `flex` 约束根节点即可(组件内部 flex 链传高,`max-height` 场景不会退化成"裁切不滚动")。

<ScreenDemo>
  <NvScreenScrollArea style="max-height: 180px">
    <div v-for="r in rows" :key="r.code" style="display: flex; gap: 10px; padding: 8px 2px; border-bottom: 1px solid var(--sb-divider); font-size: 13px">
      <span style="font-family: ui-monospace, monospace; color: var(--sb-cyan)">{{ r.code }}</span>
      <span style="flex: 1; color: var(--sb-text-2)">{{ r.line }} · {{ r.text }}</span>
    </div>
  </NvScreenScrollArea>
</ScreenDemo>

```vue
<template>
  <NvScreenScrollArea style="max-height: 180px">
    <div v-for="r in rows" :key="r.code" class="row">…</div>
  </NvScreenScrollArea>
</template>
```

## 布局注意（flex / gap 内容）

组件已把 reka viewport 内容层从 `display:table` 修正为块级(否则行宽会按内容撑开、`ellipsis` 失效)。若滚动内容本身是 `flex` + `gap` 列表,布局样式请落在**自己的内层包装 div** 上:

```vue
<NvScreenScrollArea class="list">
  <div class="list-in"><!-- display:flex; flex-direction:column; gap:12px --></div>
</NvScreenScrollArea>
```

## API

| Prop     | 类型                                        | 默认      | 说明                                                 |
| -------- | ------------------------------------------- | --------- | ---------------------------------------------------- |
| `type`   | `'hover' \| 'auto' \| 'always' \| 'scroll'` | `'hover'` | 滚动条浮现时机(hover = 悬停/滚动时,挂墙常态无条干净) |
| 默认插槽 | —                                           | —         | 滚动内容                                             |

上下缘可滑提示自动出现/消失(scroll + ResizeObserver 联动,数据轮询行数变化同样生效);`prefers-reduced-motion` 下箭头不呼吸。
