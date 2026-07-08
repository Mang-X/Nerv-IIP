---
layout: page
title: InfiniteList 无限滚动
---

<script setup>
import { ref } from 'vue'
import { NvInfiniteList, NvCell, NvMobileTag } from '@nerv-iip/ui-mobile'

const products = ['齿轮箱端盖', '液压阀体 V3', '主轴箱体', '法兰盘 D80', '伺服电机座', '导轨滑块']
const stations = ['焊接 03', '装配 01', 'CNC 02', '热处理 01', '总装 02']
const tones = ['success', 'brand', 'warning', 'default']

function makeBatch(p) {
  return Array.from({ length: 8 }, (_, i) => {
    const seq = p * 8 + i + 1
    const done = (seq * 7) % 100
    return {
      code: `WO-2406-${String(400 + seq).padStart(4, '0')}`,
      product: products[seq % products.length],
      station: stations[seq % stations.length],
      qty: `${done}%`,
      variant: tones[seq % tones.length],
    }
  })
}

const orders = ref(makeBatch(0))
let page = 1
const loading = ref(false)
const finished = ref(false)

function onLoad() {
  // 模拟后端分页请求
  window.setTimeout(() => {
    orders.value.push(...makeBatch(page))
    page += 1
    loading.value = false
    if (orders.value.length >= 48) finished.value = true
  }, 900)
}
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">上拉加载更多 · 工单流水</p>
    <div class="overflow-hidden rounded-xl border border-border">
      <NvInfiniteList
        v-model="loading"
        :finished="finished"
        finished-text="已加载全部工单"
        class="h-[460px]"
        @load="onLoad"
      >
        <NvCell
          v-for="o in orders"
          :key="o.code"
          :title="o.product"
          :note="`${o.code} · ${o.station}`"
        >
          <template #value>
            <NvMobileTag :variant="o.variant" size="sm">{{ o.qty }}</NvMobileTag>
          </template>
        </NvCell>
      </NvInfiniteList>
    </div>
  </section>
</template>

# InfiniteList 无限滚动

滚动到接近底部时自动触发加载下一页（Vant List 风格）。底部会显示「加载中…」「上拉加载更多」或加载完毕的「没有更多了」文案。`v-model` 绑定加载中状态，加载逻辑由你在 `load` 事件里实现。

## 基础用法

容器需要一个**确定的高度**形成内部滚动区。当滚动到距底部 `offset` 像素内时触发 `load`，组件会自动把加载标志置为 `true`；请在异步请求完成后将其置回 `false`。全部加载完成后设 `finished` 为 `true`，停止继续触发。

```vue
<script setup>
const orders = ref(initial)
const loading = ref(false)
const finished = ref(false)
let page = 1

function onLoad() {
  fetchPage(page++).then((batch) => {
    orders.value.push(...batch)
    loading.value = false
    if (orders.value.length >= total) finished.value = true
  })
}
</script>

<template>
  <NvInfiniteList v-model="loading" :finished="finished" class="h-[460px]" @load="onLoad">
    <NvCell v-for="o in orders" :key="o.code" :title="o.product" :note="o.code" />
  </NvInfiniteList>
</template>
```

## 何时使用

- **用 InfiniteList**：数据从后端分页拉取、总量未知、行高不固定。
- **用 [VirtualList](/components/mobile/virtual-list)**：数据已在本地、行高一致、需要极致滚动性能。

## 属性

| 属性           | 说明                                                          | 类型      | 默认           |
| -------------- | ------------------------------------------------------------- | --------- | -------------- |
| `v-model`      | 加载中状态（触发时自动置 `true`，请求完成后自行置回 `false`） | `boolean` | `false`        |
| `finished`     | 是否已全部加载完（为 `true` 时不再触发 `load`）               | `boolean` | `false`        |
| `offset`       | 距底部多少像素内触发加载                                      | `number`  | `80`           |
| `finishedText` | 加载完毕时的底部文案                                          | `string`  | `'没有更多了'` |
| `class`        | 根容器类（**须指定高度**）                                    | `string`  | —              |

## 事件

| 事件   | 说明                           | 回调参数 |
| ------ | ------------------------------ | -------- |
| `load` | 接近底部、需要加载下一页时触发 | —        |

::: warning 注意
首屏若内容不足以撑满滚动区，可能不会触发 `load`。建议初始就预置一批数据（如上例 `makeBatch(0)`），或在挂载后主动加载首页。
:::

</MobileDoc>
