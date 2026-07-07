<script setup lang="ts">
import { ScreenPanel, ScreenScrollArea, Sparkline, TrendChart } from '@nerv-iip/ui'
import { ClipboardList, FileCheck2, FileWarning, Scale } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import DefectPareto from '@/components/quality/DefectPareto.vue'
import { useBackLink } from '@/composables/useBackLink'
import { NCR_SLA_HOURS, type QualityBoard } from '@/data/contracts/quality'
import { fetchQualityBoard } from '@/data/fetchers/quality'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useScreenData } from '@/screen-kit'

// 质量看板（spec §六）：质量健康度 + 待办闭环 —— 一眼看清不良率是否越红线、
// 该催哪张 NCR、缺陷集中在哪条线。与产线屏同一个故事：电芯线卷绕机报警 ⇔
// 帕累托 TOP1/2 电芯缺陷、最老超期 NCR 挂 WO-1951。5s 轮询。
const scope = useAccessScope()
const backLink = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
const { data: board, refresh } = useScreenData<QualityBoard>(
  () => fetchQualityBoard(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 5000 },
)
watch(
  () => [scope.currentFactoryId, scope.personaId],
  async () => {
    await refresh()
    // 撞上在途轮询（inFlight 跳过）时补一拍，避免短暂显示旧工厂数据
    if (board.value && board.value.factoryId !== scope.currentFactoryId) await refresh()
  },
)

const factoryName = computed(
  () => scope.factories.find((f) => f.id === scope.currentFactoryId)?.name ?? '全部车间',
)
const nf = new Intl.NumberFormat('en-US')

/** 龄期 → 远距可读：62 → 2d 14h */
function fmtAge(h: number): string {
  return h >= 24 ? `${Math.floor(h / 24)}d ${h % 24}h` : `${h}h`
}

const overRedLine = computed(
  () => !!board.value && board.value.kpis.defectRatePct > board.value.kpis.redLinePct,
)

/** NCR 状态机分布（看板底部收口行） */
const statusCount = computed(() => {
  const c = { review: 0, disposing: 0, verify: 0 }
  for (const r of board.value?.ncrs ?? []) c[r.status]++
  return c
})

/** 批合格率最低层（语义降黄）与积压最重层（组内强调） */
const worstLayerKey = computed(() => {
  const ls = board.value?.layers ?? []
  return ls.length ? ls.reduce((a, b) => (b.passRate < a.passRate ? b : a)).key : ''
})
const hotBacklogKey = computed(() => {
  const ls = board.value?.layers ?? []
  return ls.length ? ls.reduce((a, b) => (b.backlog > a.backlog ? b : a)).key : ''
})

