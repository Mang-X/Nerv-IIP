<script setup lang="ts">
import type {
  BusinessConsoleNcrCloseRequest,
  BusinessConsoleNcrDispositionRequest,
  BusinessConsoleQualityNcrItem,
  BusinessConsoleQualityNcrDetailResponse,
} from '@nerv-iip/api-client'
import { qualitySourceTypeLabels } from '@nerv-iip/business-core'
import type { FileUploadCompletedFile, NvDataTableColumn } from '@nerv-iip/ui'
import {
  createBusinessConsoleQualityNcr,
  downloadBusinessConsoleQualityEvidenceContent,
  getBusinessConsoleQualityNcrQueryOptions,
} from '@nerv-iip/api-client'
import NcrEvidenceUpload from '@/components/quality/NcrEvidenceUpload.vue'
import BusinessDocumentApprovalPanel from '@/components/business/BusinessDocumentApprovalPanel.vue'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import SourceDocumentPicker from '@/components/business/SourceDocumentPicker.vue'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { recoverLifecycleAction } from '@/composables/shared/lifecycleAction'
import { useQualityNcrs } from '@/composables/quality/useBusinessQuality'
import { NCR_DISPOSITION_DOCUMENT_TYPE } from '@/data/approvalReference'
import {
  NCR_DISPOSITION_TYPE_OPTIONS,
  ncrDispositionRequiresCentralApproval,
  ncrDispositionRequiresEvidence,
} from '@/data/qualityReference'
import {
  labelFor,
  NCR_STATUS_LABELS,
  QUALITY_SOURCE_TYPE_LABELS,
  REWORK_WORK_ORDER_CREATION_STATUS_LABELS,
} from '@/data/businessLabels'
import { usePagedList } from '@/composables/shared/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useAuthStore } from '@/stores/auth'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
  notifyWarning,
} from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvAlertDialogTrigger,
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialogTitle,
  NvDialogHeader,
  NvDialogFooter,
  NvDialogDescription,
  NvDialogContent,
  NvDialog,
  NvDropdownMenuItem,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, RefreshCwIcon, SendIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '不合格品处理',
    requiredPermissions: ['business.quality.ncr.read'],
  },
})

const route = useRoute()
const auth = useAuthStore()
const initialNcrKeyword = firstQuery(route.query.ncrId)
const {
  closeNcr,
  closeNcrPending,
  filters,
  ncrActionGate,
  ncrs,
  ncrsError,
  ncrsPending,
  ncrsTotal,
  refreshNcrs,
  submitDisposition,
  submitDispositionPending,
} = useQualityNcrs(initialNcrKeyword ? { keyword: initialNcrKeyword } : {})
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})

const selectedNcr = shallowRef<BusinessConsoleQualityNcrDetailResponse>()
const detailOpen = shallowRef(false)
const evidenceFiles = shallowRef<FileUploadCompletedFile[]>([])
const evidenceBusy = shallowRef(false)
const evidenceDownloadPending = shallowRef(false)
const dispositionOpening = shallowRef(0)
watch([detailOpen, () => filters.organizationId, () => filters.environmentId], () => {
  evidenceFiles.value = []
  evidenceBusy.value = false
})
const reworkSubmissionOutcome = shallowRef<'idle' | 'processing' | 'failed' | 'unknown'>('idle')
/**
 * 关闭不合格品的二次确认框。原先是**非受控**（只靠 `NvAlertDialogTrigger` 开、
 * `NvAlertDialogAction` 关），关框时机因此完全落在组件里：点确认即无条件关框
 * （confirm-destroy 规则 3），关单失败时用户连原因表单都看不见了。改为受控后
 * 由 `submitCloseNcr` 决定——成功才关，失败留在原地可重试。
 */
const closeConfirmOpen = shallowRef(false)
// 值必须与后端 NonconformanceReport 的状态字完全一致（读面按 Status 精确匹配过滤）。
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '待处置', value: 'open' },
  { label: '处置中', value: 'disposition-in-progress' },
  { label: '已关闭', value: 'closed' },
]

const dispositionForm = reactive({
  dispositionType: 'rework',
  dispositionApprovalChainId: '',
  mrbReviewApproved: false,
  mrbComment: '',
})
const closeForm = reactive({
  reason: '',
  scrapMovementId: '',
  returnDocumentId: '',
})

