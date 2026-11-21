// 大屏 mock 的**产能口径单一真相源**：班制、产线节拍、车间交付口径。
// 所有屏的产量/计划/达成都从这里推，避免各屏各拍一个数字。
// 双班产能按约 1,600 件成品/班设置。同一件物料会经过多条产线，
// 全厂产量取末道包装线，车间产量取交给下游的数量。

import { LINES } from './masterdata'

/**
 * 产线当前工单号 —— 号段 `WO-2026-#####`。
 * 29 周约 3600 张工单、周日停产，当前进度落在 034xx 段；按产线下标偏移，
 * 保证「产线屏正在生产的工单」与「质量屏 NCR 挂的工单」是同一张（跨屏对得上）。
 */
export function woOf(lineId: string): string {
  const idx = Math.max(
    0,
    LINES.findIndex((l) => l.id === lineId),
  )
  return `WO-2026-${String(3421 + idx).padStart(5, '0')}`
}

/** 班次时长（分钟）为早班/中班各 480 min。 */
export const SHIFT_MINUTES = 480
/** 每日排产时长（分钟）：双班 960 min（08:00–24:00）。 */
export const PRODUCTION_MINUTES_PER_DAY = SHIFT_MINUTES * 2

export interface ShiftInfo {
  name: string
  range: string
  /** 当班已过分钟（0–480） */
  elapsedMin: number
  /** 当班剩余分钟（0 = 已收班） */
  remainingMin: number
  /** true = 当前不在排班时段（00:00–08:00），展示的是中班收盘数 */
  closed: boolean
}

/**
 * 当班（双班制）。00:00–08:00 无排班：大屏展示**已收班的中班**全量结果，
 * 而不是伪造一个「夜班」—— 宁沪减振没有夜班，凭空造一个当场被现场人员戳穿。
 */
export function shiftNow(now = new Date()): ShiftInfo {
  const minOfDay = now.getHours() * 60 + now.getMinutes()
  if (minOfDay >= 480 && minOfDay < 960) {
    const elapsed = minOfDay - 480
    return {
      name: '早班',
      range: '08:00–16:00',
      elapsedMin: elapsed,
      remainingMin: SHIFT_MINUTES - elapsed,
      closed: false,
    }
  }
  if (minOfDay >= 960) {
    const elapsed = minOfDay - 960
    return {
      name: '中班',
      range: '16:00–24:00',
      elapsedMin: elapsed,
      remainingMin: SHIFT_MINUTES - elapsed,
      closed: false,
    }
  }
  // 00:00–08:00：停产时段，冻结在中班收盘
  return {
    name: '中班（已收班）',
    range: '16:00–24:00',
    elapsedMin: SHIFT_MINUTES,
    remainingMin: 0,
    closed: true,
  }
}

/** 当日排产进度分数（08:00 起 0 → 24:00 封板 1）；00:00–08:00 视为昨日封板。 */
export function dayProgress(now = new Date()): number {
  const minOfDay = now.getHours() * 60 + now.getMinutes()
  if (minOfDay < 480) return 1
  return Math.min(1, (minOfDay - 480) / PRODUCTION_MINUTES_PER_DAY)
}

/** 产线工艺档案：标准节拍（s/件）+ 工序路线 + 在制产品。 */
export interface LineProfile {
  /** 标准节拍 s/件 */
  taktSec: number
  steps: string[]
  /** 在制产品（L0 SKU 名） */
  product: string
  /** 关键工序位：报警线为停摆工序、关注线为节拍瓶颈工序 */
  keyIdx: number
  /** 该线属于哪一段价值链（车间交付口径按它归并） */
  stage: 'machining' | 'assembly' | 'finishing'
  /** true = 该线产出计入所属车间的**对外交付**（用于避免同车间内串行重复计数） */
  delivers: boolean
}

/**
 * 14 条产线的节拍表。班产能 = 480×60 / taktSec，链条各段班产能对齐在 ≈1,600 件：
 *  · 活塞杆 800×2 = 1,600 / 缸筒 800×2 = 1,600 / 精磨 1,600（活塞杆全量精磨）
 *  · 总成 320×3（前减）+ 327×2（后减）= 1,614
 *  · 阀系预装 1,600（每台总成 1 套）/ 电泳 1,600 / 性能终检 1,600 / 包装 1,600
 * 上下游班产能一致 ⇒ 没有凭空冒出来的在制品，账能圆。
 */
