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
      <div class="screen-layout__fx" aria-hidden="true" />
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
  /* 指挥舱氛围底：环境光晕 ×3 + 对齐网格 + 斜光束 + 底部收暗。
     面板走半透明背景，让这些层透出来 —— 通透感来自这里。 */
  background:
    linear-gradient(180deg, transparent 82%, rgba(0, 0, 0, 0.3)),
    radial-gradient(1400px 340px at 50% -6%, rgba(74, 166, 238, 0.11), transparent 70%),
    radial-gradient(920px 640px at 5% 2%, rgba(74, 166, 238, 0.07), transparent 65%),
    radial-gradient(1050px 720px at 97% 100%, rgba(139, 155, 230, 0.065), transparent 70%),
    linear-gradient(115deg, transparent 40%, rgba(120, 190, 255, 0.032) 50%, transparent 60%),
    linear-gradient(rgba(255, 255, 255, 0.016) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.016) 1px, transparent 1px),
    var(--sb-bg);
  background-size:
    auto,
    auto,
    auto,
    auto,
    auto,
    64px 64px,
    64px 64px,
    auto;
}
/* 顶部环境光的缓呼吸层（reduced-motion 静止） */
.screen-layout__fx {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 0;
  background: radial-gradient(1200px 480px at 50% -2%, rgba(96, 180, 255, 0.055), transparent 68%);
  animation: sl-breathe 14s ease-in-out infinite;
}
@keyframes sl-breathe {
  50% {
    opacity: 0.35;
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
  .screen-layout__fx {
    animation: none;
  }
}
</style>
