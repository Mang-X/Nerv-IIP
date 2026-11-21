<script setup lang="ts">
import {
  NvRingGauge,
  NvScreenFreshness,
  NvScreenPanel,
  NvScreenScrollArea,
  NvSparkline,
  NvScreenStatusTag,
  NvScreenTrendChart,
  useScreenData,
} from '@nerv-iip/ui'
import { ChevronDown, CircleCheck, OctagonAlert, UserRound, Users } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { useBackLink } from '@/composables/useBackLink'
import { paramColor } from '@/components/equipment/paramColors'
import LineAndonHero from '@/components/line/LineAndonHero.vue'
import type { LineBoard } from '@/data/contracts/line'
import { fetchLineBoard } from '@/data/fetchers/line'
import { OEE_PLACEHOLDER_BADGE } from '@/data/copy'
import { formatScreenFreshness } from '@/data/freshness'
import { IS_REAL_DATA } from '@/data/config'
import ScreenLayout from '@/layouts/ScreenLayout.vue'

// 单线监控大屏（spec §四）：现场远距可读；横幅只在报警/停机时出现；
// 安灯呼叫-响应闭环 待 MAN-322（诚实标注）。4s 轮询。
const route = useRoute('/line/[id]')
const scope = useAccessScope()
const lineId = computed(() => String(route.params.id ?? ''))

const {
  data: board,
  isStale,
  lastUpdated,
  refresh,
} = useScreenData<LineBoard | null>(
  () => fetchLineBoard(lineId.value, scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 4000 },
)
const freshness = computed(() => formatScreenFreshness(isStale.value, lastUpdated.value))
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

// 返回链按来路识别（统一 composable）：车间下钻来回原车间、门厅来回门厅
const backLink = useBackLink(() => ({ to: '/line', label: '返回产线监控' }))

