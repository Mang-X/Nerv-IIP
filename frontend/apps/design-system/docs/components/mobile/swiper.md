---
layout: page
title: Swiper 轮播图
---

<script setup>
import { Swiper, MobileButton, Tag } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const panels = [
  { title: '产线 A 区', sub: '稼动率 92%', tone: 'var(--brand)' },
  { title: '产线 B 区', sub: '稼动率 78%', tone: 'oklch(0.62 0.15 150)' },
  { title: '产线 C 区', sub: '稼动率 64%', tone: 'oklch(0.65 0.15 40)' },
]

const orders = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖', qty: '120 / 200', station: '焊接 03' },
  { code: 'WO-2406-0426', product: '液压阀体 V3', qty: '60 / 80', station: '装配 01' },
  { code: 'WO-2406-0433', product: '主轴箱体', qty: '15 / 50', station: 'CNC 02' },
]
const current = ref(0)
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">基础用法 · 自动播放</p>
    <Swiper :autoplay="3000" loop class="aspect-[16/9]">
      <div
        v-for="p in panels"
        :key="p.title"
        class="flex h-full w-full shrink-0 flex-col items-center justify-center text-white"
        :style="{ background: p.tone }"
      >
        <div class="text-xl font-semibold">{{ p.title }}</div>
        <div class="mt-1 text-sm opacity-90">{{ p.sub }}</div>
      </div>
    </Swiper>
  </section>
  <section>
    <p class="ds-mdoc-label">数据驱动 · 工单卡片（可交互，指示器内置底部）</p>
    <Swiper v-model:index="current" :items="orders" :frame="false">
      <template #default="{ item }">
        <div class="w-full shrink-0 px-1 pb-7">
          <div class="rounded-2xl border border-border bg-card p-4">
            <div class="flex items-center justify-between">
              <span class="text-sm text-muted-foreground">{{ item.code }}</span>
              <Tag variant="brand" size="sm">{{ item.station }}</Tag>
            </div>
            <div class="mt-2 text-lg font-medium">{{ item.product }}</div>
            <div class="mt-1 text-sm text-muted-foreground">完成进度 {{ item.qty }}</div>
            <MobileButton variant="primary" size="sm" block class="mt-3">报工</MobileButton>
          </div>
        </div>
      </template>
    </Swiper>
  </section>
</template>

# Swiper 轮播图

横向轮播。指针驱动（触摸与鼠标皆可），轨道随手指 1:1 移动，松手后吸附到最近一屏；非循环时滑到两端会有橡皮筋回弹。幻灯片可由默认插槽直接书写，或通过 `:items` 数据驱动。自包含实现，不依赖外部轮播库。

## 基础用法

默认插槽放置任意全屏内容，`autoplay` 设置自动切换毫秒数，`loop` 开启首尾循环。底部圆点指示当前位置。

```vue
<Swiper :autoplay="3000" loop class="aspect-[16/9]">
  <div v-for="p in panels" :key="p.title" class="h-full w-full shrink-0">…</div>
</Swiper>
```

## 数据驱动

传入 `:items` 后，默认作用域插槽按 `{ item, index }` 渲染每屏；用 `v-model:index` 双向绑定当前页。

```vue
<Swiper v-model:index="current" :items="orders">
  <template #default="{ item }">
    <div class="w-full shrink-0">{{ item.product }}</div>
  </template>
</Swiper>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `items` | 数据驱动的幻灯片列表（省略则用默认插槽） | `unknown[]` | — |
| `autoplay` | 自动切换间隔（毫秒，0 关闭） | `number` | `0` |
| `loop` | 首尾循环 | `boolean` | `false` |
| `dots` | 显示圆点指示器 | `boolean` | `true` |
| `indicator` | 指示器位置：`overlay` 浮于幻灯片上 / `outside` 外置于下方(可交互内容避让) | `'overlay' \| 'outside'` | `'overlay'` |
| `frame` | 视口圆角灰底背板（图片/横幅用）；幻灯片本身是卡片时关闭，避免内外圆角不一致 | `boolean` | `true` |
| `v-model:index` | 当前页索引 | `number` | `0` |

## 事件

| 事件 | 说明 | 回调参数 |
|---|---|---|
| `change` | 切换到新一页 | `(index: number)` |

</MobileDoc>
