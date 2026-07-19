---
title: NvScreenFreshness 数据新鲜度
---

<script setup>
import { NvScreenFreshness } from '@nerv-iip/ui'
</script>

# NvScreenFreshness 数据新鲜度

用于大屏稳定页脚，显示某一个数据源的实时、滞留或等待状态。组件只负责状态视觉；调用方必须按各自数据源传入完整文案和最后更新时间，不要把不同轮询源合并成一个状态。

<ScreenDemo>
  <div style="display:flex; gap:28px; align-items:center; flex-wrap:wrap">
    <NvScreenFreshness tone="live" label="实时 · 最后更新 10:24:08" />
    <NvScreenFreshness tone="stale" label="数据滞留 · 最后更新 10:18:01" />
    <NvScreenFreshness tone="wait" label="等待首次数据" />
  </div>
</ScreenDemo>

```vue
<NvScreenFreshness tone="live" label="实时 · 最后更新 10:24:08" />
<NvScreenFreshness tone="stale" label="数据滞留 · 最后更新 10:18:01" />
```

状态始终由圆点和文字共同表达，不依赖颜色单独传达。实时圆点的呼吸动效在 `prefers-reduced-motion` 下停止。

## 属性

| 属性    | 说明                             | 类型                    | 默认   |
| ------- | -------------------------------- | ----------------------- | ------ |
| `tone`  | 当前数据源的新鲜度状态           | `live \| stale \| wait` | `wait` |
| `label` | 包含状态和最后更新时间的完整文案 | `string`                | 必填   |
