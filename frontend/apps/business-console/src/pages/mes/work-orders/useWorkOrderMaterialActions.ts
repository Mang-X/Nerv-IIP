import type { EntityPickerOption, NvDataTableColumn } from '@nerv-iip/ui'
import { makeIdempotencyKey } from '@/composables/mes'
import { useSkuNames } from '@/composables/master-data/useSkuNames'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { readFaceText } from '@/utils/readFace'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import { computed, reactive, ref } from 'vue'
import type { Ref } from 'vue'
import type { useMesWorkOrderDetail } from '@/composables/mes'

export function useWorkOrderMaterialActions(
  {
    filters,
    materialIssueRequests,
    createMaterialIssueRequest,
    createMaterialIssueRequestPending,
    confirmLineSideReceipt,
    confirmLineSideReceiptPending,
    returnLineSideMaterial,
    returnLineSideMaterialPending,
    refreshMaterialReadiness,
  }: Pick<
    ReturnType<typeof useMesWorkOrderDetail>,
    | 'filters'
    | 'materialIssueRequests'
    | 'createMaterialIssueRequest'
    | 'createMaterialIssueRequestPending'
    | 'confirmLineSideReceipt'
    | 'confirmLineSideReceiptPending'
    | 'returnLineSideMaterial'
    | 'returnLineSideMaterialPending'
    | 'refreshMaterialReadiness'
  >,
  operationTasks: Readonly<
    Ref<
      NonNullable<
        NonNullable<ReturnType<typeof useMesWorkOrderDetail>['detail']['value']>['operationTasks']
      >
    >
  >,
  permissionCodes: Readonly<Ref<readonly string[]>>,
  resolveSkuLabel: (value?: string | null) => string,
) {
  // --- 发起领料 / 线边收料（#1324）---
  // 齐套区的「下一步动作」以前指向一个 PC 上并不存在的动作（只有 PDA 能发起）。这里补齐两处入口，
  // 语义与校验与 PDA 侧同源：领料必须指定物料且数量>0；收料数量可省（默认收齐），指定时必须>0。
  const canManageMaterials = computed(() => permissionCodes.value.includes(P.mesMaterialsManage))
  // 单位是物料主档的事实（钢材 kg、油品 l、计件件号才是 pcs）。界面写死一个占位单位会被 MES 直接拒收，
  // 也无法在库存腿上换算——取不到主档单位就阻断提交并说明原因，绝不猜（#1294 同款姿势）。
  const { resolveBaseUom, skusPending } = useSkuNames()
  const OPEN_ISSUE_STATUSES = new Set(['requested', 'partiallyreceived'])
  const issueOpen = ref(false)
  const issueForm = reactive({
    materialId: '',
    quantity: '',
    operationTaskId: '',
    idempotencyKey: '',
  })
  const receiveOpen = ref(false)
  const receiveError = ref('')
  const receiveForm = reactive({
    requestId: '',
    materialCode: '',
    quantity: '',
    materialLotId: '',
  })
  const returnOpen = ref(false)
  const returnForm = reactive({ requestId: '', quantity: '', idempotencyKey: '' })

  const materialIssueRows = computed(() => materialIssueRequests.value)
  type MaterialIssueRow = (typeof materialIssueRows)['value'][number]
  const materialIssueColumns: NvDataTableColumn<MaterialIssueRow>[] = [
    {
      key: 'requestId',
      header: '领料单',
      cellClass: 'font-medium',
      accessor: (r) => readFaceText(r.requestId),
    },
    {
      key: 'materialId',
      header: '物料',
      accessor: (r) => resolveSkuLabel(r.materialCode ?? r.materialId),
    },
    { key: 'requestedQuantity', header: '申请量', align: 'end', width: 'w-20' },
    { key: 'receivedQuantity', header: '已收', align: 'end', width: 'w-20' },
    { key: 'status', header: '状态', width: 'w-24' },
    // WMS 回写的出库单号：没有回写就是「仓库还没接单」，不能用空白冒充已发料。
    { key: 'wmsRequestId', header: '出库单', width: 'w-32' },
    { key: 'actions', header: '操作', width: 'w-28' },
  ]

  const operationTaskOptions = computed<EntityPickerOption[]>(() =>
    [...operationTasks.value]
      .sort((a, b) => (a.operationSequence ?? 0) - (b.operationSequence ?? 0))
      .flatMap((task) => {
        const value = task.operationTaskId?.trim()
        if (!value) return []
        const sequence = `第 ${task.operationSequence ?? 0} 道工序`
        const taskNo = task.operationTaskNo?.trim()
        const workCenter = task.workCenterName?.trim() || task.workCenterCode?.trim()
        return [
          {
            value,
            label: taskNo || sequence,
            hint: [taskNo ? sequence : '', workCenter].filter(Boolean).join(' · '),
          },
        ]
      }),
  )

  const issueUomCode = computed(() => resolveBaseUom(issueForm.materialId.trim()))
  const issueUomBlockReason = computed(() => {
    if (!issueForm.materialId.trim()) return ''
    if (issueUomCode.value) return ''
    return skusPending.value
      ? '正在读取物料主档单位，请稍候。'
      : '物料主档没有基本计量单位，无法发起领料。请先在物料主数据补齐该物料的基本计量单位。'
  })
  const canSubmitIssue = computed(() => {
    if (!canManageMaterials.value) return false
    if (!issueForm.materialId.trim()) return false
    if (!issueUomCode.value) return false
    if (!issueForm.quantity.trim()) return true
    const quantity = Number(issueForm.quantity)
    return Number.isFinite(quantity) && quantity > 0
  })
  const canSubmitReceipt = computed(() => {
    if (!canManageMaterials.value) return false
    if (!receiveForm.requestId) return false
    if (!receiveForm.quantity.trim()) return true
    const quantity = Number(receiveForm.quantity)
    return Number.isFinite(quantity) && quantity > 0
  })

  function openIssueDialog(materialId?: string) {
    if (!canManageMaterials.value) return
    issueForm.materialId = materialId ?? ''
    issueForm.quantity = ''
    issueForm.operationTaskId = ''
    issueForm.idempotencyKey = makeIdempotencyKey(
      `issue-${filters.workOrderId}-${materialId ?? 'material'}`,
    )
    issueOpen.value = true
  }

  function canReceive(row: MaterialIssueRow) {
    return (
      canManageMaterials.value &&
      OPEN_ISSUE_STATUSES.has((row.status ?? '').toLowerCase().replace(/[^a-z]/g, ''))
    )
  }

  function returnableQuantityOfRow(row: MaterialIssueRow) {
    return Math.max(0, (row.receivedQuantity ?? 0) - (row.consumedQuantity ?? 0))
  }

  const returnFormMaxQuantity = computed(() => {
    const row = materialIssueRows.value.find((item) => item.requestId === returnForm.requestId)
    return row ? returnableQuantityOfRow(row) : undefined
  })

  function canReturn(row: MaterialIssueRow) {
    return (
      canManageMaterials.value &&
      Boolean(row.materialLotId?.trim()) &&
      returnableQuantityOfRow(row) > 0 &&
      ['received', 'partiallyreceived'].includes((row.status ?? '').toLowerCase())
    )
  }

  function openReceiveDialog(row: MaterialIssueRow) {
    if (!canReceive(row)) return
    receiveError.value = ''
    receiveForm.requestId = row.requestId ?? ''
    receiveForm.materialCode = row.materialCode ?? row.materialId ?? ''
    receiveForm.quantity = ''
    receiveForm.materialLotId = row.materialLotId ?? ''
    receiveOpen.value = true
  }

  function openReturnDialog(row: MaterialIssueRow) {
    if (!canReturn(row)) return
    returnForm.requestId = row.requestId ?? ''
    returnForm.quantity = String(returnableQuantityOfRow(row))
    returnForm.idempotencyKey = makeIdempotencyKey(`return-${row.requestId ?? 'request'}`)
    returnOpen.value = true
  }

  async function submitIssue() {
    if (!canSubmitIssue.value || createMaterialIssueRequestPending.value) return
    const quantity = issueForm.quantity.trim() ? Number(issueForm.quantity) : undefined
    try {
      const uomCode = issueUomCode.value
      if (!uomCode) return
      await createMaterialIssueRequest({
        materialId: issueForm.materialId.trim(),
        uomCode,
        quantity,
        operationTaskId: issueForm.operationTaskId.trim() || undefined,
        // 写面重试要落在同一张领料单上：幂等键在打开弹窗时冻结，成功后才换新。
        idempotencyKey: issueForm.idempotencyKey,
      })
      issueOpen.value = false
      notifySuccess('已发起领料，仓库出库单生成后会回写到本页。')
      void refreshMaterialReadiness()
    } catch (error) {
      // 分层透传（#1298）：后端拒绝原因原样上屏，不用「操作失败」吞掉缺料/状态冲突的真因。
      notifyOperationFailure('发起领料失败', error, '发起领料失败，请稍后重试。')
    }
  }

  async function submitReceipt() {
    if (!canSubmitReceipt.value || confirmLineSideReceiptPending.value) return
    receiveError.value = ''
    const receivedQuantity = receiveForm.quantity.trim() ? Number(receiveForm.quantity) : undefined
    try {
      await confirmLineSideReceipt(receiveForm.requestId, {
        receivedQuantity,
        materialLotId: receiveForm.materialLotId.trim() || undefined,
      })
      receiveOpen.value = false
      notifySuccess('收料已受理，正在更新齐套。')
    } catch (error) {
      receiveError.value = inlineErrorMessage(error, '线边收料失败，请稍后重试。')
      notifyOperationFailure('线边收料失败', error, '线边收料失败，请稍后重试。')
    }
  }

  async function submitReturn() {
    if (!returnForm.requestId || returnLineSideMaterialPending.value) return
    const returnedQuantity = Number(returnForm.quantity)
    const row = materialIssueRows.value.find((item) => item.requestId === returnForm.requestId)
    const returnableQuantity = row ? returnableQuantityOfRow(row) : 0
    if (!Number.isFinite(returnedQuantity) || returnedQuantity <= 0) {
      notifyError('退料数量必须大于 0。')
      return
    }
    if (returnedQuantity > returnableQuantity) {
      notifyError(`退料数量不能超过当前可退数量 ${returnableQuantity}。`)
      return
    }
    try {
      await returnLineSideMaterial(returnForm.requestId, {
        returnedQuantity,
        idempotencyKey: returnForm.idempotencyKey,
      })
      returnOpen.value = false
      notifySuccess('已提交线边退料，线边可用量将随库存过账回读更新。')
      void refreshMaterialReadiness()
    } catch (error) {
      notifyOperationFailure('线边退料失败', error, '线边退料失败，请稍后重试。')
    }
  }

  return {
    canManageMaterials,
    issueOpen,
    issueForm,
    receiveOpen,
    receiveError,
    receiveForm,
    returnOpen,
    returnForm,
    materialIssueRows,
    materialIssueColumns,
    operationTaskOptions,
    issueUomCode,
    issueUomBlockReason,
    canSubmitIssue,
    canSubmitReceipt,
    openIssueDialog,
    canReceive,
    returnFormMaxQuantity,
    canReturn,
    openReceiveDialog,
    openReturnDialog,
    submitIssue,
    submitReceipt,
    submitReturn,
  }
}
