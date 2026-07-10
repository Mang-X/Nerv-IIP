<script setup lang="ts">
// 等距工厂蓝图线框：俯视厂房/产线/设备的极淡技术线框 + 运行节点呼吸。
const U = 0.866
const V = 0.5
const cell = 56
const ox = 600
const oy = 240

function iso(ix: number, iy: number, iz: number) {
  return { x: ox + (ix - iy) * cell * U, y: oy + (ix + iy) * cell * V - iz * cell }
}

const G = 9
const gridLines: { x1: number; y1: number; x2: number; y2: number }[] = []
for (let i = 0; i <= G; i++) {
  const a = iso(i, 0, 0)
  const b = iso(i, G, 0)
  gridLines.push({ x1: a.x, y1: a.y, x2: b.x, y2: b.y })
  const c = iso(0, i, 0)
  const d = iso(G, i, 0)
  gridLines.push({ x1: c.x, y1: c.y, x2: d.x, y2: d.y })
}

const blocks = [
  { ix: 1, iy: 1, w: 2, d: 2, h: 1.5 },
  { ix: 5, iy: 1, w: 2, d: 3, h: 1 },
  { ix: 1, iy: 5, w: 3, d: 2, h: 0.8 },
  { ix: 5.5, iy: 5, w: 2, d: 2.5, h: 1.9 },
  { ix: 4, iy: 3.5, w: 1, d: 1, h: 0.6 },
  { ix: 7.5, iy: 6.5, w: 1.2, d: 1.6, h: 1.3 },
]

function p(a: { x: number; y: number }) {
  return `${a.x.toFixed(1)},${a.y.toFixed(1)}`
}

const cubes = blocks.map(({ ix, iy, w, d, h }) => {
  const top = `${p(iso(ix, iy, h))} ${p(iso(ix + w, iy, h))} ${p(iso(ix + w, iy + d, h))} ${p(iso(ix, iy + d, h))}`
  const left = `${p(iso(ix, iy + d, h))} ${p(iso(ix, iy + d, 0))} ${p(iso(ix + w, iy + d, 0))} ${p(iso(ix + w, iy + d, h))}`
  const right = `${p(iso(ix + w, iy, h))} ${p(iso(ix + w, iy, 0))} ${p(iso(ix + w, iy + d, 0))} ${p(iso(ix + w, iy + d, h))}`
  const c = iso(ix + w / 2, iy + d / 2, h)
  return { top, left, right, cx: c.x, cy: c.y }
})
</script>

<template>
  <svg class="iso" viewBox="0 0 1200 760" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
    <g class="grid">
      <line v-for="(l, i) in gridLines" :key="i" :x1="l.x1" :y1="l.y1" :x2="l.x2" :y2="l.y2" />
    </g>
    <g v-for="(c, i) in cubes" :key="`c${i}`">
      <polygon class="face-l" :points="c.left" />
      <polygon class="face-r" :points="c.right" />
      <polygon class="face-t" :points="c.top" />
      <circle class="node" :cx="c.cx" :cy="c.cy" r="3" :style="{ animationDelay: `${i * 0.5}s` }" />
    </g>
  </svg>
</template>

<style scoped>
@layer app {
  .iso {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
  }
  .grid line {
    stroke: rgba(120, 160, 220, 0.05);
    stroke-width: 1;
  }
  .face-t {
    fill: rgba(74, 166, 238, 0.05);
    stroke: rgba(120, 190, 245, 0.22);
    stroke-width: 1;
  }
  .face-l {
    fill: rgba(74, 166, 238, 0.022);
    stroke: rgba(120, 160, 220, 0.12);
    stroke-width: 1;
  }
  .face-r {
    fill: rgba(74, 166, 238, 0.04);
    stroke: rgba(120, 160, 220, 0.16);
    stroke-width: 1;
  }
  .node {
    fill: var(--nv-scr-cyan);
    filter: drop-shadow(0 0 5px var(--nv-scr-cyan));
    animation: iso-pulse 4s ease-in-out infinite;
  }
  @keyframes iso-pulse {
    0%,
    100% {
      opacity: 0.35;
    }
    50% {
      opacity: 1;
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .node {
      animation: none;
    }
  }
}
</style>
