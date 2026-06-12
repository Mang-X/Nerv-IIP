export interface PdaTaskKind {
  id: string
  label: string
  group: 'wms' | 'mes' | 'equipment'
  route: string
  /** 对应作业页是否已落地；false 时应用墙入口 disabled。 */
  routeReady: boolean
}

export const PDA_TASK_KINDS: PdaTaskKind[] = [
  // 合并 #378(MES) + #379(equipment) + #380(WMS) 后，全部 PDA 域均已交付，所有入口点亮。
  { id: 'wms.inbound', label: '收货入库', group: 'wms', route: '/wms/inbound', routeReady: true },
  { id: 'wms.putaway', label: '上架', group: 'wms', route: '/wms/putaway', routeReady: true },
  { id: 'wms.pick', label: '拣货', group: 'wms', route: '/wms/pick', routeReady: true },
  { id: 'wms.review', label: '复核发货', group: 'wms', route: '/wms/review', routeReady: true },
  { id: 'wms.count', label: '盘点', group: 'wms', route: '/wms/count', routeReady: true },
  { id: 'mes.report', label: '报工', group: 'mes', route: '/mes/report', routeReady: true },
  { id: 'mes.issue', label: '领料', group: 'mes', route: '/mes/issue', routeReady: true },
  { id: 'mes.receipt', label: '完工入库', group: 'mes', route: '/mes/receipt', routeReady: true },
  { id: 'mes.operation', label: '工序执行', group: 'mes', route: '/mes/operation', routeReady: true },
  { id: 'equipment.repair', label: '报修', group: 'equipment', route: '/equipment/repair', routeReady: true },
  { id: 'equipment.inspect', label: '点检', group: 'equipment', route: '/equipment/inspect', routeReady: true },
  { id: 'equipment.alarms', label: '查看报警', group: 'equipment', route: '/equipment/alarms', routeReady: true },
]

const byId = new Map(PDA_TASK_KINDS.map((k) => [k.id, k]))

export function getPdaTaskKind(id: string): PdaTaskKind | undefined {
  return byId.get(id)
}
