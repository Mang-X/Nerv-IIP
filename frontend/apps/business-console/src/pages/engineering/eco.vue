<script setup lang="ts">
import {
  cancelScheduledBusinessConsoleEngineeringChange,
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringDocumentsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  rescheduleBusinessConsoleEngineeringChange,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import type {
  BusinessConsoleEngineeringChangeImpactNode,
  BusinessConsoleEngineeringChangeImpactRisk,
  BusinessConsoleEngineeringChangeItem,
  BusinessConsoleReleaseEngineeringChangeRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell, StatusTone } from '@nerv-iip/ui'
import BusinessDocumentApprovalPanel from '@/components/business/BusinessDocumentApprovalPanel.vue'
import EngineeringWorkOrderImpacts from '@/components/mes/EngineeringWorkOrderImpacts.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useEngineeringChanges } from '@/composables/engineering'
import { PUBLISHED } from '@/composables/engineering/engineeringShared'
import {
  useEngineeringVersionCatalog,
  usePublishedMbomCatalog,
  usePublishedRoutingCatalog,
} from '@/composables/engineering/useEngineeringVersionCatalog'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ENGINEERING_CHANGE_ORDER_DOCUMENT_TYPE,
  ENGINEERING_CHANGE_ORDER_TEMPLATE_CODE,
} from '@/data/approvalReference'
import {
  NvButton,
  NvDataTable,
  NvDatePicker,
  NvDialog,
  NvDialogBody,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { NetworkIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { formatDate, today } from '@/utils/format'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '工程变更',
    requiredPermissions: ['business.engineering.changes.read'],
  },
})

const {
  changes,
  changesError,
  changesPending,
  changesTotal,
  filters,
  refresh,
  releaseChange,
  releasePending,
  previewImpact,
  previewPending,
  impactPreview,
  clearImpactPreview,
  fetchChangeDetail,
} = useEngineeringChanges()

// 受影响版本的对象种类（versionKind）。后端按字符串接收，这里给贴切的工程对象枚举。
const VERSION_KIND_OPTIONS = [
  { label: '设计 BOM（EBOM）', value: 'EngineeringBom' },
  { label: '制造 BOM（MBOM）', value: 'ManufacturingBom' },
  { label: '工艺路线', value: 'Routing' },
  { label: '生产版本', value: 'ProductionVersion' },
  { label: '工程文档', value: 'EngineeringDocument' },
]
function versionKindLabel(kind?: string | null) {
  const key = kind?.trim().toLowerCase().replace(/[-_]/g, '')
  return (
    VERSION_KIND_OPTIONS.find((option) => option.value.toLowerCase() === key)?.label ?? '未知类型'
  )
}

const ebomCatalog = useEngineeringVersionCatalog(
  filters,
  ({ skuCode: _skuCode, ...query }) =>
    listBusinessConsoleEngineeringBomsQueryOptions({ query: { ...query, status: PUBLISHED } }),
  (row) => JSON.stringify([row.bomCode, row.revision]),
)
const mbomCatalog = usePublishedMbomCatalog(filters)
const routingCatalog = usePublishedRoutingCatalog(filters)
const productionCatalog = useEngineeringVersionCatalog(
  filters,
  (query) => listBusinessConsoleEngineeringProductionVersionsQueryOptions({ query }),
  (row) => row.productionVersionId ?? '',
)
const documentCatalog = useEngineeringVersionCatalog(
  filters,
  ({ skuCode: _skuCode, ...query }) =>
    listBusinessConsoleEngineeringDocumentsQueryOptions({ query }),
  (row) => JSON.stringify([row.documentNumber, row.revision]),
)
const productionVersionOptions = computed(() =>
  productionCatalog.items.value
    .filter((row) => !!row.productionVersionId)
    .map((row) => ({
      value: row.productionVersionId as string,
      label: row.validFrom
        ? `${row.skuCode ?? '未知物料'} · 生效 ${row.validFrom.slice(0, 10)}`
        : (row.skuCode ?? '未知物料'),
      ...(row.isDefault ? { hint: '默认版本' } : {}),
    })),
)

function versionOption(code?: string | null, revision?: string | null, owner?: string | null) {
  if (!code || !revision) return []
  return [
    {
      value: `${code}:${revision}`,
      label: [code, `修订 ${revision}`, owner].filter(Boolean).join(' · '),
    },
  ]
}

