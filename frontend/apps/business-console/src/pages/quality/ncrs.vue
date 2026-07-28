<script setup lang="ts">
import type {
  BusinessConsoleNcrCloseRequest,
  BusinessConsoleNcrDispositionRequest,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import BusinessDocumentApprovalPanel from '@/components/business/BusinessDocumentApprovalPanel.vue'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { hasBusinessContext } from '@/composables/businessContextBinding'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import { labelFor, NCR_STATUS_LABELS, QUALITY_SOURCE_TYPE_LABELS } from '@/data/businessLabels'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvAlertDialogTrigger,
  NvButton,
  NvDataTable,
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
  Spinner,
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
const initialNcrKeyword = firstQuery(route.query.ncrId)
const {
  closeNcr,
  closeNcrPending,
  filters,
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

const selectedNcr = shallowRef<BusinessConsoleQualityItem>()
const detailOpen = shallowRef(false)
// 值必须与后端 NonconformanceReport 的状态字完全一致（读面按 Status 精确匹配过滤）。
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '待处置', value: 'open' },
  { label: '处置中', value: 'disposition-in-progress' },
  { label: '已关闭', value: 'closed' },
]

const dispositionForm = reactive({
  dispositionType: 'use-as-is',
  dispositionApprovalChainId: '',
  attachmentFileIds: '',
})
const closeForm = reactive({
  reason: '',
  reworkWorkOrderId: '',
  scrapMovementId: '',
  returnDocumentId: '',
})

// 上下文穿透：从工单带入时，关闭动作的返工工单默认填入来源工单。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
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

const listErrorMessage = computed(() => formatError(ncrsError.value))
const selectedNcrId = computed(() => selectedNcr.value?.id ?? '')
// 带出式录入：所选 NCR 行已有的事实一律只读呈现，不做成看起来还能改的输入位。
const ncrContextItems = computed(() => {
  const ncr = selectedNcr.value
  if (!ncr) return []
  return [
    { label: '来源单据', value: ncr.sourceDocumentId },
    { label: '来源类型', value: labelFor(QUALITY_SOURCE_TYPE_LABELS, ncr.sourceType) },
    { label: '物料', value: ncr.skuCode },
    { label: '不合格数量', value: ncr.defectQuantity },
    { label: '不合格原因', value: ncr.defectReason },
    { label: '批次', value: ncr.batchNo },
    { label: '序列号', value: ncr.serialNo },
    { label: '返工工单', value: closeForm.reworkWorkOrderId },
    // 关闭原因是必填的关单审计事实，已关闭的单必须回显，否则看起来像"关了但没写原因"。
    ...(ncr.closeReason ? [{ label: '关闭原因', value: ncr.closeReason }] : []),
  ]
})
const canSubmitDisposition = computed(
  () =>
    hasBusinessContext(filters) &&
    isNonEmpty(selectedNcrId.value) &&
    isNonEmpty(dispositionForm.dispositionType),
)
const canCloseNcr = computed(
  () =>
    hasBusinessContext(filters) && isNonEmpty(selectedNcrId.value) && isNonEmpty(closeForm.reason),
)
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})

type NcrRow = BusinessConsoleQualityItem
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

function openNcr(ncr: BusinessConsoleQualityItem) {
  selectedNcr.value = ncr
  dispositionForm.dispositionApprovalChainId = ''
  closeForm.reason = ''
  closeForm.reworkWorkOrderId =
    contextWorkOrderId.value || (isPresent(ncr.sourceDocumentId) ? ncr.sourceDocumentId : '')
  detailOpen.value = true
}