// —— 趋势图（实际 vs 节拍产能）：今日 12h / 近 30 天 tab 切换；
//    y 轴刻度随数据生成、x 轴抽稀、悬停按点取值 ——
const trendRange = ref<string | number>('12h')
const TREND_RANGES = [
  { label: '今日 12h', value: '12h' },
  { label: '近 30 天', value: '30d' },
]
const trendData = computed(() => {
  const b = board.value
  if (!b) return null
  if (trendRange.value === '30d') {
    return {
      actual: b.daily30.output,
      plan: b.daily30.plan,
      hoverLabels: b.daily30.labels,
      xLabels: b.daily30.labels.filter((_, i) => i % 5 === 0),
      planLabel: '日计划',
    }
  }
  return {
    actual: b.hourly,
    plan: Array.from({ length: 12 }, () => b.planPerHour),
    hoverLabels: b.hourLabels,
    xLabels: b.hourLabels.filter((_, i) => i % 2 === 0),
    planLabel: '节拍产能',
  }
})
const trendY = computed(() => {
  const t = trendData.value
  if (!t) return []
  const peak = Math.max(1, ...t.actual, ...t.plan)
  const mag = 10 ** Math.floor(Math.log10(peak))
  const top = Math.ceil(peak / mag) * mag
  return Array.from({ length: 6 }, (_, i) => nf.format(Math.round((top * (5 - i)) / 5)))
})
const trendPin = computed(() => {
  const t = trendData.value
  if (!t) return undefined
  const last = t.actual.length - 1
  return {
    x: last,
    label: t.hoverLabels[last] ?? '',
    actual: nf.format(t.actual[last] ?? 0),
    plan: nf.format(t.plan[last] ?? 0),
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
      <p class="lb-andon-h">
        本页产线聚合为演示数据 ·
        <RouterLink to="/andon" class="lb-back">进入真实安灯队列</RouterLink>
      </p>
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

          <!-- 当班四格：一次合格率 / 停机 / 班组长 / 在岗（设定集 §5：班组是车间级，无线长岗位） -->
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
              <dt><UserRound :size="13" class="lb-stat-ic" />班组长</dt>
              <dd class="lb-stat-txt">{{ board.crew.leader }}</dd>
            </div>
            <div>
              <dt><Users :size="13" class="lb-stat-ic" />在岗</dt>
              <dd>{{ board.crew.operators }}<small> 人</small></dd>
            </div>
          </dl>

          <NvScreenScrollArea class="lb-devs">
            <h5 class="lb-devs-t">
              线上设备 · {{ board.devices.length }} 台 <small>点击展开参数</small>
            </h5>
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
                      <span class="lb-dp-spark"
                        ><NvSparkline :data="p.spark" :color="paramColor(p.kind, p.tone)"
                      /></span>
                      <b :style="{ color: paramColor(p.kind, p.tone) }">
                        {{ p.value === null ? '—' : `${p.value}${p.unit}` }}
                      </b>
                    </div>
                  </div>
                </div>
              </Transition>
            </div>
          </NvScreenScrollArea>

          <div v-if="IS_REAL_DATA" class="lb-andon-mini">
            <RouterLink to="/andon" class="lb-back">查看真实安灯待响应 / 处理中队列 ›</RouterLink>
            <p class="lb-andon-h">请在安灯看板选择已授权的真实作业范围</p>
          </div>
          <div v-else class="lb-andon-mini">
            <div class="lb-andon-h">
              <span>安灯呼叫</span>
              <NvScreenStatusTag tone="amber" label="演示数据" />
            </div>
            <div v-for="a in board.andon" :key="a.time + a.station" class="lb-andon-row">
              <span class="lb-andon-time">{{ a.time }}</span>
              <span class="lb-andon-txt">{{ a.station }} · {{ a.type }} · {{ a.response }}</span>
              <b class="lb-andon-state" :class="{ open: a.state === '响应中' }">{{ a.state }}</b>
            </div>
            <div v-if="!board.andon.length" class="lb-andon-empty">
              <i class="lb-andon-ok" />当班无安灯呼叫
            </div>
          </div>
        </section>

        <!-- 右：产量/节拍 · 小时趋势 · 当前工单 -->
        <div class="lb-right">
          <div class="lb-row1">
            <NvScreenPanel title="当班产量" class="lb-output">
              <template #extra>
                <span class="lb-shift"
                  >{{ board.shift.name }} {{ board.shift.range }} · 剩余
                  {{ fmtMin(board.shift.remainingMin) }}</span
                >
              </template>
              <div class="lb-out-in">
                <div class="lb-out-hero">
                  <div class="lb-out-v">
                    {{ nf.format(board.output.good)
                    }}<small>/ {{ nf.format(board.output.plan) }} 件</small>
                  </div>
                  <div class="lb-out-sub">
                    <span>良品 <b class="ok">{{ nf.format(board.output.good) }}</b></span>
                    <span
                      >报废
                      <b :class="{ bad: board.output.scrap > 0 }">{{ board.output.scrap }}</b></span
                    >
                    <span
                      >返修
                      <b :class="{ warn: board.output.rework > 0 }">{{
                        board.output.rework
                      }}</b></span
                    >
                  </div>
                </div>
                <NvRingGauge
                  :value="board.output.achievement"
                  label="当班达成率"
                  :size="132"
                  :value-size="34"
                />
              </div>
            </NvScreenPanel>

            <NvScreenPanel title="节拍达成" class="lb-takt">
              <div class="lb-takt-in">
                <div class="lb-takt-v" :class="{ late: board.takt.deviationPct > 0 }">
                  <span class="lb-num"
                    >{{ board.takt.deviationPct > 0 ? '+' : '' }}{{ board.takt.deviationPct
                    }}<small>%</small></span
                  >
                  <i class="lb-score-line" aria-hidden="true" />
                </div>
                <p class="lb-takt-sub">
                  标准 {{ board.takt.standardSec }}s · 实际
                  <b :class="{ late: board.takt.deviationPct > 0 }">{{ board.takt.actualSec }}s</b>
                </p>
                <p class="lb-takt-hint">
                  {{ board.takt.deviationPct > 0 ? '节拍落后，关注瓶颈工位' : '节拍达标' }}
                </p>
              </div>
            </NvScreenPanel>

            <NvScreenPanel title="产线 OEE" class="lb-oee">
              <template #extra>
                <NvScreenStatusTag tone="amber" :label="OEE_PLACEHOLDER_BADGE" />
              </template>
              <div class="lb-oee-in">
                <div
                  class="lb-oee-big"
                  :class="{ warn: board.oee.overall < 75, bad: board.oee.overall < 55 }"
                >
                  <span class="lb-num">{{ board.oee.overall }}<small>%</small></span>
                  <i class="lb-score-line" aria-hidden="true" />
                </div>
                <dl class="lb-oee-rates">
                  <div>
                    <dt>可用率</dt>
                    <dd :class="{ warn: board.oee.availability < 90 }">
                      {{ board.oee.availability }}%
                    </dd>
                  </div>
                  <div>
                    <dt>性能率</dt>
                    <dd :class="{ warn: board.oee.performance < 90 }">
                      {{ board.oee.performance }}%
                    </dd>
                  </div>
                  <div>
                    <dt>良品率</dt>
                    <dd>{{ board.oee.quality }}%</dd>
                  </div>
                </dl>
              </div>
            </NvScreenPanel>
          </div>

          <NvScreenTrendChart
            v-if="trendData"
            v-model:range="trendRange"
            class="lb-trend"
            title="产量趋势"
            :actual="trendData.actual"
            :plan="trendData.plan"
            :hover-labels="trendData.hoverLabels"
            :x-labels="trendData.xLabels"
            :y-labels="trendY"
            :tooltip="trendPin"
            actual-label="实际产量"
            :plan-label="trendData.planLabel"
            :ranges="TREND_RANGES"
          />

          <div class="lb-row3">
            <NvScreenPanel v-if="board.wo" title="当前工单" class="lb-wo">
              <template #extra>
                <NvScreenStatusTag
                  v-if="board.wo.kitting === 'short'"
                  tone="amber"
                  label="线边缺料"
                />
                <span v-else class="lb-kitting">线边齐套</span>
              </template>
              <div class="lb-wo-in">
                <div class="lb-wo-info">
                  <div class="lb-wo-head">
                    <span class="lb-wo-code">{{ board.wo.code }}</span>
                    <b class="lb-wo-product">{{ board.wo.product }}</b>
                  </div>
                  <!-- 工序流分布（流水线：各工序同时在产）——工位累计完成沿流向递减，
                     段间差值即在制；停摆红 / 瓶颈黄 -->
                  <div class="lb-wo-flow">
                    <template v-for="(s, i) in board.wo.stations" :key="s.name">
                      <div class="lb-station" :class="s.state">
                        <span class="lb-station-name">
                          {{ s.name }}
                          <em v-if="s.state === 'blocked'">停摆</em>
                          <em v-else-if="s.state === 'bottleneck'">瓶颈</em>
                        </span>
                        <b class="lb-station-done">{{ nf.format(s.done) }}</b>
                      </div>
                      <span
                        v-if="i < board.wo.stations.length - 1"
                        class="lb-flow-arrow"
                        aria-hidden="true"
                        >›</span
                      >
                    </template>
                  </div>
                  <div class="lb-wo-nums">
                    <span
                      >完工 <b>{{ nf.format(board.wo.qtyDone) }}</b> /
                      {{ nf.format(board.wo.qtyPlan) }}</span
                    >
                    <span>工序间在制 <b>{{ nf.format(board.wo.wip) }}</b></span>
                  </div>
                </div>
                <div class="lb-due">
                  <dt>距交付</dt>
                  <dd :class="{ warn: board.wo.dueInMin < 120 }">
                    {{ fmtMin(board.wo.dueInMin) }}
                  </dd>
                </div>
              </div>
            </NvScreenPanel>

            <!-- OEE 24 小时热力（waffle 4×6）：一格一小时，分档着色，悬停读值 -->
            <NvScreenPanel title="OEE · 24h 热力" class="lb-waffle">
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
            </NvScreenPanel>
          </div>
        </div>
      </div>

      <footer class="lb-foot">
        <RouterLink :to="backLink.to" class="lb-back">‹ {{ backLink.label }}</RouterLink>
        <span>产量、节拍与合格率按当前数据窗口推算</span>
        <NvScreenFreshness :tone="freshness.tone" :label="freshness.text" />
      </footer>
    </div>

    <div v-else class="lb-empty">
      <p>该产线不在当前账号权限范围内，或不存在</p>
      <RouterLink :to="backLink.to" class="lb-back">‹ {{ backLink.label }}</RouterLink>
    </div>
  </ScreenLayout>
</template>

<style scoped src="../../styles/line-layout-and-equipment.css"></style>
<style scoped src="../../styles/line-performance.css"></style>
<style scoped src="../../styles/line-stop-and-operation-flow.css"></style>
