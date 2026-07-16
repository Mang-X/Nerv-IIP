---
layout: page
title: NvMobileGrid 宫格
---

<script setup>
import { NvMobileGrid } from '@nerv-iip/ui-mobile'
import {
  BellIcon, BoxesIcon, ChartColumnIcon, ClipboardListIcon,
  ScanLineIcon, TruckIcon, UsersIcon, WrenchIcon,
} from '@lucide/vue'
import { ref } from 'vue'

const gridItems = [
  { key: 'wo', icon: ClipboardListIcon, text: '工单', badge: 12 },
  { key: 'scan', icon: ScanLineIcon, text: '扫码' },
  { key: 'mtl', icon: BoxesIcon, text: '物料', badge: true },
  { key: 'report', icon: ChartColumnIcon, text: '报工' },
  { key: 'device', icon: WrenchIcon, text: '设备' },
  { key: 'plan', icon: TruckIcon, text: '排产' },
  { key: 'team', icon: UsersIcon, text: '班组' },
  { key: 'alert', icon: BellIcon, text: '告警', badge: 3 },
]
const last = ref('')
function onGrid(item) {
  last.value = item.text
}
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">基础用法</p>
    <div class="w-full overflow-hidden rounded-xl border border-border bg-card">
      <NvMobileGrid :items="gridItems" :columns="4" @select="onGrid" />
    </div>
  </section>
</template>

# NvMobileGrid 宫格

首页/工作台的功能入口宫格，图标加文字单元按 N 列排布，支持角标（数字或圆点），点击回传所选项。

## 基础用法

`items` 提供入口，`columns` 控制列数；`badge` 为数字显示计数，为 `true` 显示圆点。

```vue
<script setup>
import { NvMobileGrid } from '@nerv-iip/ui-mobile'
import { ClipboardListIcon, ScanLineIcon, BoxesIcon, BellIcon } from '@lucide/vue'

const gridItems = [
  { key: 'wo', icon: ClipboardListIcon, text: '工单', badge: 12 },
  { key: 'scan', icon: ScanLineIcon, text: '扫码' },
  { key: 'mtl', icon: BoxesIcon, text: '物料', badge: true },
  { key: 'alert', icon: BellIcon, text: '告警', badge: 3 },
]
function onGrid(item) {
  console.log('打开：', item.text)
}
</script>

<template>
  <NvMobileGrid :items="gridItems" :columns="4" @select="onGrid" />
</template>
```

## 属性

| 属性       | 说明             | 类型         | 默认    |
| ---------- | ---------------- | ------------ | ------- |
| `items`    | 入口列表         | `GridItem[]` | —       |
| `columns`  | 列数             | `number`     | `4`     |
| `bordered` | 是否显示发线边框 | `boolean`    | `false` |
| `square`   | 单元是否正方形   | `boolean`    | `false` |

`GridItem`：`{ key?: string; icon?: Component; text?: string; badge?: string \| number \| boolean }`

## 事件

| 事件     | 说明         | 回调参数                          |
| -------- | ------------ | --------------------------------- |
| `select` | 点击某个入口 | `(item: GridItem, index: number)` |

</MobileDoc>
