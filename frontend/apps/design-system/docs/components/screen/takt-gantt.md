---
title: TaktGantt 节拍甘特
---

<script setup>
import { ref } from 'vue'
import { NvTaktGantt } from '@nerv-iip/ui'

const rows = ref([
  { name: '焊接线 A', segs: [['run', 34], ['idle', 6], ['run', 60]] },
  { name: '装配线 B', segs: [['idle', 22], ['stop', 8], ['run', 18], ['idle', 30], ['run', 22]] },
  { name: 'CNC 线 C', segs: [['alarm', 14], ['stop', 20], ['run', 12], ['alarm', 10], ['stop', 14], ['run', 12], ['alarm', 18]] },
])

const axis = ref(['09:30', '09:40', '09:50', '10:00', '10:10', '10:20', '10:30'])
</script>

# TaktGantt 节拍甘特

节拍(Takt)甘特图:每条产线一行 —— 右对齐的线名加一根分成多段色块的横条(运行青 / 待机琥珀 / 停机灰 / 报警红)。横条上方是共享时间轴,下方图例标注四种色调。完全由 `rows` + `axis` 驱动,每行的段宽之和约为 100。基于独立的 `--sb-*` 工业蓝令牌。

::: tip 容器
`NvTaktGantt` **自带** [`NvScreenPanel`](./screen-panel) 容器,直接使用即可。宽组件用 `<ScreenDemo wide>` 占满整列。
:::

## 基础用法

不传任何 props 时使用内置的三产线示例(焊接 / 装配 / CNC,节拍 58s)。

<ScreenDemo wide>
  <NvTaktGantt />
</ScreenDemo>

```vue
<NvTaktGantt />
```

## 数据驱动

传入 `rows`:每行 `{ name, segs }`,`segs` 是 `[色调, 宽度百分比]` 的有序数组,段宽之和约 100。`axis` 是时间轴刻度。

<ScreenDemo wide>
  <NvTaktGantt
    title="节拍 Takt 45s"
    :rows="rows"
    :axis="axis"
  />
</ScreenDemo>

```vue
<script setup>
const rows = [
  {
    name: '焊接线 A',
    segs: [
      ['run', 34],
      ['idle', 6],
      ['run', 60],
    ],
  },
  {
    name: '装配线 B',
    segs: [
      ['idle', 22],
      ['stop', 8],
      ['run', 18],
      ['idle', 30],
      ['run', 22],
    ],
  },
  {
    name: 'CNC 线 C',
    segs: [
      ['alarm', 14],
      ['stop', 20],
      ['run', 12],
      ['alarm', 10],
      ['stop', 14],
      ['run', 12],
      ['alarm', 18],
    ],
  },
]
const axis = ['09:30', '09:40', '09:50', '10:00', '10:10', '10:20', '10:30']
</script>

<NvTaktGantt title="节拍 Takt 45s" :rows="rows" :axis="axis" />
```

## 属性

| 属性    | 说明                                      | 类型                              | 默认                                                              |
| ------- | ----------------------------------------- | --------------------------------- | ----------------------------------------------------------------- |
| `rows`  | 每条产线一行:线名 + 有序段,段宽之和约 100 | `{ name: string; segs: Seg[] }[]` | 内置三产线示例                                                    |
| `axis`  | 横条区上方的时间轴刻度                    | `string[]`                        | `['09:30', '09:40', '09:50', '10:00', '10:10', '10:20', '10:30']` |
| `title` | 面板标题                                  | `string`                          | `'节拍 Takt 58s'`                                                 |

`Seg` 为 `[色调, 宽度百分比]`,色调取 `'run'`(运行) / `'idle'`(待机) / `'stop'`(停机) / `'alarm'`(报警)。
