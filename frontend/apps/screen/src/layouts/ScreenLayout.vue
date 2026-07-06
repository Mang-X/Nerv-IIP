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
      <!-- 舱底 v3：点阵粒子场（密度自顶向下渐隐）——现代、简洁，替代线网格 -->
      <div class="screen-layout__dots" aria-hidden="true" />
      <!-- 光路：两条贝塞尔光径 + 流动光点（数据在流动的活感），reduced-motion 隐藏光点 -->
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
          id="sl-route-a"
          d="M -80 940 C 500 1040, 820 560, 1200 620 S 1800 150, 2000 120"
          fill="none"
          stroke="url(#sl-path-a)"
          stroke-width="1.2"
        />
        <path
          id="sl-route-b"
          d="M 2000 880 C 1480 780, 1180 1010, 740 900 S 180 470, -80 560"
          fill="none"
          stroke="url(#sl-path-b)"
          stroke-width="1"
        />
        <circle class="comet" r="2.6" fill="#9fd4ff">
          <animateMotion dur="26s" repeatCount="indefinite">
            <mpath href="#sl-route-a" />
          </animateMotion>
        </circle>
        <circle class="comet dim" r="2.2" fill="#aab6ef">
          <animateMotion dur="34s" begin="-14s" repeatCount="indefinite">
            <mpath href="#sl-route-b" />
          </animateMotion>
        </circle>
        <!-- 点阵场里亮起的几颗活点（错相缓呼吸），坐标落在 26px 栅格上 -->
        <g class="sparks">
          <circle cx="390" cy="182" r="1.8" />
          <circle cx="1014" cy="130" r="1.6" class="s2" />
          <circle cx="1560" cy="234" r="1.8" class="s3" />
          <circle cx="234" cy="546" r="1.6" class="s2" />
          <circle cx="1716" cy="676" r="1.6" class="s3" />
          <circle cx="702" cy="1014" r="1.8" />
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
