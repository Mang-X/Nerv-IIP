<script setup lang="ts">
import { ScreenPanel, ScreenSegmented, StatusLight, StatusTag, TrendChart } from '@nerv-iip/ui'
import {
  Boxes,
  CircleCheck,
  ClipboardList,
  OctagonAlert,
  PackageCheck,
  Recycle,
  UserRound,
} from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import WorkshopLineCard from '@/components/workshop/WorkshopLineCard.vue'
import type { WorkshopBoard } from '@/data/contracts/workshop'
import { fetchWorkshopBoard } from '@/data/fetchers/workshop'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useScreenData } from '@/screen-kit'

// 车间总览大屏（MAN-315，spec §三）：车间主任「当班作战室」——
// 顶带定调（达成/产量/质量/停机/齐套/交接），左墙看线，中栏看趋势与求救，
// 右栏料/质/人。产线区与产线屏 buildLineCards 精确同源。5s 轮询。
const route = useRoute('/workshop/[id]')
const router = useRouter()
const scope = useAccessScope()
const workshopId = computed(() => String(route.params.id ?? ''))

const { data: board, refresh } = useScreenData<WorkshopBoard | null>(
  () => fetchWorkshopBoard(workshopId.value, scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 5000 },
)
watch(
  () => [workshopId.value, scope.currentFactoryId, scope.personaId],
  async () => {
    await refresh()
    // 撞上在途轮询（inFlight 跳过）时补一拍，避免短暂显示旧车间数据
    if (board.value && board.value.workshopId !== workshopId.value) await refresh()
  },
)

// —— 车间切换器（persona 可见车间；大屏轮播/多车间巡检场景）——
const wsOptions = computed(() => scope.visibleWorkshops.map((w) => ({ label: w.name, value: w.id })))
const wsModel = computed<string | number>({
  get: () => workshopId.value,
  set: (id) => {
    if (id && String(id) !== workshopId.value) void router.replace(`/workshop/${id}`)
  },
})