const versionCatalogs = {
  EngineeringBom: {
    options: computed(() =>
      ebomCatalog.items.value.flatMap((row) =>
        versionOption(row.bomCode, row.revision, row.parentItemCode),
      ),
    ),
    pending: ebomCatalog.pending,
    error: ebomCatalog.error,
    refresh: ebomCatalog.refresh,
  },
  ManufacturingBom: {
    options: computed(() =>
      mbomCatalog.items.value.flatMap((row) =>
        versionOption(row.bomCode, row.revision, row.skuCode),
      ),
    ),
    pending: mbomCatalog.pending,
    error: mbomCatalog.error,
    refresh: mbomCatalog.refresh,
  },
  Routing: {
    options: computed(() =>
      routingCatalog.items.value.flatMap((row) =>
        versionOption(row.routingCode, row.revision, row.skuCode),
      ),
    ),
    pending: routingCatalog.pending,
    error: routingCatalog.error,
    refresh: routingCatalog.refresh,
  },
  ProductionVersion: {
    options: productionVersionOptions,
    pending: productionCatalog.pending,
    error: productionCatalog.error,
    refresh: productionCatalog.refresh,
  },
  EngineeringDocument: {
    options: computed(() =>
      documentCatalog.items.value.flatMap((row) =>
        versionOption(row.documentNumber, row.revision, row.itemCode),
      ),
    ),
    pending: documentCatalog.pending,
    error: documentCatalog.error,
    refresh: documentCatalog.refresh,
  },
}

function versionCatalog(kind?: string | null) {
  const key = kind?.trim().toLowerCase().replace(/[-_]/g, '')
  return Object.entries(versionCatalogs).find(([name]) => name.toLowerCase() === key)?.[1]
}

function affectedVersionLabel(kind?: string | null, versionId?: string | null) {
  if (!versionId) return '—'
  return (
    versionCatalog(kind)?.options.value.find((option) => option.value === versionId)?.label ??
    (kind?.trim().toLowerCase().replace(/[-_]/g, '') === 'productionversion'
      ? '暂无法显示版本名称'
      : versionId)
  )
}

// 后端是一步发布（Open→Approve→Release），变更落库即为已发布状态——不假造草稿/待审。
const STATUS_FILTER_OPTIONS = [
  { label: '全部状态', value: 'all' },
  { label: '计划生效', value: 'Scheduled' },
  { label: '已发布', value: 'Published' },
  { label: '已取消', value: 'Cancelled' },
]
const statusFilter = ref('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch(
  [page, pageSize],
  () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  },
  { immediate: true },
)

// 工程变更状态词表：后端主链是一步发布（Open→Approve→Release），但历史/导入数据里
// 仍会出现 Draft / Scheduled 这类生命周期取值，缺词就会把英文码印到「状态」列上。
const ECO_STATUS_DISPLAY: Record<string, { label: string; tone: StatusTone }> = {
  draft: { label: '草稿', tone: 'warning' },
  open: { label: '待处理', tone: 'warning' },
  submitted: { label: '已提交', tone: 'warning' },
  approved: { label: '已批准', tone: 'info' },
  scheduled: { label: '计划生效', tone: 'info' },
  released: { label: '已发布', tone: 'success' },
  published: { label: '已发布', tone: 'success' },
  effective: { label: '已生效', tone: 'success' },
  archived: { label: '已归档', tone: 'neutral' },
  cancelled: { label: '已取消', tone: 'neutral' },
  rejected: { label: '已驳回', tone: 'danger' },
}

function ecoStatus(status?: string | null): { label: string; tone: StatusTone } {
  const raw = (status ?? '').trim()
  if (!raw) return { label: '已发布', tone: 'success' }
  const hit = ECO_STATUS_DISPLAY[raw.toLowerCase().replace(/[-_\s]/g, '')]
  if (hit) return hit
  if (import.meta.env.DEV) console.warn(`[工程变更] 词表缺失: ${raw}，请补 ECO_STATUS_DISPLAY`)
  return { label: raw, tone: 'neutral' }
}

const releasedCount = computed(() => changes.value.length)
const affectedTotal = computed(() =>
  changes.value.reduce((sum, c) => sum + (c.affectedVersions?.length ?? 0), 0),
)

const ecoCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'released',
    label: '已发布变更',
    value: releasedCount.value,
    unit: '个',
    meta: '当前筛选范围内已生效',
  },
  {
    key: 'affected',
    label: '受影响版本',
    value: affectedTotal.value,
    unit: '个',
    meta: '这些变更累计触及的 BOM / 路线版本',
  },
])

const listErrorMessage = computed(() => inlineErrorMessage(changesError.value))

const columns: NvDataTableColumn<BusinessConsoleEngineeringChangeItem>[] = [
  { key: 'changeNumber', header: '变更号', cellClass: 'font-medium' },
  { key: 'reason', header: '变更原因' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'affected', header: '受影响版本', width: 'w-28', align: 'end' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]
const impactColumns: NvDataTableColumn<BusinessConsoleEngineeringChangeImpactNode>[] = [
  { key: 'nodeType', header: '对象', width: 'w-36' },
  { key: 'displayName', header: '影响节点', width: 'w-48', cellClass: 'font-medium' },
  { key: 'impactLevel', header: '级别', width: 'w-28' },
  { key: 'skuCode', header: 'SKU', width: 'w-32' },
  { key: 'route', header: '跳转', align: 'end', width: 'w-24' },
]
const riskColumns: NvDataTableColumn<BusinessConsoleEngineeringChangeImpactRisk>[] = [
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'message', header: '风险提示', accessor: (row) => riskMessage(row) },
]

// ── 发布变更向导（一步发布，非多步审批）────────────────────────
interface AffectedRow {
  versionKind: string
  versionId: string
}
interface EcoForm {
  changeNumber: string
  reason: string
  approvalReferenceId: string
  effectiveDate: string | null
  affectedVersions: AffectedRow[]
}
function selectVersionKind(row: AffectedRow, value: unknown) {
  if (typeof value !== 'string' || value === row.versionKind) return
  row.versionKind = value
  row.versionId = ''
}

