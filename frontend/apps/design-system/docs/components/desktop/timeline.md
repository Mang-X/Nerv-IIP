---
title: NvTimeline 时间线
---

<script setup>
import { NvTimeline } from '@nerv-iip/ui'
import {
  FilePlus2Icon,
  PackageCheckIcon,
  PlayIcon,
  TriangleAlertIcon,
} from '@lucide/vue'

const items = [
  {
    key: 'created',
    title: '工单创建',
    label: '06-17 08:12',
    description: '由 MES 排产自动下发，物料已预占。',
    tone: 'neutral',
    icon: FilePlus2Icon,
  },
  {
    key: 'kitted',
    title: '物料齐套',
    label: '06-17 08:40',
    description: '5 项物料从 WH-A 出库并送达线边仓。',
    tone: 'success',
    icon: PackageCheckIcon,
  },
  {
    key: 'started',
    title: '开工',
    label: '06-17 09:05',
    description: '张伟在 WC-CNC-07 扫码开工，节拍 42s/件。',
    tone: 'brand',
    icon: PlayIcon,
  },
  {
    key: 'warn',
    title: '首检预警',
    label: '06-17 09:21',
    description: '孔径 Φ12.02 接近上公差，已通知工艺复核。',
    tone: 'warning',
    icon: TriangleAlertIcon,
  },
]
</script>

# NvTimeline 时间线

工序流转 / 操作日志的垂直时间线。`NvTimeline` 用不透明语义色节点串接在连接轨上，每个事件含标题、元信息标签与描述，并可在末尾追加脉动的“进行中”节点。

## 工序日志

<Demo>
  <div class="w-96">
    <NvTimeline :items="items" pending pending-text="精加工中…" />
  </div>
</Demo>

```vue
<NvTimeline :items="items" pending pending-text="精加工中…" />
```

其中每个 `TimelineItem` 形如：

```ts
const items = [
  {
    key: 'started',
    title: '开工',
    label: '06-17 09:05',
    description: '张伟在 WC-CNC-07 扫码开工，节拍 42s/件。',
    tone: 'brand',
    icon: PlayIcon,
  },
  // …
]
```

## 属性

| 属性          | 说明                                                            | 类型             | 默认      |
| ------------- | --------------------------------------------------------------- | ---------------- | --------- |
| `items`       | 事件数组（`title` / `label` / `description` / `tone` / `icon`） | `TimelineItem[]` | —         |
| `pending`     | 末尾追加脉动的进行中节点                                        | `boolean`        | `false`   |
| `pendingText` | 进行中节点文案                                                  | `string`         | `进行中…` |
| `reverse`     | 最新优先排序                                                    | `boolean`        | `false`   |

`TimelineItem.tone` 取值：`brand \| success \| warning \| danger \| neutral`。
