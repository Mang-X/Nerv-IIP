<script setup lang="ts">
import { Bot, Boxes, Building2, Cpu, Factory, ShieldCheck, Users } from 'lucide-vue-next'
import type { Component } from 'vue'

// 各模块 = 一个等距工厂方块（带图标 + 标签），方块之间按真实集成关系有向数据流动。
// CRM→ERP→MES→WMS→WCS（业务/执行主链），IIoT→MES（设备数据），MES→QMS（质检），ERP→WMS（库存）。
type Tone = 'cyan' | 'green' | 'amber' | 'indigo'
interface Mod {
  id: string
  sx: number
  sy: number
  w: number
  d: number
  h: number
  icon: Component
  name: string
  en: string
  tone: Tone
}

const cell = 34
const U = 0.866
const V = 0.5

// 分散四周、间距拉大、避开中心登录框（约 x 480–960 / y 195–615）。MES 为核心，方块最大。
const mods: Mod[] = [
  {
    id: 'crm',
    sx: 175,
    sy: 138,
    w: 1.5,
    d: 1.4,
    h: 1.5,
    icon: Users,
    name: '客户管理',
    en: 'CRM',
    tone: 'indigo',
  },
  {
    id: 'erp',
    sx: 120,
    sy: 420,
    w: 1.6,
    d: 1.5,
    h: 1.7,
    icon: Building2,
    name: '经营管理',
    en: 'ERP',
    tone: 'indigo',
  },
  {
    id: 'iiot',
    sx: 235,
    sy: 672,
    w: 1.3,
    d: 1.3,
    h: 1.2,
    icon: Cpu,
    name: '设备物联',
    en: 'IIoT',
    tone: 'green',
  },
  {
    id: 'mes',
    sx: 700,
    sy: 120,
    w: 1.9,
    d: 1.7,
    h: 1.9,
    icon: Factory,
    name: '制造执行',
    en: 'MES',
    tone: 'cyan',
  },
  {
    id: 'qms',
    sx: 1250,
    sy: 150,
    w: 1.4,
    d: 1.5,
    h: 1.3,
    icon: ShieldCheck,
    name: '质量管理',
    en: 'QMS',
    tone: 'amber',
  },
  {
    id: 'wms',
    sx: 1255,
    sy: 448,
    w: 1.6,
    d: 1.6,
    h: 1.5,
    icon: Boxes,
    name: '仓储管理',
    en: 'WMS',
    tone: 'cyan',
  },
  {
    id: 'wcs',
    sx: 770,
    sy: 678,
    w: 1.5,
    d: 1.4,
    h: 1.4,
    icon: Bot,
    name: '设备控制',
    en: 'WCS',
    tone: 'green',
  },
]

function fx(sx: number, ix: number, iy: number) {
  return sx + (ix - iy) * cell * U
}
function fy(sy: number, ix: number, iy: number, iz: number) {
  return sy + (ix + iy) * cell * V - iz * cell
}
function ptOf(x: number, y: number) {
  return `${x.toFixed(1)},${y.toFixed(1)}`
}

const cubes = mods.map((m) => {
  const { sx, sy, w, d, h } = m
  const top = `${ptOf(fx(sx, 0, 0), fy(sy, 0, 0, h))} ${ptOf(fx(sx, w, 0), fy(sy, w, 0, h))} ${ptOf(fx(sx, w, d), fy(sy, w, d, h))} ${ptOf(fx(sx, 0, d), fy(sy, 0, d, h))}`
  const left = `${ptOf(fx(sx, 0, d), fy(sy, 0, d, h))} ${ptOf(fx(sx, 0, d), fy(sy, 0, d, 0))} ${ptOf(fx(sx, w, d), fy(sy, w, d, 0))} ${ptOf(fx(sx, w, d), fy(sy, w, d, h))}`
  const right = `${ptOf(fx(sx, w, 0), fy(sy, w, 0, h))} ${ptOf(fx(sx, w, 0), fy(sy, w, 0, 0))} ${ptOf(fx(sx, w, d), fy(sy, w, d, 0))} ${ptOf(fx(sx, w, d), fy(sy, w, d, h))}`
  const ax = fx(sx, w / 2, d / 2)
  const ay = fy(sy, w / 2, d / 2, h)
  const labelY = fy(sy, w, d, 0) + 20
  const labelX = fx(sx, w / 2, d / 2)
  return {
    top,
    left,
    right,
    ax,
    ay,
    labelX,
    labelY,
    icon: m.icon,
    name: m.name,
    en: m.en,
    tone: m.tone,
  }
})