function blankAffected(): AffectedRow {
  return { versionKind: '', versionId: '' }
}
function blankForm(): EcoForm {
  return {
    changeNumber: '',
    reason: '',
    approvalReferenceId: '',
    effectiveDate: today(),
    affectedVersions: [blankAffected()],
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<EcoForm>(blankForm())

// 对象种类是上游：换了种类，原来那条版本标识必然作废，清空重选。
// 只在行数不变时比对，避免增删行导致的索引位移误清。
watch(
  () => form.affectedVersions.map((row) => row.versionKind),
  (kinds, previousKinds) => {
    if (!previousKinds || previousKinds.length !== kinds.length) return
    kinds.forEach((kind, index) => {
      if (previousKinds[index] !== kind) form.affectedVersions[index].versionId = ''
    })
  },
)

const reasonValid = computed(() => form.reason.trim().length > 0)
const changeNumberValid = computed(() => form.changeNumber.trim().length > 0)
const approvalValid = computed(() => form.approvalReferenceId.trim().length > 0)
const effectiveValid = computed(() => !!form.effectiveDate)
function affectedValid(row: AffectedRow) {
  const id = row.versionId.trim()
  if (!row.versionKind || !id) return false
  if (row.versionKind === 'ProductionVersion') return true
  const separator = id.indexOf(':')
  return separator > 0 && !!id.slice(0, separator).trim() && !!id.slice(separator + 1).trim()
}
function affectedVersionOptions(row: AffectedRow) {
  const options = versionCatalog(row.versionKind)?.options.value ?? []
  const value = row.versionId.trim()
  return affectedValid(row) && !options.some((option) => option.value === value)
    ? [{ value, label: value, hint: '手动指定' }, ...options]
    : options
}

const affectedListValid = computed(
  () => form.affectedVersions.length > 0 && form.affectedVersions.every(affectedValid),
)
const canSubmit = computed(
  () =>
    changeNumberValid.value &&
    reasonValid.value &&
    approvalValid.value &&
    effectiveValid.value &&
    affectedListValid.value,
)
const canPreview = computed(() => effectiveValid.value && affectedListValid.value)
const impactNodes = computed(() => impactPreview.value?.nodes ?? [])
const impactRisks = computed(() => impactPreview.value?.risks ?? [])

function openCreate() {
  Object.assign(form, blankForm())
  clearImpactPreview()
  showErrors.value = false
  formOpen.value = true
}
function addAffected() {
  form.affectedVersions.push(blankAffected())
}
function removeAffected(index: number) {
  if (form.affectedVersions.length <= 1) return
  form.affectedVersions.splice(index, 1)
}

async function previewFormImpact() {
  if (!canPreview.value) {
    showErrors.value = true
    return
  }
  try {
    await previewImpact({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      effectiveDate: form.effectiveDate ?? undefined,
      affectedVersions: form.affectedVersions.map((row) => ({
        versionKind: row.versionKind.trim(),
        versionId: row.versionId.trim(),
      })),
    })
  } catch (error) {
    notifyOperationFailure('预览变更影响失败', error, '预览变更影响失败，请稍后重试。')
  }
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const body: BusinessConsoleReleaseEngineeringChangeRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    changeNumber: form.changeNumber.trim(),
    reason: form.reason.trim(),
    approvalReferenceId: form.approvalReferenceId.trim(),
    effectiveDate: form.effectiveDate ?? undefined,
    affectedVersions: form.affectedVersions.map((row) => ({
      versionKind: row.versionKind.trim(),
      versionId: row.versionId.trim(),
    })),
  }
  try {
    await releaseChange(body)
    notifySuccess(
      form.effectiveDate && form.effectiveDate > today()
        ? `已提交工程变更，计划生效于 ${formatDate(form.effectiveDate)}。`
        : `已发布工程变更，受影响版本 ${form.affectedVersions.length} 个。`,
    )
    showErrors.value = false
    formOpen.value = false
  } catch (error) {
    notifyOperationFailure('发布工程变更失败', error, '发布工程变更失败，请稍后重试。')
  }
}

// ── 查看变更明细（get-by-id 看受影响版本）──────────────────────
const route = useRoute()
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringChangeItem | null>(null)
const detailPending = ref(false)
const auth = useAuthStore()
const canManageSchedule = computed(() =>
  auth.principal?.permissionCodes?.includes('business.engineering.changes.manage'),
)
const scheduleAction = ref<'reschedule' | 'cancel'>()
const scheduleReason = ref('')
const scheduleDate = ref<string | null>(null)
const schedulePending = ref(false)

function openScheduleAction(action: 'reschedule' | 'cancel') {
  scheduleReason.value = ''
  scheduleDate.value = viewTarget.value?.effectiveDate ?? null
  scheduleAction.value = action
}

async function submitScheduleAction() {
  const action = scheduleAction.value
  const changeNumber = viewTarget.value?.changeNumber
  const reason = scheduleReason.value.trim()
  const effectiveDate = scheduleDate.value
  if (!action || !changeNumber || !reason || schedulePending.value) return
  if (action === 'reschedule' && (!effectiveDate || effectiveDate <= today())) return

  schedulePending.value = true
  const body = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    changeNumber,
    reason,
  }
  try {
    const { data } =
      effectiveDate && action === 'reschedule'
        ? await rescheduleBusinessConsoleEngineeringChange({
            body: { ...body, effectiveDate },
            throwOnError: true,
          })
        : await cancelScheduledBusinessConsoleEngineeringChange({ body, throwOnError: true })
    if (!data.success) throw data
    scheduleAction.value = undefined
    viewOpen.value = false
    notifySuccess(action === 'reschedule' ? '工程变更已改期。' : '工程变更调度已取消。')
    await refresh()
  } catch (error) {
    notifyError(error, '更新工程变更调度失败，请稍后重试。')
  } finally {
    schedulePending.value = false
  }
}

const releasedChangeNumber = computed(() => {
  const status = viewTarget.value?.status?.toLowerCase()
  return viewOpen.value && !detailPending.value && (status === 'published' || status === 'released')
    ? viewTarget.value?.changeNumber
    : undefined
})
const viewAffected = computed(() => viewTarget.value?.affectedVersions ?? [])
let detailRequest = 0
async function openView(changeNumber?: string | null, row?: BusinessConsoleEngineeringChangeItem) {
  if (!changeNumber) return
  const request = ++detailRequest
  const { organizationId, environmentId } = filters
  const isCurrent = () =>
    request === detailRequest &&
    viewOpen.value &&
    organizationId === filters.organizationId &&
    environmentId === filters.environmentId

  viewTarget.value = row ?? null
  viewOpen.value = true
  detailPending.value = true
  try {
    const detail = await fetchChangeDetail(changeNumber)
    if (!isCurrent()) return
    if (detail?.changeNumber === changeNumber) {
      viewTarget.value = detail
    } else if (!row) {
      notifyError(undefined, '未找到该工程变更。')
      viewOpen.value = false
    }
  } catch (error) {
    if (!isCurrent()) return
    notifyError(error, '加载工程变更详情失败，请稍后重试。')
    if (!row) viewOpen.value = false
  } finally {
    if (request === detailRequest) detailPending.value = false
  }
}