// 上下文穿透：从工单带入时提供返回入口；返工工单绑定只读取 MES 回执事实。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
const contextOperationTaskId = computed(() => firstQuery(route.query.operationTaskId))
const sourceWorkOrderRoute = computed(() => ({
  path: `/mes/work-orders/${encodeURIComponent(contextWorkOrderId.value)}`,
  query: { operationTaskId: contextOperationTaskId.value || undefined },
}))
// 从 MES 质量项点具体 NCR 带入时，定位并自动打开对应 NCR 处置抽屉。
const targetNcrId = computed(() => firstQuery(route.query.ncrId))
const targetNcr = computed(() =>
  targetNcrId.value
    ? ncrs.value.find((n) => n.id === targetNcrId.value || n.code === targetNcrId.value)
    : undefined,
)
const targetNcrMissing = computed(
  () => !!targetNcrId.value && !ncrsPending.value && !targetNcr.value,
)
const locatedTargetId = shallowRef('')

const listErrorMessage = computed(() => inlineErrorMessage(ncrsError.value))
const selectedNcrId = computed(() => selectedNcr.value?.id ?? '')
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManageNcr = computed(() => permissionCodes.value.includes('business.quality.ncr.manage'))
const canReadMesWorkOrders = computed(() =>
  permissionCodes.value.includes('business.mes.work-orders.read'),
)
const reworkWorkOrderLink = computed(() => {
  const ncr = selectedNcr.value
  return canReadMesWorkOrders.value &&
    ncr?.reworkWorkOrderCreationStatus?.toLowerCase() === 'created' &&
    ncr.reworkWorkOrderId?.trim()
    ? `/mes/work-orders/${encodeURIComponent(ncr.reworkWorkOrderId)}`
    : ''
})
// 带出式录入：所选 NCR 行已有的事实一律只读呈现，不做成看起来还能改的输入位。
const ncrContextItems = computed(() => {
  const ncr = selectedNcr.value
  if (!ncr) return []
  return [
    { label: '来源单据', value: ncr.sourceDocumentId },
    { label: '来源类型', value: labelFor(QUALITY_SOURCE_TYPE_LABELS, ncr.sourceType) },
    { label: '物料', value: ncrSkuLabel(ncr.skuCode) },
    { label: '不合格数量', value: ncr.defectQuantity },
    { label: '不合格原因', value: ncr.defectReason },
    { label: '批次', value: ncr.batchNo },
    { label: '序列号', value: ncr.serialNo },
    ...(ncr.attachmentFileIds
      ? [{ label: '已关联证据', value: `${ncr.attachmentFileIds.length} 个` }]
      : []),
    {
      label: '返工工单创建状态',
      value: labelFor(
        REWORK_WORK_ORDER_CREATION_STATUS_LABELS,
        ncr.reworkWorkOrderCreationStatus,
        '未知状态',
      ),
    },
    { label: '返工工单', value: ncr.reworkWorkOrderId },
    // 关闭原因是必填的关单审计事实，已关闭的单必须回显，否则看起来像"关了但没写原因"。
    ...(ncr.closeReason ? [{ label: '关闭原因', value: ncr.closeReason }] : []),
  ]
})
/** 该处置是否需要 MRB 评审 + 中央审批链（与后端同一判定，界面据此自解释地展开录入位）。 */
const requiresCentralApproval = computed(() =>
  ncrDispositionRequiresCentralApproval(dispositionForm.dispositionType),
)
const requiresDispositionEvidence = computed(() =>
  ncrDispositionRequiresEvidence(dispositionForm.dispositionType),
)
/** MRB 评审人取当前登录人：评审是本人当场作出的结论，不允许替他人署名。 */
const mrbReviewerId = computed(() => auth.principal?.principalId ?? auth.principal?.loginName ?? '')
const mrbReviewerLabel = computed(
  () => auth.principal?.loginName || mrbReviewerId.value || '当前账号',
)
const dispositionBlockers = computed(() => {
  const blockers: string[] = []
  if (requiresCentralApproval.value) {
    if (!dispositionForm.mrbReviewApproved) blockers.push('该处置需 MRB 评审通过后才能提交。')
    if (!mrbReviewerId.value) blockers.push('当前账号无法识别评审人身份，无法署名 MRB 评审。')
    if (!isNonEmpty(dispositionForm.dispositionApprovalChainId))
      blockers.push('该处置需关联一条已批准的处置审批链。')
  }
  if (requiresDispositionEvidence.value && evidenceFiles.value.length === 0) {
    blockers.push('该处置需先上传处置证据（附件）。')
  }
  return blockers
})
function dispositionRequest(reviewedAtUtc: string): BusinessConsoleNcrDispositionRequest {
  return {
    dispositionType: dispositionForm.dispositionType.trim(),
    dispositionApprovalChainId: optionalText(dispositionForm.dispositionApprovalChainId),
    attachmentFileIds: evidenceFiles.value.map((file) => file.fileId),
    mrbReviews: requiresCentralApproval.value
      ? [
          {
            reviewerId: mrbReviewerId.value,
            decision: 'approved',
            comment: optionalText(dispositionForm.mrbComment),
            reviewedAtUtc,
          },
        ]
      : undefined,
  }
}
const canSubmitDisposition = computed(
  () =>
    hasBusinessContext(filters) &&
    canManageNcr.value &&
    !evidenceBusy.value &&
    isNonEmpty(selectedNcrId.value) &&
    isNonEmpty(dispositionForm.dispositionType) &&
    dispositionBlockers.value.length === 0 &&
    ncrActionGate(
      selectedNcrId.value,
      selectedNcr.value?.status,
      dispositionForm.dispositionType,
      'submit-disposition',
      dispositionRequest(''),
    ).executable,
)
const canCloseNcr = computed(
  () =>
    hasBusinessContext(filters) &&
    canManageNcr.value &&
    isNonEmpty(selectedNcrId.value) &&
    isNonEmpty(closeForm.reason) &&
    ncrActionGate(
      selectedNcrId.value,
      selectedNcr.value?.status,
      selectedNcr.value?.status?.toLowerCase() === 'disposition-in-progress'
        ? 'recorded'
        : undefined,
      'close',
    ).executable,
)
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})

