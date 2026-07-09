<script setup lang="ts">
import {
  ScreenBarChart,
  ScreenDonut,
  ScreenPanel,
  ScreenScrollArea,
  ScrollBoard,
  StatusLight,
  StatusTag,
  useScreenData,
} from '@nerv-iip/ui'
import {
  ArrowDownToLine,
  ArrowUpFromLine,
  Bot,
  ChartPie,
  ClipboardCheck,
  Container,
  Forklift,
  GitFork,
  LayoutList,
  MoveHorizontal,
  MoveVertical,
  OctagonAlert,
  PackageSearch,
  Scale,
  Shuffle,
} from 'lucide-vue-next'
import { type Component, computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { useBackLink } from '@/composables/useBackLink'
import { IS_REAL_DATA } from '@/data/config'
import type {
  WarehouseBoard,
  WarehouseOpsTick,
  WcsAdapterKind,
  WhTaskRow,
} from '@/data/contracts/warehouse'
import { fetchWarehouseBoard, fetchWarehouseOpsTick } from '@/data/fetchers/warehouse'
import { scopedOverride, type Scoped } from '@/data/scope'
import ScreenLayout from '@/layouts/ScreenLayout.vue'

// 仓储物流大屏（MAN-318）：WMS 作业指挥屏 —— 一眼掌握当日出入库进度、
// 上架/拣货/盘点积压与流速、WCS 失败告警，调度人力补到积压环节。
// 刷新分层：主数据（KPI/出入库进度）10s · 任务看板/WCS 15s；真实模式由 WMS 分页 list 前端聚合，
// 轮询降频兼顾 Gateway 限流（#734）。页面隐藏时 useScreenData 统一暂停轮询、断后端保留上次数据标 stale。
// 库存水位/低库存预警半屏仍缺 Inventory 读面聚合（待 #570），一期不做（诚实定位）。
const scope = useAccessScope()
const backLink = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
// 两条流各自标记取数时的 scope，切换工厂/persona 时旧 scope 的 tick 不会覆盖新 board。
const scopeKey = computed(() => `${scope.currentFactoryId}::${scope.personaId}`)
const {
  data: boardEnv,
  lastUpdated,
  isStale: boardStale,
  refresh,
} = useScreenData<Scoped<WarehouseBoard>>(
  async () => ({
    scopeKey: scopeKey.value,
    data: await fetchWarehouseBoard(scope.currentFactoryId),
  }),
  { intervalMs: 10000 },
)
const {
  data: opsEnv,
  isStale: opsStale,
  refresh: refreshOps,
} = useScreenData<Scoped<WarehouseOpsTick>>(
  async () => ({
    scopeKey: scopeKey.value,
    data: await fetchWarehouseOpsTick(scope.currentFactoryId),
  }),
  { intervalMs: 15000 },
)
const board = computed(() => boardEnv.value?.data)
// ops 仅在其 scope 与当前 board 一致时才优先（否则回退 board 完整快照）；页脚 stale 同时反映主板与 tick。
const ops = computed(() => scopedOverride(opsEnv.value, boardEnv.value))
const isStale = computed(() => Boolean(boardStale.value || opsStale.value))
watch(
  () => [scope.currentFactoryId, scope.personaId],
  () => {
    void refresh()
    void refreshOps()
  },
)

const factoryName = computed(
  () => scope.factories.find((f) => f.id === scope.currentFactoryId)?.name ?? '全部车间',
)
const nf = new Intl.NumberFormat('en-US')
// 真实模式：入库/出库 facade 仅文档级（无行级数量），口径为「单」；mock 演示数据为「行」。
const flowUnit = IS_REAL_DATA ? '单' : '行'

/** 分钟 → 龄期短格式（45m / 1h 12m） */
function fmtAge(min: number): string {
  if (min >= 60) return `${Math.floor(min / 60)}h ${String(min % 60).padStart(2, '0')}m`
  return `${min}m`
}
const updatedAt = computed(() => {
  const t = lastUpdated.value
  if (!t) return '—'
  const d = new Date(t)
  const p = (x: number) => String(x).padStart(2, '0')
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
})

// —— 顶部 KPI 带：出库进度 hero（发不出货是仓库第一失职 —— 指挥屏第一焦点）+
//    五格（入库/积压/差异/失败），语义色渐隐强调线，主次分明 ——
const heroKpi = computed(() => {
  const b = board.value
  if (!b) return null
  return {
    value: b.kpis.outboundPct,
    sub: IS_REAL_DATA
      ? `发运 ${b.outbound.docsDone}/${b.outbound.docsTotal} 单`
      : `${nf.format(b.outbound.linesDone)}/${nf.format(b.outbound.linesTotal)} 行 · 发运 ${b.outbound.docsDone}/${b.outbound.docsTotal} 单`,
  }
})
interface BandCell {
  icon: Component
  label: string
  value: string
  unit: string
  sub: string
  /** 数字与强调线的语义色（积压是工作量非异常 —— 保持中性，异常色只给超时/失败/差异） */
  tone: 'cyan' | 'neutral' | 'warn' | 'bad' | 'ok'
  /** 副行独立语义色（如积压格的「超时 N」红标） */
  subTone?: 'warn' | 'bad'
}
const bandCells = computed<BandCell[]>(() => {
  const b = board.value
  if (!b) return []
  return [
    {
      icon: ArrowDownToLine,
      label: '当日入库进度',
      value: String(b.kpis.inboundPct),
      unit: '%',
      sub: `${nf.format(b.inbound.linesDone)}/${nf.format(b.inbound.linesTotal)} ${flowUnit}`,
      tone: 'cyan',
    },
    {
      icon: PackageSearch,
      label: '拣货积压',
      value: String(b.kpis.pickBacklog),
      unit: '项',
      sub: b.pick.overdue > 0 ? `超时 ${b.pick.overdue}` : '无超时',
      tone: 'neutral',
      subTone: b.pick.overdue > 0 ? 'bad' : undefined,
    },
    {
      icon: Forklift,
      label: '上架积压',
      value: String(b.kpis.putawayBacklog),
      unit: '项',
      sub: b.putaway.overdue > 0 ? `超时 ${b.putaway.overdue}` : '无超时',
      tone: 'neutral',
      subTone: b.putaway.overdue > 0 ? 'bad' : undefined,
    },
    {
      icon: OctagonAlert,
      label: 'WCS 失败',
      value: String(b.kpis.wcsFailed),
      unit: '条',
      sub:
        b.kpis.wcsFailed > 0
          ? `累计重试 ${b.wcs.failures.reduce((n, x) => n + x.retries, 0)} 次`
          : '链路正常',
      tone: b.kpis.wcsFailed > 0 ? 'bad' : 'ok',
    },
    {
      icon: Scale,
      label: '盘点差异',
      value: String(b.kpis.countVariance),
      unit: '位',
      sub: `已盘 ${b.count.counted}/${b.count.planned} 位`,
      tone: b.kpis.countVariance > 0 ? 'warn' : 'ok',
    },
  ]
})

// —— 作业任务看板（3s tick 优先，主数据兜底；两者同源必然一致）——
const pick = computed(() => ops.value?.pick ?? board.value?.pick)
const putaway = computed(() => ops.value?.putaway ?? board.value?.putaway)
const count = computed(() => ops.value?.count ?? board.value?.count)
const wcs = computed(() => ops.value?.wcs ?? board.value?.wcs)
const overdueTop = computed(() => ops.value?.overdueTop ?? board.value?.overdueTop ?? [])

interface TaskGroupView {
  key: string
  icon: Component
  title: string
  rows: WhTaskRow[]
  backlog: number
  overdue: number
  isCount: boolean
  meta: string
  flex: number
  speed: number
}
const taskGroups = computed<TaskGroupView[]>(() => {
  const p = pick.value
  const pa = putaway.value
  const c = count.value
  if (!p || !pa || !c) return []
  return [
    {
      key: 'pick',
      icon: PackageSearch,
      title: '拣货',
      rows: p.rows,
      backlog: p.backlog,
      overdue: p.overdue,
      isCount: false,
      meta: `今日完成 ${nf.format(p.doneToday)}`,
      flex: 1.5,
      speed: 24,
    },
    {
      key: 'putaway',
      icon: Forklift,
      title: '上架',
      rows: pa.rows,
      backlog: pa.backlog,
      overdue: pa.overdue,
      isCount: false,
      meta: `今日完成 ${nf.format(pa.doneToday)}`,
      flex: 1.15,
      speed: 20,
    },
    {
      key: 'count',
      icon: ClipboardCheck,
      title: '盘点',
      rows: c.rows,
      backlog: c.rows.length,
      overdue: c.overdue,
      isCount: true,
      meta: `已盘 ${c.counted}/${c.planned} 位`,
      flex: 0.9,
      speed: 16,
    },
  ]
})
const taskSummary = computed(() => {
  const p = pick.value
  const pa = putaway.value
  if (!p || !pa) return ''
  const created = p.createdToday + pa.createdToday
  const done = p.doneToday + pa.doneToday
  return `今日创建 ${nf.format(created)} · 已完成 ${nf.format(done)} · 在办 ${p.backlog + pa.backlog}`
})

const ADAPTER_ICONS: Record<WcsAdapterKind, Component> = {
  stacker: Container,
  agv: Bot,
  shuttle: Shuffle,
  conveyor: MoveHorizontal,
  sorter: GitFork,
  hoist: MoveVertical,
}

// WCS 链路负载视图：环形图 ↔ 适配器明细列表，点击切换（同一空间内二选一，列表得满高）。
const wcsView = ref<'chart' | 'list'>('chart')
</script>

<template>
  <ScreenLayout title="Nerv-IIP 仓储物流大屏" :line="factoryName" screen="指挥中心大屏 05">
    <div v-if="board" class="wb">
      <!-- 顶部 KPI 带：出库进度 hero + 五格，语义色渐隐强调线 -->
      <ScreenPanel class="wb-band">
        <div class="wb-band-in">
          <div v-if="heroKpi" class="wb-hero">
            <div class="wb-hero-v">
              <span class="wb-num">{{ heroKpi.value }}<small>%</small></span>
              <i class="wb-kpi-line cyan" aria-hidden="true" />
            </div>
            <div class="wb-hero-l">
              <ArrowUpFromLine :size="17" :stroke-width="1.8" class="wb-kpi-ic" />当日出库进度
            </div>
            <span class="wb-hero-sub">{{ heroKpi.sub }}</span>
          </div>
          <div v-for="c in bandCells" :key="c.label" class="wb-kpi">
            <dt class="wb-kpi-t">
              <component :is="c.icon" :size="17" :stroke-width="1.8" class="wb-kpi-ic" />{{
                c.label
              }}
            </dt>
            <dd class="wb-kpi-v" :class="c.tone">
              <span class="wb-num"
                >{{ c.value }}<small>{{ c.unit }}</small></span
              >
              <i class="wb-kpi-line" :class="c.tone" aria-hidden="true" />
            </dd>
            <span
              class="wb-kpi-sub"
              :class="{
                bad: (c.subTone ?? c.tone) === 'bad',
                warn: (c.subTone ?? c.tone) === 'warn',
              }"
              >{{ c.sub }}</span
            >
          </div>
        </div>
      </ScreenPanel>

      <div class="wb-main">
        <!-- 左：出入库双进度（大数字 + 发丝进度条 + 12h 流量） -->
        <section class="wb-flows">
          <ScreenPanel title="当日入库 · ASN" class="wb-flow">
            <template #extra>
              <span class="wb-flow-docs"
                >收货单 {{ board.inbound.docsDone }}/{{ board.inbound.docsTotal }}</span
              >
            </template>
            <div class="wb-flow-hero">
              <span class="wb-flow-v">
                {{ nf.format(board.inbound.linesDone)
                }}<small>/ {{ nf.format(board.inbound.linesTotal) }} {{ flowUnit }}</small>
              </span>
            </div>
            <div class="wb-bar"><i :style="{ width: `${board.inbound.pct}%` }" /></div>
            <div class="wb-flow-meta">
              <span>
                收货完成率
                <b
                  >{{
                    board.inbound.docsTotal > 0
                      ? Math.round((board.inbound.docsDone / board.inbound.docsTotal) * 100)
                      : 0
                  }}%</b
                >
              </span>
              <span v-if="board.inbound.postFailedDocs > 0" class="wb-postfail">
                过账失败 {{ board.inbound.postFailedDoc }}
              </span>
              <span v-else class="wb-postok"><i class="wb-okdot" />过账无异常</span>
            </div>
            <!-- 小时流量是离散量 —— 柱状比面积曲线更诚实（每小时一根柱） -->
            <div class="wb-flow-spark">
              <ScreenBarChart
                :series="[{ label: '入库', color: '#4aa6ee', data: board.inbound.hourly }]"
                :hover-labels="board.inbound.hourLabels"
                autoplay
              />
            </div>
            <div class="wb-flow-x">
              <span>{{ board.inbound.hourLabels[0] }}</span>
              <span>{{ board.inbound.hourLabels[6] }}</span>
              <span>现在</span>
            </div>
          </ScreenPanel>

          <ScreenPanel title="当日出库 · SO" class="wb-flow out">
            <template #extra>
              <span class="wb-flow-docs"
                >发运 {{ board.outbound.docsDone }}/{{ board.outbound.docsTotal }} 单</span
              >
            </template>
            <div class="wb-flow-hero">
              <span class="wb-flow-v">
                {{ nf.format(board.outbound.linesDone)
                }}<small>/ {{ nf.format(board.outbound.linesTotal) }} {{ flowUnit }}</small>
              </span>
            </div>
            <div class="wb-bar"><i :style="{ width: `${board.outbound.pct}%` }" /></div>
            <div class="wb-flow-meta">
              <span v-if="board.outbound.customers > 0"
                >客户 <b>{{ board.outbound.customers }}</b> 家</span
              >
              <span v-if="board.outbound.latestShipment" class="wb-latest"
                >最近发运 {{ board.outbound.latestShipment }}</span
              >
            </div>
            <div class="wb-flow-spark">
              <ScreenBarChart
                :series="[{ label: '出库', color: '#8b9be6', data: board.outbound.hourly }]"
                :hover-labels="board.outbound.hourLabels"
                autoplay
                :autoplay-ms="2800"
              />
            </div>
            <div class="wb-flow-x">
              <span>{{ board.outbound.hourLabels[0] }}</span>
              <span>{{ board.outbound.hourLabels[6] }}</span>
              <span>现在</span>
            </div>
          </ScreenPanel>
        </section>

        <!-- 中：作业任务看板（拣货 / 上架 / 盘点分组，超时红标，自动滚动） -->
        <ScreenPanel title="作业任务看板" class="wb-tasks">
          <template #extra>
            <span class="wb-tasks-sum">{{ taskSummary }}</span>
          </template>
          <div class="wb-tk">
            <section
              v-for="g in taskGroups"
              :key="g.key"
              class="tg"
              :data-kind="g.key"
              :style="{ flex: g.flex }"
            >
              <header class="tg-h">
                <component :is="g.icon" :size="14" :stroke-width="1.8" class="tg-ic" />
                <b class="tg-name">{{ g.title }}</b>
                <span class="tg-cnt"
                  >{{ g.isCount ? '待盘' : '积压' }} <b>{{ g.backlog }}</b></span
                >
                <em v-if="g.overdue > 0" class="tg-late">超时 {{ g.overdue }}</em>
                <span v-if="g.isCount && count" class="tg-var" :class="{ on: count.variance > 0 }">
                  差异 {{ count.variance }} 位
                </span>
                <span class="tg-rule" aria-hidden="true" />
                <span class="tg-done">{{ g.meta }}</span>
              </header>
              <div class="tg-cols" :class="{ count: g.isCount }">
                <template v-if="!g.isCount">
                  <span>单号</span><span>物料</span><span class="r">数量</span><span>库位流向</span
                  ><span>来源单</span><span class="r">龄期</span>
                </template>
                <template v-else>
                  <span>单号</span><span>物料</span><span class="r">账面数量</span
                  ><span>待盘库位</span><span class="r">龄期</span>
                </template>
              </div>
              <div class="tg-list">
                <ScrollBoard :items="g.rows" :row-key="(r: WhTaskRow) => r.id" :speed="g.speed">
                  <template #row="{ item }">
                    <div class="tg-row" :class="{ count: g.isCount, late: item.overdue }">
                      <span class="tg-id">{{ item.id }}</span>
                      <span class="tg-sku">{{ item.sku }}</span>
                      <span class="tg-qty r"
                        >{{ nf.format(item.qty) }}<small> {{ item.unit }}</small></span
                      >
                      <span v-if="!g.isCount" class="tg-route">
                        <span class="tg-from">{{ item.from }}</span>
                        <i class="tg-arrow" aria-hidden="true">→</i>
                        <span class="tg-to">{{ item.to }}</span>
                      </span>
                      <span v-else class="tg-route">{{ item.from }}</span>
                      <span v-if="!g.isCount" class="tg-ref">{{ item.ref }}</span>
                      <span class="tg-age r" :class="{ late: item.overdue }">
                        {{ fmtAge(item.ageMin) }}<em v-if="item.overdue">超时</em>
                      </span>
                    </div>
                  </template>
                </ScrollBoard>
              </div>
            </section>
          </div>
        </ScreenPanel>

        <!-- 右：WCS 自动化（失败告警 + 指令状态合并为一块 —— 单一 flex 容器内动态共享
             垂直空间：失败多则告警区滚动、不挤压下方；省一个面板头/间距给适配器行） · 任务超时榜 -->
        <section class="wb-wcs">
          <ScreenPanel
            title="WCS 自动化"
            :accent="wcs && wcs.failures.length > 0 ? 'red' : undefined"
            class="wb-wcsboard"
          >
            <template #extra>
              <span class="wf-count" :class="{ calm: !wcs || wcs.failures.length === 0 }">
                {{
                  wcs && wcs.failures.length > 0 ? `${wcs.failures.length} 条未恢复` : '链路正常'
                }}
              </span>
            </template>
            <div v-if="wcs" class="wm">
              <!-- ① 失败告警（异常优先置顶；约 3 条完整显示，更多滚动） -->
              <ScreenScrollArea class="wf-list">
                <div v-for="x in wcs.failures" :key="x.cmd" class="wf-row">
                  <i class="wf-dot" aria-hidden="true" />
                  <div class="wf-main">
                    <div class="wf-top">
                      <b class="wf-ad">{{ x.adapter }}</b>
                      <span class="wf-cmd">{{ x.cmd }}</span>
                      <span class="wf-since">{{ x.firstAt }} 起 · {{ x.sinceMin }} min</span>
                    </div>
                    <div class="wf-sub">
                      <span class="wf-err">{{ x.error }}</span>
                      <em class="wf-retry">重试 {{ x.retries }} 次</em>
                    </div>
                  </div>
                </div>
                <div v-if="wcs.failures.length === 0" class="wf-empty">
                  <StatusLight tone="run" label="无失败指令" />
                </div>
              </ScreenScrollArea>
              <!-- ② 链路负载：环形图 ↔ 适配器明细列表点击切换（同一区域二选一，列表得满高） -->
              <div class="wm-toggle">
                <span class="wm-toggle-t">链路负载</span>
                <div class="wm-seg" role="tablist" aria-label="链路负载视图">
                  <button
                    type="button"
                    class="wm-seg-b"
                    :class="{ on: wcsView === 'chart' }"
                    role="tab"
                    :aria-selected="wcsView === 'chart'"
                    aria-label="环形图"
                    @click="wcsView = 'chart'"
                  >
                    <ChartPie :size="13" :stroke-width="1.9" />
                  </button>
                  <button
                    type="button"
                    class="wm-seg-b"
                    :class="{ on: wcsView === 'list' }"
                    role="tab"
                    :aria-selected="wcsView === 'list'"
                    aria-label="适配器明细"
                    @click="wcsView = 'list'"
                  >
                    <LayoutList :size="13" :stroke-width="1.9" />
                  </button>
                </div>
              </div>
              <div class="wm-body">
                <Transition name="wm-swap" mode="out-in">
                  <!-- 环形：排队/执行/失败 = 当前在链指令，中心 = 今日完成 -->
                  <div v-if="wcsView === 'chart'" key="chart" class="wm-chart">
                    <ScreenDonut
                      class="wa-donut"
                      :size="104"
                      :segments="[
                        {
                          label: '排队',
                          value: wcs.counts.queued,
                          color: 'rgba(160, 200, 245, 0.45)',
                        },
                        { label: '执行中', value: wcs.counts.running, color: '#4aa6ee' },
                        { label: '失败', value: wcs.counts.failed, color: '#ef5a63' },
                      ]"
                    >
                      <b class="wa-dn-num">{{ nf.format(wcs.counts.completed) }}</b>
                      <span class="wa-dn-cap">今日完成</span>
                    </ScreenDonut>
                  </div>
                  <!-- 适配器明细（满高，超出滚动） -->
                  <ScreenScrollArea v-else key="list" class="wa-rows">
                    <div v-for="a in wcs.adapters" :key="a.kind" class="wa-row">
                      <component
                        :is="ADAPTER_ICONS[a.kind]"
                        :size="14"
                        :stroke-width="1.8"
                        class="wa-ic"
                      />
                      <span class="wa-name">{{ a.label }}</span>
                      <span class="wa-nums"
                        >执行 <b>{{ a.running }}</b> · 排队 <b>{{ a.queued }}</b></span
                      >
                      <span class="wa-done">完成 {{ nf.format(a.completed) }}</span>
                      <b v-if="a.failed > 0" class="wa-fail">失败 {{ a.failed }}</b>
                    </div>
                    <div v-if="wcs.adapters.length === 0" class="wf-empty">
                      <StatusLight tone="run" label="无在链设备" />
                    </div>
                  </ScreenScrollArea>
                </Transition>
              </div>
            </div>
          </ScreenPanel>

          <ScreenPanel title="任务超时榜 · TOP5" class="wb-overdue">
            <template #extra>
              <StatusTag
                tone="amber"
                :label="IS_REAL_DATA ? '龄期按创建时刻推算' : '龄期推算 · 待 #570'"
              />
            </template>
            <div class="wo-list">
              <div v-for="(r, i) in overdueTop" :key="r.id" class="wo-row">
                <b class="wo-rank" :class="{ top: i === 0 }">{{ i + 1 }}</b>
                <span class="wo-id">{{ r.id }}</span>
                <span class="wo-kind">{{ r.kindLabel }}</span>
                <span class="wo-sku">{{ r.sku }}</span>
                <b class="wo-age">{{ fmtAge(r.ageMin) }}</b>
              </div>
              <div v-if="overdueTop.length === 0" class="wo-empty">
                <StatusLight tone="run" label="无超时任务" />
              </div>
            </div>
          </ScreenPanel>
        </section>
      </div>

      <footer class="wb-foot">
        <span class="wb-foot-l">
          <RouterLink :to="backLink.to" class="wb-back">‹ {{ backLink.label }}</RouterLink>
          <span
            >{{ IS_REAL_DATA ? 'WMS 作业域实时数据' : 'WMS 作业域演示数据' }} · 龄期 / 吞吐 /
            适配器聚合为前端推算 · 库存半屏读面 待 #570</span
          >
        </span>
        <span class="wb-foot-r">
          当日吞吐 <b>{{ nf.format(board.kpis.throughputLines) }}</b> {{ flowUnit }} （入
          {{ nf.format(board.inbound.linesDone) }} · 出 {{ nf.format(board.outbound.linesDone) }}）
          · 更新 <b class="wb-foot-ts">{{ updatedAt }}</b>
          <span v-if="isStale" class="wb-stale">· 数据暂未刷新</span>
        </span>
      </footer>
    </div>
    <div v-else class="wb-loading">连接数据…</div>
  </ScreenLayout>
