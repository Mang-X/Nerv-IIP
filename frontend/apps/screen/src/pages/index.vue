<script setup lang="ts">
import * as icons from 'lucide-vue-next'
import {
  NvGlowDivider,
  NvKpiBar,
  NvScreenScaler,
  NvScreenSegmented,
  NvScreenStatusLight,
  NvScreenStatusTag,
  useScreenData,
} from '@nerv-iip/ui'
import { useNow } from '@vueuse/core'
import { Activity, AlertTriangle, Cpu, PackageCheck } from 'lucide-vue-next'
import { type Component, computed, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import LauncherCard from '@/components/launcher/LauncherCard.vue'
import type { LauncherSummary } from '@/data/contracts/launcher'
import { fetchLauncherSummary } from '@/data/fetchers/launcher'
import { SCREENS } from '@/data/screens'

const scope = useAccessScope()
const cards = computed(() => SCREENS.filter((s) => scope.canSeeScreen(s.key)))
// lucide 图标按名取用；显式转 Component 以过 vue-tsc 的 <component :is> 类型校验。
const iconMap = icons as unknown as Record<string, Component>
function iconOf(name: string): Component {
  return iconMap[name] ?? iconMap.SquareDashed
}

// —— 门厅实时摘要（mock seam，#570 就绪只换 fetcher）——
const {
  data: summary,
  lastUpdated,
  isStale,
  refresh,
} = useScreenData<LauncherSummary>(
  () => fetchLauncherSummary(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 5000 },
)
watch(
  () => [scope.currentFactoryId, scope.personaId],
  async () => {
    await refresh()
    // 撞上在途轮询（inFlight 跳过）时补一拍，避免短暂显示旧工厂数据
    if (summary.value && summary.value.factoryId !== scope.currentFactoryId) await refresh()
  },
)

function glanceOf(key: string) {
  return summary.value?.glances.find((g) => g.key === key)
}

// —— 工厂切换（多工厂 persona 才显示）——
const factoryOptions = computed(() => scope.factories.map((f) => ({ label: f.name, value: f.id })))
const factoryModel = computed<string | number>({
  get: () => scope.currentFactoryId,
  set: (id) => scope.switchFactory(String(id)),
})

// —— 时钟 / 更新时间 ——
const now = useNow({ interval: 1000 })
const pad = (n: number) => String(n).padStart(2, '0')
const clock = computed(
  () =>
    `${pad(now.value.getHours())}:${pad(now.value.getMinutes())}:${pad(now.value.getSeconds())}`,
)
const WEEKDAYS = ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六']
const dateText = computed(() => {
  const d = now.value
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${WEEKDAYS[d.getDay()]}`
})
const updatedText = computed(() => {
  const ts = lastUpdated.value
  if (!ts) return '—'
  const d = new Date(ts)
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
})

// —— 全厂脉搏 KPI 带 ——
interface KpiCell {
  icon?: Component
  value: string
  label: string
  tone?: 'cyan' | 'amber' | 'green'
  ring?: number
}
const kpiItems = computed<KpiCell[]>(() => {
  const k = summary.value?.kpis
  if (!k) return []
  return [
    { icon: PackageCheck, value: k.output.toLocaleString('en-US'), label: '今日产量（件）' },
    { value: `${k.achievement}%`, label: '计划达成率', tone: 'cyan', ring: k.achievement },
    { icon: Cpu, value: `${k.runningDevices}/${k.totalDevices}`, label: '运行设备' },
    {
      icon: AlertTriangle,
      value: String(k.openAlarms),
      label: '未恢复报警',
      tone: k.openAlarms > 0 ? 'amber' : undefined,
    },
    {
      icon: Activity,
      value: `${k.health}%`,
      label: '综合健康度',
      tone: k.health >= 85 ? 'green' : 'amber',
    },
  ]
})

const scopeCounts = computed(() => {
  const devices = summary.value?.kpis.totalDevices
  return `${scope.visibleWorkshops.length} 车间 · ${scope.visibleLines.length} 产线 · ${devices ?? '—'} 设备`
})
</script>

<template>
  <NvScreenScaler :design-width="1920" :design-height="1080">
    <div class="hall">
      <header class="hall-top">
        <div>
          <h1 class="hall-title">生产指挥中心</h1>
          <p class="hall-sub">NERV-IIP 工业数据大屏</p>
        </div>
        <div class="hall-clock">
          <div class="hall-time">{{ clock }}</div>
          <div class="hall-meta">
            <span>{{ dateText }}</span>
            <NvScreenStatusLight
              :tone="isStale ? 'idle' : 'run'"
              :label="isStale ? '数据链路波动' : '数据链路正常'"
            />
          </div>
        </div>
      </header>

      <NvGlowDivider />

      <div class="hall-ctx">
        <div class="hall-ctx-left">
          <NvScreenSegmented
            v-if="scope.factories.length > 1"
            v-model="factoryModel"
            :options="factoryOptions"
          />
          <NvScreenStatusTag tone="cyan" :label="scope.persona.label" />
        </div>
        <span class="hall-counts">{{ scopeCounts }}</span>
      </div>

      <div class="hall-kpi">
        <NvKpiBar v-if="kpiItems.length" :items="kpiItems" />
        <div v-else class="hall-kpi-skl" aria-hidden="true" />
      </div>

      <main class="hall-cards" :class="{ single: cards.length === 1, grid: cards.length > 3 }">
        <RouterLink v-for="s in cards" :key="s.key" :to="s.route" class="hall-card-link">
          <LauncherCard
            :title="s.title"
            :desc="s.desc"
            :icon="iconOf(s.icon)"
            :glance="glanceOf(s.key)"
          />
        </RouterLink>
        <p v-if="cards.length === 0" class="hall-empty">当前账号无可见大屏，请联系管理员开通</p>
      </main>

      <footer class="hall-foot">
        <span>数据更新 {{ updatedText }}</span>
        <span>演示数据流 · 后端接入待 #570</span>
      </footer>
    </div>
  </NvScreenScaler>
</template>

<style scoped>
.hall {
  width: 1920px;
  height: 1080px;
  box-sizing: border-box;
  padding: 40px 64px 26px;
  display: flex;
  flex-direction: column;
  gap: 22px;
  color: var(--sb-text);
  /* 门厅底：顶部一层极淡的蓝色环境光 + 96px 对齐网格（耳语级），保持近黑 */
  background:
    radial-gradient(1100px 460px at 50% -6%, rgba(74, 166, 238, 0.06), transparent 70%),
    linear-gradient(rgba(255, 255, 255, 0.013) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.013) 1px, transparent 1px), var(--sb-bg);
  background-size:
    auto,
    96px 96px,
    96px 96px,
    auto;
}

.hall-top {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
}
.hall-title {
  margin: 0;
  font-size: 34px;
  font-weight: 600;
  letter-spacing: 0.08em;
  color: #fff;
}
.hall-sub {
  margin: 7px 0 0;
  font-size: 15px;
  letter-spacing: 0.18em;
  color: var(--sb-muted);
}
.hall-clock {
  text-align: right;
}
.hall-time {
  font-size: 44px;
  font-weight: 700;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  color: #fff;
  text-shadow: var(--sb-value-glow);
}
.hall-meta {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 16px;
  margin-top: 9px;
  font-size: 13px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}

.hall-ctx {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.hall-ctx-left {
  display: flex;
  align-items: center;
  gap: 14px;
}
.hall-counts {
  font-size: 14px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}

.hall-kpi {
  min-height: 74px;
}
.hall-kpi-skl {
  height: 74px;
  border: 1px solid var(--sb-line);
  border-radius: 8px;
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  opacity: 0.5;
}

.hall-cards {
  flex: 1;
  min-height: 0;
  display: flex;
  gap: 26px;
  align-items: stretch;
}
.hall-cards.single {
  justify-content: center;
  align-items: center;
}
.hall-cards.single .hall-card-link {
  flex: 0 1 620px;
  height: min(600px, 100%);
}
/* 4+ 张卡（M2 后 6 屏）：3×2 网格 + 卡内密度收紧（页面级适配，组件原版不动） */
.hall-cards.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  grid-auto-rows: minmax(0, 1fr);
  gap: 18px;
}
/* grid 子项默认 min-height:auto 会被 chips 内容撑破行高 —— 强制可收缩，超出裁切 */
.hall-cards.grid .hall-card-link {
  min-height: 0;
}
.hall-cards.grid :deep(.lc) {
  overflow: hidden;
}
.hall-cards.grid :deep(.lc) {
  padding: 18px 22px 14px;
}
.hall-cards.grid :deep(.lc-ic) {
  width: 38px;
  height: 38px;
}
.hall-cards.grid :deep(.lc-ic svg) {
  width: 22px;
  height: 22px;
}
.hall-cards.grid :deep(.lc-title) {
  font-size: 20px;
}
.hall-cards.grid :deep(.lc-desc) {
  margin-top: 2px;
  font-size: 12.5px;
}
.hall-cards.grid :deep(.lc-stats) {
  margin-top: 6px;
}
.hall-cards.grid :deep(.lc-stat) {
  padding: 6px 2px;
}
.hall-cards.grid :deep(.lc-stat dt) {
  font-size: 13px;
}
.hall-cards.grid :deep(.lc-stat dd) {
  font-size: 19px;
}
.hall-cards.grid :deep(.lc-zone) {
  margin: 8px 0 6px;
  padding-top: 8px;
  gap: 6px;
}
.hall-cards.grid :deep(.lc-zone-t) {
  font-size: 12px;
}
.hall-cards.grid :deep(.lc-chips) {
  gap: 6px;
  /* 一瞥只保完整首行：整行级裁切，绝不出现半截 chip 残影 */
  max-height: 23px;
  overflow: hidden;
}
.hall-cards.grid :deep(.lc-chip) {
  height: 23px;
  padding: 0 10px;
  font-size: 12px;
}
.hall-cards.grid :deep(.lc-foot) {
  padding-top: 8px;
  font-size: 12.5px;
}
.hall-card-link {
  flex: 1 1 0;
  min-width: 0;
  display: block;
  text-decoration: none;
  border-radius: var(--sb-radius);
}
.hall-card-link:focus-visible {
  outline: none;
  box-shadow:
    0 0 0 2px var(--sb-bg),
    0 0 0 4px var(--sb-cyan-dim);
}
.hall-empty {
  margin: auto;
  font-size: 16px;
  color: var(--sb-faint);
}

.hall-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-top: 1px solid var(--sb-divider);
  padding-top: 14px;
  font-size: 13px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
</style>
