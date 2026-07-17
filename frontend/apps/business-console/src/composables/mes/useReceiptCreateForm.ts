import type { BusinessConsoleMesCreateReceiptRequest } from '@nerv-iip/api-client'
import { computed, reactive, ref, watch } from 'vue'

import {
  makeIdempotencyKey,
  useMesFinishedGoodsReceipts,
  useMesWorkOrderProducedLots,
} from '@/composables/useBusinessMes'
import { notifyError, notifySuccess } from '@/utils/notify'

/** 登记完工入库所需的工单上下文（由路由页编排后传入，表单本身只负责登记态）。 */
export interface ReceiptCreateContext {
  organizationId: string
  environmentId: string
  workOrderId: string
  skuId: string
  /** 工单详情/报工带出的建议入库数量（可选，操作员可改）。 */
  initialQuantity?: string
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function toPositiveNumber(value: string) {
  const parsed = toOptionalNumber(value)
  return parsed !== undefined && parsed > 0 ? parsed : undefined
}
function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed || undefined
}
function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}
// 累计申请量超过工单完工数量：后端返回带 WorkOrderId 后缀的技术消息，收敛成 issue 指定的一线业务文案。
// 命中时返回映射文案（作为「实际错误消息」传入 notifyError，绕过其 ≤60 字中文原文透传）；否则 undefined。
function overQuantityMessage(error: unknown): string | undefined {
  const raw = error instanceof Error ? error.message : error ? String(error) : ''
  if (raw && (raw.includes('累计完工入库申请数量超过') || raw.includes('完工数量'))) {
    return '累计请求量超过完工数量，请先核对该工单的报工完成数量后再登记入库。'
  }
  return undefined
}

/**
 * 完工入库「登记表单」状态与提交，从路由页抽出，供 ReceiptCreateSheet 复用（路由页只负责编排上下文与开合）。
 * 产出批次来自工单权威产出端点；提交结果一律走 toast，成功后重置留在原地支持高频连录（见各轮复审）。
 */
