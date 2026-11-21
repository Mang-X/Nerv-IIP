import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'

type Row = BusinessConsoleMesWorkOrderItem

export const columns: NvDataTableColumn<Row>[] = [
  { key: 'workOrderId', header: '工单', sortable: true, cellClass: 'font-medium' },
  { key: 'status', header: '状态', sortable: true, width: 'w-24' },
  { key: 'urgency', header: '紧急度', width: 'w-28' },
  {
    key: 'quantity',
    header: '数量',
    align: 'end',
    sortable: true,
    width: 'w-24',
    accessor: (r) => r.quantity ?? 0,
  },
  { key: 'completedQuantity', header: '完成量', align: 'end', sortable: true, width: 'w-24' },
  {
    key: 'dueUtc',
    header: '交期',
    sortable: true,
    width: 'w-44',
    accessor: (r) => (r.dueUtc ? new Date(r.dueUtc).getTime() : 0),
  },
  {
    key: 'operationCount',
    header: '工序',
    sortable: true,
    accessor: (r) => r.operationTasks?.length ?? 0,
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

export function rowKey(order: Row) {
  return order.workOrderId ?? `${order.skuId ?? 'wo'}-${order.dueUtc ?? ''}`
}
