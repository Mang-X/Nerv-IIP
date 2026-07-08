---
title: NvCarousel 轮播图
---

<script setup>
import { NvCarousel } from '@nerv-iip/ui'

const lines = [
  { zone: '焊接 A 区', oee: '92%', tone: 'var(--brand)' },
  { zone: '装配 B 区', oee: '78%', tone: 'oklch(0.62 0.15 150)' },
  { zone: 'CNC C 区', oee: '64%', tone: 'oklch(0.65 0.16 40)' },
]
const orders = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖', station: '焊接 03' },
  { code: 'WO-2406-0426', product: '液压阀体 V3', station: '装配 01' },
  { code: 'WO-2406-0433', product: '主轴箱体', station: 'CNC 02' },
]
</script>

# NvCarousel 轮播图

横向轮播——移动端 [Swiper](/components/mobile/swiper) 的桌面版。指针可拖拽、吸附到最近一屏，并补充 PC 专属的**左右箭头**（hover 浮现）与**悬停暂停**的自动播放。幻灯片由默认插槽直接书写，或 `:items` 数据驱动。自包含，不依赖外部轮播库。

## 基础用法

`autoplay` 设自动切换毫秒，`loop` 首尾循环。鼠标移入暂停，箭头随 hover 浮现。

<Demo block>
  <NvCarousel :autoplay="3500" loop class="aspect-[21/9]">
    <div
      v-for="l in lines"
      :key="l.zone"
      class="flex h-full w-full shrink-0 flex-col items-center justify-center text-white"
      :style="{ background: l.tone }"
    >
      <div class="text-2xl font-semibold">{{ l.zone }}</div>
      <div class="mt-1 text-sm opacity-90">OEE {{ l.oee }}</div>
    </div>
  </NvCarousel>
</Demo>

```vue
<NvCarousel :autoplay="3500" loop class="aspect-[21/9]">
  <div v-for="l in lines" :key="l.zone">…</div>
</NvCarousel>
```

## 数据驱动

传 `:items`，默认作用域插槽按 `{ item, index }` 渲染每屏；幻灯片本身是卡片时用 `:frame="false"` 关掉视口灰底。

<Demo block>
  <NvCarousel :items="orders" :frame="false">
    <template #default="{ item }">
      <div class="w-full shrink-0 px-1 pb-8">
        <div class="rounded-xl border border-border bg-card p-5">
          <div class="text-sm text-muted-foreground">{{ item.code }} · {{ item.station }}</div>
          <div class="mt-2 text-lg font-medium">{{ item.product }}</div>
        </div>
      </div>
    </template>
  </NvCarousel>
</Demo>

```vue
<NvCarousel :items="orders" :frame="false">
  <template #default="{ item }">…</template>
</NvCarousel>
```

## 属性

| 属性            | 说明                                     | 类型        | 默认    |
| --------------- | ---------------------------------------- | ----------- | ------- |
| `items`         | 数据驱动幻灯片（省略则用默认插槽）       | `unknown[]` | —       |
| `autoplay`      | 自动切换间隔（毫秒，0 关闭；hover 暂停） | `number`    | `0`     |
| `loop`          | 首尾循环                                 | `boolean`   | `false` |
| `arrows`        | 左右箭头（hover 浮现）                   | `boolean`   | `true`  |
| `dots`          | 圆点指示器                               | `boolean`   | `true`  |
| `frame`         | 视口圆角灰底背板                         | `boolean`   | `true`  |
| `v-model:index` | 当前页索引                               | `number`    | `0`     |

## 事件

| 事件     | 说明         | 回调参数          |
| -------- | ------------ | ----------------- |
| `change` | 切换到新一页 | `(index: number)` |
