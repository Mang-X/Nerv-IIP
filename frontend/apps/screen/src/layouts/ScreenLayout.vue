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
      <ScreenHeader :title="title" :time="time" :date="date" :line="line" :screen="screen" />
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
}
.screen-layout__body {
  flex: 1;
  min-height: 0;
  margin-top: 16px;
}
</style>
