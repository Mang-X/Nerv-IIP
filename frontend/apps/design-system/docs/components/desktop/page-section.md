---
title: NvPageSection 内容区块
---

<script setup>
import { NvPageSection, NvPageGrid } from '@nerv-iip/ui'
</script>

# NvPageSection 内容区块

带可选居中页眉（eyebrow + 标题 + 描述）的纵向内容区块（参考 Nuxt UI），用于概览页 / 落地页的分段编排。

## 带页眉的区块

<Demo>
  <NvPageSection
    class="w-full !py-8"
    eyebrow="控制平面"
    title="一处掌握全厂"
    description="工单、设备、质检与节拍，按区块清晰编排。"
  >
    <NvPageGrid :cols="3">
      <div v-for="t in ['工单','设备','质检']" :key="t" class="rounded-xl border border-border bg-card p-4 text-center text-sm text-muted-foreground">{{ t }}</div>
    </NvPageGrid>
  </NvPageSection>
</Demo>

```vue
<NvPageSection eyebrow="控制平面" title="一处掌握全厂" description="…">
  <NvPageGrid :cols="3"><!-- 卡片 --></NvPageGrid>
</NvPageSection>
```

## 属性

| 属性          | 说明                           | 类型     |
| ------------- | ------------------------------ | -------- |
| `eyebrow`     | 标题上方的小标签               | `string` |
| `title`       | 区块标题                       | `string` |
| `description` | 区块描述                       | `string` |
| `#header`     | 自定义页眉插槽（替代上述三项） | slot     |
