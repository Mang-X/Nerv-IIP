import { useBusinessMasterDataResources } from './useBusinessMasterData'
import type { MasterDataListPickerType } from './useSearchableDirectoryPicker'

/**
 * 编码 → 名称：新增弹窗把调用方带出的上级编码显示成名称。与层级选择器同一份整表查询
 * （`useMasterDataListPicker`），不多发请求。列表还没回来时先显示编码。
 */
export function useMasterDataDisplayName(resourceType: MasterDataListPickerType) {
  const catalog = useBusinessMasterDataResources(resourceType)
  catalog.filters.take = 500
  return (code: string) =>
    catalog.resources.value.find((row) => row.code === code)?.displayName || code
}
