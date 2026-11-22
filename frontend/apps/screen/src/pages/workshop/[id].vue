<script setup lang="ts">
import {
  NvScreenFreshness,
  NvScreenPanel,
  NvScreenScrollArea,
  NvScreenSegmented,
  NvScreenStatusLight,
  NvScreenStatusTag,
  NvScreenTrendChart,
  useScreenData,
} from '@nerv-iip/ui'
import {
  Boxes,
  CircleCheck,
  ClipboardList,
  OctagonAlert,
  PackageCheck,
  Recycle,
  UserRound,
} from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import WorkshopLineCard from '@/components/workshop/WorkshopLineCard.vue'
import { useBackLink } from '@/composables/useBackLink'
import type { WorkshopBoard } from '@/data/contracts/workshop'
import { fetchWorkshopBoard } from '@/data/fetchers/workshop'
import { OEE_PLACEHOLDER_BADGE } from '@/data/copy'
import { formatScreenFreshness } from '@/data/freshness'
import ScreenLayout from '@/layouts/ScreenLayout.vue'

// 车间总览大屏（MAN-315，spec §三）：车间主任「当班作战室」——
// 顶带定调（达成/产量/质量/停机/齐套/交接），左墙看线，中栏看趋势与求救，
// 右栏料/质/人。产线区与产线屏 buildLineCards 精确同源。5s 轮询。
const route = useRoute('/workshop/[id]')
const router = useRouter()
const scope = useAccessScope()
const workshopId = computed(() => String(route.params.id ?? ''))

const {
  data: board,
  isStale,
  lastUpdated,
  refresh,
} = useScreenData<WorkshopBoard | null>(
  () => fetchWorkshopBoard(workshopId.value, scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 5000 },
)
const freshness = computed(() => formatScreenFreshness(isStale.value, lastUpdated.value))
watch(
  () => [workshopId.value, scope.currentFactoryId, scope.personaId],
  async () => {
    await refresh()
    // 撞上在途轮询（inFlight 跳过）时补一拍，避免短暂显示旧车间数据
    if (board.value && board.value.workshopId !== workshopId.value) await refresh()
  },
)

// —— 车间切换器（persona 可见车间；大屏轮播/多车间巡检场景）——
const wsOptions = computed(() =>
  scope.visibleWorkshops.map((w) => ({ label: w.name, value: w.id })),
)
const wsModel = computed<string | number>({
  get: () => workshopId.value,
  set: (id) => {
    if (id && String(id) !== workshopId.value) void router.replace(`/workshop/${id}`)
  },
})

const stateTone = computed(() =>
  board.value?.state === 'alarm'
    ? ('alarm' as const)
    : board.value?.state === 'attention'
      ? ('idle' as const)
      : ('run' as const),
)

// 返回链按来路识别：门厅进回门厅、工厂总览下钻回工厂总览；直接输 URL 走 fallback
const backLink = useBackLink(() =>
  scope.canSeeScreen('factory')
    ? { to: '/factory', label: '返回工厂总览' }
    : { to: '/', label: '返回大屏门厅' },
)

