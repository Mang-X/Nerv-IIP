export interface PdaTaskKind {
  id: string
  label: string
  group: 'wms' | 'mes' | 'equipment'
  route: string
  /** 对应作业页是否已落地；false 时应用墙入口 disabled。 */
  routeReady: boolean
}

export const PDA_TASK_KINDS: PdaTaskKind[] = [
  { id: 'wms.inbound', label: '收货入库', group: 'wms', route: '/wms/inbound', routeReady: true },
  { id: 'wms.putaway', label: '上架', group: 'wms', route: '/wms/putaway', routeReady: true },
  { id: 'wms.pick', label: '拣货', group: 'wms', route: '/wms/pick', routeReady: true },
  { id: 'wms.review', label: '复核发货', group: 'wms', route: '/wms/review', routeReady: true },
  { id: 'wms.count', label: '盘点', group: 'wms', route: '/wms/count', routeReady: true },
  { id: 'mes.report', label: '报工', group: 'mes', route: '/mes/report', routeReady: false },
  { id: 'mes.issue', label: '领料', group: 'mes', route: '/mes/issue', routeReady: false },
  { id: 'mes.receipt', label: '完工入库', group: 'mes', route: '/mes/receipt', routeReady: false },
  { id: 'mes.operation', label: '工序执行', group: 'mes', route: '/mes/operation', routeReady: false },
  { id: 'equipment.repair', label: '报修', group: 'equipment', route: '/equipment/repair', routeReady: false },
  { id: 'equipment.inspect', label: '点检', group: 'equipment', route: '/equipment/inspect', routeReady: false },
]

const byId = new Map(PDA_TASK_KINDS.map((k) => [k.id, k]))

export function getPdaTaskKind(id: string): PdaTaskKind | undefined {
  return byId.get(id)
}