export const LINE_PROFILES: Record<string, LineProfile> = {
  'LINE-WB-ROD-01': {
    taktSec: 36,
    steps: ['棒料下料', 'CNC 精车', '车端面滚花', '在线测径'],
    product: '活塞杆 φ22×420',
    keyIdx: 1,
    stage: 'machining',
    delivers: false, // 活塞杆需经精磨线才交付装配
  },
  'LINE-WB-ROD-02': {
    taktSec: 36,
    steps: ['棒料下料', 'CNC 精车', '车端面滚花', '在线测径'],
    product: '活塞杆 φ25×460',
    keyIdx: 1,
    stage: 'machining',
    delivers: false,
  },
  'LINE-WB-TUB-01': {
    taktSec: 36,
    steps: ['管料下料', 'CNC 镗孔', '环缝焊接', '在线检漏'],
    product: '缸筒 φ50×300',
    keyIdx: 1,
    stage: 'machining',
    delivers: true,
  },
  'LINE-WB-TUB-02': {
    taktSec: 36,
    steps: ['管料下料', 'CNC 镗孔', '环缝焊接', '在线检漏'],
    product: '缸筒 φ55×340',
    keyIdx: 1,
    stage: 'machining',
    delivers: true,
  },
  'LINE-WB-GRD-01': {
    taktSec: 18,
    steps: ['上料', '外圆精磨', '抛光', '尺寸终检'],
    product: '活塞杆精磨（φ22 / φ25 混流）',
    keyIdx: 1,
    stage: 'machining',
    delivers: true, // 机加车间交付装配的活塞杆从这里出
  },
  'LINE-WB-FA-01': {
    taktSec: 90,
    steps: ['缸筒上料', '阀系压入', '充油封口', '气密检测'],
    product: 'P1 平台前滑柱总成（左）',
    keyIdx: 1,
    stage: 'assembly',
    delivers: true,
  },
  'LINE-WB-FA-02': {
    taktSec: 90,
    steps: ['缸筒上料', '阀系压入', '充油封口', '气密检测'],
    product: 'P1 平台前滑柱总成（右）',
    keyIdx: 1,
    stage: 'assembly',
    delivers: true,
  },
  'LINE-WB-FA-03': {
    taktSec: 90,
    steps: ['缸筒上料', '阀系压入', '充油封口', '气密检测'],
    product: 'S1 平台前滑柱总成（左）',
    keyIdx: 1,
    stage: 'assembly',
    delivers: true,
  },
  'LINE-WB-RA-01': {
    taktSec: 88,
    steps: ['缸筒上料', '活塞杆压装', '充油封口', '气密检测'],
    product: 'P2 平台后减振器总成（左）',
    keyIdx: 1,
    stage: 'assembly',
    delivers: true,
  },
  'LINE-WB-RA-02': {
    taktSec: 88,
    steps: ['缸筒上料', '活塞杆压装', '充油封口', '气密检测'],
    product: 'S2 平台后减振器总成（右）',
    keyIdx: 1,
    stage: 'assembly',
    delivers: true,
  },
  'LINE-WB-VA-01': {
    taktSec: 18,
    steps: ['阀片叠装', '伺服压装', '扭矩确认'],
    product: '阀系组件 标准型',
    keyIdx: 1,
    stage: 'assembly',
    delivers: false, // 车间内部中间件，不重复计入交付
  },
  'LINE-WB-CT-01': {
    taktSec: 18,
    steps: ['前处理', '电泳', '固化', '膜厚抽检'],
    product: '总成电泳涂装（混流）',
    keyIdx: 1,
    stage: 'finishing',
    delivers: false,
  },
  'LINE-WB-TS-01': {
    taktSec: 18,
    steps: ['上台装夹', '阻尼力曲线采集', '异响判定', '合格标签打印'],
    product: '总成性能终检（混流）',
    keyIdx: 1,
    stage: 'finishing',
    delivers: false,
  },
  'LINE-WB-PK-01': {
    taktSec: 18,
    steps: ['配对码放', '装箱', '成品箱贴', '码垛入库'],
    product: '成品装箱（混流）',
    keyIdx: 1,
    stage: 'finishing',
    delivers: true, // 全厂「成品下线」口径就是这条线
  },
}

export const DEFAULT_LINE_PROFILE: LineProfile = {
  taktSec: 60,
  steps: ['上料', '加工', '检测'],
  product: '通用件',
  keyIdx: 1,
  stage: 'assembly',
  delivers: true,
}

export function lineProfileOf(lineId: string): LineProfile {
  return LINE_PROFILES[lineId] ?? DEFAULT_LINE_PROFILE
}

/** 末道车间：全厂「今日成品下线」取它的交付数（不是 Σ 车间）。 */
export const FINAL_WORKSHOP_ID = 'WS-03'

/** 按标准节拍反推的班计划产量（件）。 */
export function shiftPlanOf(lineId: string, elapsedMin = SHIFT_MINUTES): number {
  return Math.max(1, Math.floor((elapsedMin * 60) / lineProfileOf(lineId).taktSec))
}

// —— 班组（车间级，6 个班组 = 3 车间 × 早/中班）——
// 班组长使用固定的 mock 人员名称。
export interface TeamRef {
  code: string
  name: string
  workshopId: string
  shift: '早班' | '中班'
  leader: string
  /** 在册操作工数（19 名操作工按 6 组轮转：前 18 人每组 3 名，第 19 人补末组） */
  operators: number
}

export const TEAMS: TeamRef[] = [
  {
    code: 'TEAM-WB-MC-A',
    name: '机加车间早班组',
    workshopId: 'WS-01',
    shift: '早班',
    leader: '刘立新',
    operators: 3,
  },
  {
    code: 'TEAM-WB-MC-B',
    name: '机加车间中班组',
    workshopId: 'WS-01',
    shift: '中班',
    leader: '陈雪梅',
    operators: 3,
  },
  {
    code: 'TEAM-WB-AS-A',
    name: '装配车间早班组',
    workshopId: 'WS-02',
    shift: '早班',
    leader: '杨丽娟',
    operators: 3,
  },
  {
    code: 'TEAM-WB-AS-B',
    name: '装配车间中班组',
    workshopId: 'WS-02',
    shift: '中班',
    leader: '赵秀兰',
    operators: 3,
  },
  {
    code: 'TEAM-WB-SP-A',
    name: '表面与包装车间早班组',
    workshopId: 'WS-03',
    shift: '早班',
    leader: '黄浩然',
    operators: 3,
  },
  {
    code: 'TEAM-WB-SP-B',
    name: '表面与包装车间中班组',
    workshopId: 'WS-03',
    shift: '中班',
    leader: '周金花',
    operators: 4,
  },
]

/** 当班班组（00:00–08:00 停产时段沿用中班班组 —— 大屏展示的是它的收盘数）。 */
export function teamOf(workshopId: string, shiftName: string): TeamRef | undefined {
  const wanted = shiftName.startsWith('早') ? '早班' : '中班'
  return TEAMS.find((t) => t.workshopId === workshopId && t.shift === wanted)
}