</template>

<style scoped>
.wb {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.wb-loading {
  height: 100%;
  display: grid;
  place-content: center;
  color: var(--sb-muted);
  font-size: 15px;
}

/* —— 顶部 KPI 带：出库 hero + 五格 + 发丝分隔（主次分明，远视距标签 14px 起） —— */
.wb-band {
  flex: none;
}
.wb-band-in {
  display: grid;
  grid-template-columns: 300px repeat(5, 1fr);
  align-items: center;
}
.wb-hero {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  padding: 2px 26px 0 8px;
}
.wb-hero-v {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  font-size: 48px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
  text-shadow: var(--sb-value-glow);
}
.wb-hero-v .wb-num small {
  font-size: 19px;
  font-weight: 600;
  margin-left: 3px;
  color: var(--sb-muted);
}
.wb-hero-l {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  margin-top: 9px;
  font-size: 14.5px;
  color: var(--sb-muted);
}
.wb-hero-sub {
  display: block;
  margin-top: 6px;
  font-size: 12.5px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.wb-kpi {
  position: relative;
  padding: 2px 22px;
}
.wb-kpi::before {
  content: '';
  position: absolute;
  left: 0;
  top: 6px;
  bottom: 6px;
  width: 1px;
  background: var(--sb-divider);
}
/* dt 必须块级 —— inline 级会与数字并排（格宽变化时布局不稳定） */
.wb-kpi-t {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 14px;
  color: var(--sb-muted);
}
.wb-kpi-ic {
  color: var(--sb-faint);
  flex: none;
}
.wb-kpi-v {
  margin: 8px 0 0;
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  font-size: 34px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
  text-shadow: var(--sb-value-glow);
}
.wb-kpi-v.bad {
  color: var(--sb-red);
  text-shadow: 0 0 20px rgba(239, 90, 99, 0.4);
}
.wb-kpi-v.warn {
  color: var(--sb-amber);
  text-shadow: none;
}
.wb-num {
  display: inline-flex;
  align-items: flex-end;
  line-height: 1;
}
.wb-num small {
  font-size: 0.42em;
  font-weight: 600;
  margin-left: 3px;
  padding-bottom: 0.12em;
  color: var(--sb-muted);
}
/* 数据强调线：语义色渐隐短线（同 line 屏 .lb-score-line 语言，替代图标装饰） */
.wb-kpi-line {
  width: 44px;
  height: 2px;
  margin-top: 9px;
  border-radius: 1px;
  background: linear-gradient(90deg, rgba(255, 255, 255, 0.32), transparent);
  opacity: 0.8;
}
.wb-kpi-line.cyan {
  background: linear-gradient(90deg, var(--sb-cyan), transparent);
}
.wb-kpi-line.bad {
  background: linear-gradient(90deg, var(--sb-red), transparent);
}
.wb-kpi-line.warn {
  background: linear-gradient(90deg, var(--sb-amber), transparent);
}
.wb-kpi-line.ok {
  background: linear-gradient(90deg, var(--sb-green), transparent);
}
.wb-kpi-sub {
  display: block;
  margin-top: 7px;
  font-size: 12px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.wb-kpi-sub.bad {
  color: var(--sb-red);
}
.wb-kpi-sub.warn {
  color: var(--sb-amber);
}

/* —— 主体三列 —— */
.wb-main {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 452px minmax(0, 1fr) 464px;
  gap: 14px;
}

/* —— 左列：出入库双进度 —— */
.wb-flows {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-height: 0;
}
.wb-flow {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.wb-flow-docs {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.wb-flow-hero {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
}
.wb-flow-v {
  font-size: 46px;
  font-weight: 800;
  line-height: 1;
  color: #fff;
  text-shadow: var(--sb-value-glow);
  font-variant-numeric: tabular-nums;
}
.wb-flow-v small {
  font-size: 17px;
  font-weight: 600;
  color: var(--sb-muted);
  margin-left: 7px;
}
/* 发丝进度条：3px 轨道 + 语义色渐变充盈 */
.wb-bar {
  height: 3px;
  margin-top: 13px;
  border-radius: 2px;
  background: rgba(255, 255, 255, 0.07);
  overflow: hidden;
}
.wb-bar i {
  display: block;
  height: 100%;
  border-radius: 2px;
  background: linear-gradient(90deg, rgba(74, 166, 238, 0.35), var(--sb-cyan));
  transition: width 0.6s var(--sb-ease-emphasized);
}
.wb-flow.out .wb-bar i {
  background: linear-gradient(90deg, rgba(139, 155, 230, 0.35), var(--sb-indigo));
}
.wb-flow-meta {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-top: 10px;
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.wb-flow-meta b {
  color: var(--sb-text);
  font-weight: 700;
}
.wb-postfail {
  color: var(--sb-red);
  font-weight: 600;
}
.wb-postok {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--sb-faint);
}
.wb-okdot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--sb-green);
  box-shadow: 0 0 6px var(--sb-green);
}
.wb-latest {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--sb-faint);
}
.wb-flow-spark {
  flex: 1;
  min-height: 44px;
  margin-top: 13px;
}
.wb-flow-x {
  display: flex;
  justify-content: space-between;
  margin-top: 5px;
  font-size: 11px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}

/* —— 中列：作业任务看板 —— */
.wb-tasks {
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
}
.wb-tasks-sum {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.wb-tk {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.tg {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.tg + .tg {
  border-top: 1px solid var(--sb-divider);
  padding-top: 10px;
}
.tg-h {
  display: flex;
  align-items: center;
  gap: 9px;
  min-height: 22px;
}
.tg-ic {
  color: var(--sb-faint);
  flex: none;
}
.tg-name {
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.06em;
  color: var(--sb-text);
}
.tg-cnt {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.tg-cnt b {
  color: var(--sb-text);
  font-weight: 700;
}
.tg-late {
  font-style: normal;
  font-size: 12px;
  font-weight: 600;
  color: var(--sb-red);
  font-variant-numeric: tabular-nums;
}
.tg-var {
  font-size: 12px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.tg-var.on {
  color: var(--sb-amber);
}
.tg-rule {
  flex: 1;
  height: 1px;
  margin: 0 4px;
  background: linear-gradient(90deg, rgba(255, 255, 255, 0.07), transparent);
}
.tg-done {
  font-size: 12px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}

/* 列头与行共用 grid 模板（发丝行分隔） */
.tg-cols,
.tg-row {
  display: grid;
  grid-template-columns: 104px minmax(0, 1fr) 80px 224px 88px 104px;
  gap: 10px;
  align-items: center;
}
.tg-cols.count,
.tg-row.count {
  grid-template-columns: 104px minmax(0, 1fr) 104px 140px 104px;
}
.tg-cols {
  margin-top: 7px;
  padding: 0 2px 5px;
  font-size: 11.5px;
  color: var(--sb-faint);
  border-bottom: 1px solid var(--sb-divider);
}
.r {
  text-align: right;
}
.tg-list {
  flex: 1;
  min-height: 34px;
}
.tg-row {
  padding: 6.5px 2px;
  font-size: 12.5px;
  border-bottom: 1px solid var(--sb-divider);
}
.tg-id {
  font-family: ui-monospace, monospace;
  font-size: 12px;
  color: var(--sb-cyan);
  white-space: nowrap;
}
.tg-row.late .tg-id {
  color: var(--sb-red);
}
.tg-sku {
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.tg-qty {
  color: var(--sb-text);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.tg-qty small {
  font-weight: 400;
  color: var(--sb-faint);
  font-size: 11px;
}
.tg-route {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
}
.tg-from {
  color: var(--sb-muted);
  overflow: hidden;
  text-overflow: ellipsis;
}
.tg-arrow {
  font-style: normal;
  color: var(--sb-faint);
  opacity: 0.7;
  flex: none;
}
.tg-to {
  color: var(--sb-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
}
.tg-ref {
  font-family: ui-monospace, monospace;
  font-size: 11.5px;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.tg-age {
  font-variant-numeric: tabular-nums;
  color: var(--sb-muted);
  white-space: nowrap;
}
.tg-age.late {
  color: var(--sb-red);
  font-weight: 700;
}
.tg-age em {
  font-style: normal;
  font-size: 10.5px;
  font-weight: 600;
  margin-left: 4px;
  letter-spacing: 0.04em;
}

/* —— 右列：WCS 自动化（合并面板：失败告警 + 环形 + 适配器行动态共享空间） —— */
.wb-wcs {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-height: 0;
}
.wb-wcsboard {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.wm {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.wf-count {
  font-size: 13px;
  color: var(--sb-red);
  font-variant-numeric: tabular-nums;
}
.wf-count.calm {
  color: var(--sb-green);
}
/* 失败告警区：约 3 条完整显示，更多滚动（失败量增长不挤压下方环形/适配器行） */
.wf-list {
  flex: none;
  max-height: 156px;
}
.wf-row {
  display: flex;
  gap: 10px;
  padding: 9px 2px;
}
.wf-row + .wf-row {
  border-top: 1px solid var(--sb-divider);
}
/* 失败红脉冲 —— 辉光只给活数据 */
.wf-dot {
  width: 9px;
  height: 9px;
  margin-top: 4px;
  border-radius: 50%;
  background: var(--sb-red);
  box-shadow: 0 0 9px var(--sb-red);
  flex: none;
  animation: wf-pulse 1.6s ease-in-out infinite;
}
@keyframes wf-pulse {
  50% {
    opacity: 0.35;
  }
}
.wf-main {
  flex: 1;
  min-width: 0;
}
.wf-top {
  display: flex;
  align-items: baseline;
  gap: 9px;
  min-width: 0;
}
.wf-ad {
  font-size: 13.5px;
  font-weight: 700;
  color: var(--sb-text);
  flex: none;
}
.wf-cmd {
  font-family: ui-monospace, monospace;
  font-size: 11.5px;
  color: var(--sb-muted);
  flex: none;
}
.wf-since {
  flex: 1;
  min-width: 0;
  text-align: right;
  font-size: 12px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wf-sub {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  margin-top: 4px;
  min-width: 0;
}
.wf-err {
  flex: 1;
  min-width: 0;
  font-size: 12.5px;
  color: var(--sb-red);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wf-retry {
  font-style: normal;
  font-size: 12px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  flex: none;
}
.wf-empty {
  padding: 12px 2px;
  display: flex;
  justify-content: center;
}

/* 链路负载视图切换：分段器 + 环形/列表二选一区域 */
.wm-toggle {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 8px;
  padding-top: 9px;
  border-top: 1px solid var(--sb-divider);
}
.wm-toggle-t {
  font-size: 12.5px;
  color: var(--sb-muted);
}
.wm-seg {
  display: inline-flex;
  gap: 2px;
  padding: 2px;
  border-radius: 7px;
  background: rgba(255, 255, 255, 0.05);
}
.wm-seg-b {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 20px;
  border: 0;
  border-radius: 5px;
  background: transparent;
  color: var(--sb-faint);
  cursor: pointer;
  transition:
    background 0.2s var(--sb-ease-emphasized),
    color 0.2s var(--sb-ease-emphasized);
}
.wm-seg-b:hover {
  color: var(--sb-text-2);
}
.wm-seg-b.on {
  background: rgba(74, 166, 238, 0.18);
  color: var(--sb-cyan);
}
.wm-body {
  flex: 1;
  min-height: 0;
  margin-top: 8px;
  display: flex;
  flex-direction: column;
}
/* 环形视图：区域内居中 */
.wm-chart {
  flex: 1;
  min-height: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}
.wa-donut {
  flex: none;
}
/* 视图切换动效：淡入 + 轻微上滑（out-in 顺序切换，不叠加） */
.wm-swap-enter-active,
.wm-swap-leave-active {
  transition:
    opacity 0.22s var(--sb-ease-emphasized),
    transform 0.22s var(--sb-ease-emphasized);
}
.wm-swap-enter-from {
  opacity: 0;
  transform: translateY(6px);
}
.wm-swap-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
.wa-dn-num {
  font-size: 21px;
  font-weight: 800;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
  text-shadow: var(--sb-value-glow);
}
.wa-dn-cap {
  margin-top: 4px;
  font-size: 10.5px;
  color: var(--sb-muted);
}
/* 适配器行占剩余空间；min-height 0 保证被压缩时内部滚动而非溢出容器 */
.wa-rows {
  flex: 1;
  min-height: 0;
}
/* 环形区图例更紧凑（右列窄面板预算） */
.wa-donut :deep(.sb-dn-legend) {
  gap: 4px;
}
.wa-row {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 7px 2px;
  font-size: 13px;
}
.wa-row + .wa-row {
  border-top: 1px solid var(--sb-divider);
}
.wa-ic {
  color: var(--sb-faint);
  flex: none;
}
.wa-name {
  flex: none;
  width: 88px;
  color: var(--sb-text-2);
  white-space: nowrap;
}
.wa-nums {
  flex: 1;
  min-width: 0;
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.wa-nums b {
  color: var(--sb-text);
  font-weight: 700;
}
.wa-done {
  flex: none;
  font-size: 12px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.wa-fail {
  flex: none;
  font-size: 12.5px;
  font-weight: 700;
  color: var(--sb-red);
  font-variant-numeric: tabular-nums;
}

/* 任务超时榜 */
.wb-overdue {
  flex: none;
}
.wo-list {
  display: flex;
  flex-direction: column;
}
.wo-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 7px 2px;
  font-size: 13px;
}
.wo-row + .wo-row {
  border-top: 1px solid var(--sb-divider);
}
.wo-rank {
  width: 18px;
  flex: none;
  text-align: center;
  font-size: 12.5px;
  font-weight: 700;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.wo-rank.top {
  color: var(--sb-red);
}
.wo-id {
  font-family: ui-monospace, monospace;
  font-size: 12px;
  color: var(--sb-cyan);
  flex: none;
  width: 66px;
}
.wo-kind {
  flex: none;
  font-size: 12px;
  color: var(--sb-muted);
}
.wo-sku {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.wo-age {
  flex: none;
  font-weight: 700;
  color: var(--sb-red);
  font-variant-numeric: tabular-nums;
}
.wo-empty {
  padding: 10px 2px;
  display: flex;
  justify-content: center;
}

/* —— 页脚 —— */
.wb-foot {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  font-size: 12.5px;
  color: var(--sb-faint);
  border-top: 1px solid var(--sb-divider);
  padding-top: 11px;
  font-variant-numeric: tabular-nums;
}
.wb-foot-r b {
  color: var(--sb-text-2);
  font-weight: 700;
}
.wb-stale {
  color: var(--sb-amber);
  font-weight: 600;
}
.wb-foot-l {
  display: inline-flex;
  align-items: center;
  gap: 18px;
  min-width: 0;
}
.wb-back {
  color: var(--sb-cyan);
  text-decoration: none;
  font-size: 13.5px;
  flex: none;
}

@media (prefers-reduced-motion: reduce) {
  .wf-dot {
    animation: none;
  }
  .wb-bar i {
    transition: none;
  }
  .wm-swap-enter-active,
  .wm-swap-leave-active,
  .wm-seg-b {
    transition: none;
  }
  .wm-swap-enter-from,
  .wm-swap-leave-to {
    transform: none;
  }
}
</style>