async function submitNcrDisposition() {
  if (!hasBusinessContext(filters)) {
    notifyError('业务范围尚未就绪，请稍后重试。')
    return
  }
  if (!canSubmitDisposition.value) return
  const body: BusinessConsoleNcrDispositionRequest = {
    dispositionType: dispositionForm.dispositionType.trim(),
    dispositionApprovalChainId: optionalText(dispositionForm.dispositionApprovalChainId),
    attachmentFileIds: splitCsv(dispositionForm.attachmentFileIds),
  }
  const label = selectedNcr.value?.code ?? selectedNcrId.value
  try {
    await submitDisposition(selectedNcrId.value, body)
  } catch (error) {
    notifyError(error, '处置提交失败，请稍后重试。')
    return
  }
  notifySuccess(`不合格品 ${label} 处置已提交。`)
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
    reworkWorkOrderId: optionalText(closeForm.reworkWorkOrderId),
    scrapMovementId: optionalText(closeForm.scrapMovementId),
    returnDocumentId: optionalText(closeForm.returnDocumentId),
  }
  const label = selectedNcr.value?.code ?? selectedNcrId.value
  try {
    await closeNcr(selectedNcrId.value, body)
  } catch (error) {
    notifyError(error, '不合格品关闭失败，请稍后重试。')
    return
  }
  notifySuccess(`不合格品 ${label} 已关闭。`)
  detailOpen.value = false
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function splitCsv(value: string) {
  const values = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
  return values.length ? values : undefined
}
function qualityItemSummary(item: BusinessConsoleQualityItem) {
  const values = [
    labelFor(QUALITY_SOURCE_TYPE_LABELS, item.sourceType) || undefined,
    item.sourceDocumentId,
    item.skuCode,
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
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function isPresent(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

watch(detailOpen, (open) => {
  if (!open) selectedNcr.value = undefined
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
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="不合格品处理"
      :breadcrumbs="[{ label: '质量' }]"
      :count="`${ncrsTotal} 条 NCR`"
    >
      <template #actions>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
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
          <NvDropdownMenuItem @click="openNcr(row)">打开处置</NvDropdownMenuItem>
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

          <form class="grid gap-3 rounded-lg border p-3" @submit.prevent="submitNcrDisposition">
            <h2 class="text-base font-semibold text-foreground">提交处置</h2>
            <NvFieldGroup class="grid gap-3">
              <NvField>
                <NvFieldLabel>处置类型</NvFieldLabel>
                <NvSelect v-model="dispositionForm.dispositionType">
                  <NvSelectTrigger aria-label="处置类型"><NvSelectValue /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem value="use-as-is">让步接收</NvSelectItem>
                    <NvSelectItem value="rework">返工</NvSelectItem>
                    <NvSelectItem value="scrap">报废</NvSelectItem>
                    <NvSelectItem value="return-to-supplier">退供应商</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <BusinessDocumentApprovalPanel
                v-model="dispositionForm.dispositionApprovalChainId"
                title="处置审批链"
                source-service="quality"
                document-type="quality-ncr"
                :document-id="selectedNcr?.code ?? selectedNcr?.id"
              />
              <NvField>
                <NvFieldLabel for="ncr-disposition-files">附件</NvFieldLabel>
                <NvInput
                  id="ncr-disposition-files"
                  v-model="dispositionForm.attachmentFileIds"
                  placeholder="多个附件用逗号分隔"
                />
              </NvField>
            </NvFieldGroup>
            <div class="flex justify-end">
              <NvButton type="submit" :disabled="submitDispositionPending || !canSubmitDisposition">
                <Spinner v-if="submitDispositionPending" aria-hidden="true" />
                <SendIcon v-else aria-hidden="true" />
                提交处置
              </NvButton>
            </div>
          </form>

          <form class="grid gap-3 rounded-lg border p-3" @submit.prevent>
            <h2 class="text-base font-semibold text-foreground">关闭不合格品</h2>
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
                <NvInput id="ncr-return" v-model="closeForm.returnDocumentId" />
              </NvField>
            </NvFieldGroup>

            <NvSheetFooter>
              <NvAlertDialog>
                <NvAlertDialogTrigger as-child>
                  <NvButton
                    type="button"
                    variant="destructive"
                    :disabled="closeNcrPending || !canCloseNcr"
                  >
                    <Spinner v-if="closeNcrPending" aria-hidden="true" />
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
                    <NvAlertDialogAction @click="submitCloseNcr">确认关闭</NvAlertDialogAction>
                  </NvAlertDialogFooter>
                </NvAlertDialogContent>
              </NvAlertDialog>
            </NvSheetFooter>
          </form>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
