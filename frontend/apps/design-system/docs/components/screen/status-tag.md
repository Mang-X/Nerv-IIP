---
title: StatusTag 状态标签
---

<script setup>
import { StatusTag } from '@nerv-iip/ui'
</script>

# StatusTag 状态标签

小号语义标签:彩色圆点 + 文字,外包一圈着色发丝边、底为极淡的色晕(不用实色填充块 —— 保持大屏底色为暗)。运行别名(`run` / `idle` / `alarm`)折叠到调色板。圆点配文字共同传达状态,不依赖颜色单独识别。

## 运行态别名

`run` / `idle` / `alarm` 对应运行、待机、报警三态。

<ScreenDemo>
  <div style="display:flex; gap:12px; align-items:center;">
    <StatusTag tone="run" label="运行中" />
    <StatusTag tone="idle" label="待机" />
    <StatusTag tone="alarm" label="报警" />
  </div>
</ScreenDemo>

```vue
<StatusTag tone="run" label="运行中" />
<StatusTag tone="idle" label="待机" />
<StatusTag tone="alarm" label="报警" />
```

## 原始颜色

也可直接用调色板颜色名,覆盖更多业务语义。

<ScreenDemo>
  <div style="display:flex; gap:12px; align-items:center;">
    <StatusTag tone="cyan" label="数据采集" />
    <StatusTag tone="green" label="达标" />
    <StatusTag tone="amber" label="预警" />
    <StatusTag tone="red" label="超限" />
  </div>
</ScreenDemo>

```vue
<StatusTag tone="cyan" label="数据采集" />
<StatusTag tone="red" label="超限" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `tone` | 运行别名或原始调色板颜色 | `run \| idle \| alarm \| cyan \| green \| amber \| red` | `run` |
| `label` | 标签文字,如「运行中」/「待机」/「报警」 | `string` | `运行中` |