watch(
  [() => route.query.changeNumber, () => filters.organizationId, () => filters.environmentId],
  ([changeNumber]) => {
    detailRequest++
    viewOpen.value = false
    viewTarget.value = null
    detailPending.value = false
    scheduleAction.value = undefined
    if (typeof changeNumber === 'string' && changeNumber.trim() && hasBusinessContext(filters)) {
      void openView(changeNumber.trim())
    }
  },
  { immediate: true },
)

function impactNodeTypeLabel(type?: string | null) {
  const labels: Record<string, string> = {
    'engineering-bom': 'EBOM',
    'manufacturing-bom': 'MBOM',
    routing: '工艺路线',
    'production-version': '生产版本',
    'mrp-candidate': 'MRP 候选',
    'mes-work-order-candidate': 'MES 工单',
    'aps-plan-candidate': 'APS 排程',
  }
  return labels[(type ?? '').toLowerCase()] ?? (type || '影响节点')
}

function impactLevelTone(level?: string | null): StatusTone {
  const normalized = (level ?? '').toLowerCase()
  if (normalized === 'direct') return 'danger'
  if (normalized === 'derived') return 'warning'
  if (normalized === 'downstream' || normalized === 'candidate') return 'info'
  return 'info'
}

function impactLevelLabel(level?: string | null) {
  const labels: Record<string, string> = {
    direct: '直接',
    derived: '派生',
    downstream: '下游',
    candidate: '候选',
  }
  return labels[(level ?? '').toLowerCase()] ?? (level || '候选')
}

function riskSeverityLabel(severity?: string | null) {
  const labels: Record<string, string> = { critical: '严重', error: '严重', warning: '警告' }
  return labels[(severity ?? '').toLowerCase()] ?? '提示'
}

// 影响预览的风险说明有三条是英文原文，按风险码给中文说法；其余沿用服务端给的中文。
const RISK_MESSAGES: Record<string, string> = {
  'unsupported-affected-version': '该类受影响版本暂不支持影响预览。',
  'invalid-production-version-id': '生产版本标识无效，请重新选择生产版本。',
  'production-version-not-found': '找不到该生产版本，请确认版本是否存在。',
}
function riskMessage(risk: BusinessConsoleEngineeringChangeImpactRisk) {
  return RISK_MESSAGES[risk.code ?? ''] ?? risk.message ?? '—'
}

