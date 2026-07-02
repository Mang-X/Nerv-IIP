// 排产组件文档页共享的样例数据(仅供 design-system demo)。
// 贴合 APS SchedulePlanContract 形状,经 toModel 归一化;缺口卡片字段(负责人/优先级/
// 齐套/维度归属等)补样例值以便展示。每次 makeModel() 返回全新模型,页面各持一份、互不影响。
//
// toModel 只映射契约真实字段(时间/资源/依赖/冲突/未排…),不涉及 plannedStart/End、
// isMilestone/milestoneLabel、blockKind、kitting/load/changeoverMin —— 这些在 makeModel()
// 里按 ScheduleTask 模型(见 packages/scheduling/src/model/types.ts)后置补齐,以便一处数据
// 就把图例讲的能力(计划 vs 实际基线 / 里程碑 / 资源时间块 / 齐套分级 / 换型 / 瓶颈)全演出来。
import { toModel } from '@nerv-iip/scheduling'
import type { ScheduleModel, ScheduleTask } from '@nerv-iip/scheduling'

const H = 3_600_000
const base = Date.parse('2026-06-10T08:00:00.000Z')
const iso = (h: number) => new Date(base + h * H).toISOString()

const demoPlan = {
  planId: 'APS-2026-0610',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: iso(0),
  assignments: [
    { assignmentId: 'WO1-10', orderId: 'WO-2026-001', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: iso(0), endUtc: iso(3), isLocked: false },
    { assignmentId: 'WO1-20', orderId: 'WO-2026-001', operationId: '折弯', operationSequence: 20, resourceId: '折弯-02', workCenterId: '折弯-02', startUtc: iso(3), endUtc: iso(6), isLocked: false },
    { assignmentId: 'WO1-30', orderId: 'WO-2026-001', operationId: '焊接', operationSequence: 30, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: iso(6), endUtc: iso(11), isLocked: true },
    { assignmentId: 'WO2-10', orderId: 'WO-2026-002', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: iso(3), endUtc: iso(7), isLocked: false },
    { assignmentId: 'WO2-20', orderId: 'WO-2026-002', operationId: '机加工', operationSequence: 20, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: iso(7), endUtc: iso(13), isLocked: false },
    { assignmentId: 'WO3-10', orderId: 'WO-2026-003', operationId: '装配', operationSequence: 10, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: iso(11), endUtc: iso(16), isLocked: false },
    { assignmentId: 'WO3-20', orderId: 'WO-2026-003', operationId: '总装', operationSequence: 20, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: iso(13), endUtc: iso(20), isLocked: false },
  ],
  resourceLoads: [
    { resourceId: '激光切割-01', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 420, availableMinutes: 480, utilization: 0.88 },
    { resourceId: '折弯-02', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 180, availableMinutes: 480, utilization: 0.38 },
    { resourceId: '焊接-01', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 600, availableMinutes: 480, utilization: 1.25 },
    { resourceId: '加工中心-03', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 540, availableMinutes: 480, utilization: 1.13 },
  ],
  conflicts: [
    { conflictId: 'cf1', reasonCode: 'capacity', severity: 'warning', orderId: 'WO-2026-003', operationId: '装配', resourceId: '焊接-01', message: '焊接-01 在该时段超出可用产能' },
    { conflictId: 'cf2', reasonCode: 'dueDate', severity: 'error', orderId: 'WO-2026-003', operationId: '总装', resourceId: '加工中心-03', message: '预计完工晚于交期' },
  ],
  unscheduledOperations: [
    { orderId: 'WO-2026-004', operationId: '喷涂', reasonCode: 'material', message: '面漆未齐套,等待采购到货' },
  ],
  changeSummary: [],
  ganttItems: [],
}