export function useReceiptCreateForm(
  context: () => ReceiptCreateContext,
  options: { onCreated?: () => void } = {},
) {
  const {
    createReceiptRequest,
    createReceiptRequestError,
    createReceiptRequestPending,
    refreshReceiptRequests,
  } = useMesFinishedGoodsReceipts()
  const { producedLots, producedLotsPending, producedLotsError, refreshProducedLots } =
    useMesWorkOrderProducedLots(() => context().workOrderId)

  const form = reactive({
    quantity: '1',
    unitCost: '',
    uomCode: 'EA',
    requestedAtUtc: toLocalDateTimeInput(new Date()),
    producedLotNo: '',
    // 幂等键按「登记会话」生成：会话内瞬时失败重投复用同键→后端回放不重复入库；成功后 resetForm 轮换新键→连录产生独立申请。
    idempotencyKey: makeIdempotencyKey('receipt'),
  })

  // 单一产出批次时自动选中；当前选择若已不在候选集合中（工单切换/成功重置）则清空，避免提交陈旧批次。
  function applyDefaultProducedLot() {
    const lots = producedLots.value
    if (lots.length === 1) {
      form.producedLotNo = lots[0].producedLotNo
    } else if (!lots.some((l) => l.producedLotNo === form.producedLotNo)) {
      form.producedLotNo = ''
    }
  }
  watch(producedLots, applyDefaultProducedLot, { immediate: true })
  watch(producedLotsError, (err) => {
    if (err) form.producedLotNo = ''
  })
  // 工单详情/报工带出的建议数量：进入或工单切换时预填（操作员可改；成功后 resetForm 恢复默认）。
  watch(
    () => context().initialQuantity,
    (quantity) => {
      if (quantity && isNonEmpty(quantity)) form.quantity = quantity
    },
    { immediate: true },
  )

  const producedLotPlaceholder = computed(() =>
    producedLotsPending.value
      ? '加载产出批次…'
      : producedLotsError.value
        ? '产出批次加载失败'
        : producedLots.value.length === 0
          ? '暂无产出批次'
          : '选择产出批次',
  )

  // 当前所选批次：用于按剩余可入库量限制登记数量（后端按批次上限拒绝，前端闭环避免必然失败的请求）。
  const selectedLot = computed(() =>
    producedLots.value.find((l) => l.producedLotNo === form.producedLotNo),
  )
  const QUANTITY_TOLERANCE = 0.000001
  const quantityValid = computed(() => {
    const quantity = toPositiveNumber(form.quantity)
    if (quantity === undefined) return false
    const remaining = selectedLot.value?.remainingQuantity
    return remaining === undefined || quantity <= remaining + QUANTITY_TOLERANCE
  })

  // 字段级无效标记（校验时机对齐 create-dialog：点提交才标红）。
  const invalid = computed(() => ({
    producedLotNo: !isNonEmpty(form.producedLotNo),
    quantity: !quantityValid.value,
    unitCost: toPositiveNumber(form.unitCost) === undefined,
    uomCode: !isNonEmpty(form.uomCode),
    requestedAtUtc: !isNonEmpty(form.requestedAtUtc),
  }))

  const canSubmit = computed(() => {
    const ctx = context()
    return (
      isNonEmpty(ctx.organizationId) &&
      isNonEmpty(ctx.environmentId) &&
      isNonEmpty(ctx.workOrderId) &&
      isNonEmpty(ctx.skuId) &&
      !invalid.value.producedLotNo &&
      !invalid.value.quantity &&
      !invalid.value.unitCost &&
      !invalid.value.uomCode &&
      !invalid.value.requestedAtUtc
    )
  })

  // 点提交才标红（feedback-and-notifications：字段级校验内联标红 + 顶部汇总，不发请求也不 toast）。
  const showErrors = ref(false)

  function resetForm() {
    form.quantity = '1'
    form.unitCost = ''
    form.uomCode = 'EA'
    // 成功后先清空产出批次：多批次工单强制操作员重新选择（避免连录误记到上一批次），单一批次再由
    // applyDefaultProducedLot 自动回填。
    form.producedLotNo = ''
    applyDefaultProducedLot()
    form.requestedAtUtc = toLocalDateTimeInput(new Date())
    // 仅在登记成功后调用：轮换幂等键，使同一工单的下一笔登记成为一笔独立申请（连录不回放旧单）。
    form.idempotencyKey = makeIdempotencyKey('receipt')
    showErrors.value = false
  }

  async function submit(): Promise<boolean> {
    // 点提交才校验：必填/超量未过则标红 + 顶部汇总，不发请求（create-dialog 硬规则）。
    showErrors.value = true
    if (!canSubmit.value) return false
    const ctx = context()
    const body: BusinessConsoleMesCreateReceiptRequest = {
      organizationId: ctx.organizationId.trim(),
      environmentId: ctx.environmentId.trim(),
      workOrderId: ctx.workOrderId.trim(),
      skuId: ctx.skuId.trim(),
      producedLotNo: form.producedLotNo.trim(),
      quantity: toPositiveNumber(form.quantity),
      unitCost: toPositiveNumber(form.unitCost),
      uomCode: form.uomCode.trim(),
      requestedAtUtc: toIsoFromLocalInput(form.requestedAtUtc),
      idempotencyKey: optionalText(form.idempotencyKey) ?? makeIdempotencyKey('receipt'),
    }
    // 登记成功与「列表刷新」是两件独立的事：刷新失败不得否定已成功的登记（否则同时看到成功+失败并可能重复提交）。
    try {
      await createReceiptRequest(body)
    } catch {
      const err = createReceiptRequestError.value ?? undefined
      const overQuantity = overQuantityMessage(err)
      notifyError(overQuantity ? new Error(overQuantity) : err, '登记完工入库失败，请稍后重试。')
      return false
    }
    notifySuccess(`已登记完工入库 · 工单 ${body.workOrderId ?? ''}，可在列表查看入库状态。`)
    resetForm()
    void Promise.resolve(refreshReceiptRequests()).catch(() => {})
    options.onCreated?.()
    return true
  }

  return {
    form,
    producedLots,
    producedLotsPending,
    producedLotsError,
    refreshProducedLots,
    producedLotPlaceholder,
    selectedLot,
    canSubmit,
    showErrors,
    invalid,
    createReceiptRequestPending,
    submit,
  }
}