// —— 不良率趋势（今日 12h / 近 30 天）：红色虚线为红线阈值（经 --sb-indigo 局部
//    重映射复用 TrendChart 的 plan 线，组件零改动）——
const trendRange = ref<string | number>('12h')
const TREND_RANGES = [
  { label: '今日 12h', value: '12h' },
  { label: '近 30 天', value: '30d' },
]
// 分层曲线用字面量色 —— --sb-indigo 在本页被局部重映射成红线色，变量会被污染
const LAYER_COLORS: Record<string, string> = { iqc: '#5fbf7a', ipqc: '#f0ad4e', fqc: '#8b9be6' }
const trendData = computed(() => {
  const b = board.value
  if (!b) return null
  const red = b.kpis.redLinePct
  if (trendRange.value === '30d') {
    return {
      actual: b.trend30.ratePct,
      plan: b.trend30.ratePct.map(() => red),
      // 悬停带当日判定批次 —— 周日检验量低谷在这里读得到
      hoverLabels: b.trend30.labels.map((l, i) => `${l} · 判 ${nf.format(b.trend30.lots[i])} 批`),
      xLabels: b.trend30.labels.filter((_, i) => i % 5 === 0),
      // 分层对比：全厂一条总曲线掩盖结构 —— 30 天视图叠加来料/过程/成品三层
      series: b.layers.map((l) => ({
        label: l.code,
        color: LAYER_COLORS[l.key] ?? '#8b9be6',
        data: l.trend30,
      })),
    }
  }
  return {
    actual: b.trend12h.ratePct,
    plan: b.trend12h.ratePct.map(() => red),
    hoverLabels: b.trend12h.labels,
    xLabels: b.trend12h.labels.filter((_, i) => i % 2 === 0),
    // 12h 无分层时序读面（诚实缺口，不造假分层小时数据）
    series: undefined,
  }
})
// y 轴刻度：与 TrendChart 内部量程算法同式（数据向上取整，含分层序列），标签才不说谎
const trendY = computed(() => {
  const t = trendData.value
  if (!t) return []
  const peak = Math.max(1, ...t.actual, ...t.plan, ...(t.series ?? []).flatMap((s) => s.data))
  const mag = 10 ** Math.floor(Math.log10(peak))
  const top = Math.ceil(peak / mag) * mag
  return Array.from({ length: 6 }, (_, i) => ((top * (5 - i)) / 5).toFixed(1))
})
const trendPin = computed(() => {
  const t = trendData.value
  if (!t) return undefined
  const last = t.actual.length - 1
  return {
    x: last,
    label: t.hoverLabels[last] ?? '',
    actual: (t.actual[last] ?? 0).toFixed(2),
    plan: (t.plan[last] ?? 0).toFixed(2),
  }
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 质量看板大屏" :line="factoryName" screen="指挥中心大屏 06">
    <div v-if="board" class="qb">
      <!-- 顶部 KPI 带：双主角（批合格率 / 不良率-红线）+ 四格待办 -->
      <ScreenPanel class="qb-band">
        <div class="qb-band-in">
          <div class="qb-hero">
            <dt class="qb-hero-t">当日批次合格率</dt>
            <div class="qb-hero-v ok">
              <span class="qb-num">{{ board.kpis.batchPassRate }}<small>%</small></span>
              <i class="qb-score-line" aria-hidden="true" />
            </div>
            <p class="qb-hero-sub">
              合格 <b>{{ board.kpis.batchPassed }}</b> / 判定 {{ board.kpis.batchTotal }} 批
            </p>
          </div>

          <div class="qb-hero">
            <dt class="qb-hero-t">整体不良率（件）</dt>
            <div class="qb-hero-v" :class="overRedLine ? 'bad' : 'ok'">
              <span class="qb-num">{{ board.kpis.defectRatePct.toFixed(2) }}<small>%</small></span>
              <i class="qb-score-line" aria-hidden="true" />
            </div>
            <p class="qb-hero-sub">
              红线 {{ board.kpis.redLinePct.toFixed(2) }}%
              <b v-if="overRedLine" class="qb-over">
                越线 +{{ (board.kpis.defectRatePct - board.kpis.redLinePct).toFixed(2) }}pp
              </b>
            </p>
          </div>

          <div class="qb-cells">
            <div class="qb-cell">
              <dt><FileWarning :size="17" class="qb-cell-ic" />待处置 NCR</dt>
              <dd :class="{ warn: board.kpis.openNcr > 0 }">{{ board.kpis.openNcr }}</dd>
              <p class="qb-cell-sub" :class="{ bad: board.kpis.overdueNcr > 0 }">
                超期 {{ board.kpis.overdueNcr }}
              </p>
            </div>
            <div class="qb-cell">
              <dt><ClipboardList :size="17" class="qb-cell-ic" />检验积压</dt>
              <dd>{{ board.kpis.inspectionBacklog }}</dd>
              <p class="qb-cell-sub" :class="{ warn: board.kpis.backlogOldestHours > 24 }">
                最老 {{ fmtAge(board.kpis.backlogOldestHours) }}
              </p>
            </div>
            <div class="qb-cell">
              <dt><FileCheck2 :size="17" class="qb-cell-ic" />条件放行在途</dt>
              <dd>{{ board.kpis.conditionalRelease }}</dd>
              <p class="qb-cell-sub">含让步接收</p>
            </div>
            <div class="qb-cell">
              <dt><Scale :size="17" class="qb-cell-ic" />MRB 待评审</dt>
              <dd>{{ board.kpis.mrbPending }}</dd>
              <p class="qb-cell-sub">评审即出处置</p>
            </div>
          </div>
        </div>
      </ScreenPanel>

      <div class="qb-main">
        <!-- 左：NCR 待处置看板（龄期降序，超期红呼吸置顶） -->
        <ScreenPanel title="NCR 待处置" class="qb-ncr">
          <template #extra>
            <span class="qb-cap">处置 SLA {{ NCR_SLA_HOURS }}h</span>
          </template>
          <ScreenScrollArea class="qb-ncr-list">
            <div v-for="r in board.ncrs" :key="r.code" class="qn-row" :class="{ overdue: r.overdue }">
              <div class="qn-top">
                <i v-if="r.overdue" class="qn-alert" aria-hidden="true" />
                <span class="qn-code">{{ r.code }}</span>
                <b class="qn-defect" :title="r.product ? `${r.product} · ${r.sourceDoc}` : r.sourceDoc">
                  {{ r.defect }}
                </b>
                <span class="qn-qty">×{{ nf.format(r.qty) }}</span>
              </div>
              <div class="qn-sub">
                <span class="qn-src">{{ r.sourceType === 'supplier' ? '来料 · ' : '' }}{{ r.source }}</span>
                <span class="qn-doc">{{ r.sourceDoc }}</span>
                <span class="qn-flex" />
                <b class="qn-age" :class="{ bad: r.overdue }">
                  <em v-if="r.overdue">超期</em>{{ fmtAge(r.ageHours) }}
                </b>
                <span class="qn-status" :class="r.status">
                  {{ r.statusLabel }}<template v-if="r.disposition"> · {{ r.disposition }}</template>
                </span>
              </div>
            </div>
          </ScreenScrollArea>
          <div class="qn-summary">
            <span>待评审 <b class="amber">{{ statusCount.review }}</b></span>
            <span>处置中 <b class="cyan">{{ statusCount.disposing }}</b></span>
            <span>待验证 <b class="green">{{ statusCount.verify }}</b></span>
            <span class="qn-flex" />
            <span :class="{ 'qn-sla-bad': board.kpis.overdueNcr > 0 }">
              超期 &gt;{{ NCR_SLA_HOURS }}h · {{ board.kpis.overdueNcr }} 条
            </span>
          </div>
        </ScreenPanel>

        <!-- 中：不良率趋势（红线阈值）+ 三层合格率 -->
        <div class="qb-mid">
          <TrendChart
            v-if="trendData"
            v-model:range="trendRange"
            class="qb-trend"
            title="不良率趋势"
            :actual="trendData.actual"
            :plan="trendData.plan"
            :hover-labels="trendData.hoverLabels"
            :x-labels="trendData.xLabels"
            :y-labels="trendY"
            :tooltip="trendPin"
            :series="trendData.series"
            actual-label="全厂不良率"
            plan-label="红线"
            :ranges="TREND_RANGES"
          />

          <ScreenPanel title="三层合格率" class="qb-tri">
            <template #extra>
              <span class="qb-cap">当日批次口径</span>
            </template>
            <div class="qb-tri-in">
              <div v-for="l in board.layers" :key="l.key" class="qt-cell">
                <dt class="qt-t">{{ l.label }} <small>{{ l.code }}</small></dt>
                <div class="qt-v" :class="worstLayerKey === l.key ? 'warn' : 'ok'">
                  <span class="qb-num">{{ l.passRate }}<small>%</small></span>
                  <i class="qb-score-line" aria-hidden="true" />
                </div>
                <p class="qt-sub">合格 <b>{{ l.lotsPassed }}</b> / {{ l.lotsDone }} 批</p>
                <p class="qt-sub2">
                  件不良 {{ l.pieceDefectPct.toFixed(2) }}%
                  <b v-if="l.failedTop && l.lotsDone - l.lotsPassed > 0" class="qt-fail">
                    {{ l.failedTop.name }} {{ l.failedTop.count }} 批未过
                  </b>
                </p>
                <!-- 分层 30 天件不良率：全厂一条总曲线掩盖分层差异，
                     过程检的事故酝酿抬升在这里一眼可见 -->
                <div class="qt-spark">
                  <Sparkline
                    :data="l.trend30"
                    area
                    :color="worstLayerKey === l.key ? 'var(--sb-amber)' : 'var(--sb-cyan)'"
                  />
                </div>
                <p class="qt-spark-cap">近 30 天件不良率</p>
              </div>
            </div>
          </ScreenPanel>
        </div>

        <!-- 右：缺陷帕累托 TOP5 + 检验任务积压 -->
        <div class="qb-right">
          <ScreenPanel title="缺陷帕累托 TOP5" class="qb-pareto">
            <template #extra>
              <span class="qb-cap">近 7 天 · {{ nf.format(board.paretoTotal) }} 件</span>
            </template>
            <DefectPareto :items="board.pareto" :total="board.paretoTotal" />
          </ScreenPanel>

          <ScreenPanel title="检验任务积压" class="qb-backlog">
            <template #extra>
              <span class="qb-cap">按来源分层</span>
            </template>
            <div class="qb-ib">
              <div v-for="l in board.layers" :key="l.key" class="ib-group">
                <div class="ib-top">
                  <span class="ib-label">{{ l.label }} <small>{{ l.code }}</small></span>
                  <b class="ib-n" :class="{ warn: hotBacklogKey === l.key && l.backlog > 0 }">
                    {{ l.backlog }}<small> 项</small>
                  </b>
                </div>
                <div class="ib-meta">
                  <span>最老 <b :class="{ warn: l.oldestHours > 24 }">{{ fmtAge(l.oldestHours) }}</b></span>
                  <span v-if="l.backlogTop" class="ib-topsrc">{{ l.backlogTop.name }} {{ l.backlogTop.count }} 项</span>
                  <span class="qn-flex" />
                  <span class="ib-today">今日 {{ l.lotsDone }} / {{ l.lotsDue }}</span>
                </div>
                <div class="ib-bar">
                  <i :style="{ width: `${Math.min(100, (l.lotsDone / Math.max(1, l.lotsDue)) * 100)}%` }" />
                </div>
              </div>
            </div>
          </ScreenPanel>
        </div>
      </div>

      <footer class="qb-foot">
        <span class="qb-foot-l">
          <RouterLink :to="backLink.to" class="qb-back">‹ {{ backLink.label }}</RouterLink>
          <span>合格率 / 不良率 / 帕累托为演示推算 · NCR 与检验明细就绪</span>
        </span>
        <span>缺陷码 Quality ↔ MES 口径映射 · MRB/CAPA · 聚合端点 待 #570</span>
      </footer>
    </div>

    <div v-else class="qb-loading">连接数据…</div>
  </ScreenLayout>
</template>

<style scoped>
.qb {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.qb-loading {
  height: 100%;
  display: grid;
  place-content: center;
  color: var(--sb-muted);
  font-size: 15px;
}
.qb-cap {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}

/* 大数字 + 单位下标 + 语义色渐隐强调线（与产线屏同源语言） */
.qb-num {
  display: inline-flex;
  align-items: flex-end;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
.qb-num small {
  font-size: 0.42em;
  font-weight: 600;
  margin-left: 2px;
  padding-bottom: 0.14em;
}
.qb-score-line {
  width: 46px;
  height: 2px;
  margin-top: 10px;
  border-radius: 1px;
  background: linear-gradient(90deg, currentColor, transparent);
  opacity: 0.75;
}

/* —— 顶部 KPI 带 —— */
.qb-band-in {
  display: flex;
  align-items: center;
  gap: 34px;
}
.qb-hero {
  flex: none;
  min-width: 218px;
  position: relative;
}
.qb-hero + .qb-hero::before,
.qb-cells::before {
  content: '';
  position: absolute;
  left: -17px;
  top: 6px;
  bottom: 6px;
  width: 1px;
  background: var(--sb-divider);
}
.qb-hero-t {
  font-size: 14.5px;
  color: var(--sb-muted);
  letter-spacing: 0.04em;
}
.qb-hero-v {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  margin-top: 9px;
  font-size: 50px;
  font-weight: 800;
  color: var(--sb-text);
  text-shadow: var(--sb-value-glow);
}
.qb-hero-v.ok {
  color: var(--sb-text);
}
.qb-hero-v.ok .qb-score-line {
  color: var(--sb-green);
}
.qb-hero-v.bad {
  color: var(--sb-red);
  text-shadow: 0 0 20px rgba(239, 90, 99, 0.4);
}
.qb-hero-sub {
  margin: 9px 0 0;
  font-size: 13px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.qb-hero-sub b {
  color: var(--sb-text-2);
  font-weight: 700;
}
.qb-over {
  margin-left: 8px;
  color: var(--sb-red);
  font-weight: 700;
}
.qb-cells {
  flex: 1;
  position: relative;
  display: flex;
  align-items: center;
  min-width: 0;
}
.qb-cell {
  flex: 1;
  min-width: 0;
  padding: 4px 20px;
  position: relative;
}
.qb-cell + .qb-cell::before {
  content: '';
  position: absolute;
  left: 0;
  top: 8px;
  bottom: 8px;
  width: 1px;
  background: var(--sb-divider);
}
/* 大屏远视距：KPI 标签 14px 起 */
.qb-cell dt {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 14px;
  color: var(--sb-muted);
  white-space: nowrap;
}
.qb-cell-ic {
  color: var(--sb-faint);
  flex: none;
}
.qb-cell dd {
  margin: 8px 0 0;
  font-size: 31px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.qb-cell dd.warn {
  color: var(--sb-text);
}
.qb-cell-sub {
  margin: 7px 0 0;
  font-size: 12px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.qb-cell-sub.bad {
  color: var(--sb-red);
  font-weight: 600;
}
.qb-cell-sub.warn {
  color: var(--sb-amber);
}

/* —— 主体三列 —— */
.qb-main {
  flex: 1;
  min-height: 0;
  min-width: 0;
  display: grid;
  grid-template-columns: 1.42fr 1.18fr 1fr;
  gap: 16px;
}
.qb-mid,
.qb-right {
  display: grid;
  gap: 14px;
  min-height: 0;
  min-width: 0;
}
.qb-mid {
  grid-template-rows: 1fr auto;
}
.qb-right {
  grid-template-rows: auto 1fr;
}
.qb-trend {
  min-height: 0;
}
/* 红线阈值线：把 TrendChart 的 plan（indigo）在本面板内重映射为红（组件零改动） */
.qb-trend {
  --sb-indigo: rgba(239, 90, 99, 0.9);
}

/* —— NCR 看板 —— */
.qb-ncr {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.qb-ncr-list {
  flex: 1;
  min-height: 0;
}
.qn-row {
  padding: 7px 2px 8px;
}
.qn-row + .qn-row {
  border-top: 1px solid var(--sb-divider);
}
.qn-row.overdue {
  background: linear-gradient(90deg, rgba(239, 90, 99, 0.055), transparent 70%);
  border-radius: 6px;
}
.qn-top {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  line-height: 1.4;
}
/* 超期红呼吸 —— 全屏唯一的呼吸元素，只给活异常 */
.qn-alert {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--sb-red);
  box-shadow: 0 0 8px var(--sb-red);
  animation: qn-breathe 1.8s ease-in-out infinite;
}
@keyframes qn-breathe {
  50% {
    opacity: 0.35;
  }
}
.qn-code {
  flex: none;
  font-family: ui-monospace, monospace;
  font-size: 13px;
  color: var(--sb-cyan);
}
.qn-defect {
  flex: 1;
  min-width: 0;
  font-size: 14.5px;
  font-weight: 600;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.qn-qty {
  flex: none;
  font-size: 13px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.qn-sub {
  display: flex;
  align-items: baseline;
  gap: 10px;
  margin-top: 3px;
  padding-left: 2px;
  font-size: 12.5px;
  min-width: 0;
  line-height: 1.35;
}
.qn-src {
  flex: none;
  color: var(--sb-text-2);
}
.qn-doc {
  flex: none;
  font-family: ui-monospace, monospace;
  font-size: 12px;
  color: var(--sb-faint);
}
.qn-flex {
  flex: 1;
  min-width: 0;
}
.qn-age {
  flex: none;
  font-weight: 600;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.qn-age.bad {
  color: var(--sb-red);
}
.qn-age em {
  font-style: normal;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.05em;
  margin-right: 5px;
}
.qn-status {
  flex: none;
  min-width: 96px;
  text-align: right;
  font-size: 12.5px;
  white-space: nowrap;
}
.qn-status.review {
  color: var(--sb-amber);
}
.qn-status.disposing {
  color: var(--sb-cyan);
}
.qn-status.verify {
  color: var(--sb-green);
}
/* 状态机收口行：待评审 → 处置中 → 待验证 分布 + 超期合计 */
.qn-summary {
  flex: none;
  display: flex;
  align-items: center;
  gap: 18px;
  margin-top: 9px;
  padding-top: 10px;
  border-top: 1px solid var(--sb-divider);
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.qn-summary b {
  font-weight: 700;
  margin-left: 2px;
}
.qn-summary b.amber {
  color: var(--sb-amber);
}
.qn-summary b.cyan {
  color: var(--sb-cyan);
}
.qn-summary b.green {
  color: var(--sb-green);
}
.qn-sla-bad {
  color: var(--sb-red);
}

/* —— 三层合格率 —— */
.qb-tri {
  min-height: 0;
}
.qb-tri-in {
  display: flex;
  align-items: stretch;
}
.qt-cell {
  flex: 1;
  min-width: 0;
  padding: 2px 18px 0;
  position: relative;
}
.qt-cell:first-child {
  padding-left: 2px;
}
.qt-cell + .qt-cell::before {
  content: '';
  position: absolute;
  left: 0;
  top: 6px;
  bottom: 6px;
  width: 1px;
  background: var(--sb-divider);
}
.qt-t {
  font-size: 13px;
  color: var(--sb-muted);
  white-space: nowrap;
}
.qt-t small {
  font-size: 11px;
  color: var(--sb-faint);
  letter-spacing: 0.06em;
  margin-left: 3px;
}
.qt-v {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  margin-top: 9px;
  font-size: 34px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-text);
}
.qt-v.ok .qb-score-line {
  color: var(--sb-green);
}
.qt-v.warn {
  color: var(--sb-amber);
}
.qt-sub {
  margin: 9px 0 0;
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.qt-sub b {
  color: var(--sb-text-2);
  font-weight: 700;
}
.qt-sub2 {
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
/* 分层 30 天件不良率迷你趋势 */
.qt-spark {
  height: 30px;
  margin-top: 9px;
}
.qt-spark-cap {
  margin: 4px 0 0;
  font-size: 11px;
  color: var(--sb-faint);
}
/* 未过批次来源：独立一行，避免与件不良率挤压截断 */
.qt-fail {
  display: block;
  margin-top: 3px;
  font-weight: 600;
  color: var(--sb-amber);
}

/* —— 检验任务积压 —— */
.qb-pareto {
  min-height: 0;
}
.qb-backlog {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.qb-ib {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-evenly;
  gap: 10px;
}
.ib-group {
  padding: 2px 0;
}
.ib-top {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
}
.ib-label {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--sb-text-2);
}
.ib-label small {
  font-size: 11px;
  font-weight: 500;
  color: var(--sb-faint);
  letter-spacing: 0.06em;
  margin-left: 4px;
}
.ib-n {
  font-size: 24px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.ib-n small {
  font-size: 12px;
  font-weight: 500;
  color: var(--sb-muted);
}
.ib-n.warn {
  color: var(--sb-amber);
}
.ib-meta {
  display: flex;
  align-items: baseline;
  gap: 12px;
  margin-top: 6px;
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  min-width: 0;
}
.ib-meta b {
  font-weight: 600;
  color: var(--sb-text-2);
}
.ib-meta b.warn {
  color: var(--sb-amber);
}
.ib-topsrc {
  color: var(--sb-faint);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.ib-today {
  flex: none;
  color: var(--sb-muted);
}
/* 今日进度：发丝轨道 + 青色渐隐填充（静态不发光） */
.ib-bar {
  margin-top: 8px;
  height: 4px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.045);
  box-shadow: inset 0 0 0 1px var(--sb-line);
  overflow: hidden;
}
.ib-bar i {
  display: block;
  height: 100%;
  border-radius: 999px;
  background: linear-gradient(90deg, rgba(74, 166, 238, 0.66), rgba(74, 166, 238, 0.16));
  transition: width 0.6s var(--sb-ease-emphasized);
}

/* —— 页脚（诚实标注） —— */
.qb-foot-l {
  display: inline-flex;
  align-items: center;
  gap: 18px;
  min-width: 0;
}
.qb-back {
  color: var(--sb-cyan);
  text-decoration: none;
  font-size: 13.5px;
  flex: none;
}
.qb-foot {
  flex: none;
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
  .qn-alert {
    animation: none;
  }
  .ib-bar i {
    transition: none;
  }
}
</style>