// 模块间有向数据流（非中心汇聚）
const flowDefs: [string, string][] = [
  ['crm', 'erp'],
  ['erp', 'mes'],
  ['iiot', 'mes'],
  ['mes', 'qms'],
  ['mes', 'wms'],
  ['wms', 'wcs'],
  ['erp', 'wms'],
]
function anchor(id: string) {
  const i = mods.findIndex((m) => m.id === id)
  return { x: cubes[i].ax, y: cubes[i].ay }
}
const flows = flowDefs.map(([a, b], i) => {
  const A = anchor(a)
  const B = anchor(b)
  const mx = (A.x + B.x) / 2
  const my = (A.y + B.y) / 2
  const dx = B.x - A.x
  const dy = B.y - A.y
  const len = Math.hypot(dx, dy) || 1
  const off = (i % 2 ? 1 : -1) * (40 + ((i * 33) % 80))
  const px = mx + (-dy / len) * off
  const py = my + (dx / len) * off
  return {
    d: `M${A.x.toFixed(0)} ${A.y.toFixed(0)} Q${px.toFixed(0)} ${py.toFixed(0)} ${B.x.toFixed(0)} ${B.y.toFixed(0)}`,
    dur: 9 + (i % 4),
  }
})
</script>

<template>
  <svg class="net" viewBox="0 0 1440 810" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
    <!-- 模块间数据流 -->
    <path v-for="(f, i) in flows" :key="`r${i}`" class="rail" :d="f.d" />
    <path
      v-for="(f, i) in flows"
      :key="`f${i}`"
      class="flux"
      :d="f.d"
      :style="{ animationDelay: `${i * -1.9}s`, animationDuration: `${f.dur}s` }"
    />

    <!-- 模块方块 + 图标 + 标签 -->
    <g v-for="(c, i) in cubes" :key="`c${i}`" :class="c.tone">
      <polygon class="face-l" :points="c.left" />
      <polygon class="face-r" :points="c.right" />
      <polygon class="face-t" :points="c.top" />
      <foreignObject :x="c.ax - 13" :y="c.ay - 13" width="26" height="26">
        <div class="ico">
          <component :is="c.icon" :size="20" :stroke-width="1.7" />
        </div>
      </foreignObject>
      <text class="label" :x="c.labelX" :y="c.labelY" text-anchor="middle">{{ c.name }}</text>
      <text class="en" :x="c.labelX" :y="c.labelY + 15" text-anchor="middle">{{ c.en }}</text>
    </g>
  </svg>
</template>

<style scoped>
@layer app {
  .net {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
  }
  .rail {
    fill: none;
    stroke: rgba(120, 160, 220, 0.1);
    stroke-width: 1;
  }
  .flux {
    fill: none;
    stroke: var(--nv-scr-cyan);
    stroke-width: 1.4;
    stroke-dasharray: 5 60;
    opacity: 0.85;
    animation-name: flux;
    animation-timing-function: linear;
    animation-iteration-count: infinite;
  }
  @keyframes flux {
    from {
      stroke-dashoffset: 65;
    }
    to {
      stroke-dashoffset: 0;
    }
  }

  /* 方块面：按模块域多色 */
  .face-t {
    fill: rgba(74, 166, 238, 0.06);
    stroke: rgba(120, 190, 245, 0.32);
    stroke-width: 1;
  }
  .face-l {
    fill: rgba(74, 166, 238, 0.025);
    stroke: rgba(120, 160, 220, 0.16);
    stroke-width: 1;
  }
  .face-r {
    fill: rgba(74, 166, 238, 0.045);
    stroke: rgba(120, 160, 220, 0.2);
    stroke-width: 1;
  }
  .green .face-t {
    fill: rgba(69, 208, 137, 0.06);
    stroke: rgba(69, 208, 137, 0.34);
  }
  .green .face-r {
    stroke: rgba(69, 208, 137, 0.2);
  }
  .amber .face-t {
    fill: rgba(242, 193, 78, 0.06);
    stroke: rgba(242, 193, 78, 0.34);
  }
  .amber .face-r {
    stroke: rgba(242, 193, 78, 0.2);
  }
  .indigo .face-t {
    fill: rgba(139, 155, 230, 0.06);
    stroke: rgba(139, 155, 230, 0.34);
  }
  .indigo .face-r {
    stroke: rgba(139, 155, 230, 0.2);
  }

  .ico {
    width: 26px;
    height: 26px;
    display: grid;
    place-items: center;
    color: var(--nv-scr-cyan);
  }
  .green .ico {
    color: var(--nv-scr-green);
  }
  .amber .ico {
    color: var(--nv-scr-amber);
  }
  .indigo .ico {
    color: var(--nv-scr-indigo);
  }

  .label {
    fill: var(--nv-scr-text-2);
    font-size: 13px;
    letter-spacing: 0.02em;
  }
  .en {
    fill: var(--nv-scr-faint);
    font-size: 10px;
    letter-spacing: 0.16em;
  }

  @media (prefers-reduced-motion: reduce) {
    .flux {
      display: none;
    }
  }
}
</style>