const stateTone = computed(() =>
  board.value?.state === 'alarm' ? ('alarm' as const) : board.value?.state === 'attention' ? ('idle' as const) : ('run' as const),
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

// —— 当班累计曲线：y 轴刻度随数据生成、x 轴抽稀、常驻 pin 在最新点 ——
const trendY = computed(() => {
  const c = board.value?.shiftCurve
  if (!c) return []
  const peak = Math.max(1, ...c.actual, ...c.plan)
  const mag = 10 ** Math.floor(Math.log10(peak))
  const top = Math.ceil(peak / mag) * mag
  return Array.from({ length: 6 }, (_, i) => nf.format(Math.round((top * (5 - i)) / 5)))
})
const trendX = computed(() => {
  const labels = board.value?.shiftCurve.labels ?? []
  return labels.length > 9 ? labels.filter((_, i) => i % 2 === 0) : labels
})
const trendPin = computed(() => {
  const c = board.value?.shiftCurve
  if (!c) return undefined
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
        <ScreenSegmented v-if="wsOptions.length > 1" v-model="wsModel" :options="wsOptions" />
        <div class="wb-top-right">
          <StatusLight :tone="stateTone" :label="board.stateLabel" />
          <span class="wb-top-meta">
            主任 {{ board.managerName }} · {{ board.shift.name }} {{ board.shift.range }} ·
            剩余 {{ fmtMin(board.shift.remainingMin) }}
          </span>
        </div>
      </div>

      <!-- 当班 KPI 带：达成率主数 + 产量/合格率/报废返修/停机/齐套/交接 -->
      <ScreenPanel class="wb-band">
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
              <dt><PackageCheck :size="13" class="wb-cell-ic" />当班产量（件）</dt>
              <dd>
                {{ nf.format(board.output.actual) }}<small>/ {{ nf.format(board.output.plan) }}</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><CircleCheck :size="13" class="wb-cell-ic" />一次合格率</dt>
              <dd :class="{ warn: board.quality.fpy < 98 }">{{ board.quality.fpy }}<small>%</small></dd>
            </div>
            <div class="wb-cell">
              <dt><Recycle :size="13" class="wb-cell-ic" />报废 / 返修</dt>
              <dd>
                {{ board.quality.scrap }}<small>/ {{ board.quality.rework }} 件</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><OctagonAlert :size="13" class="wb-cell-ic" />当班停机</dt>
              <dd :class="{ bad: board.downtime.count > 0 }">
                {{ board.downtime.count }}<small> 次 · {{ board.downtime.totalMin }} min</small>
              </dd>
            </div>
            <div class="wb-cell">
              <dt><Boxes :size="13" class="wb-cell-ic" />物料齐套</dt>
              <dd :class="{ warn: board.kitting.rate < 100 }">{{ board.kitting.rate }}<small>%</small></dd>
            </div>
            <div class="wb-cell">
              <dt><ClipboardList :size="13" class="wb-cell-ic" />交接遗留</dt>
              <dd :class="{ warn: board.crew.handoverIssues > 0 }">
                {{ board.crew.handoverIssues }}<small> 项</small>
              </dd>
            </div>
          </dl>
        </div>
      </ScreenPanel>

      <div class="wb-main">
        <!-- 左：产线状态墙（与产线屏同源，点卡下钻）+ 工单交付预警 -->
        <section class="wb-lines">
          <div class="sec-h">
            <i class="sec-glyph" aria-hidden="true" />
            <span class="sec-t">产线状态</span>
            <span class="sec-rule" aria-hidden="true" />
            <span class="sec-meta">
              {{ board.lines.length }} 条
              <template v-if="board.lineStates.alarm"> · <b class="bad">{{ board.lineStates.alarm }} 报警</b></template>
              <template v-if="board.lineStates.attention"> · <b class="warn">{{ board.lineStates.attention }} 关注</b></template>
              · 点击进入单线屏
            </span>
          </div>
          <div class="wb-lines-list">
            <WorkshopLineCard v-for="l in board.lines" :key="l.id" :card="l" />
          </div>
          <div class="wb-woa">
            <h5 class="wb-sub-h">工单交付预警</h5>
            <div v-for="w in board.woAlerts" :key="w.code" class="wb-woa-row">
              <span class="wb-woa-code">{{ w.code }}</span>
              <span class="wb-woa-txt">{{ w.product }} · {{ w.lineName }}</span>
              <b class="wb-woa-due" :class="w.kind === 'overdue' ? 'bad' : 'warn'">{{ w.dueText }}</b>
            </div>
            <div v-if="!board.woAlerts.length" class="wb-empty-row">
              <i class="wb-ok-dot" />无临期 / 超期工单
            </div>
          </div>
        </section>

        <!-- 中：当班累计曲线 + 停机/报警事件流 -->
        <div class="wb-center">
          <TrendChart
            class="wb-trend"
            title="当班产量累计"
            :actual="board.shiftCurve.actual"
            :plan="board.shiftCurve.plan"
            :hover-labels="board.shiftCurve.labels"
            :x-labels="trendX"
            :y-labels="trendY"
            :tooltip="trendPin"
            :tabs="false"
            actual-label="实际累计"
            plan-label="计划累计"
          />
          <ScreenPanel title="停机 · 报警" class="wb-events">
            <template #extra>
              <span class="wb-dev-sum">{{ devSummary }}</span>
            </template>
            <div class="wb-ev-list sb-scroll">
              <div v-for="e in board.events" :key="e.id" class="wb-ev" :class="e.level">
                <span class="wb-ev-time">{{ e.time }}</span>
                <i class="wb-ev-dot" aria-hidden="true" />
                <span class="wb-ev-line">{{ e.lineName }}</span>
                <span class="wb-ev-txt">{{ e.text }}</span>
                <b class="wb-ev-st">{{ e.status }}</b>
              </div>
              <div v-if="!board.events.length" class="wb-empty-row wb-ev-empty">
                <i class="wb-ok-dot" />当班无停机 · 无设备报警
              </div>
            </div>
          </ScreenPanel>
        </div>

        <!-- 右：齐套/缺料 · 质量 · 班组（诚实口径） -->
        <div class="wb-right">
          <ScreenPanel title="物料齐套" class="wb-kit">
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
                  <dd :class="{ bad: board.kitting.woBlocked > 0 }">{{ board.kitting.woBlocked }}</dd>
                </div>
              </dl>
            </div>
            <h5 class="wb-sub-h">缺料 Top</h5>
            <div class="wb-shorts sb-scroll">
              <div v-for="s in board.kitting.shortages" :key="s.code" class="wb-short">
                <div class="wb-short-l">
                  <b class="wb-short-mat">{{ s.material }}</b>
                  <span class="wb-short-meta">{{ s.code }} · {{ s.lineName }} · {{ s.wo }}</span>
                </div>
                <div class="wb-short-r">
                  <span class="wb-short-qty">
                    <b>-{{ nf.format(s.shortQty) }}</b><small>/ {{ nf.format(s.requiredQty) }}</small>
                  </span>
                  <span class="wb-short-eta">{{ s.eta }}</span>
                </div>
              </div>
              <div v-if="!board.kitting.shortages.length" class="wb-empty-row">
                <i class="wb-ok-dot" />线边物料齐套
              </div>
            </div>
          </ScreenPanel>

          <ScreenPanel title="当班质量" class="wb-quality">
            <dl class="wb-q-nums">
              <div>
                <dt>一次合格率</dt>
                <dd :class="{ warn: board.quality.fpy < 98 }">{{ board.quality.fpy }}<small>%</small></dd>
              </div>
              <div>
                <dt>报废</dt>
                <dd :class="{ bad: board.quality.scrap > 0 }">{{ board.quality.scrap }}<small> 件</small></dd>
              </div>
              <div>
                <dt>返修</dt>
                <dd :class="{ warn: board.quality.rework > 0 }">{{ board.quality.rework }}<small> 件</small></dd>
              </div>
            </dl>
            <div class="wb-ncr">
              <div v-for="n in board.quality.ncr" :key="n.code" class="wb-ncr-row">
                <span class="wb-ncr-code">{{ n.code }}</span>
                <span class="wb-ncr-txt">{{ n.lineName }} · {{ n.text }}</span>
                <b class="wb-ncr-st">{{ n.status }}</b>
              </div>
              <div v-if="!board.quality.ncr.length" class="wb-empty-row">
                <i class="wb-ok-dot" />无待办 NCR
              </div>
            </div>
          </ScreenPanel>

          <ScreenPanel title="当班班组" class="wb-crew">
            <template #extra>
              <StatusTag tone="amber" label="花名册口径 · 考勤未接入" />
            </template>
            <div class="wb-crew-head">
              <span class="wb-crew-team">{{ board.crew.teamName }}</span>
              <span class="wb-crew-lead"><UserRound :size="13" class="wb-cell-ic" />组长 {{ board.crew.leader }}</span>
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
          </ScreenPanel>
        </div>
      </div>

      <footer class="wb-foot">
        <RouterLink :to="scope.canSeeScreen('factory') ? '/factory' : '/'" class="wb-back">
          ‹ {{ scope.canSeeScreen('factory') ? '返回工厂总览' : '返回大屏门厅' }}
        </RouterLink>
        <span>产量 / 达成 / 齐套为演示推算 · 待 #570；在岗 / 人效为数据缺口，仅展示花名册口径</span>
      </footer>
    </div>

    <div v-else class="wb-empty">
      <p>该车间不在当前账号权限范围内，或不存在</p>
      <RouterLink to="/" class="wb-back">‹ 返回大屏门厅</RouterLink>
    </div>
  </ScreenLayout>
</template>

<style scoped>
.wb {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 13px;
}
.wb-empty {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 14px;
  align-items: center;
  justify-content: center;
  color: var(--sb-muted);
  font-size: 16px;
}
.wb-back {
  color: var(--sb-cyan);
  text-decoration: none;
  font-size: 13.5px;
}

/* —— 顶行：切换器 + 车间态/班次 —— */
.wb-top {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  min-height: 34px;
}
.wb-top-right {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 18px;
}
.wb-top-meta {
  font-size: 13px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}

/* —— KPI 带 —— */
.wb-band {
  flex: none;
}
.wb-band-in {
  display: flex;
  align-items: center;
  gap: 28px;
}
.wb-hero {
  flex: none;
  padding: 2px 6px 0 2px;
}
.wb-hero-v {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  font-size: 54px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-green);
  font-variant-numeric: tabular-nums;
}
.wb-hero-v.warn {
  color: var(--sb-amber);
}
.wb-hero-v.bad {
  color: var(--sb-red);
}
.wb-hero-l {
  margin-top: 8px;
  font-size: 13px;
  color: var(--sb-muted);
}
/* 大数字 + 单位下标（与产线屏同款语言） */
.wb-num {
  display: inline-flex;
  align-items: flex-end;
  line-height: 1;
}
.wb-num small {
  font-size: 0.42em;
  font-weight: 600;
  margin-left: 2px;
  padding-bottom: 0.14em;
}
/* 数据强调线：数字下语义色渐隐短线（替代图标装饰） */
.wb-score-line {
  width: 46px;
  height: 2px;
  margin-top: 9px;
  border-radius: 1px;
  background: linear-gradient(90deg, currentColor, transparent);
  opacity: 0.75;
}
.wb-cells {
  flex: 1;
  min-width: 0;
  margin: 0;
  display: flex;
  align-items: center;
}
.wb-cell {
  flex: 1;
  min-width: 0;
  padding: 4px 20px;
  position: relative;
  white-space: nowrap;
}
.wb-cell + .wb-cell::before {
  content: '';
  position: absolute;
  left: 0;
  top: 7px;
  bottom: 7px;
  width: 1px;
  background: var(--sb-divider);
}
.wb-cell dt {
  font-size: 12.5px;
  color: var(--sb-muted);
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.wb-cell-ic {
  color: var(--sb-faint);
  flex: none;
}
.wb-cell dd {
  margin: 7px 0 0;
  font-size: 26px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.wb-cell dd small {
  font-size: 13px;
  font-weight: 500;
  color: var(--sb-muted);
  margin-left: 2px;
}
.wb-cell dd.warn {
  color: var(--sb-amber);
}
.wb-cell dd.bad {
  color: var(--sb-red);
}

/* —— 主体三列 —— */
.wb-main {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 458px minmax(0, 1fr) 418px;
  gap: 15px;
}

/* 区块标题（无外壳区域用，与工厂/产线屏同款语言） */
.sec-h {
  display: flex;
  align-items: center;
  gap: 11px;
  margin-bottom: 11px;
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
.sec-meta {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.sec-meta b.bad {
  color: var(--sb-red);
  font-weight: 600;
}
.sec-meta b.warn {
  color: var(--sb-amber);
  font-weight: 600;
}

/* 左列：产线墙 + 交付预警 */
.wb-lines {
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
}
.wb-lines-list {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.wb-woa {
  flex: none;
  margin-top: 11px;
  padding-top: 9px;
  border-top: 1px solid var(--sb-divider);
}
.wb-sub-h {
  margin: 0 0 4px;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.05em;
  color: var(--sb-muted);
}
.wb-woa-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 2px;
  font-size: 13px;
  min-width: 0;
}
.wb-woa-code {
  flex: none;
  font-family: ui-monospace, monospace;
  color: var(--sb-cyan);
}
.wb-woa-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wb-woa-due {
  flex: none;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}
.wb-woa-due.bad {
  color: var(--sb-red);
}
.wb-woa-due.warn {
  color: var(--sb-amber);
}

/* 空态 = 健康（不造假异常） */
.wb-empty-row {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 7px 2px;
  color: var(--sb-muted);
  font-size: 13px;
}
.wb-ok-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--sb-green);
  box-shadow: 0 0 8px var(--sb-green);
  flex: none;
}

/* 中列：趋势 + 事件流 */
.wb-center {
  display: grid;
  grid-template-rows: minmax(0, 1fr) auto;
  gap: 13px;
  min-height: 0;
  min-width: 0;
}
.wb-trend {
  min-height: 0;
}
.wb-events {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.wb-dev-sum {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.wb-ev-list {
  min-height: 52px;
  max-height: 176px;
  overflow-y: auto;
}
.wb-ev {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 9px 2px;
  font-size: 14px;
  min-width: 0;
}
.wb-ev + .wb-ev {
  border-top: 1px solid var(--sb-divider);
}
.wb-ev-time {
  flex: none;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  font-size: 13px;
}
.wb-ev-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex: none;
  background: var(--sb-faint);
}
.wb-ev.alarm .wb-ev-dot {
  background: var(--sb-red);
  box-shadow: 0 0 8px var(--sb-red);
  animation: wb-ev-pulse 1.6s ease-in-out infinite;
}
.wb-ev.downtime .wb-ev-dot,
.wb-ev.warn .wb-ev-dot {
  background: var(--sb-amber);
  box-shadow: 0 0 8px var(--sb-amber);
}
@keyframes wb-ev-pulse {
  50% {
    opacity: 0.35;
  }
}
.wb-ev-line {
  flex: none;
  color: var(--sb-muted);
  font-size: 13px;
}
.wb-ev-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wb-ev.alarm .wb-ev-txt {
  color: var(--sb-text);
}
.wb-ev-st {
  flex: none;
  font-size: 13px;
  font-weight: 600;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.wb-ev.alarm .wb-ev-st {
  color: var(--sb-red);
}
.wb-ev.downtime .wb-ev-st,
.wb-ev.warn .wb-ev-st {
  color: var(--sb-amber);
}
.wb-ev-empty {
  padding: 13px 2px;
}

/* 右列：齐套 / 质量 / 班组 */
.wb-right {
  display: flex;
  flex-direction: column;
  gap: 13px;
  min-height: 0;
  min-width: 0;
}
/* 齐套面板内容自适应 —— 缺料空态（多数车间的健康态）不拉空高度，
   剩余高度让给当班质量（NCR 列表可增长） */
.wb-kit {
  flex: none;
  display: flex;
  flex-direction: column;
}
.wb-kit-top {
  display: flex;
  align-items: center;
  gap: 24px;
  margin-bottom: 10px;
}
.wb-kit-v {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  font-size: 42px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-green);
  font-variant-numeric: tabular-nums;
}
.wb-kit-v.warn {
  color: var(--sb-amber);
}
.wb-kit-mini {
  flex: 1;
  margin: 0;
  display: flex;
  gap: 26px;
}
.wb-kit-mini dt {
  font-size: 12.5px;
  color: var(--sb-muted);
}
.wb-kit-mini dd {
  margin: 5px 0 0;
  font-size: 23px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.wb-kit-mini dd.bad {
  color: var(--sb-red);
}
.wb-shorts {
  max-height: 236px;
  overflow-y: auto;
}
.wb-short {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 2px;
  min-width: 0;
}
.wb-short + .wb-short {
  border-top: 1px solid var(--sb-divider);
}
.wb-short-l {
  flex: 1;
  min-width: 0;
}
.wb-short-mat {
  display: block;
  font-size: 14.5px;
  font-weight: 600;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wb-short-meta {
  display: block;
  margin-top: 2px;
  font-size: 12px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wb-short-r {
  flex: none;
  text-align: right;
}
.wb-short-qty b {
  font-size: 17px;
  font-weight: 700;
  color: var(--sb-red);
  font-variant-numeric: tabular-nums;
}
.wb-short-qty small {
  font-size: 12px;
  color: var(--sb-muted);
  margin-left: 2px;
}
.wb-short-eta {
  display: block;
  margin-top: 2px;
  font-size: 11.5px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}

.wb-quality {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.wb-quality .wb-ncr {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}
.wb-q-nums {
  margin: 0;
  display: grid;
  grid-template-columns: 1.2fr 1fr 1fr;
  gap: 10px;
}
.wb-q-nums dt {
  font-size: 12.5px;
  color: var(--sb-muted);
}
.wb-q-nums dd {
  margin: 5px 0 0;
  font-size: 24px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.wb-q-nums dd small {
  font-size: 12.5px;
  font-weight: 500;
  color: var(--sb-muted);
}
.wb-q-nums dd.warn {
  color: var(--sb-amber);
}
.wb-q-nums dd.bad {
  color: var(--sb-red);
}
.wb-ncr {
  margin-top: 9px;
  padding-top: 7px;
  border-top: 1px solid var(--sb-divider);
}
.wb-ncr-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 2px;
  font-size: 13px;
  min-width: 0;
}
.wb-ncr-code {
  flex: none;
  font-family: ui-monospace, monospace;
  color: var(--sb-cyan);
}
.wb-ncr-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wb-ncr-st {
  flex: none;
  font-weight: 600;
  color: var(--sb-amber);
}

.wb-crew {
  flex: none;
}
.wb-crew-head {
  display: flex;
  align-items: baseline;
  gap: 14px;
}
.wb-crew-team {
  font-size: 21px;
  font-weight: 700;
  color: #fff;
  letter-spacing: 0.04em;
}
.wb-crew-lead {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--sb-muted);
}
.wb-crew-nums {
  margin: 11px 0 0;
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 10px;
}
.wb-crew-nums dt {
  font-size: 12.5px;
  color: var(--sb-muted);
}
.wb-crew-nums dd {
  margin: 5px 0 0;
  font-size: 24px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.wb-crew-nums dd small {
  font-size: 12.5px;
  font-weight: 500;
  color: var(--sb-muted);
}
.wb-crew-nums dd.warn {
  color: var(--sb-amber);
}
.wb-crew-note {
  margin: 10px 0 0;
  padding-top: 8px;
  border-top: 1px solid var(--sb-divider);
  font-size: 12.5px;
  color: var(--sb-amber);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.wb-foot {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  font-size: 12.5px;
  color: var(--sb-faint);
  border-top: 1px solid var(--sb-divider);
  padding-top: 10px;
}

@media (prefers-reduced-motion: reduce) {
  .wb-ev.alarm .wb-ev-dot {
    animation: none;
  }
}
</style>
