<script setup lang="ts">
import {
  AlarmTable,
  KpiBar,
  OeeHero,
  ScreenHeader,
  ScreenPanel,
  StatusCard,
  TaktGantt,
  TrendChart,
} from '@nerv-iip/ui'
import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * MES 运营看板 — the "complete example": assembled entirely from the real
 * @nerv-iip/ui `screen/` components (no hand-rolled markup). Fixed 1920×1080
 * canvas scaled to the viewport; the board only owns the backdrop + grid layout.
 */
const scale = ref(1)
function fit() {
  scale.value = Math.min(window.innerWidth / 1920, window.innerHeight / 1080)
}
onMounted(() => {
  fit()
  window.addEventListener('resize', fit)
})
onBeforeUnmount(() => window.removeEventListener('resize', fit))

const lines = [
  { name: '焊接线 A', state: '运行', label: '运行中', tone: 'run', plan: '1,240', actual: '1,156', rate: '93.2%', downtime: '28 分钟' },
  { name: '装配线 B', state: '待机', label: '待机中', tone: 'idle', plan: '980', actual: '742', rate: '75.7%', downtime: '42 分钟' },
  { name: 'CNC 线 C', state: '报警', label: '报警中', tone: 'alarm', plan: '760', actual: '312', rate: '41.1%', downtime: '114 分钟' },
] as const
</script>

<template>
  <div class="sb-fit">
    <div class="sb-stage" :style="{ transform: `scale(${scale})` }">
      <div class="sb-grid" /><div class="sb-noise" /><div class="sb-glow" />
      <div class="sb-wrap">
        <ScreenHeader
          title="智能工厂 MES 运营看板"
          time="2024-06-12 10:24:36"
          date="星期三"
          line="全部产线"
          screen="中央控制室大屏 01"
        />

        <div class="sb-row r1">
          <ScreenPanel>
            <OeeHero label="设备综合效率 OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" />
          </ScreenPanel>
          <StatusCard v-for="l in lines" :key="l.name" v-bind="l" />
        </div>

        <div class="sb-row">
          <TaktGantt />
        </div>

        <div class="sb-row r3">
          <TrendChart />
          <AlarmTable />
        </div>

        <KpiBar />
      </div>
    </div>
  </div>
</template>

<style scoped>
.sb-fit {
  position: fixed;
  inset: 0;
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #05070d;
  overflow: hidden;
}
.sb-stage {
  position: relative;
  width: 1920px;
  height: 1080px;
  flex: none;
  transform-origin: center;
  background: radial-gradient(120% 82% at 50% -6%, var(--sb-bg-accent) 0%, #0e1a32 36%, #0a1120 60%, var(--sb-bg) 80%, #05070d 100%);
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
  font-family: ui-sans-serif, system-ui, -apple-system, 'Segoe UI', 'PingFang SC', 'Microsoft YaHei', sans-serif;
}
.sb-grid {
  position: absolute;
  inset: 0;
  background-image: linear-gradient(rgba(125, 170, 255, 0.06) 1px, transparent 1px), linear-gradient(90deg, rgba(125, 170, 255, 0.06) 1px, transparent 1px);
  background-size: 60px 60px;
  -webkit-mask-image: radial-gradient(90% 80% at 50% 28%, #000 20%, transparent 100%);
  mask-image: radial-gradient(90% 80% at 50% 28%, #000 20%, transparent 100%);
  opacity: 0.5;
}
.sb-noise {
  position: absolute;
  inset: 0;
  pointer-events: none;
  opacity: 0.035;
  mix-blend-mode: overlay;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='160'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E");
}
.sb-glow {
  position: absolute;
  top: -160px;
  left: 50%;
  width: 900px;
  height: 380px;
  transform: translateX(-50%);
  background: radial-gradient(closest-side, rgba(0, 160, 220, 0.12), transparent);
}
.sb-wrap {
  position: relative;
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: 0 30px 24px;
}
.sb-row {
  display: grid;
  gap: 18px;
  margin-top: 18px;
}
.r1 {
  grid-template-columns: 1.4fr 1fr 1fr 1fr;
}
.r3 {
  grid-template-columns: 1.55fr 1fr;
  flex: 1;
  min-height: 0;
}
.r3 > * {
  min-height: 0;
}
</style>
