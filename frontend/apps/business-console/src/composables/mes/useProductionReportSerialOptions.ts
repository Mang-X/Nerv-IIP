import {
  getBusinessConsoleMasterDataResourceDetail,
  getBusinessConsoleMesWorkOrderDetail,
  listBusinessConsoleBarcodeTemplates,
  type BusinessConsoleBarcodeTemplateItem,
} from '@nerv-iip/api-client'
import { computed, reactive, shallowRef, watch } from 'vue'

import { bindBusinessContext } from '@/composables/businessContextBinding'
import { useMesPrincipalWorkScope } from '@/composables/useBusinessMes'
import { notifyError } from '@/utils/notify'
import type { ProductionReportContext } from './useProductionReportForm'

/**
 * 只消费权威 SKU 策略与启用模板目录，不把策略或规则传入报工。
 *
 * 这里**不**复校验策略码是否落在 `serial-tracking-policy` 码集内（#3747）：该判定的权威在网关
 * 报工协调器（`production-serial-policy-invalid` → `stableErrorMessages` 已有中文文案），
 * 前端再抄一份码集的净效果只有一个——字典新增策略码时本页先一步把用户挡死。
 * 本地只区分「是不是 on-production」，因为只有它决定要不要选标签模板。
 */
export function useProductionReportSerialOptions(context: () => ProductionReportContext | null) {
  const businessContext = bindBusinessContext(reactive({ organizationId: '', environmentId: '' }))
  const scope = useMesPrincipalWorkScope(businessContext, 'business.mes.work-orders.read')
  const serialPolicy = shallowRef<string>()
  const serialOptionsPending = shallowRef(false)
  const labelTemplates = shallowRef<BusinessConsoleBarcodeTemplateItem[]>([])
  const labelTemplatesStatus = shallowRef<'idle' | 'loading' | 'ready' | 'failed'>('idle')
  const serialOptionsReady = shallowRef(false)
  let generation = 0

  async function refreshSerialOptions() {
    const currentGeneration = ++generation
    serialPolicy.value = undefined
    labelTemplates.value = []
    labelTemplatesStatus.value = 'idle'
    serialOptionsReady.value = false
    serialOptionsPending.value = false
    const current = context()
    if (!current || !scope.scopeReady.value) return
    serialOptionsPending.value = true
    try {
      const query = { ...businessContext }
      const selected = scope.requireSelectedScope()
      const workOrder = await getBusinessConsoleMesWorkOrderDetail({
        path: { workOrderId: current.workOrderId },
        query: { ...query, scopeKind: selected.kind, scopeId: selected.id },
        throwOnError: true,
      })
      if (!workOrder.data?.success || !workOrder.data.data?.skuId) {
        throw workOrder.data ?? new Error('无法读取报工物料，请刷新工单后重试。')
      }
      const sku = await getBusinessConsoleMasterDataResourceDetail({
        path: { resourceType: 'sku', code: workOrder.data.data.skuId },
        query,
        throwOnError: true,
      })
      const policy = sku.data?.data?.serialTrackingPolicy
      if (!sku.data?.success || !sku.data.data?.active || !policy) {
        throw new Error('物料的序列号追踪设置不可用，请联系基础数据维护人员。')
      }
      if (generation !== currentGeneration) return
      serialPolicy.value = policy
      serialOptionsReady.value = true
      const templates: BusinessConsoleBarcodeTemplateItem[] = []
      if (policy === 'on-production') {
        labelTemplatesStatus.value = 'loading'
        let skip = 0
        let total = 0
        do {
          const response = await listBusinessConsoleBarcodeTemplates({
            query: { ...query, status: 'active', skip, take: 500 },
            throwOnError: true,
          })
          if (!response.data?.success) throw response.data
          const rows = response.data.data?.templates ?? []
          templates.push(...rows.filter((row) => row.status === 'active' && row.templateId))
          total = response.data.data?.total ?? 0
          skip += rows.length
          if (!rows.length) break
          if (generation !== currentGeneration) return
        } while (skip < total)
      }
      if (generation !== currentGeneration) return
      serialPolicy.value = policy
      labelTemplates.value = templates
      if (policy === 'on-production') labelTemplatesStatus.value = 'ready'
      serialOptionsReady.value = true
    } catch (error) {
      if (generation === currentGeneration) {
        if (labelTemplatesStatus.value === 'loading') labelTemplatesStatus.value = 'failed'
        notifyError(error, '报工标签设置读取失败，请重新加载；无权限时请联系管理员。')
      }
    } finally {
      if (generation === currentGeneration) serialOptionsPending.value = false
    }
  }

  watch(
    () => [
      context()?.workOrderId,
      businessContext.organizationId,
      businessContext.environmentId,
      scope.principalIdentity.value,
      scope.scopeReady.value,
      scope.selectedScope.value?.kind,
      scope.selectedScope.value?.id,
    ],
    refreshSerialOptions,
    { immediate: true },
  )
  return {
    serialPolicy,
    labelTemplates,
    labelTemplatesStatus,
    serialOptionsReady,
    serialOptionsPending: computed(() => serialOptionsPending.value || scope.scopePending.value),
    refreshSerialOptions,
  }
}