/** 分钟 → 「Xh Ym」远距格式 */
function fmtMin(min: number): string {
  const h = Math.floor(min / 60)
  const m = min % 60
  return h > 0 ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m`
}
const nf = new Intl.NumberFormat('en-US')

// 达成率语义色：≥93 正常（绿）、85–92 关注（黄）、<85 异常（红）
const achTone = computed(() => {
  const a = board.value?.output.achievement ?? 100
  return a < 85 ? 'bad' : a < 93 ? 'warn' : ''
})

// —— 产量趋势：当班累计 / 近 30 天双时间维度（与产线屏趋势同一交互语言）；
//    y 轴刻度随数据生成、x 轴抽稀、当班模式常驻 pin 在最新点 ——
const trendRange = ref<string | number>('shift')
const WS_TREND_RANGES = [
  { label: '当班累计', value: 'shift' },
  { label: '近 30 天', value: '30d' },
]
// 分线曲线色：报警红 / 关注黄（语义直读），正常线冷色轮换
const LINE_TREND_COLORS = [
  'var(--nv-scr-cyan)',
  'var(--nv-scr-green)',
  'var(--nv-scr-indigo)',
  'var(--nv-scr-workshop-series-teal)',
  'var(--nv-scr-workshop-series-violet)',
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
      actualLabel: '日产量',
      planLabel: '日计划',
      series: undefined,
    }
  }
  const labels = b.shiftCurve.labels
  return {
    actual: b.shiftCurve.actual,
    plan: b.shiftCurve.plan,
    hoverLabels: labels,
    xLabels: labels.length > 9 ? labels.filter((_, i) => i % 2 === 0) : labels,
    actualLabel: '车间累计',
    planLabel: '计划',
    // 分线累计（总曲线 = Σ 各线，mock 构造性勾稽）：谁在贡献、谁在掉速分线可读
    series: b.shiftCurve.byLine.map((l, i) => ({
      label: l.name,
      color:
        l.state === 'alarm'
          ? 'var(--nv-scr-red)'
          : l.state === 'attention'
            ? 'var(--nv-scr-chart-attention)'
            : LINE_TREND_COLORS[i % LINE_TREND_COLORS.length],
      data: l.data,
    })),
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
  const c = board.value?.shiftCurve
  if (!c || trendRange.value !== 'shift') return undefined
  const last = c.actual.length - 1
  return {
    x: last,
    label: c.labels[last] ?? '',
    actual: nf.format(c.actual[last] ?? 0),
    plan: nf.format(c.plan[last] ?? 0),
  }
})

// 设备状态摘要（事件流面板右上；与设备屏计数同源）
const devSummary = computed(() => {
  const d = board.value?.devices
  if (!d) return ''
  const parts = [`设备 ${d.total}`, `运行 ${d.run}`]
  if (d.idle) parts.push(`待机 ${d.idle}`)
  if (d.down) parts.push(`停机 ${d.down}`)
  if (d.alarm) parts.push(`报警 ${d.alarm}`)
  if (d.offline) parts.push(`失联 ${d.offline}`)
  return parts.join(' · ')
})
</script>

<template>
  <ScreenLayout
    title="Nerv-IIP 车间总览大屏"
    :line="board?.workshopName ?? '车间'"
    screen="指挥中心大屏 04"
  >
    <div v-if="board" class="wb">
      <!-- 顶行：车间切换器（persona 可见范围） + 车间态 + 主任/班次 -->
      <div class="wb-top">
        <NvScreenSegmented v-if="wsOptions.length > 1" v-model="wsModel" :options="wsOptions" />
        <div class="wb-top-right">
          <NvScreenStatusLight :tone="stateTone" :label="board.stateLabel" />
          <span class="wb-top-meta">
            主任 {{ board.managerName }} · {{ board.shift.name }} {{ board.shift.range }} · 剩余
            {{ fmtMin(board.shift.remainingMin) }}
          </span>
        </div>
      </div>

      <!-- 当班 KPI 带：达成率主数 + 产量/合格率/报废返修/停机/齐套/交接 -->
      <NvScreenPanel class="wb-band">
        <div class="wb-band-in">
          <div class="wb-hero">
            <div class="wb-hero-v" :class="achTone">
              <span class="wb-num">{{ board.output.achievement }}<small>%</small></span>
              <i class="wb-score-line" aria-hidden="true" />
            </div>
            <div class="wb-hero-l">当班达成率</div>
          </div>
          <dl class="wb-cells">
            <div class="wb-cell">
              <dt><PackageCheck :size="17" class="wb-cell-ic" />当班产量（件）</dt>
              <dd>
                {{ nf.format(board.output.actual)
                }}<small>/ {{ nf.format(board.output.plan) }}</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><CircleCheck :size="17" class="wb-cell-ic" />一次合格率</dt>
              <dd :class="{ warn: board.quality.fpy < 98 }">
                {{ board.quality.fpy }}<small>%</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><Recycle :size="17" class="wb-cell-ic" />报废 / 返修</dt>
              <dd>{{ board.quality.scrap }}<small>/ {{ board.quality.rework }} 件</small></dd>
            </div>
            <div class="wb-cell">
              <dt><OctagonAlert :size="17" class="wb-cell-ic" />当班停机</dt>
              <dd :class="{ bad: board.downtime.count > 0 }">
                {{ board.downtime.count }}<small> 次 · {{ board.downtime.totalMin }} min</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><Boxes :size="17" class="wb-cell-ic" />物料齐套</dt>
              <dd :class="{ warn: board.kitting.rate < 100 }">
                {{ board.kitting.rate }}<small>%</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><ClipboardList :size="17" class="wb-cell-ic" />交接遗留</dt>
              <dd :class="{ warn: board.crew.handoverIssues > 0 }">
                {{ board.crew.handoverIssues }}<small> 项</small>
              </dd>
            </div>
          </dl>
        </div>
      </NvScreenPanel>

      <div class="wb-main">
        <!-- 左：产线状态墙（与产线屏同源，点卡下钻）+ 工单交付预警 -->
        <section class="wb-lines">
          <div class="sec-h">
            <i class="sec-glyph" aria-hidden="true" />
            <span class="sec-t">产线状态</span>
            <span class="sec-rule" aria-hidden="true" />
            <span class="sec-meta">
              {{ board.lines.length }} 条
              <template v-if="board.lineStates.alarm">
                · <b class="bad">{{ board.lineStates.alarm }} 报警</b></template
              >
              <template v-if="board.lineStates.attention">
                · <b class="warn">{{ board.lineStates.attention }} 关注</b></template
              >
              · 点击进入单线屏
            </span>
          </div>
          <NvScreenScrollArea class="wb-lines-list">
            <div class="wb-lines-in">
              <WorkshopLineCard v-for="l in board.lines" :key="l.id" :card="l" />
            </div>
          </NvScreenScrollArea>
          <NvScreenPanel title="当班班组" class="wb-crew">
            <template #extra>
              <NvScreenStatusTag tone="amber" label="花名册口径 · 考勤未接入" />
            </template>
            <div class="wb-crew-head">
              <span class="wb-crew-team">{{ board.crew.teamName }}</span>
              <span class="wb-crew-lead"
                ><UserRound :size="17" class="wb-cell-ic" />组长 {{ board.crew.leader }}</span
              >
            </div>
            <dl class="wb-crew-nums">
              <div>
                <dt>计划应到</dt>
                <dd>{{ board.crew.headcountPlanned }}<small> 人</small></dd>
              </div>
              <div>
                <dt>技能覆盖</dt>
                <dd>{{ board.crew.skillCoverage }}<small>%</small></dd>
              </div>
              <div>
                <dt>交接遗留</dt>
                <dd :class="{ warn: board.crew.handoverIssues > 0 }">
                  {{ board.crew.handoverIssues }}<small> 项</small>
                </dd>
              </div>
            </dl>
            <p v-if="board.crew.handoverNote" class="wb-crew-note">{{ board.crew.handoverNote }}</p>
          </NvScreenPanel>
          <div class="wb-woa" :class="{ 'is-empty': !board.woAlerts.length }">
            <h5 class="wb-sub-h">工单交付预警</h5>
            <div v-for="w in board.woAlerts" :key="w.code" class="wb-woa-row">
              <span class="wb-woa-code">{{ w.code }}</span>
              <span class="wb-woa-txt">{{ w.product }} · {{ w.lineName }}</span>
              <b class="wb-woa-due" :class="w.kind === 'overdue' ? 'bad' : 'warn'">{{
                w.dueText
              }}</b>
            </div>
            <div v-if="!board.woAlerts.length" class="wb-empty-row">
              <i class="wb-ok-dot" />无临期 / 超期工单
            </div>
          </div>
        </section>

        <!-- 中：产量趋势（当班累计 / 近 30 天，拿大头）+ 停机/报警事件流（固定高内滚） -->
        <div class="wb-center">
          <NvScreenTrendChart
            v-if="trendData"
            v-model:range="trendRange"
            class="wb-trend"
            title="产量趋势"
            :actual="trendData.actual"
            :plan="trendData.plan"
            :hover-labels="trendData.hoverLabels"
            :x-labels="trendData.xLabels"
            :y-labels="trendY"
            :tooltip="trendPin"
            :series="trendData.series"
            :actual-label="trendData.actualLabel"
            :plan-label="trendData.planLabel"
            :ranges="WS_TREND_RANGES"
          />
          <NvScreenPanel title="停机 · 报警" class="wb-events">
            <template #extra>
              <span class="wb-dev-sum">{{ devSummary }}</span>
            </template>
            <NvScreenScrollArea class="wb-ev-list">
              <div
                v-for="e in board.events"
                :key="e.id"
                class="wb-ev"
                :class="[e.level, { resolved: e.resolved }]"
              >
                <span class="wb-ev-time">{{ e.time }}</span>
                <i class="wb-ev-dot" aria-hidden="true" />
                <span class="wb-ev-line">{{ e.lineName }}</span>
                <span class="wb-ev-txt">{{ e.text }}</span>
                <b class="wb-ev-st">{{ e.status }}</b>
              </div>
              <div v-if="!board.events.length" class="wb-empty-row wb-ev-empty">
                <i class="wb-ok-dot" />当班无停机 · 无设备报警
              </div>
            </NvScreenScrollArea>
          </NvScreenPanel>
        </div>

        <!-- 右：车间效率 OEE · 齐套/缺料 · 质量（指标域；班组随「人」归左列执行域） -->
        <div class="wb-right">
          <NvScreenPanel title="车间效率 OEE" class="wb-oee">
            <template #extra>
              <NvScreenStatusTag tone="amber" :label="OEE_PLACEHOLDER_BADGE" />
            </template>
            <div class="wb-oee-top">
              <div
                class="wb-oee-hero"
                :class="{ warn: board.oee.overall < 75, bad: board.oee.overall < 60 }"
              >
                <span class="wb-num">{{ board.oee.overall }}<small>%</small></span>
                <i class="wb-score-line" aria-hidden="true" />
              </div>
              <dl class="wb-oee-apq">
                <div>
                  <dt>可用率 A</dt>
                  <dd :class="{ warn: board.oee.availability < 90 }">
                    {{ board.oee.availability }}<small>%</small>
                  </dd>
                </div>
                <div>
                  <dt>性能率 P</dt>
                  <dd :class="{ warn: board.oee.performance < 90 }">
                    {{ board.oee.performance }}<small>%</small>
                  </dd>
                </div>
                <div>
                  <dt>良品率 Q</dt>
                  <dd>{{ board.oee.quality }}<small>%</small></dd>
                </div>
              </dl>
            </div>
            <div class="wb-oee-lines">
              <div v-for="l in board.oee.byLine" :key="l.lineId" class="wb-oee-line">
                <span class="wb-oee-name">{{ l.name }}</span>
                <span class="wb-oee-track">
                  <i :class="l.state" :style="{ width: `${l.oee}%` }" />
                </span>
                <b class="wb-oee-v" :class="l.state">{{ l.oee }}<small>%</small></b>
              </div>
            </div>
          </NvScreenPanel>
          <NvScreenPanel title="物料齐套" class="wb-kit">
            <div class="wb-kit-top">
              <div class="wb-kit-v" :class="{ warn: board.kitting.rate < 100 }">
                <span class="wb-num">{{ board.kitting.rate }}<small>%</small></span>
                <i class="wb-score-line" aria-hidden="true" />
              </div>
              <dl class="wb-kit-mini">
                <div>
                  <dt>在产工单</dt>
                  <dd>{{ board.kitting.woActive }}</dd>
                </div>
                <div>
                  <dt>阻塞</dt>
                  <dd :class="{ bad: board.kitting.woBlocked > 0 }">
                    {{ board.kitting.woBlocked }}
                  </dd>
                </div>
              </dl>
            </div>
            <h5 class="wb-sub-h">缺料 Top</h5>
            <NvScreenScrollArea class="wb-shorts">
              <div v-for="s in board.kitting.shortages" :key="s.code" class="wb-short">
                <div class="wb-short-l">
                  <b class="wb-short-mat">{{ s.material }}</b>
                  <span class="wb-short-meta">{{ s.code }} · {{ s.lineName }} · {{ s.wo }}</span>
                </div>
                <div class="wb-short-r">
                  <span class="wb-short-qty">
                    <b>-{{ nf.format(s.shortQty) }}</b
                    ><small>/ {{ nf.format(s.requiredQty) }}</small>
                  </span>
                  <span class="wb-short-eta">{{ s.eta }}</span>
                </div>
              </div>
              <div v-if="!board.kitting.shortages.length" class="wb-empty-row">
                <i class="wb-ok-dot" />线边物料齐套
              </div>
            </NvScreenScrollArea>
          </NvScreenPanel>

          <NvScreenPanel title="当班质量" class="wb-quality">
            <template #extra>
              <!-- FPY/报废/返修大数字在顶部 KPI 带已有 —— 此处只留摘要，面板专注 NCR -->
              <span class="wb-q-sum">
                FPY <b :class="{ warn: board.quality.fpy < 98 }">{{ board.quality.fpy }}%</b> · 报废
                <b :class="{ bad: board.quality.scrap > 0 }">{{ board.quality.scrap }}</b> · 返修
                <b>{{ board.quality.rework }}</b>
              </span>
            </template>
            <NvScreenScrollArea class="wb-ncr">
              <div v-for="n in board.quality.ncr" :key="n.code" class="wb-ncr-row">
                <span class="wb-ncr-code">{{ n.code }}</span>
                <span class="wb-ncr-txt">{{ n.lineName }} · {{ n.text }}</span>
                <b class="wb-ncr-st">{{ n.status }}</b>
              </div>
              <div v-if="!board.quality.ncr.length" class="wb-empty-row">
                <i class="wb-ok-dot" />无待办 NCR
              </div>
            </NvScreenScrollArea>
          </NvScreenPanel>
        </div>
      </div>

      <footer class="wb-foot">
        <RouterLink :to="backLink.to" class="wb-back">‹ {{ backLink.label }}</RouterLink>
        <span>产量、达成与齐套按当前窗口推算；在岗与人效按花名册口径展示</span>
        <NvScreenFreshness :tone="freshness.tone" :label="freshness.text" />
      </footer>
    </div>

    <div v-else class="wb-empty">
      <p>该车间不在当前账号权限范围内，或不存在</p>
      <RouterLink :to="backLink.to" class="wb-back">‹ {{ backLink.label }}</RouterLink>
    </div>
  </ScreenLayout>
</template>

<style scoped src="../../styles/workshop-layout-and-kpis.css"></style>
<style scoped src="../../styles/workshop-lines-and-events.css"></style>
<style scoped src="../../styles/workshop-quality-and-crew.css"></style>