const WC: Record<string, { color: string; device: [string, string]; team: [string, string]; line: [string, string] }> = {
  '激光切割-01': { color: 'cut', device: ['DEV-L1', '激光切割机 L1'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金线'] },
  '折弯-02': { color: 'bend', device: ['DEV-B2', '数控折弯机 B2'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金线'] },
  '焊接-01': { color: 'weld', device: ['DEV-W1', '焊接机器人 W1'], team: ['T-B', '乙班'], line: ['LN-WELD', '焊装线'] },
  '加工中心-03': { color: 'mach', device: ['DEV-C3', '数控机床 M3'], team: ['T-B', '乙班'], line: ['LN-MACH', '机加线'] },
}
const PRODUCT: Record<string, string> = { 'WO-2026-001': '前减振器总成', 'WO-2026-002': '后桥壳体', 'WO-2026-003': '转向节' }

// 计划基线(plannedStart/End)与实际错开:让甘特画出「计划 vs 实际」双层条。
// key = assignmentId(即 ScheduleTask.id)。WO-2026-003 两道工序实际较计划晚 3h(资源过载连带顺延)。
const PLANNED: Record<string, [number, number]> = {
  'WO1-30': [6, 10], // 焊接:计划 4h,实际 5h(略超,进度偏差)
  'WO3-10': [8, 13], // 装配:计划 08:00 起,实际晚 3h 起(前序占用焊接-01)
  'WO3-20': [10, 17], // 总装:计划 10:00 起,实际晚 3h 起(顺延至超交期)
}
// 齐套 / 换型 / 载荷分级:让图例每格都能在图上找到对应。key = assignmentId。
const KITTING: Record<string, number> = { 'WO1-10': 1, 'WO3-10': 0.6 } // 足(绿) / 危(红)
const CHANGEOVER: Record<string, number> = { 'WO2-20': 45 } // 换型 chip 出现
const LOAD: Record<string, number> = { 'WO3-10': 1.25 } // 过载瓶颈(焊接-01)

/** 返回一份全新的、可拖拽编辑的示例排程模型。 */
export function makeModel(): ScheduleModel {
  const m = toModel(demoPlan as never)
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = WC[t.workCenterId ?? ''] ?? WC['激光切割-01']
    t.product = PRODUCT[t.orderId] ?? '通用件'
    t.quantity = 120
    t.dueUtc = iso(20)
    t.priority = t.orderId === 'WO-2026-003' ? 'high' : 'medium'
    // 齐套/载荷/换型:默认中性值,再按 key 覆盖出分级差异。
    t.kitting = KITTING[t.id] ?? 0.85
    t.load = LOAD[t.id] ?? 0.8
    if (CHANGEOVER[t.id] != null) t.changeoverMin = CHANGEOVER[t.id]
    t.colorKey = wc.color
    t.isRush = t.orderId === 'WO-2026-002'
    t.status = { label: t.locked ? '进行中' : '未开始', tone: t.locked ? 'info' : 'neutral' }
    // 计划基线:仅部分工序设,演示「计划 vs 实际」偏差。
    const planned = PLANNED[t.id]
    if (planned) {
      t.plannedStartUtc = iso(planned[0])
      t.plannedEndUtc = iso(planned[1])
    }
    t.dimensions = {
      workCenter: t.dimensions?.workCenter ?? { id: t.workCenterId ?? '', label: t.workCenterId ?? '' },
      device: { id: wc.device[0], label: wc.device[1] },
      team: { id: wc.team[0], label: wc.team[1] },
      line: { id: wc.line[0], label: wc.line[1] },
    }
  }

  // 阶段里程碑:贴在 WO-2026-001 焊接条尾的菱形 + 标签(不独占一行)。
  const weld = m.tasks.find((t) => t.id === 'WO1-30')
  if (weld) weld.milestoneLabel = '冲焊完成'

  // 独立里程碑节点(type 用模型允许的 'operation' + isMilestone,渲染为菱形、无时长)。
  // 挂到 WO-2026-001 分组下,与其工序同组显示。
  const milestone: ScheduleTask = {
    id: 'WO1-MS',
    orderId: 'WO-2026-001',
    operationId: '',
    operationSequence: 40,
    parentId: 'order:WO-2026-001',
    type: 'operation',
    text: '冲焊下线',
    startUtc: iso(11),
    endUtc: iso(11),
    isMilestone: true,
    colorKey: 'weld',
    locked: false,
    hasConflict: false,
    conflictReason: null,
  }

  // 资源时间块(非工单):渲染为斜纹块、不可拖拽,仅进资源排产板对应泳道。
  // 用 dimensions 让其在各维度都落到正确泳道;blockKind 决定斜纹配色与详情文案。
  const blockDims = (rid: string): ScheduleTask['dimensions'] => {
    const wc = WC[rid]
    return wc
      ? {
          workCenter: { id: rid, label: rid },
          device: { id: wc.device[0], label: wc.device[1] },
          team: { id: wc.team[0], label: wc.team[1] },
          line: { id: wc.line[0], label: wc.line[1] },
        }
      : { workCenter: { id: rid, label: rid } }
  }
  const maintenance: ScheduleTask = {
    id: 'BLK-MNT-1',
    orderId: '',
    operationId: '',
    operationSequence: 0,
    type: 'operation',
    text: '定期保养',
    resourceId: '折弯-02',
    workCenterId: '折弯-02',
    dimensions: blockDims('折弯-02'),
    startUtc: iso(1),
    endUtc: iso(4),
    blockKind: 'maintenance',
    locked: true,
    hasConflict: false,
    conflictReason: null,
  }
  const changeover: ScheduleTask = {
    id: 'BLK-CO-1',
    orderId: '',
    operationId: '',
    operationSequence: 0,
    type: 'operation',
    text: '产品换型',
    resourceId: '加工中心-03',
    workCenterId: '加工中心-03',
    dimensions: blockDims('加工中心-03'),
    startUtc: iso(13),
    endUtc: iso(14),
    blockKind: 'changeover',
    locked: true,
    hasConflict: false,
    conflictReason: null,
  }
  const downtime: ScheduleTask = {
    id: 'BLK-DT-1', orderId: '', operationId: '', operationSequence: 0, type: 'operation',
    text: '计划停机', resourceId: '焊接-01', workCenterId: '焊接-01', dimensions: blockDims('焊接-01'),
    startUtc: iso(16), endUtc: iso(19), blockKind: 'downtime', locked: true, hasConflict: false, conflictReason: null,
  }
  const lineChange: ScheduleTask = {
    id: 'BLK-LC-1', orderId: '', operationId: '', operationSequence: 0, type: 'operation',
    text: '换线窗口', resourceId: '激光切割-01', workCenterId: '激光切割-01', dimensions: blockDims('激光切割-01'),
    startUtc: iso(7), endUtc: iso(9), blockKind: 'lineChange', locked: true, hasConflict: false, conflictReason: null,
  }

  // 四类资源时间块全出,演示完整底纹:维护(灰)/换型(橙)/停机(红)/换线(蓝)。
  m.tasks.push(milestone, maintenance, changeover, downtime, lineChange)
  return m
}

// ── 资源时间块(底纹)专用演示 ──────────────────────────────────────────
// makeModel() 的四类块散落在 20h horizon 的不同时段,默认视口一屏看不全。这里造一份紧凑模型:
// 4 条工序分落 4 个资源泳道,并在同一个较窄时间窗(08:00–14:00,共 6h)内给每条泳道各放一类块,
// 让四类斜纹底纹(维护/停机/换线/换型)在 scale="hour" 初始视口一屏内全部可见。
// 用与 makeModel() 一致的 assignment→toModel 路径,再后置补 blockKind / dimensions / 卡片字段。
const blkBase = Date.parse('2026-06-10T08:00:00.000Z')
const blkIso = (h: number) => new Date(blkBase + h * H).toISOString()

const blockDemoPlan = {
  planId: 'APS-2026-0610-BLK',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: blkIso(0),
  // 每条泳道一条工序,占窗口前半(0–3h),把后半(3–5.5h)让给资源时间块,块与工序不重叠、都在视口内。
  assignments: [
    { assignmentId: 'BD-1', orderId: 'WO-2026-051', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: blkIso(0), endUtc: blkIso(2), isLocked: false },
    { assignmentId: 'BD-2', orderId: 'WO-2026-052', operationId: '折弯', operationSequence: 10, resourceId: '折弯-02', workCenterId: '折弯-02', startUtc: blkIso(0), endUtc: blkIso(2.5), isLocked: false },
    { assignmentId: 'BD-3', orderId: 'WO-2026-053', operationId: '焊接', operationSequence: 10, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: blkIso(0), endUtc: blkIso(3), isLocked: false },
    { assignmentId: 'BD-4', orderId: 'WO-2026-054', operationId: '机加工', operationSequence: 10, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: blkIso(0), endUtc: blkIso(2), isLocked: false },
  ],
  resourceLoads: [
    { resourceId: '激光切割-01', windowStartUtc: blkIso(0), windowEndUtc: blkIso(6), assignedMinutes: 120, availableMinutes: 360, utilization: 0.33 },
    { resourceId: '折弯-02', windowStartUtc: blkIso(0), windowEndUtc: blkIso(6), assignedMinutes: 150, availableMinutes: 360, utilization: 0.42 },
    { resourceId: '焊接-01', windowStartUtc: blkIso(0), windowEndUtc: blkIso(6), assignedMinutes: 180, availableMinutes: 360, utilization: 0.5 },
    { resourceId: '加工中心-03', windowStartUtc: blkIso(0), windowEndUtc: blkIso(6), assignedMinutes: 120, availableMinutes: 360, utilization: 0.33 },
  ],
  conflicts: [],
  unscheduledOperations: [],
  changeSummary: [],
  ganttItems: [],
}

const BLK_PRODUCT: Record<string, string> = {
  'WO-2026-051': '横梁支架',
  'WO-2026-052': '悬架臂',
  'WO-2026-053': '车架总成',
  'WO-2026-054': '齿轮箱体',
}

/**
 * 资源时间块(底纹)专用示例:4 条工序分落 4 个资源泳道,并在同一个较窄时间窗(6h)内给每条泳道各
 * 放一类块——维护(灰,折弯-02)/ 停机(红,焊接-01)/ 换线(蓝,激光切割-01)/ 换型(橙,加工中心-03),
 * 让四类斜纹底纹在 scale="hour" 初始视口一屏内全部可见。返回全新 ScheduleModel。
 */
export function makeBlockDemoModel(): ScheduleModel {
  const m = toModel(blockDemoPlan as never)
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = WC[t.workCenterId ?? ''] ?? WC['激光切割-01']
    t.product = BLK_PRODUCT[t.orderId] ?? '通用件'
    t.quantity = 80
    t.colorKey = wc.color
    t.dimensions = {
      workCenter: t.dimensions?.workCenter ?? { id: t.workCenterId ?? '', label: t.workCenterId ?? '' },
      device: { id: wc.device[0], label: wc.device[1] },
      team: { id: wc.team[0], label: wc.team[1] },
      line: { id: wc.line[0], label: wc.line[1] },
    }
  }

  // 四类资源时间块,各落一条泳道、都在窗口后半(3–5.5h),与工序不重叠、同屏可见。
  const blockDims = (rid: string): ScheduleTask['dimensions'] => {
    const wc = WC[rid]
    return wc
      ? {
          workCenter: { id: rid, label: rid },
          device: { id: wc.device[0], label: wc.device[1] },
          team: { id: wc.team[0], label: wc.team[1] },
          line: { id: wc.line[0], label: wc.line[1] },
        }
      : { workCenter: { id: rid, label: rid } }
  }
  const mkBlock = (
    id: string,
    text: string,
    rid: string,
    blockKind: NonNullable<ScheduleTask['blockKind']>,
    start: number,
    end: number,
  ): ScheduleTask => ({
    id,
    orderId: '',
    operationId: '',
    operationSequence: 0,
    type: 'operation',
    text,
    resourceId: rid,
    workCenterId: rid,
    dimensions: blockDims(rid),
    startUtc: blkIso(start),
    endUtc: blkIso(end),
    blockKind,
    locked: true,
    hasConflict: false,
    conflictReason: null,
  })

  m.tasks.push(
    mkBlock('BD-BLK-LC', '换线窗口', '激光切割-01', 'lineChange', 3, 5),
    mkBlock('BD-BLK-MNT', '定期保养', '折弯-02', 'maintenance', 3, 5),
    mkBlock('BD-BLK-DT', '计划停机', '焊接-01', 'downtime', 3.5, 5.5),
    mkBlock('BD-BLK-CO', '产品换型', '加工中心-03', 'changeover', 3, 5),
  )
  // horizon 由 toModel 仅按工序(0–3h)派生,但块延到 5.5h;显式把窗口拉到 0–6h,
  // 让声明范围完整覆盖工序 + 四类块,scale="hour" 初始视口一屏内全部可见。
  m.horizon = { startUtc: blkIso(0), endUtc: blkIso(6) }
  return m
}

// ── 工作日历 / 班次演示 ────────────────────────────────────────────────
// 引擎按「本地时间」着色:周末(周六/周日)= 周末底纹;每天 08:00 前或 20:00 后 = 非工作/夜班底纹;
// 资源板小时刻度还会插入三班制刻度(夜 00–08 / 早 08–16 / 中 16–24)。这里造一份跨周末、跨昼夜的
// horizon(周五午后 → 周一早间),放几条工序,让读者能亲眼指认「哪块是周末 / 夜班 / 非工作」。
// 2026-06-12 = 周五,06-13 周六,06-14 周日,06-15 周一。基点取周五 12:00(本地)。
const calBase = new Date(2026, 5, 12, 12, 0, 0) // 月份 0-based:5 = 6 月
const calIso = (h: number) => new Date(calBase.getTime() + h * H).toISOString()

const calPlan = {
  planId: 'APS-2026-0612-CAL',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: calIso(0),
  assignments: [
    // 周五午后早班尾:一条正常工时内的工序。
    { assignmentId: 'CAL-1', orderId: 'WO-2026-011', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: calIso(1), endUtc: calIso(4), isLocked: false },
    // 周五夜班:跨入 20:00 后的非工作/夜班底纹区(赶工)。
    { assignmentId: 'CAL-2', orderId: 'WO-2026-011', operationId: '焊接', operationSequence: 20, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: calIso(7), endUtc: calIso(11), isLocked: false },
    // 周六:整条落在周末底纹上(周末加班)。
    { assignmentId: 'CAL-3', orderId: 'WO-2026-012', operationId: '机加工', operationSequence: 10, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: calIso(26), endUtc: calIso(31), isLocked: false },
    // 周一早班:回到正常工时。
    { assignmentId: 'CAL-4', orderId: 'WO-2026-012', operationId: '折弯', operationSequence: 20, resourceId: '折弯-02', workCenterId: '折弯-02', startUtc: calIso(69), endUtc: calIso(73), isLocked: false },
  ],
  resourceLoads: [
    { resourceId: '激光切割-01', windowStartUtc: calIso(0), windowEndUtc: calIso(76), assignedMinutes: 180, availableMinutes: 480, utilization: 0.38 },
    { resourceId: '焊接-01', windowStartUtc: calIso(0), windowEndUtc: calIso(76), assignedMinutes: 240, availableMinutes: 480, utilization: 0.5 },
    { resourceId: '折弯-02', windowStartUtc: calIso(0), windowEndUtc: calIso(76), assignedMinutes: 240, availableMinutes: 480, utilization: 0.5 },
    { resourceId: '加工中心-03', windowStartUtc: calIso(0), windowEndUtc: calIso(76), assignedMinutes: 300, availableMinutes: 480, utilization: 0.63 },
  ],
  conflicts: [],
  unscheduledOperations: [],
  changeSummary: [],
  ganttItems: [],
}

const CAL_PRODUCT: Record<string, string> = { 'WO-2026-011': '横梁支架', 'WO-2026-012': '悬架臂' }

/**
 * 跨周末、跨昼夜的示例排程模型,用于演示引擎自动渲染的日历要素:
 * 周末底纹、非工作/夜班底纹、三班制刻度、「现在」标线。horizon 覆盖周五→周一含夜间。
 * 与 makeModel() 同风格,返回全新 ScheduleModel。
 */
export function makeCalendarModel(): ScheduleModel {
  const m = toModel(calPlan as never)
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = WC[t.workCenterId ?? ''] ?? WC['激光切割-01']
    t.product = CAL_PRODUCT[t.orderId] ?? '通用件'
    t.quantity = 80
    t.colorKey = wc.color
    t.dimensions = {
      workCenter: t.dimensions?.workCenter ?? { id: t.workCenterId ?? '', label: t.workCenterId ?? '' },
      device: { id: wc.device[0], label: wc.device[1] },
      team: { id: wc.team[0], label: wc.team[1] },
      line: { id: wc.line[0], label: wc.line[1] },
    }
  }
  return m
}
