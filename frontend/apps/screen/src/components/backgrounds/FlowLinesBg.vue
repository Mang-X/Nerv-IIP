<script setup lang="ts">
// 产线数据流：工厂数据总线管网（主干 + 支线）+ 沿线缓慢流动的数据光 + 交汇节点。
// 极淡、慢速、克制，体现全厂数据贯通汇入指挥中心。
const mains = [
  'M-60 140 H540 Q600 140 600 200 V300 Q600 360 660 360 H1520',
  'M1520 700 H820 Q760 700 760 640 V480 Q760 420 700 420 H-60',
]
const branches = [
  'M280 -40 V210 Q280 270 340 270 H560',
  'M1160 850 V580 Q1160 520 1100 520 H760',
  'M-60 440 H160 Q220 440 220 500 V850',
  'M1520 320 H1280 Q1220 320 1220 260 V-40',
]
const nodes = [
  { x: 600, y: 200, ping: true },
  { x: 660, y: 360, ping: false },
  { x: 760, y: 640, ping: false },
  { x: 700, y: 420, ping: true },
  { x: 340, y: 270, ping: false },
  { x: 1100, y: 520, ping: false },
  { x: 220, y: 500, ping: true },
  { x: 1220, y: 260, ping: false },
]
</script>

<template>
  <svg class="flow" viewBox="0 0 1440 810" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
    <!-- 底层管路 -->
    <path v-for="(d, i) in mains" :key="`mr${i}`" class="rail main" :d="d" />
    <path v-for="(d, i) in branches" :key="`br${i}`" class="rail" :d="d" />
    <!-- 流动数据光 -->
    <path
      v-for="(d, i) in mains"
      :key="`mf${i}`"
      class="flux main"
      :d="d"
      :style="{ animationDelay: `${i * -9}s`, animationDuration: '22s' }"
    />
    <path
      v-for="(d, i) in branches"
      :key="`bf${i}`"
      class="flux"
      :d="d"
      :style="{ animationDelay: `${i * -4}s`, animationDuration: '17s' }"
    />
    <!-- 交汇节点 + 数据包到达扩散环 -->
    <g v-for="(n, i) in nodes" :key="`n${i}`">
      <circle
        v-if="n.ping"
        class="ping"
        :cx="n.x"
        :cy="n.y"
        r="4"
        :style="{ animationDelay: `${i * 0.9}s` }"
      />
      <circle class="node" :cx="n.x" :cy="n.y" r="3.4" :style="{ animationDelay: `${i * 0.6}s` }" />
    </g>
  </svg>
</template>

<style scoped>
.flow {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}
.rail {
  fill: none;
  stroke: rgba(120, 160, 220, 0.07);
  stroke-width: 1;
}
.rail.main {
  stroke: rgba(120, 160, 220, 0.1);
  stroke-width: 1.5;
}
.flux {
  fill: none;
  stroke: var(--sb-cyan);
  stroke-width: 1.4;
  stroke-dasharray: 10 300;
  opacity: 0.85;
  filter: drop-shadow(0 0 4px var(--sb-cyan-dim));
  animation-name: flux;
  animation-timing-function: linear;
  animation-iteration-count: infinite;
}
.flux.main {
  stroke-width: 1.8;
  stroke-dasharray: 16 360;
}
@keyframes flux {
  to {
    stroke-dashoffset: -2200;
  }
}
.node {
  fill: var(--sb-cyan);
  filter: drop-shadow(0 0 6px var(--sb-cyan));
  animation: flow-pulse 4.5s ease-in-out infinite;
}
.ping {
  fill: none;
  stroke: var(--sb-cyan);
  stroke-width: 1.2;
  transform-box: fill-box;
  transform-origin: center;
  animation: ping 5s ease-out infinite;
}
@keyframes flow-pulse {
  0%,
  100% {
    opacity: 0.32;
  }
  50% {
    opacity: 0.95;
  }
}
@keyframes ping {
  0% {
    transform: scale(1);
    opacity: 0.5;
  }
  70%,
  100% {
    transform: scale(4);
    opacity: 0;
  }
}
@media (prefers-reduced-motion: reduce) {
  .flux,
  .ping {
    display: none;
  }
  .node {
    animation: none;
  }
}
</style>
