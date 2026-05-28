import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesProductionPlanRow,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'

const localPlanStorageKey = 'nerv-iip.business-console.demo.production-plans'
const localWorkOrderStorageKey = 'nerv-iip.business-console.demo.work-orders'

export const demoSkus: BusinessConsoleResourceItem[] = [
  { resourceType: 'sku', code: 'FG-SAD-FRT-001', displayName: '前减振器总成 左/右通用', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'FG-SAD-RR-001', displayName: '后减振器总成', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'SA-PISTON-32', displayName: '活塞杆 32mm 镀铬', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'SA-TUBE-45', displayName: '外筒 45mm 喷涂件', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'SA-VALVE-A', displayName: '阀系组件 A 型', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'SA-OIL-HV32', displayName: '减振器油 HV32', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'SA-SEAL-KIT-A', displayName: '油封组件 A 型', active: true, snapshotVersion: '2026.05' },
  { resourceType: 'sku', code: 'PK-SAD-CARTON', displayName: '减振器总成包装箱', active: true, snapshotVersion: '2026.05' },
]

export const demoResourceGroups: { key: string; title: string; rows: BusinessConsoleResourceItem[] }[] = [
  {
    key: 'site',
    title: '工厂',
    rows: [
      { resourceType: 'site', code: 'PLANT-NB', displayName: '宁波减振器工厂', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'site', code: 'PLANT-CQ', displayName: '重庆售后备件工厂', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'production-line',
    title: '产线',
    rows: [
      { resourceType: 'production-line', code: 'LINE-FRT-A', displayName: '前减 A 线', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'production-line', code: 'LINE-RR-B', displayName: '后减 B 线', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'production-line', code: 'LINE-SPARE', displayName: '售后备件柔性线', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'work-center',
    title: '工作中心',
    rows: [
      { resourceType: 'work-center', code: 'WC-TUBE-WELD', displayName: '筒体焊接工作中心', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'work-center', code: 'WC-OIL-FILL', displayName: '注油封口工作中心', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'work-center', code: 'WC-DAMP-TEST', displayName: '阻尼测试工作中心', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'work-center', code: 'WC-PACK', displayName: '包装入库工作中心', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'shift',
    title: '班次',
    rows: [
      { resourceType: 'shift', code: 'SHIFT-DAY', displayName: '白班 08:00-20:00', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'shift', code: 'SHIFT-NIGHT', displayName: '夜班 20:00-08:00', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'device-asset',
    title: '设备',
    rows: [
      { resourceType: 'device-asset', code: 'EQ-WELD-01', displayName: '环焊机 01', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'device-asset', code: 'EQ-ROD-ASM-01', displayName: '活塞杆装配台 01', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'device-asset', code: 'EQ-FILL-02', displayName: '真空注油机 02', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'device-asset', code: 'EQ-TEST-01', displayName: '阻尼力试验台 01', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'work-calendar',
    title: '工作日历',
    rows: [
      { resourceType: 'work-calendar', code: 'CAL-SAD-STD', displayName: '减振器标准双班日历', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'team',
    title: '班组',
    rows: [
      { resourceType: 'team', code: 'TEAM-FRT-DAY', displayName: '前减 A 线白班组', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'team', code: 'TEAM-RR-NIGHT', displayName: '后减 B 线夜班组', active: true, snapshotVersion: '2026.05' },
    ],
  },
  {
    key: 'personnel-skill',
    title: '人员技能',
    rows: [
      { resourceType: 'personnel-skill', code: 'user-ops-01:WELD', displayName: '焊接操作 L2', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'personnel-skill', code: 'user-ops-02:FILL_SEAL', displayName: '注油封口 L2', active: true, snapshotVersion: '2026.05' },
      { resourceType: 'personnel-skill', code: 'user-quality-01:DAMP_TEST', displayName: '阻尼测试判定 L3', active: true, snapshotVersion: '2026.05' },
    ],
  },
]

export const demoPartners: (BusinessConsoleResourceItem & { role: 'customer' | 'supplier' })[] = [
  { resourceType: 'business-partner', code: 'CUST-GAC', displayName: '广汽乘用车', active: true, snapshotVersion: '2026.05', role: 'customer' },
  { resourceType: 'business-partner', code: 'CUST-BYD-CHASSIS', displayName: '比亚迪底盘事业部', active: true, snapshotVersion: '2026.05', role: 'customer' },
  { resourceType: 'business-partner', code: 'SUP-PISTON-HD', displayName: '华东活塞杆', active: true, snapshotVersion: '2026.05', role: 'supplier' },
  { resourceType: 'business-partner', code: 'SUP-SEAL-NB', displayName: '宁波密封件', active: true, snapshotVersion: '2026.05', role: 'supplier' },
  { resourceType: 'business-partner', code: 'SUP-OIL-CC', displayName: '长城润滑油', active: true, snapshotVersion: '2026.05', role: 'supplier' },
  { resourceType: 'business-partner', code: 'SUP-PACK-HZ', displayName: '杭州包装材料', active: true, snapshotVersion: '2026.05', role: 'supplier' },
]

export function demoResourcesOf(resourceType: string) {
  return demoResourceGroups.find((group) => group.key === resourceType)?.rows ?? []
}

export const demoProductionFacts = [
  { code: 'PV-FRT-2026-A', name: '前减总成量产版本 A', bom: 'MBOM-FRT-001', routing: 'RT-FRT-A', status: '已发布' },
  { code: 'PV-RR-2026-B', name: '后减总成量产版本 B', bom: 'MBOM-RR-001', routing: 'RT-RR-B', status: '已发布' },
  { code: 'RT-FRT-A', name: '前减焊接-注油-阻尼测试-包装', bom: '4 道工序', routing: 'WC-TUBE-WELD → WC-OIL-FILL → WC-DAMP-TEST → WC-PACK', status: '生效中' },
  { code: 'RT-RR-B', name: '后减装配-注油-阻尼测试-包装', bom: '4 道工序', routing: 'LINE-RR-B 标准节拍', status: '生效中' },
]

export const demoProductionPlans: BusinessConsoleMesProductionPlanRow[] = [
  {
    productionPlanId: 'PLAN-SO-20260527-001',
    sourceSystem: 'sales-order',
    sourceDocumentId: 'SO-GAC-260527-01',
    skuId: 'FG-SAD-FRT-001',
    plannedQuantity: 1200,
    readinessStatus: 'Ready',
    plannedStartUtc: '2026-05-27T08:00:00Z',
    plannedEndUtc: '2026-05-28T20:00:00Z',
  },
  {
    productionPlanId: 'PLAN-STOCK-20260527-002',
    sourceSystem: 'stock-build',
    sourceDocumentId: 'MPS-FRT-W22',
    skuId: 'FG-SAD-RR-001',
    plannedQuantity: 800,
    readinessStatus: 'Warning',
    blockingReasons: ['夜班人员技能确认未完成'],
    plannedStartUtc: '2026-05-28T08:00:00Z',
    plannedEndUtc: '2026-05-29T20:00:00Z',
  },
  {
    productionPlanId: 'PLAN-SAFETY-20260527-003',
    sourceSystem: 'safety-stock',
    sourceDocumentId: 'INV-REPL-FRT-01',
    skuId: 'SA-SEAL-KIT-A',
    plannedQuantity: 3000,
    readinessStatus: 'Blocked',
    blockingReasons: ['油封组件来料检验未放行'],
    plannedStartUtc: '2026-05-29T08:00:00Z',
    plannedEndUtc: '2026-05-30T20:00:00Z',
  },
  {
    productionPlanId: 'PLAN-FCST-20260527-004',
    sourceSystem: 'forecast',
    sourceDocumentId: 'FCST-OEM-202606',
    skuId: 'FG-SAD-FRT-001',
    plannedQuantity: 1500,
    readinessStatus: 'Ready',
    plannedStartUtc: '2026-05-30T08:00:00Z',
    plannedEndUtc: '2026-05-31T20:00:00Z',
  },
]

export const demoWorkOrders: BusinessConsoleMesWorkOrderItem[] = [
  {
    workOrderId: 'WO-SO-260527-001',
    skuId: 'FG-SAD-FRT-001',
    productionVersionId: 'PV-FRT-2026-A',
    quantity: 1200,
    priority: 3,
    dueUtc: '2026-05-28T20:00:00Z',
    status: 'Released',
    operationTasks: [
      { operationTaskId: 'OP-WO001-10', status: 'Ready', operationSequence: 10, workCenterId: 'WC-TUBE-WELD', earliestStartUtc: '2026-05-27T08:00:00Z' },
      { operationTaskId: 'OP-WO001-20', status: 'Queued', operationSequence: 20, workCenterId: 'WC-OIL-FILL', earliestStartUtc: '2026-05-27T14:00:00Z' },
    ],
  },
  {
    workOrderId: 'WO-STOCK-260527-002',
    skuId: 'FG-SAD-RR-001',
    productionVersionId: 'PV-RR-2026-B',
    quantity: 800,
    priority: 5,
    dueUtc: '2026-05-29T20:00:00Z',
    status: 'Ready',
    operationTasks: [
      { operationTaskId: 'OP-WO002-10', status: 'Ready', operationSequence: 10, workCenterId: 'WC-OIL-FILL', earliestStartUtc: '2026-05-28T08:00:00Z' },
    ],
  },
  {
    workOrderId: 'WO-RUSH-260527-003',
    skuId: 'FG-SAD-FRT-001',
    productionVersionId: 'PV-FRT-2026-A',
    quantity: 120,
    priority: 1,
    dueUtc: '2026-05-27T20:00:00Z',
    status: 'Blocked',
    operationTasks: [
      { operationTaskId: 'OP-WO003-10', status: 'Blocked', operationSequence: 10, workCenterId: 'WC-DAMP-TEST', earliestStartUtc: '2026-05-27T12:00:00Z' },
    ],
  },
]

export const demoOperationTasks: BusinessConsoleMesOperationTaskRow[] = [
  { operationTaskId: 'OP-WO001-10', workOrderId: 'WO-SO-260527-001', status: 'Ready', operationSequence: 10, workCenterId: 'WC-TUBE-WELD', deviceAssetId: 'EQ-WELD-01', shiftId: 'SHIFT-DAY', plannedStartUtc: '2026-05-27T08:00:00Z', qualityStatus: '待首检' },
  { operationTaskId: 'OP-WO001-20', workOrderId: 'WO-SO-260527-001', status: 'Queued', operationSequence: 20, workCenterId: 'WC-OIL-FILL', deviceAssetId: 'EQ-FILL-02', shiftId: 'SHIFT-DAY', plannedStartUtc: '2026-05-27T14:00:00Z', qualityStatus: '未检' },
  { operationTaskId: 'OP-WO002-10', workOrderId: 'WO-STOCK-260527-002', status: 'Running', operationSequence: 10, workCenterId: 'WC-OIL-FILL', deviceAssetId: 'EQ-FILL-02', shiftId: 'SHIFT-NIGHT', plannedStartUtc: '2026-05-28T20:00:00Z', qualityStatus: '首检合格' },
  { operationTaskId: 'OP-WO003-10', workOrderId: 'WO-RUSH-260527-003', status: 'Blocked', operationSequence: 10, workCenterId: 'WC-DAMP-TEST', deviceAssetId: 'EQ-TEST-01', shiftId: 'SHIFT-DAY', plannedStartUtc: '2026-05-27T12:00:00Z', qualityStatus: '待复检' },
]

export const demoErpSalesOrders = [
  { documentNo: 'SO-GAC-260527-01', customer: '广汽乘用车', sku: 'FG-SAD-FRT-001', quantity: 1200, dueDate: '2026-05-30', status: '已确认' },
  { documentNo: 'SO-BYD-260527-02', customer: '比亚迪底盘事业部', sku: 'FG-SAD-RR-001', quantity: 900, dueDate: '2026-06-02', status: '待排产' },
  { documentNo: 'SO-SPARE-260527-03', customer: '售后备件中心', sku: 'FG-SAD-FRT-001', quantity: 300, dueDate: '2026-05-29', status: '加急' },
]

export const demoErpProcurement = [
  { documentNo: 'PO-ROD-260527-01', supplier: '华东活塞杆', material: 'SA-PISTON-32', quantity: 5000, dueDate: '2026-05-29', status: '在途' },
  { documentNo: 'PO-OIL-260527-02', supplier: '长城润滑油', material: 'SA-OIL-HV32', quantity: 1200, dueDate: '2026-05-28', status: '待收货' },
  { documentNo: 'PO-SEAL-260527-03', supplier: '宁波密封件', material: 'SA-SEAL-KIT-A', quantity: 8000, dueDate: '2026-05-31', status: '待检验' },
]

export const demoErpFinance = [
  { documentNo: 'COST-WO001', source: 'WO-SO-260527-001', amount: 186000, status: '成本归集', owner: '成本会计' },
  { documentNo: 'AR-SO-GAC-01', source: 'SO-GAC-260527-01', amount: 420000, status: '待开票', owner: '应收会计' },
  { documentNo: 'AP-PO-ROD-01', source: 'PO-ROD-260527-01', amount: 95000, status: '待三单匹配', owner: '应付会计' },
]

export function readLocalDemoPlans() {
  return readLocalRows<BusinessConsoleMesProductionPlanRow>(localPlanStorageKey)
}

export function writeLocalDemoPlans(rows: BusinessConsoleMesProductionPlanRow[]) {
  writeLocalRows(localPlanStorageKey, rows)
}

export function readLocalDemoWorkOrders() {
  return readLocalRows<BusinessConsoleMesWorkOrderItem>(localWorkOrderStorageKey)
}

export function writeLocalDemoWorkOrders(rows: BusinessConsoleMesWorkOrderItem[]) {
  writeLocalRows(localWorkOrderStorageKey, rows)
}

export function toDemoWorkOrderFromPlan(plan: BusinessConsoleMesProductionPlanRow, workOrderId: string, workCenterId?: string | null, dueUtc?: string | null): BusinessConsoleMesWorkOrderItem {
  const firstWorkCenter = workCenterId || (plan.skuId === 'FG-SAD-RR-001' ? 'WC-OIL-FILL' : 'WC-TUBE-WELD')
  return {
    workOrderId,
    skuId: plan.skuId,
    productionVersionId: plan.skuId === 'FG-SAD-RR-001' ? 'PV-RR-2026-B' : 'PV-FRT-2026-A',
    quantity: plan.plannedQuantity,
    priority: plan.sourceSystem === 'sales-order' ? 3 : 5,
    dueUtc: dueUtc || plan.plannedEndUtc || plan.plannedStartUtc || undefined,
    status: 'Ready',
    operationTasks: [
      {
        operationTaskId: `OP-${workOrderId}-10`,
        status: 'Ready',
        operationSequence: 10,
        workCenterId: firstWorkCenter,
        earliestStartUtc: plan.plannedStartUtc || undefined,
      },
    ],
  }
}

export function mergeByKey<T>(rows: T[], keySelector: (row: T) => string | null | undefined) {
  const seen = new Set<string>()
  const result: T[] = []

  for (const row of rows) {
    const key = keySelector(row)
    if (key && seen.has(key)) continue
    if (key) seen.add(key)
    result.push(row)
  }

  return result
}

function readLocalRows<T>(key: string) {
  if (typeof localStorage === 'undefined') return []
  try {
    const raw = localStorage.getItem(key)
    if (!raw) return []
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? parsed as T[] : []
  } catch {
    return []
  }
}

function writeLocalRows<T>(key: string, rows: T[]) {
  if (typeof localStorage === 'undefined') return
  localStorage.setItem(key, JSON.stringify(rows))
}
