// 排产组件文档页共享的样例数据(仅供 design-system demo)。
// 贴合 APS SchedulePlanContract 形状,经 toModel 归一化;缺口卡片字段(负责人/优先级/
// 齐套/维度归属等)补样例值以便展示。每次 makeModel() 返回全新模型,页面各持一份、互不影响。
import { toModel } from '@nerv-iip/scheduling'

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

/** 返回一份全新的、可拖拽编辑的示例排程模型。 */
export function makeModel() {
  const m = toModel(demoPlan as never)
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = WC[t.workCenterId ?? ''] ?? WC['激光切割-01']
    t.product = PRODUCT[t.orderId] ?? '通用件'
    t.quantity = 120
    t.dueUtc = iso(20)
    t.priority = t.orderId === 'WO-2026-003' ? 'high' : 'medium'
    t.kitting = 0.95
    t.load = 0.8
    t.colorKey = wc.color
    t.isRush = t.orderId === 'WO-2026-002'
    t.status = { label: t.locked ? '进行中' : '未开始', tone: t.locked ? 'info' : 'neutral' }
    t.dimensions = {
      workCenter: t.dimensions?.workCenter ?? { id: t.workCenterId ?? '', label: t.workCenterId ?? '' },
      device: { id: wc.device[0], label: wc.device[1] },
      team: { id: wc.team[0], label: wc.team[1] },
      line: { id: wc.line[0], label: wc.line[1] },
    }
  }
  m.groupDimensions = [
    { key: 'workCenter', label: '工作中心' },
    { key: 'device', label: '设备' },
    { key: 'team', label: '班组' },
    { key: 'line', label: '产线' },
  ]
  return m
}
