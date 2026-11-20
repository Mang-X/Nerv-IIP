import { computed } from 'vue'
import { getScreenSession } from '@/data/session'
import { assembleWarehouseBoard, fetchCountBoard, fetchWcsBoard, readReceipts, readShipments } from '@/data/real/warehouse'
import { scopeQuery, warehouseOptions, type ScopeOption } from '@/data/real/workScope'
import { accessError } from '@/data/real/readError'
import { useScopedReading } from './useScopedReading'

export function useWarehouseDomains() {
  const session = getScreenSession()
  const query = (s: ScopeOption) => scopeQuery(session, s)
  const receipts = useScopedReading(() => warehouseOptions(session, 'receipts'), s => readReceipts(query(s)))
  const shipments = useScopedReading(() => warehouseOptions(session, 'shipments'), s => readShipments(query(s)))
  const counts = useScopedReading(() => warehouseOptions(session, 'counts'), s => fetchCountBoard(query(s), Date.now()))
  const wcs = useScopedReading(async () => [{ kind:'organization', id:session.organizationId, label:'当前组织' }], () => fetchWcsBoard(session, Date.now()))
  const controls = [
    { label:'入库', scoped:true, source:receipts }, { label:'出库', scoped:true, source:shipments },
    { label:'盘点', scoped:true, source:counts }, { label:'WCS', scoped:false, source:wcs },
  ]
  const lastUpdated = computed(() => {
    const stamps = controls.flatMap(c => c.source.lastUpdated.value ? [c.source.lastUpdated.value] : [])
    return stamps.length ? Math.min(...stamps) : undefined
  })
  const selection = (scope: ScopeOption) => ({ query: query(scope), label:scope.label })
  const board = computed(() => {
    const r = receipts.data.value, s = shipments.data.value, c = counts.data.value
    if (!r || !s || !c || !receipts.scope.value || !shipments.scope.value || !counts.scope.value) return
    const automation = wcs.data.value ?? { readFailure: accessError(wcs.error.value) ?? (wcs.error.value ? '读取失败' : '读取中'),
      counts:{queued:0,running:0,completed:0,failed:0}, failures:[], adapters:[] }
    return assembleWarehouseBoard(session.organizationId, selection(receipts.scope.value), selection(shipments.scope.value), selection(counts.scope.value),
      r.inbound,s.outbound,r.putaway,s.pick,c,automation)
  })
  return { controls, board, receipts, shipments, counts, wcs, lastUpdated }
}
