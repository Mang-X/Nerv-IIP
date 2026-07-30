import type { DropdownOption } from '@nerv-iip/ui-mobile'

export const PDA_WAREHOUSE_TASK_STATUS_OPTIONS: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待执行', value: 'Open' },
  { label: '执行中', value: 'InProgress' },
  { label: '已完成', value: 'Completed' },
  { label: '差异完成', value: 'CompletedWithDifference' },
  { label: '异常待处理', value: 'Exception' },
  { label: '已取消', value: 'Cancelled' },
]

export const PDA_COUNT_EXECUTION_STATUS_OPTIONS: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待盘点', value: 'Open' },
  { label: '已完成', value: 'Completed' },
]

export const PDA_INBOUND_ORDER_STATUS_OPTIONS: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待收货', value: 'Open' },
  { label: '待质检', value: 'PendingQualityCheck' },
  { label: '已完成', value: 'Completed' },
  { label: '库存过账失败', value: 'InventoryPostingFailed' },
  { label: '已取消', value: 'Cancelled' },
]

export const PDA_OUTBOUND_ORDER_STATUS_OPTIONS: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待复核发货', value: 'Open' },
  { label: '库存过账中', value: 'InventoryPostingPending' },
  { label: '已完成', value: 'Completed' },
  { label: '库存过账失败', value: 'InventoryPostingFailed' },
  { label: '已取消', value: 'Cancelled' },
]
