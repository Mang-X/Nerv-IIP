<script setup lang="ts">
import { KpiBar, Sparkline, StatusLight } from '@nerv-iip/ui'
import { Activity, AlertTriangle, PackageCheck, Workflow } from 'lucide-vue-next'
import { type Component, computed, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import type { LineSummaryCard } from '@/data/contracts/line'
import { fetchLineCards } from '@/data/fetchers/line'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useScreenData } from '@/screen-kit'

// 产线选择器 = 迷你监控板（spec §四）：红线置顶（数据层排序）、scope 收窄、
// 点卡进入单线大屏。4s 轮询。
const scope = useAccessScope()
const { data: cards, refresh } = useScreenData<LineSummaryCard[]>(
  () => fetchLineCards(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 4000 },
)
watch(
  () => [scope.currentFactoryId, scope.personaId],
  () => {
    void refresh()
  },
)

const factoryName = computed(
  () => scope.factories.find((f) => f.id === scope.currentFactoryId)?.name ?? '全部车间',
)

const toneOf = (s: LineSummaryCard['state']) =>
  s === 'alarm' ? ('alarm' as const) : s === 'attention' ? ('idle' as const) : ('run' as const)

const stateColor = (s: LineSummaryCard['state']) =>
  s === 'alarm' ? 'var(--sb-red)' : s === 'attention' ? 'var(--sb-amber)' : 'var(--sb-cyan)'