function riskTone(severity?: string | null): StatusTone {
  const normalized = (severity ?? '').toLowerCase()
  if (normalized === 'error' || normalized === 'critical') return 'danger'
  if (normalized === 'warning') return 'warning'
  return 'info'
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工程变更"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${changesTotal} 个变更`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="changesPending"
          @click="refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="formOpen">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              发布变更
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent scrollable class="sm:max-w-3xl">
            <NvDialogHeader>
              <NvDialogTitle>发布工程变更</NvDialogTitle>
              <!-- 说明不上界面：仅供读屏播报。 -->
              <NvDialogDescription class="sr-only">
                填写变更原因、审批链、生效日与受影响版本。
              </NvDialogDescription>
            </NvDialogHeader>
            <form @submit.prevent="submitForm">
              <NvDialogBody class="grid gap-5">
                <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                  请完整填写带 * 的必填项，并确保至少一条受影响版本填好对象种类与版本。
                </p>
                <p
                  v-if="showErrors && !changeNumberValid"
                  class="text-sm text-destructive"
                  role="alert"
                >
                  请先填写变更号，审批链必须绑定到同一个 ECO 单据。
                </p>

                <FormSectionTitle>变更信息</FormSectionTitle>
                <NvFieldGroup class="grid gap-3">
                  <NvField :data-invalid="showErrors && !changeNumberValid">
                    <NvFieldLabel for="eco-change-number"
                      >变更号 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvInput
                      id="eco-change-number"
                      v-model="form.changeNumber"
                      placeholder="例如 ECO-20260801-000001"
                    />
                    <p class="text-xs text-muted-foreground">
                      先确定本次 ECO 号，审批链会以此单号绑定；审批通过后再发布变更。
                    </p>
                  </NvField>
                  <NvField :data-invalid="showErrors && !reasonValid">
                    <NvFieldLabel for="eco-reason"
                      >变更原因 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvInput
                      id="eco-reason"
                      v-model="form.reason"
                      placeholder="说明本次变更的内容与原因"
                    />
                  </NvField>
                  <BusinessDocumentApprovalPanel
                    v-model="form.approvalReferenceId"
                    title="变更审批链"
                    source-service="product-engineering"
                    :document-type="ENGINEERING_CHANGE_ORDER_DOCUMENT_TYPE"
                    :document-id="form.changeNumber || undefined"
                    :preferred-template-code="ENGINEERING_CHANGE_ORDER_TEMPLATE_CODE"
                    :allow-start="true"
                  />
                  <p
                    v-if="showErrors && !approvalValid"
                    class="text-sm text-destructive"
                    role="alert"
                  >
                    请先关联一条真实审批链，再发布工程变更。
                  </p>
                  <div class="grid gap-3 sm:grid-cols-2">
                    <NvField :data-invalid="showErrors && !effectiveValid">
                      <NvFieldLabel for="eco-effective-date">
                        生效日 <span class="text-destructive">*</span>
                      </NvFieldLabel>
                      <NvDatePicker
                        id="eco-effective-date"
                        v-model="form.effectiveDate"
                        aria-describedby="eco-effective-date-help"
                        placeholder="选择生效日"
                        class="w-full"
                      />
                      <NvFieldDescription id="eco-effective-date-help">
                        选择未来日期，提交后进入「计划生效」。此次变更在生效前不会更新下游版本。
                      </NvFieldDescription>
                    </NvField>
                  </div>
                </NvFieldGroup>

                <div class="flex items-center justify-between">
                  <FormSectionTitle>受影响版本</FormSectionTitle>
                  <NvButton type="button" variant="outline" size="sm" @click="addAffected">
                    <PlusIcon aria-hidden="true" />
                    增加一条
                  </NvButton>
                </div>
                <div class="grid gap-2">
                  <div
                    v-for="(row, index) in form.affectedVersions"
                    :key="index"
                    class="grid sm:grid-cols-[12rem_minmax(0,1fr)_auto] items-end gap-2 rounded-md border p-2"
                  >
                    <NvField :data-invalid="showErrors && !row.versionKind.trim()">
                      <NvFieldLabel :for="`eco-kind-${index}`"
                        >对象种类 <span class="text-destructive">*</span></NvFieldLabel
                      >
                      <NvSelect
                        :model-value="row.versionKind"
                        @update:model-value="selectVersionKind(row, $event)"
                      >
                        <NvSelectTrigger :id="`eco-kind-${index}`" class="min-w-0"
                          ><NvSelectValue placeholder="选择对象"
                        /></NvSelectTrigger>
                        <NvSelectContent>
                          <NvSelectItem
                            v-for="o in VERSION_KIND_OPTIONS"
                            :key="o.value"
                            :value="o.value"
                            >{{ o.label }}</NvSelectItem
                          >
                        </NvSelectContent>
                      </NvSelect>
                    </NvField>
                    <NvField :data-invalid="showErrors && !row.versionId.trim()">
                      <NvFieldLabel :for="`eco-vid-${index}`"
                        >受影响版本 <span class="text-destructive">*</span></NvFieldLabel
                      >
                      <NvEntityPicker
                        :id="`eco-vid-${index}`"
                        :model-value="row.versionId.trim()"
                        :options="affectedVersionOptions(row)"
                        @update:model-value="row.versionId = String($event ?? '')"
                        title="选择受影响版本"
                        :placeholder="row.versionKind ? '选择受影响版本' : '请先选择对象类型'"
                        :empty-text="
                          versionCatalog(row.versionKind)?.error.value
                            ? '版本目录加载失败'
                            : '暂无可选版本'
                        "
                        no-match-text="没有匹配的版本"
                        :disabled="!row.versionKind"
                        :loading="versionCatalog(row.versionKind)?.pending.value"
                        :invalid="showErrors && !affectedValid(row)"
                        aria-label="受影响版本"
                        clearable
                        :show-code="false"
                      />
                      <details v-if="row.versionKind" class="text-sm">
                        <summary class="cursor-pointer text-muted-foreground">直接输入版本</summary>
                        <NvInput
                          v-model="row.versionId"
                          :aria-label="
                            row.versionKind === 'ProductionVersion' ? '生产版本 ID' : '编号:修订'
                          "
                          :placeholder="
                            row.versionKind === 'ProductionVersion'
                              ? '输入生产版本 ID'
                              : '输入编号:修订'
                          "
                          :aria-invalid="showErrors && !affectedValid(row)"
                        />
                      </details>
                      <NvButton
                        v-if="versionCatalog(row.versionKind)?.error.value"
                        type="button"
                        variant="outline"
                        size="sm"
                        @click="versionCatalog(row.versionKind)?.refresh()"
                      >
                        重试加载版本
                      </NvButton>
                    </NvField>
                    <NvButton
                      type="button"
                      variant="ghost"
                      size="icon"
                      aria-label="删除该受影响版本"
                      :disabled="form.affectedVersions.length <= 1"
                      @click="removeAffected(index)"
                    >
                      <Trash2Icon aria-hidden="true" />
                    </NvButton>
                  </div>
                </div>

                <div class="grid gap-3 rounded-md border bg-muted/20 p-3">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <FormSectionTitle>发布前影响预览</FormSectionTitle>
                    <NvButton
                      type="button"
                      variant="outline"
                      :disabled="previewPending"
                      @click="previewFormImpact"
                    >
                      <NvSpinner v-if="previewPending" aria-hidden="true" />
                      <NetworkIcon v-else aria-hidden="true" />
                      预览影响
                    </NvButton>
                  </div>

                  <NvDataTable
                    :columns="impactColumns"
                    :rows="impactNodes"
                    :row-key="
                      (row) => `${row.nodeType}:${row.versionId}:${row.relatedVersionId ?? ''}`
                    "
                    :loading="previewPending"
                    :searchable="false"
                    :column-settings="false"
                    empty-message="填写受影响版本后可预览影响链。"
                  >
                    <template #cell-nodeType="{ row }">{{
                      impactNodeTypeLabel(row.nodeType)
                    }}</template>
                    <template #cell-impactLevel="{ row }">
                      <NvStatusBadge
                        :label="impactLevelLabel(row.impactLevel)"
                        :tone="impactLevelTone(row.impactLevel)"
                      />
                    </template>
                    <template #cell-route="{ row }">
                      <RouterLink
                        v-if="row.consoleRoute"
                        class="text-primary underline-offset-4 hover:underline"
                        :to="row.consoleRoute"
                        >打开</RouterLink
                      >
                      <span v-else class="text-muted-foreground">暂无入口</span>
                    </template>
                  </NvDataTable>

                  <NvDataTable
                    v-if="impactRisks.length"
                    :columns="riskColumns"
                    :rows="impactRisks"
                    :row-key="(row) => `${row.code}:${row.relatedVersionId ?? ''}`"
                    :searchable="false"
                    :column-settings="false"
                    empty-message="没有风险提示。"
                  >
                    <template #cell-severity="{ row }">
                      <NvStatusBadge
                        :label="riskSeverityLabel(row.severity)"
                        :tone="riskTone(row.severity)"
                      />
                    </template>
                  </NvDataTable>
                </div>
              </NvDialogBody>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="formOpen = false">取消</NvButton>
                <NvButton type="submit" :disabled="releasePending">
                  <NvSpinner v-if="releasePending" aria-hidden="true" />
                  发布变更
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="ecoCells" />

    <NvToolbar>
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="状态筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="o in STATUS_FILTER_OPTIONS" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="changesTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="changes"
      :row-key="(r) => r.changeNumber ?? ''"
      :loading="changesPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有工程变更。可发布变更，登记变更原因、审批参考与受影响版本。"
    >
      <template #cell-reason="{ row }">
        <span class="line-clamp-2 text-sm">{{ row.reason || '—' }}</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :label="ecoStatus(row.status).label" :tone="ecoStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{
        row.effectiveDate
          ? `${row.status?.toLowerCase() === 'scheduled' ? '计划生效于 ' : ''}${formatDate(row.effectiveDate)}`
          : '即时'
      }}</template>
      <template #cell-affected="{ row }">
        <span class="tabular-nums">{{ row.affectedVersions?.length ?? 0 }}</span>
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <NvButton type="button" variant="ghost" size="sm" @click="openView(row.changeNumber, row)"
            >查看</NvButton
          >
        </div>
      </template>
    </NvDataTable>

    <NvDialog
      v-if="scheduleAction"
      :open="true"
      @update:open="
        (open) => {
          if (!open) scheduleAction = undefined
        }
      "
    >
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>{{
            scheduleAction === 'reschedule' ? '确认工程变更改期' : '确认取消工程变更调度'
          }}</NvDialogTitle>
          <NvDialogDescription>{{ viewTarget?.changeNumber }}</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-3" @submit.prevent="submitScheduleAction">
          <NvField v-if="scheduleAction === 'reschedule'">
            <NvFieldLabel for="eco-schedule-date">新生效日</NvFieldLabel>
            <NvDatePicker id="eco-schedule-date" v-model="scheduleDate" />
          </NvField>
          <NvField>
            <NvFieldLabel for="eco-schedule-reason">原因</NvFieldLabel>
            <NvInput id="eco-schedule-reason" v-model="scheduleReason" required maxlength="500" />
          </NvField>
          <NvDialogFooter>
            <NvButton
              type="submit"
              :disabled="
                schedulePending ||
                !scheduleReason.trim() ||
                (scheduleAction === 'reschedule' && (!scheduleDate || scheduleDate <= today()))
              "
            >
              确认
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvSheet v-model:open="viewOpen">
      <NvSheetContent class="sm:max-w-lg">
        <NvSheetHeader>
          <NvSheetTitle>工程变更详情</NvSheetTitle>
          <NvSheetDescription>
            {{ viewTarget ? `${viewTarget.changeNumber} · ${viewTarget.reason ?? ''}` : '' }}
          </NvSheetDescription>
        </NvSheetHeader>
        <p
          v-if="detailPending && !viewTarget"
          role="status"
          class="px-4 text-sm text-muted-foreground"
        >
          加载工程变更详情…
        </p>
        <div v-if="viewTarget" class="grid min-h-0 gap-3 overflow-y-auto px-4 py-2">
          <div
            v-if="canManageSchedule && viewTarget.status?.toLowerCase() === 'scheduled'"
            class="flex gap-2"
          >
            <NvButton variant="outline" @click="openScheduleAction('reschedule')">改期</NvButton>
            <NvButton variant="outline" @click="openScheduleAction('cancel')">取消调度</NvButton>
          </div>
          <div class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">状态</span>
              <NvStatusBadge
                :label="ecoStatus(viewTarget.status).label"
                :tone="ecoStatus(viewTarget.status).tone"
              />
            </div>
            <BusinessDocumentApprovalPanel
              :model-value="viewTarget.approvalReferenceId ?? ''"
              title="变更审批链"
              source-service="product-engineering"
              :document-type="ENGINEERING_CHANGE_ORDER_DOCUMENT_TYPE"
              :document-id="viewTarget.changeNumber ?? undefined"
              :allow-start="false"
            />
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">生效日</span>
              <span class="font-medium">{{
                viewTarget.effectiveDate ? formatDate(viewTarget.effectiveDate) : '即时'
              }}</span>
            </div>
          </div>

          <h2 class="text-sm font-semibold">受影响版本</h2>
          <div
            v-if="detailPending"
            class="flex items-center gap-2 py-4 text-sm text-muted-foreground"
          >
            <NvSpinner aria-hidden="true" />
            加载受影响版本…
          </div>
          <div v-else-if="viewAffected.length" class="overflow-hidden rounded-md border">
            <table class="w-full text-sm">
              <thead class="bg-muted/40 text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 text-left font-medium">对象种类</th>
                  <th class="px-3 py-2 text-left font-medium">受影响版本</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(row, i) in viewAffected" :key="i" class="border-t">
                  <td class="px-3 py-2">{{ versionKindLabel(row.versionKind) }}</td>
                  <td class="px-3 py-2 break-all">
                    {{ affectedVersionLabel(row.versionKind, row.versionId) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该变更没有受影响版本记录。
          </p>
          <EngineeringWorkOrderImpacts
            v-if="releasedChangeNumber"
            :change-number="releasedChangeNumber"
            :organization-id="filters.organizationId"
            :environment-id="filters.environmentId"
          />
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
