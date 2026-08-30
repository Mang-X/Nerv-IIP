import { computed, reactive, watch, type Ref } from 'vue'
import { useMesProductionMaterialLots } from '@/composables/useBusinessMes'

type ReportPair = { workOrderId: string; operationTaskId: string }

export function useProductionReportMaterials(
  pair: Readonly<Ref<ReportPair | null>>,
  scrapQuantity: Readonly<Ref<number>>,
) {
  const productionMaterialLots = useMesProductionMaterialLots(() => pair.value)
  const materialSelections = reactive(
    new Map<string, { selected: boolean; consumedQuantity: string }>(),
  )

  watch(
    productionMaterialLots.availableMaterialLots,
    (rows) => {
      const knownRequestIds = new Set(rows.map((row) => row.requestId))
      for (const row of rows) {
        if (!materialSelections.has(row.requestId)) {
          materialSelections.set(row.requestId, { selected: false, consumedQuantity: '' })
        }
      }
      for (const requestId of materialSelections.keys()) {
        if (!knownRequestIds.has(requestId)) materialSelections.delete(requestId)
      }
    },
    { immediate: true },
  )

  const consumedMaterialLots = computed(() =>
    productionMaterialLots.availableMaterialLots.value.flatMap((row) => {
      const selection = materialSelections.get(row.requestId)
      const materialLotId = row.materialLotId?.trim()
      if (!selection?.selected || !materialLotId) return []
      return [
        {
          materialId: row.materialId,
          materialLotId,
          consumedQuantity: Number(selection.consumedQuantity),
          materialIssueRequestNo: row.requestId,
        },
      ]
    }),
  )

  const invalidMaterialLots = computed(
    () =>
      (scrapQuantity.value > 0 && consumedMaterialLots.value.length === 0) ||
      productionMaterialLots.availableMaterialLots.value.some((row) => {
        const selection = materialSelections.get(row.requestId)
        if (!selection?.selected) return false
        const quantity = Number(selection.consumedQuantity)
        return (
          !Number.isFinite(quantity) ||
          quantity <= 0 ||
          quantity > row.receivedQuantity - row.consumedQuantity
        )
      }),
  )

  const materialValidationMessage = computed(() =>
    scrapQuantity.value > 0 && consumedMaterialLots.value.length === 0
      ? '报废报工至少选择一个已收料批次。'
      : invalidMaterialLots.value
        ? '耗料数量必须大于 0，且不能超过该批次可用数量。'
        : '',
  )

  function resetMaterialSelections() {
    materialSelections.clear()
  }

  function materialSelected(requestId: string | undefined) {
    return requestId ? (materialSelections.get(requestId)?.selected ?? false) : false
  }

  function materialQuantity(requestId: string | undefined) {
    return requestId ? (materialSelections.get(requestId)?.consumedQuantity ?? '') : ''
  }

  function setMaterialSelected(requestId: string | undefined, selected: boolean | undefined) {
    if (!requestId) return
    const current = materialSelections.get(requestId) ?? { selected: false, consumedQuantity: '' }
    current.selected = selected ?? false
    materialSelections.set(requestId, current)
  }

  function setMaterialQuantity(requestId: string | undefined, quantity: string | undefined) {
    if (!requestId) return
    const current = materialSelections.get(requestId) ?? { selected: false, consumedQuantity: '' }
    current.consumedQuantity = quantity ?? ''
    materialSelections.set(requestId, current)
  }

  function materialRemaining(row: { receivedQuantity?: number; consumedQuantity?: number }) {
    return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(
      (row.receivedQuantity ?? 0) - (row.consumedQuantity ?? 0),
    )
  }

  return {
    ...productionMaterialLots,
    consumedMaterialLots,
    invalidMaterialLots,
    materialValidationMessage,
    resetMaterialSelections,
    materialSelected,
    materialQuantity,
    setMaterialSelected,
    setMaterialQuantity,
    materialRemaining,
  }
}
