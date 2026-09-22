import {
  getBusinessConsoleMasterDataResourceDetail,
  listBusinessConsoleBarcodeTemplates,
  type BusinessConsoleBarcodeTemplateItem,
} from '@nerv-iip/api-client'
import { computed, shallowRef, watch, type ComputedRef, type Ref } from 'vue'
import { describeRequestError } from '@/api/request-timeout'
import { useAuthStore } from '@/stores/auth'
import type { MesReportExecutionContext } from '@/composables/useBusinessMes'

export function useProductionReportSerials(
  pair: ComputedRef<{ workOrderId: string; operationTaskId: string } | null>,
  skuId: ComputedRef<string>,
  context: ComputedRef<MesReportExecutionContext | undefined>,
  goodQuantity: Ref<number>,
) {
  const auth = useAuthStore()
  const policy = shallowRef('')
  const templateId = shallowRef('')
  const templates = shallowRef<BusinessConsoleBarcodeTemplateItem[]>([])
  const pending = shallowRef(false)
  const error = shallowRef('')
  const revision = shallowRef(0)
  const resourcesAllowed = computed(
    () => auth.principal?.permissionCodes?.includes('business.masterdata.resources.read') === true,
  )
  const labelsAllowed = computed(
    () => auth.principal?.permissionCodes?.includes('business.barcodes.read') === true,
  )
  watch(
    () =>
      [
        pair.value?.workOrderId,
        pair.value?.operationTaskId,
        skuId.value,
        context.value,
        resourcesAllowed.value,
        labelsAllowed.value,
        revision.value,
      ] as const,
    async (_, __, onCleanup) => {
      let current = true
      const controller = new AbortController()
      onCleanup(() => {
        current = false
        controller.abort()
      })
      policy.value = ''
      templateId.value = ''
      templates.value = []
      error.value = ''
      pending.value = false
      const scope = context.value
      const code = skuId.value
      if (!scope || !pair.value || !code || !resourcesAllowed.value) return
      pending.value = true
      const query = { organizationId: scope.organizationId, environmentId: scope.environmentId }
      try {
        const response = await getBusinessConsoleMasterDataResourceDetail({
          path: { resourceType: 'sku', code },
          query,
          signal: controller.signal,
          throwOnError: true,
        })
        if (!current) return
        const sku = response.data?.data
        // 策略码是否落在 serial-tracking-policy 码集内由网关报工判定（#3747），本地不再抄一份码集：
        // 抄一份只会在字典新增策略码时先一步把操作工挡死。本地只区分「是不是 on-production」。
        if (!response.data?.success || !sku?.active || !sku.serialTrackingPolicy)
          throw new Error('产品追踪设置不可用，请联系基础资料管理员核对后重试。')
        policy.value = sku.serialTrackingPolicy
        if (policy.value !== 'on-production') return
        if (!sku.defaultBarcodeRuleCode?.trim())
          throw new Error('产品尚未配置条码规则，请联系基础资料管理员配置后重试。')
        if (!labelsAllowed.value) return
        const items: BusinessConsoleBarcodeTemplateItem[] = []
        let skip = 0
        while (current) {
          const response = await listBusinessConsoleBarcodeTemplates({
            query: { ...query, status: 'active', skip, take: 100 },
            signal: controller.signal,
            throwOnError: true,
          })
          if (!current) return
          if (!response.data?.success || !response.data.data)
            throw new Error('标签模板读取失败，请重试；仍不可用时联系标签管理员。')
          const page = response.data.data.templates ?? []
          items.push(
            ...page.filter(
              (item) => item.status === 'active' && item.templateId && item.templateName,
            ),
          )
          skip += page.length
          if (page.length < 100 || skip >= (response.data.data.total ?? Infinity)) break
        }
        templates.value = items
      } catch (reason) {
        if (current)
          error.value = describeRequestError(reason, '产品与标签资料读取失败，请重试。').message
      } finally {
        if (current) pending.value = false
      }
    },
    { immediate: true, flush: 'sync' },
  )
  const required = computed(() => policy.value === 'on-production')
  const quantityValid = computed(
    () =>
      !required.value ||
      (Number.isInteger(goodQuantity.value) &&
        goodQuantity.value >= 0 &&
        goodQuantity.value <= 2147483647),
  )
  const pendingCount = computed(() =>
    required.value && quantityValid.value ? goodQuantity.value : 0,
  )
  const message = computed(() => {
    if (!resourcesAllowed.value) return '当前账号没有产品资料读取权限，请联系管理员开通后再报工。'
    if (pending.value) return '正在核对产品追踪设置与标签模板…'
    if (error.value && !(required.value && goodQuantity.value === 0)) return error.value
    if (!policy.value) return '请先选择已核验的工单与工序。'
    if (!quantityValid.value) return '单件追踪的良品数须为 0 到 2147483647 的整数。'
    if (!required.value || goodQuantity.value === 0) return ''
    if (!labelsAllowed.value) return '当前账号没有标签读取权限，请联系管理员开通后再报工。'
    if (templates.value.length === 0) return '没有可用标签模板，请联系标签管理员启用模板后重试。'
    if (!templates.value.some((item) => item.templateId === templateId.value))
      return '请选择本次使用的标签模板。'
    return ''
  })
  const valid = computed(() => Boolean(policy.value) && !message.value)
  return {
    required,
    templateId,
    templates,
    pending,
    message,
    valid,
    pendingCount,
    refresh: () => {
      revision.value++
    },
  }
}
