import type { BusinessConsoleDemandSourceItem } from '@nerv-iip/api-client'
import { computed, shallowRef, watch } from 'vue'
import { demandTypeLabel } from './planningFormatters'
import type { Ref } from 'vue'

export function usePlanningDemandFilters(
  demands: Readonly<Ref<BusinessConsoleDemandSourceItem[]>>,
  orderedDemands: Readonly<Ref<readonly BusinessConsoleDemandSourceItem[]>>,
  sourceOrder: Readonly<Ref<string>>,
) {
  /**
   * 需求池筛选（GH#1292 第 5 项）：一屏几百条需求此前没有任何查找手段，计划员要找某张销售订单
   * 或某个物料只能肉眼扫。普通入口当前在已载入的需求中筛选。
   */
  const demandKeyword = shallowRef('')
  const demandTypeFilter = shallowRef('all')
  /** 类型选项按池子里真实出现过的类型生成——不列一个筛完必空的类型。 */
  const demandTypeFilterOptions = computed(() => [
    { label: '全部类型', value: 'all' },
    ...[...new Set(demands.value.map((demand) => demand.demandType).filter(Boolean))].map(
      (type) => ({
        label: demandTypeLabel(type),
        value: type as string,
      }),
    ),
  ])
  const filteredDemands = computed(() => {
    const keyword = demandKeyword.value.trim().toLowerCase()
    const type = demandTypeFilter.value
    return orderedDemands.value.filter((demand) => {
      if (
        sourceOrder.value &&
        (demand.sourceReference !== sourceOrder.value || demand.demandType !== 'sales-order')
      )
        return false
      if (type !== 'all' && demand.demandType !== type) return false
      if (!keyword) return true
      // 搜来源单号 / 物料 / 客户 / 工厂：计划员手里通常就是这四个之一。
      return [demand.sourceReference, demand.skuCode, demand.customerCode, demand.siteCode].some(
        (value) => value?.toLowerCase().includes(keyword),
      )
    })
  })
  const demandFilterActive = computed(
    () => demandKeyword.value.trim().length > 0 || demandTypeFilter.value !== 'all',
  )
  /**
   * 刷新后已选类型可能整类消失（那批需求被消化完了）。选项列表是按现有数据生成的，
   * 留着一个不在列表里的值会让下拉**显示为空白**，而表格又按它筛成空——用户看不出发生了什么。
   * 这里回落「全部类型」并明说一句，让人知道筛选被重置了、不是数据没了。
   */
  const demandTypeResetNotice = shallowRef('')
  watch(demandTypeFilterOptions, (options) => {
    const selected = demandTypeFilter.value
    if (selected === 'all' || options.some((option) => option.value === selected)) return
    demandTypeResetNotice.value = `「${demandTypeLabel(selected)}」类需求已不在当前需求池中，已切回全部类型。`
    demandTypeFilter.value = 'all'
  })
  // 用户自己改筛选就把提示撤掉，别让它常驻。
  watch([demandTypeFilter, demandKeyword], () => {
    if (demandTypeResetNotice.value && demandTypeFilter.value === 'all') return
    demandTypeResetNotice.value = ''
  })

  return {
    demandKeyword,
    demandTypeFilter,
    demandTypeFilterOptions,
    filteredDemands,
    demandFilterActive,
    demandTypeResetNotice,
  }
}
