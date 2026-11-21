import { useWarehouseDomains } from '@/composables/useWarehouseDomains'
import { useAuthorizedScreenData } from '@/composables/useAuthorizedScreenData'
import {
  ArrowDownToLine,
  Bot,
  ClipboardCheck,
  Container,
  Forklift,
  GitFork,
  MoveHorizontal,
  MoveVertical,
  OctagonAlert,
  PackageSearch,
  Scale,
  Shuffle,
} from '@lucide/vue'
import { type Component, computed, ref, watch } from 'vue'
import { useScreenClock } from './useScreenClock'
import { useAccessScope } from '@/access/useAccessScope'
import { useBackLink } from '@/composables/useBackLink'
import { IS_REAL_DATA } from '@/data/config'
import type {
  WarehouseBoard,
  WarehouseOpsTick,
  WcsAdapterKind,
  WhTaskRow,
} from '@/data/contracts/warehouse'
import { fetchDemoWarehouseBoard, fetchDemoWarehouseOpsTick } from '@/data/fetchers/warehouse'
import { formatScreenFreshness } from '@/data/freshness'
import { scopedOverride, type Scoped } from '@/data/scope'

export function useWarehouseScreen() {
  // 仓储物流大屏（MAN-318）：WMS 作业指挥屏 —— 一眼掌握当日出入库进度、
  // 上架/拣货/盘点积压与流速、WCS 失败告警，调度人力补到积压环节。
  // 刷新分层：主数据（KPI/出入库进度）10s · 任务看板/WCS 15s；真实模式由 WMS 分页 list 前端聚合，
  // 轮询降频兼顾 Gateway 限流（#734）。页面隐藏时 useScreenData 统一暂停轮询、断后端保留上次数据标 stale。
  // 库存水位/低库存预警半屏仍缺 Inventory 读面聚合（待 #570），一期不做（诚实定位）。
  const now = useScreenClock()
  const scope = useAccessScope()
  const backLink = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
  // 两条流各自标记取数时的 scope，切换工厂/persona 时旧 scope 的 tick 不会覆盖新 board。
  const scopeKey = computed(() => `${scope.currentFactoryId}::${scope.personaId}`)
  const domains = IS_REAL_DATA ? useWarehouseDomains() : undefined
  const demoBoard = domains
    ? undefined
    : useAuthorizedScreenData<Scoped<WarehouseBoard>>(
        async () => ({
          scopeKey: scopeKey.value,
          data: await fetchDemoWarehouseBoard(scope.currentFactoryId),
        }),
        { intervalMs: 10000 },
      )
  const boardEnv = demoBoard?.data ?? ref(undefined)
  const boardStale =
    demoBoard?.isStale ??
    computed(() =>
      domains?.controls.some((c) => c.source.error.value && c.source.lastUpdated.value),
    )
  const lastUpdated = demoBoard?.lastUpdated ?? domains!.lastUpdated
  const error = demoBoard?.error
  const loading = demoBoard?.loading
  function refresh() {
    return demoBoard?.refresh() ?? domains?.wcs.refresh()
  }
  const demoOps = IS_REAL_DATA
    ? undefined
    : useAuthorizedScreenData<Scoped<WarehouseOpsTick>>(
        async () => ({
          scopeKey: scopeKey.value,
          data: await fetchDemoWarehouseOpsTick(scope.currentFactoryId),
        }),
        { intervalMs: 15000 },
      )
  const {
    data: opsEnv,
    error: opsError,
    isStale: opsStale,
    lastUpdated: opsLastUpdated,
    refresh: refreshOps,
  } = demoOps ?? {
    data: ref(undefined),
    error: ref(undefined),
    isStale: ref(false),
    lastUpdated: ref(undefined),
    refresh: async () => {},
  }
  const board = computed(() => domains?.board.value ?? boardEnv.value?.data)
  // ops 仅在其 scope 与当前 board 一致时才优先（否则回退 board 完整快照）；页脚分别反映两条数据源。
  const ops = computed(() =>
    opsError.value ? undefined : scopedOverride(opsEnv.value, boardEnv.value),
  )
  const boardFreshness = computed(() =>
    formatScreenFreshness(
      boardStale.value || (!!lastUpdated.value && now.value.getTime() - lastUpdated.value > 30000),
      lastUpdated.value,
    ),
  )
  const opsFreshness = computed(() =>
    formatScreenFreshness(
      opsStale.value ||
        (!!opsLastUpdated.value && now.value.getTime() - opsLastUpdated.value > 45000),
      opsLastUpdated.value,
    ),
  )
  watch(
    () => [scope.currentFactoryId, scope.personaId],
    () => {
      void refresh()
      void refreshOps()
    },
  )

  const factoryName = computed(
    () => scope.factories.find((f) => f.id === scope.currentFactoryId)?.name ?? '未配置组织',
  )
  const nf = new Intl.NumberFormat('en-US')
  // 真实模式：入库/出库 facade 仅文档级（无行级数量），口径为「单」；mock 演示数据为「行」。
  const flowUnit = IS_REAL_DATA ? '单' : '行'

  /** 分钟 → 龄期短格式（45m / 1h 12m） */
  function fmtAge(min: number): string {
    if (min >= 60) return `${Math.floor(min / 60)}h ${String(min % 60).padStart(2, '0')}m`
    return `${min}m`
  }

  // —— 顶部 KPI 带：出库进度 hero（发不出货是仓库第一失职 —— 指挥屏第一焦点）+
  //    五格（入库/积压/差异/失败），语义色渐隐强调线，主次分明 ——
  const heroKpi = computed(() => {
    const b = board.value
    if (!b) return null
    return {
      value: b.outbound.docsTotal ? b.kpis.outboundPct : null,
      sub: IS_REAL_DATA
        ? `今日建单中已过账 ${b.outbound.docsDone}/${b.outbound.docsTotal} 单`
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
        value: b.inbound.docsTotal ? String(b.kpis.inboundPct) : '—',
        unit: b.inbound.docsTotal ? '%' : '',
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
        value: b.wcs.readFailure ? '—' : String(b.kpis.wcsFailed),
        unit: '条',
        sub:
          b.wcs.readFailure ??
          (b.kpis.wcsFailed > 0
            ? `累计重试 ${b.wcs.failures.reduce((n, x) => n + x.retries, 0)} 次`
            : '暂无失败指令'),
        tone: b.wcs.readFailure ? 'neutral' : b.kpis.wcsFailed > 0 ? 'bad' : 'ok',
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
  const inbound = computed(() => domains?.receipts.data.value?.inbound ?? board.value?.inbound)
  const outbound = computed(() => domains?.shipments.data.value?.outbound ?? board.value?.outbound)
  const pick = computed(
    () => domains?.shipments.data.value?.pick ?? ops.value?.pick ?? board.value?.pick,
  )
  const putaway = computed(
    () => domains?.receipts.data.value?.putaway ?? ops.value?.putaway ?? board.value?.putaway,
  )
  const count = computed(() => domains?.counts.data.value ?? ops.value?.count ?? board.value?.count)
  const wcs = computed(() => domains?.wcs.data.value ?? ops.value?.wcs ?? board.value?.wcs)
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
    const groups: TaskGroupView[] = []
    if (p)
      groups.push({
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
      })
    if (pa)
      groups.push({
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
      })
    if (c)
      groups.push({
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
      })
    return groups
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

  return {
    ADAPTER_ICONS,
    backLink,
    bandCells,
    board,
    boardFreshness,
    count,
    demoOps,
    domains,
    error,
    factoryName,
    flowUnit,
    fmtAge,
    heroKpi,
    inbound,
    loading,
    nf,
    opsError,
    opsFreshness,
    outbound,
    overdueTop,
    refresh,
    refreshOps,
    taskGroups,
    taskSummary,
    wcs,
    wcsView,
  }
}
