/** 库位类型：value 与库存服务存的码值一致；线边库位必须是 line-side，线边库存才看得到它。 */
export const LOCATION_TYPE_OPTIONS = [
  { value: 'storage', label: '存储库位' },
  { value: 'line-side', label: '线边库位' },
  { value: 'staging', label: '暂存区' },
  { value: 'quality-hold', label: '不合格品隔离区' },
]

export const LOCATION_STATUS_OPTIONS = [
  { value: 'active', label: '启用' },
  { value: 'inactive', label: '停用' },
]
