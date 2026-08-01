/**
 * KPI 卡片的趋势与迷你图数据。
 *
 * 两条来源，优先级固定：
 *
 * 1. **真实数据优先** —— 页面已经取到带时间戳的明细（应收/应付/订单/发货…）时，
 *    用 `seriesFromDatedItems` 按天分桶算出真实走势。
 * 2. **后端无历史读面时补形状** —— 后端只给"当前值"（MES 驾驶舱计数、库存可用量…）
 *    时，用 `shapeSeries` 由**当前真值**反推一条走势线。
 *
 * 补形状这条路有三条铁律，违反任何一条演示时都会被当场看穿：
 *
 * - **末点恒等于卡片当前值**。趋势线右端必须落在卡片上那个数字上，不允许近似。
 * - **确定性**。同一张卡在任何一次渲染/刷新都得到同一条线（key 做种子，
 *   不用 `Math.random`），否则同一页刷两次形状会变。
 * - **同比/环比由线本身算出**。`deltaFrom` 只吃已经画出来的 `series`，
 *   绝不另算一个数——卡片上的百分比与线的首尾永远对得上。
 *
 * 补的只有**形状**（走势方向与起伏），绝不新造"有小数点的业务事实"：
 * 卡片上的当前值、金额、良率一律来自后端真值。
 */

import type { NvMetricDelta } from '@nerv-iip/ui'

/** 指标语义——决定取整方式与差值口径。 */
export type KpiTrendKind = 'count' | 'amount' | 'rate'

export interface KpiTrend {
  /** 迷你图数据点，末点 === 卡片当前值。 */
  series: number[]
  /**
   * 逐点日期标签「MM-DD」，与 series 等长。
   *
   * **只有真实走势才有**。合成形状不给标签——形状是示意，日期是断言：
   * 一旦把编出来的点挂上「07-19」这种确切日期并允许悬停查询，它就从
   * 「最近在涨」变成了「07-19 的余额是 125 万」，而同一指标在明细页
   * 走真实数据，两页对同一天会给出矛盾数字，产品内部即可证伪。
   */
  seriesLabels?: string[]
  /** 该走势是否为合成形状（非真实明细算出）。调用方据此决定是否给查询入口。 */
  synthetic: boolean
  /** 变化幅度，由 series 首尾算出；点数不足或无法计算时为 undefined。 */
  delta?: NvMetricDelta
  /** 迷你图左下角：观察窗口，如「近 14 日」。 */
  footStart: string
  /** 迷你图右下角：与窗口起点的对比，如「较 14 日前 +8.2%」。 */
  footEnd: string
}

const DEFAULT_POINTS = 14

function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

