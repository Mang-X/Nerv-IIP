<script setup lang="ts">
import { ScreenHeader, ScreenScaler } from '@nerv-iip/ui'
import { useNow } from '@vueuse/core'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

// —— 舱底生成算法：种子 = 路由（每页不同、同页刷新稳定，确定性伪随机）——
// 曲线：5 锚点正弦缓变 → Catmull-Rom 转三次贝塞尔（C1 连续，线条平滑有美感）
// 光点：粗网格抖动采样（蓝噪声近似 —— 均匀不规则、疏密天然和谐，不聚簇）
function hashStr(s: string): number {
  let h = 2166136261
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return h >>> 0
}
function mulberry32(seed: number): () => number {
  let a = seed || 1
  return () => {
    a |= 0
    a = (a + 0x6d2b79f5) | 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}
interface AmbientDot {
  x: number
  y: number
  r: number
  /** 0 = 静止；1–3 = 呼吸错相组 */
  b: number
}
function genBackdrop(seedStr: string): { paths: string[]; dots: AmbientDot[] } {
  const rnd = mulberry32(hashStr(seedStr))
  const paths: string[] = []
  for (let k = 0; k < 2; k++) {
    const baseY = 180 + rnd() * 720
    const amp = 80 + rnd() * 180
    const phase = rnd() * Math.PI * 2
    const freq = 0.8 + rnd() * 0.6
    const pts: [number, number][] = Array.from({ length: 5 }, (_, i) => {
      const x = Math.round(-80 + (2080 * i) / 4)
      const y = Math.round(
        Math.max(70, Math.min(1010, baseY + amp * Math.sin(phase + i * freq) + (rnd() - 0.5) * 70)),
      )
      return [x, y]
    })
    let d = `M ${pts[0][0]} ${pts[0][1]}`
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[Math.max(0, i - 1)]
      const p1 = pts[i]
      const p2 = pts[i + 1]
      const p3 = pts[Math.min(pts.length - 1, i + 2)]
      const c1x = (p1[0] + (p2[0] - p0[0]) / 6).toFixed(0)
      const c1y = (p1[1] + (p2[1] - p0[1]) / 6).toFixed(0)
      const c2x = (p2[0] - (p3[0] - p1[0]) / 6).toFixed(0)
      const c2y = (p2[1] - (p3[1] - p1[1]) / 6).toFixed(0)
      d += ` C ${c1x} ${c1y}, ${c2x} ${c2y}, ${p2[0]} ${p2[1]}`
    }
    paths.push(d)
  }
  const dots: AmbientDot[] = []
  const GX = 7
  const GY = 4
  for (let gx = 0; gx < GX; gx++) {
    for (let gy = 0; gy < GY; gy++) {
      const keep = rnd() <= 0.6
      const jx = rnd()
      const jy = rnd()
      const jr = rnd()
      const jb = rnd()
      if (!keep) continue
      dots.push({
        x: Math.round(((gx + 0.15 + jx * 0.7) * 1920) / GX),
        y: Math.round(((gy + 0.15 + jy * 0.7) * 1080) / GY),
        r: +(1 + jr).toFixed(1),
        b: jb < 0.45 ? 1 + Math.floor(jb * 6.66) % 3 : 0,
      })
    }
  }
  return { paths, dots }
}