type NcrRow = BusinessConsoleQualityNcrItem
const columns: NvDataTableColumn<NcrRow>[] = [
  { key: 'code', header: 'NCR', cellClass: 'font-medium', accessor: (r) => r.code ?? r.id ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'summary', header: '摘要', accessor: (r) => qualityItemSummary(r) },
  // 已关闭的单在列表上直接给出关单理由，不必逐条点开。
  { key: 'closeReason', header: '关闭原因', accessor: (r) => r.closeReason ?? '—' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

/** NCR 状态中文名；词表没有的状态字原样显示，不编造说法。 */
function ncrStatusLabel(status?: string | null) {
  return labelFor(NCR_STATUS_LABELS, status) || '未知'
}

async function openNcr(ncr: BusinessConsoleQualityNcrItem) {
  dispositionOpening.value++
  evidenceFiles.value = []
  evidenceBusy.value = false
  selectedNcr.value = ncr
  dispositionForm.dispositionApprovalChainId = ''
  dispositionForm.mrbReviewApproved = false
  dispositionForm.mrbComment = ''
  closeForm.reason = ''
  closeConfirmOpen.value = false
  reworkSubmissionOutcome.value = 'idle'
  detailOpen.value = true
  if (!ncr.id || !hasBusinessContext(filters)) return

  const opening = dispositionOpening.value
  const query = { organizationId: filters.organizationId, environmentId: filters.environmentId }
  const stillSelected = () =>
    detailOpen.value &&
    dispositionOpening.value === opening &&
    selectedNcr.value?.id === ncr.id &&
    filters.organizationId === query.organizationId &&
    filters.environmentId === query.environmentId

  try {
    const detailQuery = getBusinessConsoleQualityNcrQueryOptions({ path: { ncrId: ncr.id }, query })
    const response = await detailQuery.query({
      signal: new AbortController().signal,
    } as Parameters<typeof detailQuery.query>[0])
    if (!response.success || !response.data) throw new Error('未取得不合格品详情。')
    if (stillSelected() && selectedNcr.value && selectedNcr.value.attachmentFileIds == null) {
      selectedNcr.value = {
        ...selectedNcr.value,
        attachmentFileIds: response.data.attachmentFileIds,
      }
    }
  } catch (error) {
    if (stillSelected()) {
      notifyOperationFailure('证据读取失败', error, '请重新打开不合格品详情后重试。')
    }
  }
}

async function downloadNcrEvidence(fileId: string, index: number) {
  if (!hasBusinessContext(filters) || evidenceDownloadPending.value) return
  evidenceDownloadPending.value = true
  try {
    const { data, response } = await downloadBusinessConsoleQualityEvidenceContent({
      path: { fileId },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
      parseAs: 'blob',
      throwOnError: true,
    })
    if (!data) throw new Error('未取得证据文件。')
    const disposition = response.headers.get('Content-Disposition')
    const encodedName = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
    let fileName = `证据 ${index + 1}`
    if (encodedName) {
      try {
        fileName = decodeURIComponent(encodedName)
      } catch {
        // 文件名解析失败时仍可保存证据字节。
      }
    }
    const link = document.createElement('a')
    const url = URL.createObjectURL(data)
    link.href = url
    link.download = fileName
    try {
      link.click()
    } finally {
      URL.revokeObjectURL(url)
    }
  } catch (error) {
    notifyOperationFailure('证据下载失败', error, '证据下载失败，请稍后重试。')
  } finally {
    evidenceDownloadPending.value = false
  }
}

async function submitNcrDisposition() {
  if (!hasBusinessContext(filters)) {
    notifyError('业务范围尚未就绪，请稍后重试。')
    return
  }
  if (!canSubmitDisposition.value) return
  // 需中央审批的处置：后端要求 MRB 评审记录齐备且全部为 approved，缺这一段提交必 400（#1327）。
  const body = dispositionRequest(new Date().toISOString())
  const ncrId = selectedNcrId.value
  const opening = dispositionOpening.value
  const query = { organizationId: filters.organizationId, environmentId: filters.environmentId }
  const label = selectedNcr.value?.code ?? selectedNcrId.value
  try {
    await submitDisposition(ncrId, body)
  } catch (error) {
    const outcome = error && typeof error === 'object' ? (error as Record<string, unknown>) : {}
    if (outcome.pending === true) {
      reworkSubmissionOutcome.value = 'processing'
      notifyWarning('返工请求已受理，系统工单仍在创建中；请刷新查看最新状态。')
      return
    }
    if (outcome.code === 'business-operation-unconfirmed') {
      reworkSubmissionOutcome.value = 'unknown'
      notifyWarning('返工结果暂未确认；请刷新核实，重试会沿用同一操作意图。')
      return
    }
    if (
      await recoverLifecycleAction(error, {
        reset: () => {
          detailOpen.value = false
          selectedNcr.value = undefined
        },
        refresh: refreshNcrs,
        notify: (message) => notifyError(message),
      })
    ) {
      return
    }
    reworkSubmissionOutcome.value = 'failed'
    notifyOperationFailure('处置提交失败', error, '处置提交失败，请稍后重试。')
    return
  }
  reworkSubmissionOutcome.value = 'idle'
  notifySuccess(`不合格品 ${label} 处置已提交。`)
  if (body.dispositionType === 'sort-and-screen') {
    try {
      const detailQuery = getBusinessConsoleQualityNcrQueryOptions({ path: { ncrId }, query })
      const response = await detailQuery.query({
        signal: new AbortController().signal,
      } as Parameters<typeof detailQuery.query>[0])
      if (!response.success || !response.data) throw new Error('未取得处置详情。')
      if (
        detailOpen.value &&
        dispositionOpening.value === opening &&
        selectedNcrId.value === ncrId &&
        filters.organizationId === query.organizationId &&
        filters.environmentId === query.environmentId
      ) {
        selectedNcr.value = response.data
        evidenceFiles.value = []
      }
    } catch (error) {
      notifyOperationFailure('处置已提交，详情读取失败', error, '请刷新核实处置状态与已关联证据。')
      if (dispositionOpening.value === opening && selectedNcrId.value === ncrId)
        detailOpen.value = false
    }
    return
  }
  detailOpen.value = false
}

async function submitCloseNcr() {
  if (!hasBusinessContext(filters)) {
    notifyError('业务范围尚未就绪，请稍后重试。')
    return
  }
  if (!canCloseNcr.value) return
  const body: BusinessConsoleNcrCloseRequest = {
    reason: closeForm.reason.trim(),
    scrapMovementId: optionalText(closeForm.scrapMovementId),
    returnDocumentId: optionalText(closeForm.returnDocumentId),
  }
  const label = selectedNcr.value?.code ?? selectedNcrId.value
  try {
    await closeNcr(selectedNcrId.value, body)
  } catch (error) {
    if (
      await recoverLifecycleAction(error, {
        reset: () => {
          // 单据已被他人推进：确认框与抽屉一起收掉，留着也没有可重试的动作。
          closeConfirmOpen.value = false
          detailOpen.value = false
          selectedNcr.value = undefined
        },
        refresh: refreshNcrs,
        notify: (message) => notifyError(message),
      })
    ) {
      return
    }
    // 普通失败：确认框保持打开，已填的关闭原因与关联单据都还在，用户可原地重试。
    notifyOperationFailure('不合格品关闭失败', error, '不合格品关闭失败，请稍后重试。')
    return
  }
  notifySuccess(`不合格品 ${label} 已关闭。`)
  closeConfirmOpen.value = false
  detailOpen.value = false
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function ncrSkuLabel(skuCode?: string | null) {
  return skuCode === 'MES-SKU-UNRESOLVED' ? '未解析物料' : skuCode
}
function qualityItemSummary(item: BusinessConsoleQualityNcrItem) {
  const values = [
    labelFor(QUALITY_SOURCE_TYPE_LABELS, item.sourceType) || undefined,
    item.sourceDocumentId,
    ncrSkuLabel(item.skuCode),
    item.defectQuantity === undefined || item.defectQuantity === null
      ? undefined
      : String(item.defectQuantity),
    item.defectReason,
    item.batchNo,
    item.serialNo,
  ].filter(isPresent)
  return values.length ? values.join(' / ') : '无'
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function isPresent(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

watch([() => filters.organizationId, () => filters.environmentId], () => {
  dispositionOpening.value++
  if (selectedNcr.value?.attachmentFileIds) {
    selectedNcr.value = { ...selectedNcr.value, attachmentFileIds: undefined }
  }
})

watch(detailOpen, (open) => {
  if (!open) selectedNcr.value = undefined
})

watch(ncrs, (items) => {
  if (!selectedNcr.value) return
  const row = items.find((item) => item.id === selectedNcr.value?.id)
  if (row) selectedNcr.value = { ...selectedNcr.value, ...row }
})

// 带 ncrId 进入时使用后端 keyword 定位，避免目标 NCR 因分页、筛选或排序不在当前页。
watch(
  targetNcrId,
  (id) => {
    if (id) {
      filters.status = undefined
      filters.keyword = id
    } else {
      filters.keyword = undefined
    }
    locatedTargetId.value = ''
  },
  { immediate: true },
)

// 目标 NCR 出现在结果中即自动打开其处置抽屉；每个目标只自动打开一次（手动关闭后不再弹出）。
watch(
  targetNcr,
  (ncr) => {
    if (!ncr || locatedTargetId.value === targetNcrId.value) return
    locatedTargetId.value = targetNcrId.value
    openNcr(ncr)
  },
  { immediate: true },
)
const registrationOpen = shallowRef(false)
const registering = shallowRef(false)
const registration = reactive({
  sourceType: 'receiving',
  sourceDocumentId: '',
  skuCode: '',
  defectQuantity: '',
  defectReason: '',
})
function openRegistration() {
  Object.assign(registration, {
    sourceType: 'receiving',
    sourceDocumentId: '',
    skuCode: '',
    defectQuantity: '',
    defectReason: '',
  })
  registrationOpen.value = true
}
async function registerNcr() {
  if (registering.value || !canManageNcr.value || !hasBusinessContext(filters)) return
  const defectQuantity = Number(registration.defectQuantity)
  const sourceDocumentId = registration.sourceDocumentId.trim()
  const skuCode = registration.skuCode.trim()
  const defectReason = registration.defectReason.trim()
  if (
    !sourceDocumentId ||
    !skuCode ||
    !defectReason ||
    !Number.isFinite(defectQuantity) ||
    defectQuantity <= 0
  ) {
    notifyWarning('请填写来源单据、物料、不合格原因和大于 0 的数量。')
    return
  }
  registering.value = true
  try {
    const { data } = await createBusinessConsoleQualityNcr({
      body: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        sourceType: registration.sourceType,
        sourceDocumentId,
        skuCode,
        defectQuantity,
        defectReason,
      },
      throwOnError: true,
    })
    if (!data.success || !data.data?.ncrId) throw new Error(data.message || '登记不合格品失败。')
    registrationOpen.value = false
    notifySuccess(`不合格报告 ${data.data.ncrCode ?? ''} 已登记。`)
    await openNcr({
      id: data.data.ncrId,
      code: data.data.ncrCode,
      reworkWorkOrderCreationStatus: 'not-requested',
    })
    await refreshNcrs()
  } catch (error) {
    notifyOperationFailure('登记不合格品失败', error, '登记不合格品失败，请稍后重试。')
  } finally {
    registering.value = false
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="不合格品处理"
      :breadcrumbs="[{ label: '质量' }]"
      :count="`${ncrsTotal} 条 NCR`"
    >
      <template #actions>
        <NvButton v-if="canManageNcr" size="sm" type="button" @click="openRegistration">
          登记不合格品
        </NvButton>
        <NvButton
          v-if="contextWorkOrderId && canReadMesWorkOrders"
          size="sm"
          type="button"
          variant="outline"
          as-child
        >
          <RouterLink :to="sourceWorkOrderRoute">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="ncrsPending"
          @click="refreshNcrs"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="NCR 状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>
    <p v-else-if="targetNcrMissing" class="text-sm text-warning" role="status">
      未找到 NCR {{ targetNcrId }}。请确认该 NCR 是否已归档或无权访问。
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="ncrsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="ncrs"
      :row-key="(r) => r.id ?? r.code ?? '无'"
      :loading="ncrsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围内没有不合格品报告。检验判不合格或质量冻结时会在这里生成。"
    >
      <template #cell-code="{ row }">
        <span class="font-medium">{{ row.code ?? '无' }}</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="ncrStatusLabel(row.status)" />
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`NCR 操作 ${row.code ?? ''}`">
          <NvDropdownMenuItem @click="openNcr(row)">{{
            canManageNcr ? '打开处置' : '查看详情'
          }}</NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>{{ selectedNcr?.code ?? '不合格品详情' }}</NvSheetTitle>
          <!-- 不合格品事实已在下方只读区完整呈现；此处仅供读屏播报。 -->
          <NvSheetDescription class="sr-only">
            不合格品 {{ selectedNcr?.code ?? selectedNcrId }} 的处置与关闭。
          </NvSheetDescription>
        </NvSheetHeader>

        <div class="grid gap-4 px-1">
          <div class="flex items-center justify-between gap-2 rounded-lg border p-3">
            <span class="text-sm font-medium text-foreground">状态</span>
            <NvStatusBadge
              :value="selectedNcr?.status"
              :label="ncrStatusLabel(selectedNcr?.status)"
            />
          </div>

          <CarriedContextSummary label="不合格品信息" :items="ncrContextItems" />

          <div v-if="selectedNcr?.attachmentFileIds?.length" class="flex flex-wrap gap-2">
            <NvButton
              v-for="(fileId, index) in selectedNcr.attachmentFileIds"
              :key="fileId"
              type="button"
              size="sm"
              variant="outline"
              :disabled="evidenceDownloadPending || !hasBusinessContext(filters)"
              @click="downloadNcrEvidence(fileId, index)"
            >
              下载证据 {{ index + 1 }}
            </NvButton>
          </div>

          <NvButton v-if="reworkWorkOrderLink" type="button" variant="outline" as-child>
            <RouterLink :to="reworkWorkOrderLink">
              查看返工工单 {{ selectedNcr?.reworkWorkOrderId }}
            </RouterLink>
          </NvButton>

          <p v-if="!canManageNcr" class="text-sm text-muted-foreground" role="status">
            当前账号只有 NCR 查看权限，不能提交处置或关闭 NCR。
          </p>

          <form
            v-if="canManageNcr && selectedNcr?.status === 'open'"
            class="grid gap-3 rounded-lg border p-3"
            @submit.prevent="submitNcrDisposition"
          >
            <h2 class="text-base font-semibold text-foreground">提交处置</h2>
            <p
              v-if="reworkSubmissionOutcome !== 'idle'"
              class="text-sm text-muted-foreground"
              role="status"
            >
              {{
                reworkSubmissionOutcome === 'processing'
                  ? '返工请求处理中：系统工单尚未创建完成，请刷新查看。'
                  : reworkSubmissionOutcome === 'unknown'
                    ? '返工结果未知：请先刷新核实；再次提交会沿用原操作意图。'
                    : '返工请求失败：系统没有返回成功回执，可按错误提示修正后重试。'
              }}
            </p>
            <NvFieldGroup class="grid gap-3">
              <NvField>
                <NvFieldLabel>处置类型</NvFieldLabel>
                <NvSelect v-model="dispositionForm.dispositionType">
                  <NvSelectTrigger aria-label="处置类型"><NvSelectValue /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="option in NCR_DISPOSITION_TYPE_OPTIONS"
                      :key="option.value"
                      :value="option.value"
                      >{{ option.label }}</NvSelectItem
                    >
                  </NvSelectContent>
                </NvSelect>
              </NvField>

              <!-- 需中央审批的处置：MRB 评审 + 审批链是硬前置，界面在此自解释地展开录入位。 -->
              <div
                v-if="requiresCentralApproval"
                class="grid gap-3 rounded-md border bg-muted/20 p-3"
              >
                <div class="grid gap-1">
                  <h3 class="text-sm font-semibold text-foreground">MRB 评审</h3>
                  <p class="text-xs text-muted-foreground">
                    该处置属于重大处置，需 MRB 评审通过并挂一条已批准的处置审批链后才能提交。
                  </p>
                </div>
                <NvField>
                  <NvFieldLabel>评审人</NvFieldLabel>
                  <p class="text-sm text-foreground">{{ mrbReviewerLabel }}</p>
                </NvField>
                <NvField orientation="horizontal" class="justify-between">
                  <NvFieldLabel for="ncr-mrb-approved">
                    本人代表 MRB 评审通过该处置方案 <span class="text-destructive">*</span>
                  </NvFieldLabel>
                  <NvCheckbox id="ncr-mrb-approved" v-model="dispositionForm.mrbReviewApproved" />
                </NvField>
                <NvField>
                  <NvFieldLabel for="ncr-mrb-comment">评审意见</NvFieldLabel>
                  <NvInput
                    id="ncr-mrb-comment"
                    v-model="dispositionForm.mrbComment"
                    maxlength="500"
                    placeholder="说明评审依据与结论（选填）"
                  />
                </NvField>
              </div>
              <p v-else class="text-xs text-muted-foreground">
                全检挑选由质量部门内部决定，无需 MRB 评审与中央审批链。
              </p>

              <BusinessDocumentApprovalPanel
                v-if="requiresCentralApproval"
                v-model="dispositionForm.dispositionApprovalChainId"
                title="处置审批链"
                source-service="quality"
                :document-type="NCR_DISPOSITION_DOCUMENT_TYPE"
                :document-id="selectedNcr?.code ?? selectedNcr?.id"
              />
              <NvField>
                <NvFieldLabel>
                  附件<span v-if="requiresDispositionEvidence" class="text-destructive"> *</span>
                </NvFieldLabel>
                <NcrEvidenceUpload
                  v-if="detailOpen && selectedNcrId && hasBusinessContext(filters)"
                  :key="
                    JSON.stringify([
                      dispositionOpening,
                      selectedNcrId,
                      filters.organizationId,
                      filters.environmentId,
                    ])
                  "
                  :organization-id="filters.organizationId"
                  :environment-id="filters.environmentId"
                  :ncr-id="selectedNcrId"
                  :disabled="submitDispositionPending"
                  @completed="evidenceFiles = $event"
                  @busy="evidenceBusy = $event"
                />
              </NvField>
            </NvFieldGroup>
            <ul v-if="dispositionBlockers.length" class="grid gap-1" role="status">
              <li
                v-for="blocker in dispositionBlockers"
                :key="blocker"
                class="text-xs text-muted-foreground"
              >
                {{ blocker }}
              </li>
            </ul>
            <div class="flex justify-end">
              <NvButton type="submit" :disabled="submitDispositionPending || !canSubmitDisposition">
                <NvSpinner v-if="submitDispositionPending" aria-hidden="true" />
                <SendIcon v-else aria-hidden="true" />
                提交处置
              </NvButton>
            </div>
          </form>

          <form v-if="canManageNcr" class="grid gap-3 rounded-lg border p-3" @submit.prevent>
            <h2 class="text-base font-semibold text-foreground">关闭不合格品</h2>
            <RouterLink
              v-if="
                selectedNcr?.dispositionType === 'scrap' ||
                selectedNcr?.dispositionType === 'return-to-supplier'
              "
              class="text-sm underline"
              :to="{ path: '/quality/capas', query: { sourceNcrId: selectedNcrId } }"
            >
              查看关联纠正措施
            </RouterLink>
            <NvFieldGroup class="grid gap-3">
              <NvField>
                <NvFieldLabel for="ncr-close-reason">
                  关闭原因 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvInput
                  id="ncr-close-reason"
                  v-model="closeForm.reason"
                  required
                  maxlength="500"
                  placeholder="说明关闭依据与处理结果"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="ncr-scrap">报废库存移动</NvFieldLabel>
                <NvInput id="ncr-scrap" v-model="closeForm.scrapMovementId" />
              </NvField>
              <NvField>
                <NvFieldLabel for="ncr-return">退货单据</NvFieldLabel>
                <!-- 退供应商处置挂仓储的供应商退货单；报废库存移动只能按 ID 精确查，仍手填。 -->
                <SourceDocumentPicker
                  id="ncr-return"
                  v-model="closeForm.returnDocumentId"
                  kind="wms-supplier-return"
                />
              </NvField>
            </NvFieldGroup>

            <NvSheetFooter>
              <NvAlertDialog v-model:open="closeConfirmOpen">
                <NvAlertDialogTrigger as-child>
                  <NvButton
                    type="button"
                    variant="destructive"
                    :disabled="closeNcrPending || !canCloseNcr"
                  >
                    <NvSpinner v-if="closeNcrPending" aria-hidden="true" />
                    <CheckCircle2Icon v-else aria-hidden="true" />
                    关闭不合格品
                  </NvButton>
                </NvAlertDialogTrigger>
                <NvAlertDialogContent>
                  <NvAlertDialogHeader>
                    <NvAlertDialogTitle>确认关闭该不合格品？</NvAlertDialogTitle>
                    <NvAlertDialogDescription
                      >关闭后该不合格品不再进入待处理队列。</NvAlertDialogDescription
                    >
                  </NvAlertDialogHeader>
                  <NvAlertDialogFooter>
                    <NvAlertDialogCancel>取消</NvAlertDialogCancel>
                    <!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框
                         （confirm-destroy 规则 3），关单失败时表单连带确认框一起消失。
                         `type="button"` 不能省——本框在 <form> 里，缺了会顺带触发表单提交。
                         variant 不写 = NvAlertDialogAction 的默认值 'default'，换件不改外观。 -->
                    <NvButton type="button" :disabled="closeNcrPending" @click="submitCloseNcr">
                      <NvSpinner v-if="closeNcrPending" aria-hidden="true" />
                      确认关闭
                    </NvButton>
                  </NvAlertDialogFooter>
                </NvAlertDialogContent>
              </NvAlertDialog>
            </NvSheetFooter>
          </form>
        </div>
      </NvSheetContent>
    </NvSheet>
    <NvDialog v-model:open="registrationOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>登记不合格品</NvDialogTitle>
          <NvDialogDescription
            >已有不合格检验记录时，可从该记录详情开立关联报告。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="registerNcr">
          <NvField>
            <NvFieldLabel>来源类型</NvFieldLabel>
            <NvSelect v-model="registration.sourceType">
              <NvSelectTrigger aria-label="来源类型"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="(label, value) in qualitySourceTypeLabels"
                  :key="value"
                  :value="value"
                >
                  {{ label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="ncr-sourceDocumentId">来源单据</NvFieldLabel>
            <NvInput
              id="ncr-sourceDocumentId"
              v-model="registration.sourceDocumentId"
              type="text"
              required
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="ncr-skuCode">物料编码</NvFieldLabel>
            <NvInput id="ncr-skuCode" v-model="registration.skuCode" type="text" required />
          </NvField>
          <NvField>
            <NvFieldLabel for="ncr-defectQuantity">不合格数量</NvFieldLabel>
            <NvInput
              id="ncr-defectQuantity"
              v-model="registration.defectQuantity"
              type="number"
              step="any"
              required
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="ncr-defectReason">不合格原因</NvFieldLabel>
            <NvInput
              id="ncr-defectReason"
              v-model="registration.defectReason"
              type="text"
              required
            />
          </NvField>
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="registrationOpen = false"
              >取消</NvButton
            >
            <NvButton type="submit" :disabled="registering">登记</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
