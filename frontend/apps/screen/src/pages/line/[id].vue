<script setup lang="ts">
import { RingGauge, ScreenPanel, Sparkline, StatusTag, TrendChart } from '@nerv-iip/ui'
import { ChevronDown, CircleCheck, Gauge, OctagonAlert, Timer, UserRound, Users } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { paramColor } from '@/components/equipment/paramColors'
import LineAndonHero from '@/components/line/LineAndonHero.vue'
import type { LineBoard } from '@/data/contracts/line'
import { fetchLineBoard } from '@/data/fetchers/line'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useScreenData } from '@/screen-kit'

// 单线监控大屏（spec §四）：现场远距可读；横幅只在报警/停机时出现；
// 安灯呼叫-响应闭环 待 MAN-322（诚实标注）。4s 轮询。
const route = useRoute('/line/[id]')
const scope = useAccessScope()
const lineId = computed(() => String(route.params.id ?? ''))

const { data: board, refresh } = useScreenData<LineBoard | null>(
  () => fetchLineBoard(lineId.value, scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 4000 },
)
watch(
  () => [lineId.value, scope.currentFactoryId, scope.personaId],
  () => {
    void refresh()
  },
)

/** 分钟 → 「Xh Ym」远距倒计时格式 */
function fmtMin(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  return h > 0 ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m`
}
const nf = new Intl.NumberFormat('en-US')

// —— 趋势图（实际 vs 节拍产能）：y 轴刻度随数据生成、x 轴抽稀、悬停按整点取值 ——
const planSeries = computed(() => Array.from({ length: 12 }, () => board.value?.planPerHour ?? 0))
const trendY = computed(() => {
  const b = board.value
  if (!b) return []
  const peak = Math.max(1, ...b.hourly, b.planPerHour)
  const mag = 10 ** Math.floor(Math.log10(peak))
  const top = Math.ceil(peak / mag) * mag
  return Array.from({ length: 6 }, (_, i) => nf.format(Math.round((top * (5 - i)) / 5)))
})
const trendX = computed(() => board.value?.hourLabels.filter((_, i) => i % 2 === 0) ?? [])
const trendPin = computed(() => {
  const b = board.value
  if (!b) return undefined
  return {
    x: 11,
    label: b.hourLabels[11] ?? '',
    actual: nf.format(b.hourly[11] ?? 0),
    plan: nf.format(b.planPerHour),
  }
})

// —— 设备折叠面板：点行展开该设备 4 项参数（类型色 + 趋势）——
const expandedDev = ref<string | null>(null)
function toggleDev(id: string) {
  expandedDev.value = expandedDev.value === id ? null : id
}

// —— OEE 24h 热力（waffle 4×6）：数值分档着色，悬停读整点数值 ——
function oeeBucket(v: number): string {
  if (v >= 85) return 'g4'
  if (v >= 70) return 'g3'
  if (v >= 55) return 'g2'
  return 'g1'
}
/** 热力格 i（0 = 24h 前）对应的整点标签 */
function waffleHour(i: number): string {
  const h = (new Date().getHours() - 23 + i + 48) % 24
  return `${String(h).padStart(2, '0')}:00`
}
const wTip = ref<{ i: number; v: number; x: number; y: number } | null>(null)
function wTipSet(i: number, v: number, e: MouseEvent) {
  const el = e.currentTarget as HTMLElement
  wTip.value = { i, v, x: el.offsetLeft + el.offsetWidth / 2, y: el.offsetTop }
}
</script>

<template>
  <ScreenLayout
    title="Nerv-IIP 产线监控大屏"
    :line="board?.lineName ?? '产线'"
    screen="指挥中心大屏 03"
  >
    <div v-if="board" class="lb">
      <!-- 即时停机/报警横幅：有事才出现（异常是例外） -->
      <div v-if="board.banner" class="lb-banner" :class="board.banner.level">
        <i class="lb-banner-dot" />
        <b>{{ board.banner.level === 'alarm' ? '设备报警' : '停机' }}</b>
        <span class="lb-banner-txt">{{ board.banner.text }}</span>
        <span class="lb-banner-since">{{ board.banner.since }} 起</span>
      </div>

      <div class="lb-main">
        <!-- 左：超大状态灯 + 线上设备带 -->
        <section class="lb-left">
          <LineAndonHero
            :state="board.state"
            :state-label="board.stateLabel"
            :offline-devices="board.offlineDevices"
          />

          <!-- 当班四格：一次合格率 / 停机 / 线长 / 在岗 -->
          <dl class="lb-stats">
            <div>
              <dt><CircleCheck :size="13" class="lb-stat-ic" />一次合格率</dt>
              <dd :class="{ warn: board.fpy < 98 }">{{ board.fpy }}<small>%</small></dd>
            </div>
            <div>
              <dt><OctagonAlert :size="13" class="lb-stat-ic" />当班停机</dt>
              <dd :class="{ bad: board.downtime.count > 0 }">
                {{ board.downtime.count }}<small> 次 · {{ board.downtime.totalMin }} min</small>
              </dd>
            </div>
            <div>
              <dt><UserRound :size="13" class="lb-stat-ic" />线长</dt>
              <dd class="lb-stat-txt">{{ board.crew.leader }}</dd>
            </div>
            <div>
              <dt><Users :size="13" class="lb-stat-ic" />在岗</dt>
              <dd>{{ board.crew.operators }}<small> 人</small></dd>
            </div>
          </dl>

          <div class="lb-devs">
            <h5 class="lb-devs-t">线上设备 · {{ board.devices.length }} 台 <small>点击展开参数</small></h5>
            <div v-for="d in board.devices" :key="d.id" class="lb-dev-wrap">
              <button type="button" class="lb-dev" :class="d.state" @click="toggleDev(d.id)">
                <i class="dot" :class="d.state" />
                <span class="lb-dev-name">{{ d.name }}</span>
                <span v-if="d.param" class="lb-dev-param">{{ d.param }}</span>
                <span class="lb-dev-state" :class="d.state">{{ d.stateLabel }}</span>
                <ChevronDown :size="14" class="lb-chev" :class="{ open: expandedDev === d.id }" />
              </button>
              <Transition name="dev">
                <div v-if="expandedDev === d.id" class="lb-dev-detail">
                  <div class="lb-dev-detail-in">
                    <div v-for="p in d.params" :key="p.label" class="lb-dp">
                      <span class="lb-dp-l">{{ p.label }}</span>
                      <span class="lb-dp-spark"><Sparkline :data="p.spark" :color="paramColor(p.kind, p.tone)" /></span>
                      <b :style="{ color: paramColor(p.kind, p.tone) }">
                        {{ p.value === null ? '—' : `${p.value}${p.unit}` }}
                      </b>
                    </div>
                  </div>
                </div>
              </Transition>
            </div>
          </div>

          <!-- 安灯呼叫（并入线体域，与状态灯同侧；闭环 待 MAN-322） -->
          <div class="lb-andon-mini">
            <div class="lb-andon-h">
              <span>安灯呼叫</span>
              <StatusTag tone="amber" label="闭环 · 待 MAN-322" />
            </div>
            <div v-for="a in board.andon" :key="a.time + a.station" class="lb-andon-row">
              <span class="lb-andon-time">{{ a.time }}</span>
              <span class="lb-andon-txt">{{ a.station }} · {{ a.type }} · {{ a.response }}</span>
              <b class="lb-andon-state" :class="{ open: a.state === '响应中' }">{{ a.state }}</b>
            </div>
            <div v-if="!board.andon.length" class="lb-andon-empty"><i class="lb-andon-ok" />当班无安灯呼叫</div>
          </div>
        </section>

        <!-- 右：产量/节拍 · 小时趋势 · 当前工单 -->
        <div class="lb-right">
          <div class="lb-row1">
            <ScreenPanel title="当班产量" class="lb-output">
              <template #extra>
                <span class="lb-shift">{{ board.shift.name }} {{ board.shift.range }} · 剩余 {{ fmtMin(board.shift.remainingMin) }}</span>
              </template>
              <div class="lb-out-in">
                <div class="lb-out-hero">
                  <div class="lb-out-v">
                    {{ nf.format(board.output.good) }}<small>/ {{ nf.format(board.output.plan) }} 件</small>
                  </div>
                  <div class="lb-out-sub">
                    <span>良品 <b class="ok">{{ nf.format(board.output.good) }}</b></span>
                    <span>报废 <b :class="{ bad: board.output.scrap > 0 }">{{ board.output.scrap }}</b></span>
                    <span>返修 <b :class="{ warn: board.output.rework > 0 }">{{ board.output.rework }}</b></span>
                  </div>
                </div>
                <RingGauge :value="board.output.achievement" label="当班达成率" :size="132" :value-size="34" />
              </div>
            </ScreenPanel>

            <ScreenPanel title="节拍达成" class="lb-takt">
              <div class="lb-takt-in">
                <div class="lb-takt-v" :class="{ late: board.takt.deviationPct > 0 }">
                  <Timer :size="21" :stroke-width="1.6" class="lb-ic-chip" />
                  <span class="lb-num">{{ board.takt.deviationPct > 0 ? '+' : '' }}{{ board.takt.deviationPct }}<small>%</small></span>
                </div>
                <p class="lb-takt-sub">
                  标准 {{ board.takt.standardSec }}s · 实际
                  <b :class="{ late: board.takt.deviationPct > 0 }">{{ board.takt.actualSec }}s</b>
                </p>
                <p class="lb-takt-hint">{{ board.takt.deviationPct > 0 ? '节拍落后，关注瓶颈工位' : '节拍达标' }}</p>
              </div>
            </ScreenPanel>

            <ScreenPanel title="产线 OEE" class="lb-oee">
              <template #extra>
                <StatusTag tone="amber" label="班内推算 · 待 #570" />
              </template>
              <div class="lb-oee-in">
                <div class="lb-oee-big" :class="{ warn: board.oee.overall < 75, bad: board.oee.overall < 55 }">
                  <Gauge :size="21" :stroke-width="1.6" class="lb-ic-chip" />
                  <span class="lb-num">{{ board.oee.overall }}<small>%</small></span>
                </div>
                <dl class="lb-oee-rates">
                  <div>
                    <dt>可用率</dt>
                    <dd :class="{ warn: board.oee.availability < 90 }">{{ board.oee.availability }}%</dd>
                  </div>
                  <div>
                    <dt>性能率</dt>
                    <dd :class="{ warn: board.oee.performance < 90 }">{{ board.oee.performance }}%</dd>
                  </div>
                  <div>
                    <dt>良品率</dt>
                    <dd>{{ board.oee.quality }}%</dd>
                  </div>
                </dl>
              </div>
            </ScreenPanel>
          </div>

          <TrendChart
            class="lb-trend"
            title="小时产量 · 近 12h"
            :actual="board.hourly"
            :plan="planSeries"
            :hover-labels="board.hourLabels"
            :x-labels="trendX"
            :y-labels="trendY"
            :tooltip="trendPin"
            actual-label="实际产量"
            plan-label="节拍产能"
            :tabs="false"
          />

          <div class="lb-row3">
            <ScreenPanel v-if="board.wo" title="当前工单" class="lb-wo">
            <template #extra>
              <StatusTag v-if="board.wo.kitting === 'short'" tone="amber" label="线边缺料" />
              <span v-else class="lb-kitting">线边齐套</span>
            </template>
            <div class="lb-wo-in">
              <div class="lb-wo-info">
                <div class="lb-wo-head">
                  <span class="lb-wo-code">{{ board.wo.code }}</span>
                  <b class="lb-wo-product">{{ board.wo.product }}</b>
                </div>
                <div class="lb-wo-steps">
                  <span
                    v-for="s in board.wo.steps"
                    :key="s.name"
                    class="lb-step"
                    :class="s.state"
                  >
                    <i />{{ s.name }}
                  </span>
                </div>
                <div class="lb-wo-nums">
                  <span>完工 <b>{{ nf.format(board.wo.qtyDone) }}</b> / {{ nf.format(board.wo.qtyPlan) }}</span>
                  <span>在制 WIP <b>{{ board.wo.wip }}</b></span>
                </div>
              </div>
              <div class="lb-due">
                <dt>距交付</dt>
                <dd :class="{ warn: board.wo.dueInMin < 120 }">{{ fmtMin(board.wo.dueInMin) }}</dd>
              </div>
            </div>
            </ScreenPanel>

            <!-- OEE 24 小时热力（waffle 4×6）：一格一小时，分档着色，悬停读值 -->
            <ScreenPanel title="OEE · 24h 热力" class="lb-waffle">
              <template #extra>
                <span class="lb-wf-legend">
                  <i class="g4" />≥85 <i class="g3" />70+ <i class="g2" />55+ <i class="g1" />&lt;55
                </span>
              </template>
              <div class="lb-wf-grid" @mouseleave="wTip = null">
                <i
                  v-for="(v, i) in board.hourlyOee"
                  :key="i"
                  class="lb-wf-cell"
                  :class="oeeBucket(v)"
                  @mouseenter="wTipSet(i, v, $event)"
                />
                <span
                  v-if="wTip"
                  class="lb-wf-tip"
                  :style="{ left: `${wTip.x}px`, top: `${wTip.y - 8}px` }"
                >
                  {{ waffleHour(wTip.i) }} · OEE {{ wTip.v }}%
                </span>
              </div>
              <div class="lb-wf-axis"><span>24h 前</span><span>现在</span></div>
            </ScreenPanel>
          </div>
        </div>
      </div>

      <footer class="lb-foot">
        <RouterLink to="/line" class="lb-back">‹ 返回产线总览</RouterLink>
        <span>产量 / 节拍 / 合格率为演示推算 · 待 #570</span>
      </footer>
    </div>

    <div v-else class="lb-empty">
      <p>该产线不在当前账号权限范围内，或不存在</p>
      <RouterLink to="/line" class="lb-back">‹ 返回产线总览</RouterLink>
    </div>
  </ScreenLayout>
</template>

<style scoped>
.lb {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.lb-empty {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 14px;
  align-items: center;
  justify-content: center;
  color: var(--sb-muted);
  font-size: 16px;
}
.lb-back {
  color: var(--sb-cyan);
  text-decoration: none;
  font-size: 13.5px;
}

/* —— 红色横幅（远距醒目） —— */
.lb-banner {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 13px 20px;
  border-radius: var(--sb-radius);
  font-size: 19px;
  border: 1px solid rgba(239, 90, 99, 0.5);
  background: rgba(239, 90, 99, 0.13);
  animation: lb-banner 1.6s ease-in-out infinite;
}
.lb-banner.downtime {
  border-color: rgba(242, 193, 78, 0.45);
  background: rgba(242, 193, 78, 0.1);
  animation: none;
}
@keyframes lb-banner {
  50% {
    background: rgba(239, 90, 99, 0.22);
  }
}
.lb-banner-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: var(--sb-red);
  box-shadow: 0 0 12px var(--sb-red);
  flex: none;
}
.lb-banner.downtime .lb-banner-dot {
  background: var(--sb-amber);
  box-shadow: 0 0 12px var(--sb-amber);
}
.lb-banner b {
  color: var(--sb-red);
  font-weight: 800;
  letter-spacing: 0.08em;
  flex: none;
}
.lb-banner.downtime b {
  color: var(--sb-amber);
}
.lb-banner-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.lb-banner-since {
  flex: none;
  font-size: 14px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}

/* —— 主体 —— */
.lb-main {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 440px 1fr;
  gap: 16px;
}
.lb-left {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 18px;
  padding: 26px 22px 16px;
  border-radius: var(--sb-radius);
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid var(--sb-line);
  border-top-color: rgba(255, 255, 255, 0.09);
  min-height: 0;
}
.lb-devs {
  width: 100%;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-gutter: stable;
}
/* 设备参数折叠：grid/max-height 高度过渡（展开无跳变、无滚动条闪烁） */
.lb-dev-detail {
  overflow: hidden;
}
.lb-dev-detail-in {
  padding: 4px 2px 10px 17px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.dev-enter-active,
.dev-leave-active {
  transition:
    max-height 0.26s var(--sb-ease),
    opacity 0.2s var(--sb-ease);
}
.dev-enter-from,
.dev-leave-to {
  max-height: 0;
  opacity: 0;
}
.dev-enter-to,
.dev-leave-from {
  max-height: 200px;
  opacity: 1;
}
@media (prefers-reduced-motion: reduce) {
  .dev-enter-active,
  .dev-leave-active {
    transition: none;
  }
}
.lb-devs-t {
  margin: 0 0 6px;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.06em;
  color: var(--sb-muted);
}
.lb-devs-t small {
  font-weight: 400;
  color: var(--sb-faint);
  margin-left: 6px;
}
.lb-dev {
  appearance: none;
  width: 100%;
  font: inherit;
  background: transparent;
  border: 0;
  color: inherit;
  cursor: pointer;
  text-align: left;
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 7px 2px;
  font-size: 13.5px;
  border-radius: 6px;
  transition: background-color 0.15s var(--sb-ease);
}
.lb-dev:hover {
  background: rgba(135, 208, 255, 0.06);
}
.lb-dev:focus-visible {
  outline: none;
  box-shadow: inset 0 0 0 2px var(--sb-cyan-dim);
}
.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--sb-faint);
  flex: none;
}
.dot.run {
  background: var(--sb-green);
  box-shadow: 0 0 7px var(--sb-green);
}
.dot.idle {
  background: var(--sb-amber);
  box-shadow: 0 0 7px var(--sb-amber);
}
.dot.alarm {
  background: var(--sb-red);
  box-shadow: 0 0 7px var(--sb-red);
}
.dot.down {
  background: var(--sb-muted);
}
.lb-dev-name {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.lb-dev-param {
  flex: none;
  max-width: 150px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.lb-dev-state {
  flex: none;
  font-size: 12.5px;
  color: var(--sb-muted);
}
.lb-dev-state.alarm {
  color: var(--sb-red);
}
.lb-dev-state.down,
.lb-dev-state.idle {
  color: var(--sb-amber);
}
.lb-dev-state.offline {
  color: var(--sb-faint);
}

/* —— 右列 —— */
.lb-right {
  display: grid;
  grid-template-rows: auto 1fr auto;
  gap: 14px;
  min-height: 0;
  min-width: 0;
}
.lb-row1 {
  display: grid;
  grid-template-columns: 1.5fr 0.75fr 0.95fr;
  gap: 14px;
}
.lb-out-in {
  display: flex;
  align-items: center;
  gap: 26px;
}
.lb-out-hero {
  flex: 1;
  min-width: 0;
}
.lb-out-v {
  font-size: 58px;
  font-weight: 800;
  line-height: 1;
  color: #fff;
  text-shadow: var(--sb-value-glow);
  font-variant-numeric: tabular-nums;
}
.lb-out-v small {
  font-size: 21px;
  font-weight: 600;
  color: var(--sb-muted);
  margin-left: 8px;
}
.lb-out-sub {
  display: flex;
  gap: 22px;
  margin-top: 13px;
  font-size: 14.5px;
  color: var(--sb-muted);
}
.lb-out-sub b {
  font-weight: 700;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.lb-out-sub b.ok {
  color: var(--sb-green);
}
.lb-out-sub b.bad {
  color: var(--sb-red);
}
.lb-out-sub b.warn {
  color: var(--sb-amber);
}
.lb-shift {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}

.lb-takt {
  display: flex;
  flex-direction: column;
}
.lb-takt-in {
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: center;
}
.lb-takt-v {
  display: inline-flex;
  align-items: center;
  gap: 11px;
  font-size: 42px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-green);
  font-variant-numeric: tabular-nums;
}
.lb-takt-v.late {
  color: var(--sb-red);
  text-shadow: 0 0 20px rgba(239, 90, 99, 0.45);
}
.lb-takt-sub {
  margin: 10px 0 0;
  font-size: 14px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.lb-takt-sub b {
  color: var(--sb-text);
}
.lb-takt-sub b.late {
  color: var(--sb-red);
}
.lb-takt-hint {
  margin: 6px 0 0;
  font-size: 12.5px;
  color: var(--sb-faint);
}

.lb-trend {
  min-height: 0;
}
.lb-row3 {
  display: grid;
  grid-template-columns: 1.7fr 1fr;
  gap: 14px;
  min-width: 0;
}

/* 当班四格 */
.lb-stats {
  width: 100%;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 9px;
  margin: 0;
}
.lb-stats > div {
  border: 1px solid var(--sb-line);
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.02);
  padding: 9px 12px;
}
.lb-stats dt {
  font-size: 12px;
  color: var(--sb-muted);
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.lb-stat-ic {
  color: var(--sb-faint);
  flex: none;
}
.lb-stats dd {
  margin: 4px 0 0;
  font-size: 22px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.lb-stats dd small {
  font-size: 12px;
  font-weight: 500;
  color: var(--sb-muted);
}
.lb-stats dd.warn {
  color: var(--sb-amber);
}
.lb-stats dd.bad {
  color: var(--sb-red);
}
.lb-stat-txt {
  letter-spacing: 0.04em;
}

/* 产线 OEE 卡（内容垂直居中） */
.lb-oee {
  display: flex;
  flex-direction: column;
}
.lb-oee-in {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 18px;
}
.lb-oee-big {
  display: inline-flex;
  align-items: center;
  gap: 11px;
  font-size: 44px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-green);
  font-variant-numeric: tabular-nums;
  flex: none;
}

/* 图标：发丝级去饱和线性符号（无填充块、无边框、静态不发光 —— 遵循 screen
   哲学「辉光只给活数据」）。数字才是主角，图标克制陪衬。 */
.lb-ic-chip {
  flex: none;
  display: inline-flex;
  color: var(--sb-faint);
}
/* 大数字 + 单位下标（% 沉右下，与「秒」同款底对齐） */
.lb-num {
  display: inline-flex;
  align-items: flex-end;
  line-height: 1;
}
.lb-num small {
  font-size: 0.42em;
  font-weight: 600;
  margin-left: 2px;
  padding-bottom: 0.14em;
}
.lb-oee-big.warn {
  color: var(--sb-amber);
}
.lb-oee-big.bad {
  color: var(--sb-red);
}
.lb-oee-rates {
  flex: 1;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.lb-oee-rates div {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  font-size: 12.5px;
}
.lb-oee-rates dt {
  color: var(--sb-muted);
}
.lb-oee-rates dd {
  margin: 0;
  font-weight: 700;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.lb-oee-rates dd.warn {
  color: var(--sb-amber);
}

/* 设备折叠面板 */
.lb-dev-wrap + .lb-dev-wrap {
  border-top: 1px solid var(--sb-divider);
}
.lb-chev {
  flex: none;
  color: var(--sb-faint);
  transition: transform 0.2s var(--sb-ease);
}
.lb-chev.open {
  transform: rotate(180deg);
}
.lb-dp {
  display: flex;
  align-items: center;
  gap: 10px;
}
.lb-dp-l {
  width: 62px;
  flex: none;
  font-size: 12px;
  color: var(--sb-muted);
}
.lb-dp-spark {
  flex: 1;
  height: 20px;
  min-width: 0;
}
.lb-dp b {
  flex: none;
  min-width: 72px;
  text-align: right;
  font-size: 12.5px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

/* 安灯（并入左列线体域） */
.lb-andon-mini {
  width: 100%;
  flex: none;
  border-top: 1px solid var(--sb-divider);
  padding-top: 9px;
}
.lb-andon-h {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--sb-muted);
  letter-spacing: 0.05em;
  margin-bottom: 4px;
}
.lb-andon-row {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 7px 2px;
  font-size: 13px;
}
.lb-andon-time {
  flex: none;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.lb-andon-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.lb-andon-state {
  flex: none;
  color: var(--sb-green);
}
.lb-andon-state.open {
  color: var(--sb-red);
}
.lb-andon-empty {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 7px 2px;
  color: var(--sb-muted);
  font-size: 13px;
}
.lb-andon-ok {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--sb-green);
  box-shadow: 0 0 8px var(--sb-green);
}

/* OEE 24h 热力（waffle 4×6） */
.lb-waffle {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.lb-wf-legend {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 11.5px;
  color: var(--sb-muted);
}
.lb-wf-legend i {
  width: 10px;
  height: 10px;
  border-radius: 3px;
  margin-left: 6px;
}
.lb-wf-grid {
  position: relative;
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  grid-template-rows: repeat(4, 1fr);
  gap: 6px;
}
.lb-wf-cell {
  border-radius: 4px;
  cursor: default;
  transition: filter 0.15s var(--sb-ease);
}
.lb-wf-cell:hover {
  filter: brightness(1.35);
}
.lb-wf-cell.g4,
.lb-wf-legend .g4 {
  background: rgba(69, 208, 137, 0.85);
}
.lb-wf-cell.g3,
.lb-wf-legend .g3 {
  background: rgba(69, 208, 137, 0.38);
}
.lb-wf-cell.g2,
.lb-wf-legend .g2 {
  background: rgba(242, 193, 78, 0.55);
}
.lb-wf-cell.g1,
.lb-wf-legend .g1 {
  background: rgba(239, 90, 99, 0.6);
}
.lb-wf-tip {
  position: absolute;
  transform: translate(-50%, -100%);
  padding: 4px 9px;
  border-radius: 5px;
  background: rgba(10, 16, 30, 0.97);
  border: 1px solid rgba(148, 190, 255, 0.25);
  font-size: 11.5px;
  color: var(--sb-text);
  white-space: nowrap;
  pointer-events: none;
  font-variant-numeric: tabular-nums;
  z-index: 2;
}
.lb-wf-axis {
  display: flex;
  justify-content: space-between;
  margin-top: 7px;
  font-size: 11px;
  color: var(--sb-faint);
}

.lb-wo-in {
  display: flex;
  align-items: center;
  gap: 26px;
}
.lb-wo-info {
  flex: 1;
  min-width: 0;
}
.lb-wo-head {
  display: flex;
  align-items: baseline;
  gap: 12px;
  min-width: 0;
}
.lb-wo-code {
  font-family: ui-monospace, monospace;
  font-size: 15px;
  color: var(--sb-cyan);
  flex: none;
}
.lb-wo-product {
  font-size: 19px;
  font-weight: 700;
  color: #fff;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* 工序状态机：done 青实点 / doing 亮点光晕 / todo 空点 */
.lb-wo-steps {
  display: flex;
  align-items: center;
  gap: 22px;
  margin: 13px 0 0;
  flex-wrap: wrap;
}
.lb-step {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 13.5px;
  color: var(--sb-muted);
  position: relative;
}
.lb-step i {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.14);
}
.lb-step.done {
  color: var(--sb-text-2);
}
.lb-step.done i {
  background: var(--sb-cyan-dim);
}
.lb-step.doing {
  color: var(--sb-cyan);
  font-weight: 600;
}
.lb-step.doing i {
  background: var(--sb-cyan);
  box-shadow: 0 0 8px var(--sb-cyan-dim);
}
.lb-wo-nums {
  display: flex;
  gap: 24px;
  margin-top: 12px;
  font-size: 14px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.lb-wo-nums b {
  color: var(--sb-text);
  font-weight: 700;
}
.lb-kitting {
  font-size: 12.5px;
  color: var(--sb-green);
}
.lb-due {
  flex: none;
  text-align: right;
}
.lb-due dt {
  font-size: 13px;
  color: var(--sb-muted);
}
.lb-due dd {
  margin: 6px 0 0;
  font-size: 44px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.lb-due dd.warn {
  color: var(--sb-amber);
}

.lb-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  font-size: 12.5px;
  color: var(--sb-faint);
  border-top: 1px solid var(--sb-divider);
  padding-top: 11px;
}

@media (prefers-reduced-motion: reduce) {
  .lb-banner {
    animation: none;
  }
}
</style>
