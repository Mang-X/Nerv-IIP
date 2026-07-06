<script setup lang="ts">
import { RingGauge, ScreenPanel, StatusTag, TrendChart } from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
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
              <dt>一次合格率</dt>
              <dd :class="{ warn: board.fpy < 98 }">{{ board.fpy }}<small>%</small></dd>
            </div>
            <div>
              <dt>当班停机</dt>
              <dd :class="{ bad: board.downtime.count > 0 }">
                {{ board.downtime.count }}<small> 次 · {{ board.downtime.totalMin }} min</small>
              </dd>
            </div>
            <div>
              <dt>线长</dt>
              <dd class="lb-stat-txt">{{ board.crew.leader }}</dd>
            </div>
            <div>
              <dt>在岗</dt>
              <dd>{{ board.crew.operators }}<small> 人</small></dd>
            </div>
          </dl>

          <div class="lb-devs">
            <h5 class="lb-devs-t">线上设备 · {{ board.devices.length }} 台</h5>
            <div v-for="d in board.devices" :key="d.id" class="lb-dev" :class="d.state">
              <i class="dot" :class="d.state" />
              <span class="lb-dev-name">{{ d.name }}</span>
              <span v-if="d.param" class="lb-dev-param">{{ d.param }}</span>
              <span class="lb-dev-state" :class="d.state">{{ d.stateLabel }}</span>
            </div>
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
              <div class="lb-takt-v" :class="{ late: board.takt.deviationPct > 0 }">
                {{ board.takt.deviationPct > 0 ? '+' : '' }}{{ board.takt.deviationPct }}<small>%</small>
              </div>
              <p class="lb-takt-sub">
                标准 {{ board.takt.standardSec }}s · 实际
                <b :class="{ late: board.takt.deviationPct > 0 }">{{ board.takt.actualSec }}s</b>
              </p>
              <p class="lb-takt-hint">{{ board.takt.deviationPct > 0 ? '节拍落后，关注瓶颈工位' : '节拍达标' }}</p>
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

            <!-- 安灯呼叫-响应区：一期只读展示，闭环 待 MAN-322（显式标注） -->
            <ScreenPanel title="安灯呼叫" class="lb-andon">
              <template #extra>
                <StatusTag tone="amber" label="响应闭环 · 待 MAN-322" />
              </template>
              <div v-if="board.andon.length" class="lb-andon-list">
                <div v-for="a in board.andon" :key="a.time + a.station" class="lb-andon-row">
                  <span class="lb-andon-time">{{ a.time }}</span>
                  <span class="lb-andon-txt">{{ a.station }} · {{ a.type }}</span>
                  <span class="lb-andon-resp">{{ a.response }}</span>
                  <b class="lb-andon-state" :class="{ open: a.state === '响应中' }">{{ a.state }}</b>
                </div>
              </div>
              <div v-else class="lb-andon-empty">
                <i class="lb-andon-ok" />当班无安灯呼叫
              </div>
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
}
.lb-devs-t {
  margin: 0 0 6px;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.06em;
  color: var(--sb-muted);
}
.lb-dev {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 7px 2px;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 13.5px;
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
  grid-template-columns: 1.7fr 1fr;
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

.lb-takt-v {
  font-size: 52px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-green);
  font-variant-numeric: tabular-nums;
}
.lb-takt-v small {
  font-size: 20px;
  font-weight: 600;
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

/* 安灯区 */
.lb-andon {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.lb-andon-list {
  flex: 1;
  min-height: 0;
}
.lb-andon-row {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 9px 2px;
  border-bottom: 1px solid var(--sb-divider);
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
.lb-andon-resp {
  flex: none;
  color: var(--sb-muted);
}
.lb-andon-state {
  flex: none;
  color: var(--sb-green);
}
.lb-andon-state.open {
  color: var(--sb-red);
}
.lb-andon-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 9px;
  color: var(--sb-muted);
  font-size: 13.5px;
}
.lb-andon-ok {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--sb-green);
  box-shadow: 0 0 8px var(--sb-green);
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
