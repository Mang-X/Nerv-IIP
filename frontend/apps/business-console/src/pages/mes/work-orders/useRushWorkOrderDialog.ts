import type { BusinessConsoleCreateRushWorkOrderRequest } from '@nerv-iip/api-client'
import { type useMesOperationTasks } from '@/composables/mes'
import { useMesMaterialVersionCatalog } from '@/composables/mes/useMesPickerCatalog'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { computed, reactive, watch } from 'vue'
import type { Ref } from 'vue'
import type { useMesWorkOrders } from '@/composables/mes'

export function useRushWorkOrderDialog(
  {
    filters,
    createRushWorkOrder,
    createRushWorkOrderError,
  }: Pick<
    ReturnType<typeof useMesWorkOrders>,
    'filters' | 'createRushWorkOrder' | 'createRushWorkOrderError'
  >,
  {
    productionVersionOptions,
    operationTasks,
  }: Pick<ReturnType<typeof useMesMaterialVersionCatalog>, 'productionVersionOptions'> &
    Pick<ReturnType<typeof useMesOperationTasks>, 'operationTasks'>,
  rushSheetOpen: Ref<boolean>,
  canManageWorkOrders: Readonly<Ref<boolean>>,
  newMesIdempotencyKey: (scope: string) => string,
) {
  const rushForm = reactive({
    skuId: '',
    productionVersionId: '',
    quantity: '1',
    dueUtc: toLocalDateTimeInput(new Date(Date.now() + 86_400_000)),
    workCenterId: '',
    operationTaskId: '',
    operationSequence: '10',
    durationMinutes: '60',
    idempotencyKey: newMesIdempotencyKey('rush-work-order'),
  })

  const rushVersionOptions = computed(() => productionVersionOptions(rushForm.skuId))
  const rushOperationTaskOptions = computed(() => {
    const workCenter = rushForm.workCenterId.trim()
    return operationTasks.value
      .filter((task) => !!task.operationTaskId)
      .filter((task) => !workCenter || task.workCenterCode === workCenter)
      .map((task) => ({
        value: task.operationTaskId as string,
        label: task.operationTaskNo || task.operationCode || '未编号工序任务',
        hint: [task.workOrderNo, task.workCenterName ?? task.workCenterCode]
          .filter(Boolean)
          .join(' · '),
      }))
  })

  watch(
    () => rushForm.skuId,
    () => {
      rushForm.productionVersionId = ''
    },
  )
  watch(
    () => rushForm.workCenterId,
    () => {
      rushForm.operationTaskId = ''
    },
  )

  const canCreateRush = computed(
    () =>
      canManageWorkOrders.value &&
      isNonEmpty(filters.organizationId) &&
      isNonEmpty(filters.environmentId) &&
      isNonEmpty(rushForm.skuId) &&
      isNonEmpty(rushForm.productionVersionId) &&
      toOptionalNumber(rushForm.quantity) !== undefined &&
      isNonEmpty(rushForm.dueUtc) &&
      isNonEmpty(rushForm.workCenterId) &&
      toOptionalNumber(rushForm.durationMinutes) !== undefined,
  )

  async function submitRushWorkOrder() {
    if (!canCreateRush.value) return
    const body: BusinessConsoleCreateRushWorkOrderRequest = {
      organizationId: filters.organizationId.trim(),
      environmentId: filters.environmentId.trim(),
      skuId: rushForm.skuId.trim(),
      productionVersionId: rushForm.productionVersionId.trim(),
      quantity: toOptionalNumber(rushForm.quantity),
      dueUtc: toIsoFromLocalInput(rushForm.dueUtc),
      workCenterId: rushForm.workCenterId.trim(),
      operationTaskId: optionalText(rushForm.operationTaskId),
      operationSequence: toOptionalInteger(rushForm.operationSequence),
      durationMinutes: toOptionalInteger(rushForm.durationMinutes),
      idempotencyKey: rushForm.idempotencyKey,
    }
    try {
      const response = await createRushWorkOrder(body)
      notifySuccess(`已创建急单 ${response?.data?.workOrderId ?? ''}`)
      rushForm.idempotencyKey = newMesIdempotencyKey('rush-work-order')
      rushSheetOpen.value = false
    } catch (error) {
      notifyOperationFailure(
        '创建急单失败',
        createRushWorkOrderError.value ?? error,
        '创建急单失败，请稍后重试。',
      )
    }
  }

  function optionalText(value: string) {
    const trimmed = value.trim()
    return trimmed ? trimmed : undefined
  }
  function toOptionalNumber(value: string) {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : undefined
  }
  function toOptionalInteger(value: string) {
    const parsed = Number.parseInt(value, 10)
    return Number.isFinite(parsed) ? parsed : undefined
  }
  function toIsoFromLocalInput(value: string) {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toISOString()
  }
  function toLocalDateTimeInput(date: Date) {
    const offset = date.getTimezoneOffset() * 60_000
    return new Date(date.getTime() - offset).toISOString().slice(0, 16)
  }

  function isNonEmpty(value: string) {
    return value.trim().length > 0
  }

  return {
    rushForm,
    rushVersionOptions,
    rushOperationTaskOptions,
    canCreateRush,
    submitRushWorkOrder,
  }
}