/** FNV-1a：把卡片 key 变成稳定的 32 位种子，保证同一张卡走势恒定。 */
function hashKey(key: string): number {
  let hash = 2166136261
  for (let i = 0; i < key.length; i += 1) {
    hash ^= key.charCodeAt(i)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

/** mulberry32 —— 小而稳的确定性伪随机源。 */
function mulberry32(seed: number): () => number {
  let state = seed >>> 0
  return () => {
    state = (state + 0x6d2b79f5) >>> 0
    let t = Math.imul(state ^ (state >>> 15), 1 | state)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

/** 生成以 endDate 结尾、逐日回溯的「MM-DD」标签。 */
export function dailyLabels(points: number, endDate: Date = new Date()): string[] {
  const labels: string[] = []
  for (let i = points - 1; i >= 0; i -= 1) {
    const day = new Date(endDate.getTime())
    day.setDate(day.getDate() - i)
    labels.push(`${pad2(day.getMonth() + 1)}-${pad2(day.getDate())}`)
  }
  return labels
}

function quantize(value: number, kind: KpiTrendKind, max?: number): number {
  if (kind === 'count') return Math.max(0, Math.round(value))
  if (kind === 'rate') {
    const ceiling = max ?? 100
    return Math.min(ceiling, Math.max(0, Math.round(value * 10) / 10))
  }
  return Math.max(0, Math.round(value * 100) / 100)
}

function formatAbsolute(value: number, kind: KpiTrendKind): string {
  if (kind === 'count') return String(Math.round(value))
  if (kind === 'rate') return `${value.toFixed(1)}pt`
  return value.toFixed(2)
}

/**
 * 指标的好坏方向——决定 delta 配色，不影响箭头（箭头永远跟着数值走向）。
 *
 * - `higher-better`（默认）：产量、合格率、可用量涨了是好事。
 * - `lower-better`：逾期单、不良品、欠料涨了是坏事——箭头仍朝上，配色转 danger，
 *   免得"超期 +3"被涂成绿色。
 * - `neutral`：本身无好坏的余额类（应收未结、应付未结）。涨跌都只报方向不下judgment，
 *   给财务留判断空间。
 */
export type KpiPolarity = 'higher-better' | 'lower-better' | 'neutral'

export interface DeltaOptions {
  kind?: KpiTrendKind
  polarity?: KpiPolarity
}

/**
 * 由已经画出来的 series 首尾算变化幅度。
 *
 * **这是卡片上百分比的唯一来源**：任何调用方都不许自己算一个 delta 传进去，
 * 否则线与数字会各说各话。
 *
 * - `rate` 类用**百分点**（良率 95.1% → 96.4% 是 `+1.3pt`，不是 `+1.37%`）。
 * - 起点为 0 时相对变化无意义，退化成绝对增量。
 */
export function deltaFrom(
  series: readonly number[],
  options: DeltaOptions = {},
): NvMetricDelta | undefined {
  if (series.length < 2) return undefined
  const first = series[0]
  const last = series[series.length - 1]
  if (!Number.isFinite(first) || !Number.isFinite(last)) return undefined

  const kind = options.kind ?? 'count'
  let magnitude: number
  let text: string

  if (kind === 'rate') {
    magnitude = last - first
    text = `${magnitude > 0 ? '+' : ''}${magnitude.toFixed(1)}pt`
  } else if (first === 0) {
    magnitude = last - first
    if (magnitude === 0) return { value: '持平', direction: 'flat' }
    text = `${magnitude > 0 ? '+' : '-'}${formatAbsolute(Math.abs(magnitude), kind)}`
  } else {
    magnitude = ((last - first) / Math.abs(first)) * 100
    text = `${magnitude > 0 ? '+' : ''}${magnitude.toFixed(1)}%`
  }

  // 阈值取显示精度的一半：显示成 "+0.0%" 的变化读作持平，别配上箭头。
  const threshold = kind === 'rate' ? 0.05 : 0.05
  const direction = magnitude > threshold ? 'up' : magnitude < -threshold ? 'down' : 'flat'
  if (direction === 'flat') return { value: '持平', direction: 'flat' }

  const polarity = options.polarity ?? 'higher-better'
  const tone =
    polarity === 'neutral'
      ? ('neutral' as const)
      : polarity === 'lower-better'
        ? direction === 'up'
          ? ('danger' as const)
          : ('success' as const)
        : undefined

  return { value: text, direction, tone }
}

export interface ShapeSeriesOptions extends DeltaOptions {
  /** 数据点个数，默认 14（两周）。 */
  points?: number
  /**
   * 起点相对当前值的最大偏离比例，默认 0.18（即窗口内累计变化在 ±18% 内）。
   * `rate` 类指标建议调小（良率不会两周内跳 18%）。
   */
  swing?: number
  /** 逐点抖动幅度（相对当前值），默认 0.035——够看出起伏，不至于像噪声。 */
  wobble?: number
  /** `rate` 类的上限，默认 100。 */
  max?: number
  /** 窗口右端日期，默认今天；测试与时间旅行场景可显式传入。 */
  endDate?: Date
}

/**
 * 由**当前真值**反推一条确定性走势线（后端无历史读面时用）。
 *
 * `key` 必须在页面内唯一且稳定（建议 `'<域>.<指标>'`，如 `'mes.workOrders'`），
 * 它是种子——换了 key 形状就换，同一 key 永远同一形状。
 */
export function shapeSeries(
  key: string,
  current: number,
  options: ShapeSeriesOptions = {},
): number[] {
  const points = Math.max(2, options.points ?? DEFAULT_POINTS)
  if (!Number.isFinite(current)) return []
  // 当前值为 0 时任何"走势"都是编的：给一条平线，delta 会自然退化成持平。
  if (current === 0) return Array.from({ length: points }, () => 0)

  // 负值（可用量超发、金额红冲）：quantize 对 count/amount 一律 Math.max(0, …)，
  // 直接生成会把主干全夹到 0、末点再覆盖回负值，画出「前 13 天恒为 0，今天掉到 -5000」
  // 这种悬崖线——既难看，又凭空断言了「前 13 天都是 0」这个业务事实。
  // 按绝对值生成形状再整体取负，保住走势形态。
  if (current < 0) {
    return shapeSeries(key, -current, options).map((point) => -point)
  }

  const kind = options.kind ?? 'count'
  const swing = options.swing ?? 0.18
  const wobble = options.wobble ?? 0.035
  const random = mulberry32(hashKey(key))

  // 起点比例落在 [1-swing, 1+swing]：有涨有跌，由 key 决定，不随刷新变化。
  const startRatio = 1 - swing + random() * swing * 2
  const series: number[] = []

  for (let i = 0; i < points; i += 1) {
    const t = i / (points - 1)
    // smoothstep：主干不是死直线，两端平缓中段快，像真实经营曲线。
    const eased = t * t * (3 - 2 * t)
    const trunk = current * (startRatio + (1 - startRatio) * eased)
    const jitter = i === points - 1 ? 0 : (random() - 0.5) * 2 * wobble * current
    series.push(quantize(trunk + jitter, kind, options.max))
  }

  // 铁律：末点原样落在卡片当前值上（quantize 可能挪动它，这里直接覆盖）。
  series[points - 1] = current
  return series
}

export interface DatedSeriesOptions<T> {
  /** 取该行的时间戳（ISO 串）。 */
  date: (item: T) => string | null | undefined
  /** 取该行计入桶的数值；计数场景恒返回 1。 */
  value: (item: T) => number
  points?: number
  endDate?: Date
  /**
   * `cumulative`（默认）：桶内值累加到当天为止——存量口径（在途应收、库存）。
   * `perBucket`：只算当天发生额——流量口径（当日下单量、当日发货）。
   */
  mode?: 'cumulative' | 'perBucket'
}

/**
 * 由页面已取到的**真实明细**按天分桶算走势。
 *
 * 返回 undefined 表示明细里没有可用时间戳（调用方应退回 `shapeSeries` 或干脆不画）。
 */
export function seriesFromDatedItems<T>(
  items: readonly T[],
  options: DatedSeriesOptions<T>,
): number[] | undefined {
  const points = Math.max(2, options.points ?? DEFAULT_POINTS)
  const endDate = options.endDate ?? new Date()
  const mode = options.mode ?? 'cumulative'

  const end = new Date(endDate.getTime())
  end.setHours(23, 59, 59, 999)
  const start = new Date(end.getTime())
  start.setDate(start.getDate() - (points - 1))
  start.setHours(0, 0, 0, 0)
  const dayMs = 86_400_000

  const buckets = new Array<number>(points).fill(0)
  // 存量口径下，窗口之前发生的部分构成"期初余额"，不能丢。
  let opening = 0
  let sawTimestamp = false

  for (const item of items) {
    const raw = options.date(item)
    if (!raw) continue
    const time = new Date(raw).getTime()
    if (Number.isNaN(time)) continue
    sawTimestamp = true
    const amount = options.value(item)
    if (!Number.isFinite(amount)) continue

    if (time < start.getTime()) {
      if (mode === 'cumulative') opening += amount
      continue
    }
    if (time > end.getTime()) continue
    const index = Math.min(points - 1, Math.floor((time - start.getTime()) / dayMs))
    buckets[index] += amount
  }

  if (!sawTimestamp) return undefined

  if (mode === 'perBucket') return buckets
  let running = opening
  return buckets.map((value) => {
    running += value
    return Math.round(running * 100) / 100
  })
}

/**
 * 把一条**形状真实**的走势线整体缩放，使末点精确落在卡片当前值上。
 *
 * 用于真实明细只是卡片口径的一部分（分页、只取了 open 状态…）导致末点对不上时：
 * 保留真实起伏，放弃中间点的绝对精度。**末点对齐优先于中间点精度**——
 * 演示时被盯的是卡片数字与线右端是否重合。
 */
export function alignSeriesTo(series: readonly number[], current: number): number[] {
  if (series.length === 0 || !Number.isFinite(current)) return []
  const last = series[series.length - 1]
  const aligned =
    last === 0 || !Number.isFinite(last)
      ? series.map(() => current)
      : series.map((value) => Math.round(value * (current / last) * 100) / 100)
  aligned[aligned.length - 1] = current
  return aligned
}

/**
 * 真实走势够不够画。
 *
 * 业务读面普遍是服务端分页（ERP 列表 `take = 10`），只有当前页在内存里。
 * 十来行明细分到 14 个日桶，很可能全落在同一天——算出来是「0,0,…,0,总额」
 * 这种一根竖线，既难看又会让人以为系统坏了。**少于 3 个不同取值就不算走势**，
 * 退回补形状那条路，比硬画一条退化的"真实"线诚实。
 */
export function isUsableSeries(series: readonly number[] | undefined): series is number[] {
  if (!series || series.length < 2) return false
  return new Set(series).size >= 3
}

export interface BuildKpiTrendOptions extends ShapeSeriesOptions {
  /**
   * 由真实明细算出的走势；**通过 `isUsableSeries` 才会被采用**，
   * 退化时自动回落到补形状，调用方不用自己判。
   */
  realSeries?: number[]
}

/**
 * 组装一张卡所需的全部趋势字段（series / labels / delta / 页脚）。
 *
 * 传了可用的 `realSeries` 走真实数据（并对齐末点），否则由 `key` + `current` 补形状。
 */
export function buildKpiTrend(
  key: string,
  current: number | null | undefined,
  options: BuildKpiTrendOptions = {},
): KpiTrend | undefined {
  const value = typeof current === 'number' && Number.isFinite(current) ? current : undefined
  if (value === undefined) return undefined

  const points = Math.max(2, options.points ?? DEFAULT_POINTS)
  // 局部变量而非 options.realSeries：类型守卫的窄化只对被判定的那个绑定生效
  const real = options.realSeries
  const usable = isUsableSeries(real)
  const series = usable
    ? alignSeriesTo(real, value)
    : shapeSeries(key, value, { ...options, points })
  if (series.length < 2) return undefined

  const delta = deltaFrom(series, { kind: options.kind, polarity: options.polarity })
  return {
    series,
    // 合成形状不给日期标签，调用方据此关掉悬停查询（见 KpiTrend.seriesLabels 注释）
    seriesLabels: usable ? dailyLabels(series.length, options.endDate) : undefined,
    synthetic: !usable,
    delta,
    footStart: `近 ${points} 日`,
    footEnd: delta ? `较 ${points} 日前 ${delta.value}` : '',
  }
}