// —— 顶部产线汇总带（卡片数据 rollup）——
interface KpiCell {
  icon?: Component
  value: string
  label: string
  tone?: 'cyan' | 'amber' | 'green'
  ring?: number
}
const nf = new Intl.NumberFormat('en-US')
const kpiItems = computed<KpiCell[]>(() => {
  const list = cards.value
  if (!list?.length) return []
  const alarm = list.filter((c) => c.state === 'alarm').length
  const attention = list.filter((c) => c.state === 'attention').length
  const good = list.reduce((n, c) => n + c.output.good, 0)
  const plan = list.reduce((n, c) => n + c.output.plan, 0)
  const avg = plan > 0 ? Math.round((good / plan) * 100) : 0
  return [
    { icon: Workflow, value: `${list.length - alarm - attention}/${list.length}`, label: '正常作业产线' },
    { icon: AlertTriangle, value: String(alarm), label: '报警产线', tone: alarm > 0 ? 'amber' : undefined },
    { icon: Activity, value: String(attention), label: '需关注产线', tone: attention > 0 ? 'amber' : undefined },
    { icon: PackageCheck, value: nf.format(good), label: '当班总产量（件）' },
    { value: `${avg}%`, label: '当班平均达成', tone: 'cyan', ring: avg },
  ]
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 产线监控大屏" :line="factoryName" screen="指挥中心大屏 03">
    <div v-if="cards" class="ls">
      <div class="ls-kpi">
        <KpiBar v-if="kpiItems.length" :items="kpiItems" />
      </div>

      <div class="sec-h">
        <i class="sec-glyph" aria-hidden="true" />
        <span class="sec-t">产线状态总览</span>
        <span class="sec-rule" aria-hidden="true" />
        <span class="ls-meta">{{ cards.length }} 条产线 · 点击进入单线大屏</span>
      </div>

      <div class="ls-grid">
        <RouterLink v-for="c in cards" :key="c.id" :to="`/line/${c.id}`" class="ls-link">
          <article class="ls-card" :class="c.state">
            <header class="ls-top">
              <StatusLight :tone="toneOf(c.state)" :label="c.stateLabel" />
              <span v-if="c.offlineDevices > 0" class="ls-off">{{ c.offlineDevices }} 台失联</span>
            </header>
            <h3 class="ls-name">{{ c.name }}</h3>
            <p class="ls-ws">{{ c.workshopName }}<template v-if="c.currentWo"> · {{ c.currentWo }}</template></p>
            <div class="ls-nums">
              <div>
                <dt>当班达成</dt>
                <dd :class="{ bad: c.achievement < 85 }">{{ c.achievement }}<small>%</small></dd>
              </div>
              <div>
                <dt>节拍偏差</dt>
                <dd :class="{ bad: c.taktDeviationPct > 8, warn: c.taktDeviationPct > 0 && c.taktDeviationPct <= 8 }">
                  {{ c.taktDeviationPct > 0 ? '+' : '' }}{{ c.taktDeviationPct }}<small>%</small>
                </dd>
              </div>
              <div>
                <dt>当班产量</dt>
                <dd class="ls-out">
                  {{ nf.format(c.output.good) }}<small>/ {{ nf.format(c.output.plan) }}</small>
                </dd>
              </div>
            </div>
            <div class="ls-spark">
              <Sparkline :data="c.hourly" area :color="stateColor(c.state)" />
            </div>
            <div class="ls-dots" :aria-label="`设备 ${c.deviceDots.length} 台`">
              <i v-for="(d, i) in c.deviceDots" :key="i" class="ls-dot" :class="d" />
              <span class="ls-dots-n">{{ c.deviceDots.length }} 台</span>
            </div>
            <p class="ls-alert" :class="c.state">{{ c.alert ?? '作业平稳' }}</p>
          </article>
        </RouterLink>
      </div>
    </div>
    <div v-else class="ls-loading">连接数据…</div>
  </ScreenLayout>
</template>

<style scoped>
.ls {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.ls-loading {
  height: 100%;
  display: grid;
  place-content: center;
  color: var(--sb-muted);
  font-size: 15px;
}
.sec-h {
  display: flex;
  align-items: center;
  gap: 11px;
  margin-bottom: 14px;
  min-height: 24px;
}
.sec-glyph {
  width: 8px;
  height: 18px;
  flex: none;
  border-radius: 2px;
  transform: skewX(-16deg);
  background: linear-gradient(180deg, var(--sb-cyan), rgba(74, 166, 238, 0.25));
  box-shadow: 0 0 11px rgba(74, 166, 238, 0.55);
}
.sec-t {
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.1em;
  color: #fff;
  text-shadow: 0 0 16px rgba(96, 180, 255, 0.4);
  white-space: nowrap;
}
.sec-rule {
  flex: 1;
  height: 1px;
  margin: 0 6px;
  background: linear-gradient(90deg, rgba(135, 208, 255, 0.28), rgba(255, 255, 255, 0.05) 45%, transparent);
}
.ls-meta {
  font-size: 13px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}

.ls-grid {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  grid-auto-rows: 1fr;
  gap: 14px;
}
.ls-link {
  display: block;
  min-width: 0;
  text-decoration: none;
  border-radius: var(--sb-radius);
}
.ls-link:focus-visible {
  outline: none;
  box-shadow:
    0 0 0 2px var(--sb-bg),
    0 0 0 4px var(--sb-cyan-dim);
}
.ls-card {
  height: 100%;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  padding: 16px 18px 13px;
  border-radius: var(--sb-radius);
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid var(--sb-line);
  border-top-color: rgba(255, 255, 255, 0.09);
  transition:
    border-color 0.18s var(--sb-ease),
    transform 0.12s var(--sb-ease);
}
.ls-link:hover .ls-card {
  border-color: rgba(135, 208, 255, 0.3);
}
.ls-link:active .ls-card {
  transform: scale(0.985);
}
.ls-card.alarm {
  border-color: rgba(239, 90, 99, 0.4);
  position: relative;
}
.ls-card.alarm::after {
  content: '';
  position: absolute;
  inset: -1px;
  border-radius: inherit;
  pointer-events: none;
  box-shadow: 0 0 16px -4px rgba(239, 90, 99, 0.6);
  animation: ls-alarm 1.8s ease-in-out infinite;
}
@keyframes ls-alarm {
  50% {
    opacity: 0.25;
  }
}

.ls-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.ls-off {
  padding: 2px 8px;
  border-radius: 5px;
  border: 1px dashed rgba(255, 255, 255, 0.24);
  background: repeating-linear-gradient(-45deg, rgba(255, 255, 255, 0.04) 0 6px, transparent 6px 12px);
  font-size: 11.5px;
  color: var(--sb-muted);
}
.ls-name {
  margin: 9px 0 0;
  font-size: 23px;
  font-weight: 700;
  color: #fff;
  letter-spacing: 0.04em;
}
.ls-ws {
  margin: 4px 0 0;
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.ls-kpi {
  flex: none;
  margin-bottom: 14px;
}
.ls-nums {
  display: grid;
  grid-template-columns: 1fr 1fr 1.3fr;
  gap: 10px;
  margin: 12px 0 0;
}
.ls-out small {
  color: var(--sb-muted);
}
.ls-spark {
  height: 34px;
  margin: 10px 0 6px;
}
.ls-dots {
  display: flex;
  align-items: center;
  gap: 5px;
  flex-wrap: wrap;
}
.ls-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--sb-faint);
}
.ls-dot.run {
  background: var(--sb-green);
  box-shadow: 0 0 6px var(--sb-green);
}
.ls-dot.idle {
  background: var(--sb-amber);
  box-shadow: 0 0 6px var(--sb-amber);
}
.ls-dot.alarm {
  background: var(--sb-red);
  box-shadow: 0 0 6px var(--sb-red);
}
.ls-dot.down {
  background: var(--sb-muted);
}
.ls-dots-n {
  margin-left: 4px;
  font-size: 11.5px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.ls-nums dt {
  font-size: 12px;
  color: var(--sb-muted);
}
.ls-nums dd {
  margin: 3px 0 0;
  font-size: 27px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}
.ls-nums dd small {
  font-size: 13px;
  font-weight: 600;
  margin-left: 1px;
}
.ls-nums dd.warn {
  color: var(--sb-amber);
}
.ls-nums dd.bad {
  color: var(--sb-red);
}
.ls-alert {
  margin: auto 0 0;
  padding-top: 9px;
  font-size: 12.5px;
  color: var(--sb-faint);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.ls-alert.alarm {
  color: var(--sb-red);
}
.ls-alert.attention {
  color: var(--sb-amber);
}

@media (prefers-reduced-motion: reduce) {
  .ls-card {
    transition: none;
  }
  .ls-card.alarm::after {
    animation: none;
  }
}
</style>
