<script setup lang="ts">
import { ScreenHeader } from '@nerv-iip/ui'
import { useNow } from '@vueuse/core'
import { computed } from 'vue'
import { ScreenScaler } from '@/screen-kit'

withDefaults(
  defineProps<{
    title?: string
    line?: string
    screen?: string
  }>(),
  {
    title: 'Nerv-IIP 工厂运营大屏',
    line: '全部车间',
    screen: '指挥中心大屏 01',
  },
)

const WEEKDAYS = ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六']
// 秒级展示：固定 1s 间隔，避免默认 rAF 每帧触发布局重算（大屏常驻页）
const now = useNow({ interval: 1000 })

function pad(n: number): string {
  return String(n).padStart(2, '0')
}

const time = computed(() => {
  const d = now.value
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
})
const date = computed(() => WEEKDAYS[now.value.getDay()])
</script>

<template>
  <ScreenScaler :design-width="1920" :design-height="1080">
    <div class="screen-layout">
      <!-- 舱底装饰：四角角标 + 电路走线 + 稀疏光点（个别节点缓呼吸），不做大面积泛光 -->
      <svg class="screen-layout__deco" viewBox="0 0 1920 1080" aria-hidden="true">
        <!-- 四角角标 -->
        <g class="corners">
          <path d="M14 64 V26 a12 12 0 0 1 12 -12 H64" />
          <path d="M1856 14 h38 a12 12 0 0 1 12 12 v38" />
          <path d="M14 1016 v38 a12 12 0 0 0 12 12 h38" />
          <path d="M1906 1016 v38 a12 12 0 0 1 -12 12 h-38" />
          <rect x="22" y="22" width="5" height="5" />
          <rect x="1893" y="22" width="5" height="5" />
          <rect x="22" y="1053" width="5" height="5" />
          <rect x="1893" y="1053" width="5" height="5" />
        </g>
        <!-- 电路走线（左下 / 右上），端点带节点 -->
        <g class="traces">
          <path d="M30 992 V884 l46 -46 H332" />
          <circle cx="30" cy="992" r="3" class="node" />
          <circle cx="76" cy="838" r="2.6" class="node breathe" />
          <circle cx="332" cy="838" r="3" class="node breathe delay" />
          <path d="M1890 96 v96 l-46 46 H1600" />
          <circle cx="1890" cy="96" r="3" class="node" />
          <circle cx="1844" cy="238" r="2.6" class="node breathe delay2" />
          <circle cx="1600" cy="238" r="3" class="node breathe" />
        </g>
        <!-- 稀疏光点（隐隐星尘） -->
        <g class="dust">
          <circle cx="410" cy="180" r="1.4" />
          <circle cx="700" cy="88" r="1.1" class="breathe" />
          <circle cx="1130" cy="150" r="1.5" />
          <circle cx="1460" cy="70" r="1.2" class="breathe delay" />
          <circle cx="240" cy="520" r="1.2" />
          <circle cx="1700" cy="480" r="1.4" class="breathe delay2" />
          <circle cx="920" cy="1030" r="1.3" />
          <circle cx="1330" cy="960" r="1.1" class="breathe" />
          <circle cx="560" cy="880" r="1.4" class="breathe delay2" />
          <circle cx="1820" cy="740" r="1.2" />
          <circle cx="90" cy="330" r="1.3" class="breathe delay" />
          <circle cx="1560" cy="860" r="1.2" />
        </g>
      </svg>
      <ScreenHeader class="screen-layout__chrome" :title="title" :time="time" :date="date" :line="line" :screen="screen" />
      <main class="screen-layout__body">
        <slot />
      </main>
    </div>
  </ScreenScaler>
</template>

<style scoped>
.screen-layout {
  width: 1920px;
  height: 1080px;
  box-sizing: border-box;
  padding: 22px 30px 26px;
  display: flex;
  flex-direction: column;
  position: relative;
  isolation: isolate;
  /* 舱底：顶缘细灯带 + 对齐网格 + 底部收暗 —— 不铺大面积泛光，
     氛围由 SVG 走线/光点与面板通透共同给出 */
  background:
    linear-gradient(180deg, rgba(96, 180, 255, 0.05), transparent 34px),
    linear-gradient(180deg, transparent 84%, rgba(0, 0, 0, 0.3)),
    linear-gradient(rgba(255, 255, 255, 0.014) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.014) 1px, transparent 1px),
    var(--sb-bg);
  background-size:
    auto,
    auto,
    64px 64px,
    64px 64px,
    auto;
}
.screen-layout__deco {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 0;
}
.screen-layout__deco .corners path {
  fill: none;
  stroke: rgba(126, 190, 255, 0.42);
  stroke-width: 2;
}
.screen-layout__deco .corners rect {
  fill: rgba(126, 190, 255, 0.5);
}
.screen-layout__deco .traces path {
  fill: none;
  stroke: rgba(126, 190, 255, 0.14);
  stroke-width: 1.5;
}
.screen-layout__deco .node {
  fill: rgba(126, 190, 255, 0.45);
  filter: drop-shadow(0 0 4px rgba(126, 190, 255, 0.5));
}
.screen-layout__deco .dust circle {
  fill: rgba(185, 220, 255, 0.32);
}
/* 个别节点/光点缓呼吸 —— 隐隐的活感，reduced-motion 静止 */
.screen-layout__deco .breathe {
  animation: sl-node 4.2s ease-in-out infinite;
}
.screen-layout__deco .breathe.delay {
  animation-delay: 1.4s;
}
.screen-layout__deco .breathe.delay2 {
  animation-delay: 2.8s;
}
@keyframes sl-node {
  50% {
    opacity: 0.25;
  }
}
.screen-layout__chrome,
.screen-layout__body {
  position: relative;
  z-index: 1;
}
.screen-layout__body {
  flex: 1;
  min-height: 0;
  margin-top: 16px;
}
@media (prefers-reduced-motion: reduce) {
  .screen-layout__deco .breathe {
    animation: none;
  }
}
</style>