const route = useRoute()
const backdrop = computed(() => genBackdrop(route.path))
/** comet 沿路径需要唯一 id（跨页不撞） */
const ambientId = computed(() => `amb-${hashStr(route.path).toString(36)}`)

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
      <!-- 舱底 v3：点阵粒子场（密度自顶向下渐隐）——现代、简洁，替代线网格 -->
      <div class="screen-layout__dots" aria-hidden="true" />
      <!-- 光路 + 星尘：按路由种子确定性生成（每页不同、刷新稳定）；
           曲线 Catmull-Rom 平滑，光点网格抖动采样疏密和谐。reduced-motion 静止 -->
      <svg class="screen-layout__paths" viewBox="0 0 1920 1080" aria-hidden="true">
        <defs>
          <linearGradient id="sl-path-a" x1="0" y1="1" x2="1" y2="0">
            <stop offset="0" stop-color="rgba(120,190,255,0)" />
            <stop offset="0.5" stop-color="rgba(120,190,255,0.16)" />
            <stop offset="1" stop-color="rgba(120,190,255,0)" />
          </linearGradient>
          <linearGradient id="sl-path-b" x1="1" y1="1" x2="0" y2="0">
            <stop offset="0" stop-color="rgba(139,155,230,0)" />
            <stop offset="0.5" stop-color="rgba(139,155,230,0.1)" />
            <stop offset="1" stop-color="rgba(139,155,230,0)" />
          </linearGradient>
        </defs>
        <path
          :id="`${ambientId}-a`"
          :d="backdrop.paths[0]"
          fill="none"
          stroke="url(#sl-path-a)"
          stroke-width="1.2"
        />
        <path
          :id="`${ambientId}-b`"
          :d="backdrop.paths[1]"
          fill="none"
          stroke="url(#sl-path-b)"
          stroke-width="1"
        />
        <circle class="comet" r="2.6" fill="#9fd4ff">
          <animateMotion dur="26s" repeatCount="indefinite">
            <mpath :href="`#${ambientId}-a`" />
          </animateMotion>
        </circle>
        <circle class="comet dim" r="2.2" fill="#aab6ef">
          <animateMotion dur="34s" begin="-14s" repeatCount="indefinite">
            <mpath :href="`#${ambientId}-b`" />
          </animateMotion>
        </circle>
        <g class="sparks">
          <circle
            v-for="(d, i) in backdrop.dots"
            :key="i"
            :cx="d.x"
            :cy="d.y"
            :r="d.r"
            :class="d.b === 0 ? 'still' : d.b === 2 ? 's2' : d.b === 3 ? 's3' : ''"
          />
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
  /* 舱底：顶缘细灯带 + 底部收暗；氛围主体交给点阵场与光路 */
  background:
    linear-gradient(180deg, rgba(96, 180, 255, 0.045), transparent 30px),
    linear-gradient(180deg, transparent 84%, rgba(0, 0, 0, 0.3)),
    var(--sb-bg);
}
/* 点阵粒子场：1px 光点阵，顶部密亮、向下渐隐（mask 控密度） */
.screen-layout__dots {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 0;
  background-image: radial-gradient(circle, rgba(150, 195, 255, 0.13) 1px, transparent 1.5px);
  background-size: 26px 26px;
  -webkit-mask-image: radial-gradient(1300px 760px at 50% 0%, rgba(0, 0, 0, 0.85), rgba(0, 0, 0, 0.2) 58%, rgba(0, 0, 0, 0.08));
  mask-image: radial-gradient(1300px 760px at 50% 0%, rgba(0, 0, 0, 0.85), rgba(0, 0, 0, 0.2) 58%, rgba(0, 0, 0, 0.08));
}
.screen-layout__paths {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 0;
}
.screen-layout__paths .comet {
  filter: drop-shadow(0 0 6px rgba(140, 205, 255, 0.85));
}
.screen-layout__paths .comet.dim {
  opacity: 0.7;
  filter: drop-shadow(0 0 5px rgba(150, 165, 235, 0.7));
}
.screen-layout__paths .sparks circle {
  fill: rgba(165, 210, 255, 0.5);
  filter: drop-shadow(0 0 4px rgba(140, 205, 255, 0.6));
  animation: sl-spark 4.6s ease-in-out infinite;
}
.screen-layout__paths .sparks .still {
  animation: none;
  opacity: 0.55;
}
.screen-layout__paths .sparks .s2 {
  animation-delay: 1.5s;
}
.screen-layout__paths .sparks .s3 {
  animation-delay: 3.1s;
}
@keyframes sl-spark {
  50% {
    opacity: 0.15;
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
  .screen-layout__paths .comet {
    display: none;
  }
  .screen-layout__paths .sparks circle {
    animation: none;
  }
}
</style>
