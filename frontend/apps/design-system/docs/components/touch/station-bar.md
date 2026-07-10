---
title: NvStationBar 工位栏
---

<script setup>
import { NvStationBar, NvThemeToggle } from '@nerv-iip/ui'
</script>

# NvStationBar 工位栏

工位一体机的顶部标识栏：左侧大号工位名 + 实时状态点（可脉冲），右侧自由插槽放时钟、班次、操作员。是一体机看板的页头，对应 PC 的 NvAppHeader、移动的 NvNavBar、大屏的 NvScreenHeader。

## 基础

`station` 工位名，`statusLabel` + `tone` 状态，`pulse` 让状态点呼吸（运行中）。

<Demo block>
  <NvStationBar
    station="WC-CNC-07 · 精密加工"
    status-label="运行中"
    tone="success"
    :pulse="true"
  >
    <template #right>
      <div class="text-sm text-muted-foreground">早班 · 张伟</div>
      <div class="font-mono text-2xl font-semibold tabular-nums">09:41:20</div>
      <NvThemeToggle />
    </template>
  </NvStationBar>
</Demo>

```vue
<NvStationBar station="WC-CNC-07 · 精密加工" status-label="运行中" tone="success" :pulse="true">
  <template #right>
    <div class="text-sm text-muted-foreground">早班 · 张伟</div>
    <div class="font-mono text-2xl font-semibold tabular-nums">{{ now }}</div>
  </template>
</NvStationBar>
```

## 暂停态

停机时用 `tone="warning"` 并停止脉冲，状态一眼可辨。

<Demo block>
  <NvStationBar station="WC-CNC-07 · 精密加工" status-label="已暂停" tone="warning" :pulse="false" />
</Demo>

## 属性

| 属性          | 说明             | 类型                                              | 默认      |
| ------------- | ---------------- | ------------------------------------------------- | --------- |
| `station`     | 工位名称         | `string`                                          | —         |
| `statusLabel` | 状态文本（可选） | `string`                                          | —         |
| `tone`        | 状态语气         | `success \| warning \| danger \| info \| neutral` | `success` |
| `pulse`       | 状态点是否脉冲   | `boolean`                                         | `true`    |

## 插槽

| 插槽    | 说明                             |
| ------- | -------------------------------- |
| `right` | 右侧区域：时钟 / 班次 / 操作员等 |
