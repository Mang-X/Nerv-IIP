---
title: ScrollBoard 无缝滚动板
---

<script setup>
import { NvScrollBoard } from '@nerv-iip/ui'

const alarms = [
  { time: '13:56', text: '电芯线 · 卷绕机 1# 急停触发 · 安全门未复位', level: 'alarm' },
  { time: '12:03', text: '电芯线 · 注液机 注液量偏差预警', level: 'warn' },
  { time: '10:48', text: '模组线 · EOL 测试柜 通讯超时重试', level: 'warn' },
  { time: '09:15', text: 'PACK 线 · 上料卡滞短停 8 min（已恢复）', level: 'info' },
  { time: '08:20', text: '电芯线 · 化成柜 B 温度越限预警（已恢复）', level: 'info' },
  { time: '07:51', text: '电芯二线 · 分容柜 2# 温度越限预警（已恢复）', level: 'info' },
]
</script>

# ScrollBoard 无缝滚动板

告警流 / 停机事件这类**挂墙轮巡列表**的载体:列表渲染两遍、按帧位移,滚过一半即无缝回到起点(观众看不到"跳回")。悬停暂停 + 滚轮自由查看(双向同样按半高取模无缝);内容未溢出时静止不动。基于 `useRafFn` 按帧驱动,`speed` 为像素/秒。

## 基础用法

外层容器限高,`items` + `#row` 作用域插槽:

<ScreenDemo>
  <div style="height: 150px">
    <NvScrollBoard :items="alarms" :row-key="(r) => r.time + r.text" :speed="22">
      <template #row="{ item }">
        <div style="display: flex; gap: 10px; padding: 7px 2px; font-size: 13px; border-bottom: 1px solid var(--sb-divider)">
          <span style="color: var(--sb-muted); font-variant-numeric: tabular-nums">{{ item.time }}</span>
          <span :style="{ flex: 1, color: item.level === 'alarm' ? 'var(--sb-red)' : item.level === 'warn' ? 'var(--sb-amber)' : 'var(--sb-faint)' }">
            {{ item.text }}
          </span>
        </div>
      </template>
    </NvScrollBoard>
  </div>
</ScreenDemo>

```vue
<template>
  <div style="height: 150px">
    <NvScrollBoard :items="alarms" :row-key="(r) => r.time + r.text" :speed="22">
      <template #row="{ item }">
        <div class="row">{{ item.time }} · {{ item.text }}</div>
      </template>
    </NvScrollBoard>
  </div>
</template>
```

## 交互语义

- **悬停暂停**(`pauseOnHover`,默认开):运维凑近看时列表停住;
- **滚轮自由查看**:暂停期间滚轮上下翻,同样无缝循环;
- **未溢出不滚**:条目装得下时保持静止 —— 不为动而动。

## API

| Prop           | 类型                                | 默认   | 说明                            |
| -------------- | ----------------------------------- | ------ | ------------------------------- |
| `items`        | `T[]`                               | —      | 列表数据(泛型)                  |
| `speed`        | `number`                            | `28`   | 滚动速度(px/s)                  |
| `pauseOnHover` | `boolean`                           | `true` | 悬停暂停                        |
| `rowKey`       | `(item, index) => string \| number` | 下标   | 行 key(轮询更新时保持 DOM 复用) |
| `#row` 插槽    | `{ item, index }`                   | —      | 行渲染                          |

::: tip 与 ScreenScrollArea 的分工
`NvScrollBoard` 是**自动轮巡**(无人操作的挂墙场景);[ScreenScrollArea](./screen-scroll-area) 是**手动滚动**(运维交互场景,悬浮细滚条 + 可滑提示)。按场景选择,不要混用。
:::
