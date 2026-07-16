<script setup lang="ts">
import type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleInspectionCharacteristicResult,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useQualityInspectionPlans } from '@/composables/useBusinessQuality'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldDescription,
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
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '检验任务与记录',
    requiredPermissions: ['business.quality.inspection-records.read'],
  },
})

const route = useRoute()
const initialInspectionPlanKeyword = firstQuery(route.query.inspectionPlanId)
const {
  createInspectionRecord,
  createInspectionRecordError,
  createInspectionRecordPending,
  filters,
  inspectionPlans,
  inspectionPlansError,
  inspectionPlansPending,
  inspectionPlansTotal,
  refreshInspectionPlans,
} = useQualityInspectionPlans(
  initialInspectionPlanKeyword ? { keyword: initialInspectionPlanKeyword } : {},
)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})

const recordSuccess = shallowRef('')
const recordSheetOpen = shallowRef(false)
const recordCreatedFromLocatedPlanId = shallowRef('')

const recordForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  inspectionPlanId: '',
  sourceType: 'operation',
  sourceService: 'mes-operation',
  sourceDocumentId: '',
  skuCode: 'SKU-001',
  inspectedQuantity: '1',
  batchNo: '',
  serialNo: '',
  dispositionReason: '',
  dispositionAttachmentFileIds: '',
  resultLines: [emptyLine()],
})

// 上下文穿透：从工单/工序/收货带入来源单据、批次、序列号。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
const targetInspectionPlanId = computed(() => firstQuery(route.query.inspectionPlanId))
const targetInspectionPlan = computed(() =>
  targetInspectionPlanId.value
    ? inspectionPlans.value.find(
        (plan) =>
          plan.id === targetInspectionPlanId.value || plan.code === targetInspectionPlanId.value,
      )
    : undefined,
)
const targetInspectionPlanMissing = computed(
  () =>
    !!targetInspectionPlanId.value && !inspectionPlansPending.value && !targetInspectionPlan.value,
)
const scanAuditRoute = computed(() => ({
  path: '/barcode/scans',
  query: {
    sourceWorkflow: 'quality.inspection',
    sourceDocumentId: recordForm.sourceDocumentId || targetInspectionPlanId.value || undefined,
    scannedValue: recordForm.serialNo || recordForm.batchNo || undefined,
  },
}))
const shouldCreateRecordFromLocatedPlan = computed(
  () => firstQuery(route.query.action).toLowerCase() === 'create',
)
watch(
  () => route.query,
  (query) => {
    const source =
      firstQuery(query.sourceDocumentId) ||
      firstQuery(query.workOrderId) ||
      firstQuery(query.operationTaskId)
    const batch = firstQuery(query.batchNo) || firstQuery(query.materialLotId)
    const serial = firstQuery(query.serialNo)
    if (source) recordForm.sourceDocumentId = source
    if (batch) recordForm.batchNo = batch
    if (serial) recordForm.serialNo = serial
    // 来源类型/来源服务：优先用 query 显式值；否则按入口推断——
    // 物料批且非工序入口视为收货/WMS，避免从收货进入仍归到 MES 工序来源。
    const sourceType = firstQuery(query.sourceType)
    const sourceService = firstQuery(query.sourceService)
    const receivingEntry = !!firstQuery(query.materialLotId) && !firstQuery(query.operationTaskId)
    if (sourceType) recordForm.sourceType = sourceType
    else if (receivingEntry) recordForm.sourceType = 'receiving'
    if (sourceService) recordForm.sourceService = sourceService
    else if (receivingEntry) recordForm.sourceService = 'wms'
    if (source) recordSheetOpen.value = true
  },
  { immediate: true },
)
watch(
  targetInspectionPlanId,
  (id) => {
    if (id) {
      filters.status = undefined
      filters.keyword = id
    } else {
      filters.keyword = undefined
    }
    recordCreatedFromLocatedPlanId.value = ''
  },
  { immediate: true },
)
watch(
  [targetInspectionPlan, shouldCreateRecordFromLocatedPlan],
  ([plan, shouldCreate]) => {
    if (
      !plan ||
      !shouldCreate ||
      recordCreatedFromLocatedPlanId.value === targetInspectionPlanId.value
    )
      return
    recordCreatedFromLocatedPlanId.value = targetInspectionPlanId.value
    useInspectionPlan(plan)
  },
  { immediate: true },
)

const listErrorMessage = computed(() => formatError(inspectionPlansError.value))
const createErrorMessage = computed(() => formatError(createInspectionRecordError.value))
const inspectedQuantity = computed(() => toOptionalNumber(recordForm.inspectedQuantity))
const requiresDispositionReason = computed(() =>
  recordForm.resultLines.some(
    (line) => line.result === 'failed' || line.result === 'conditional-release',
  ),
)
const validResultLines = computed(() =>
  recordForm.resultLines.filter(
    (line) =>
      isNonEmpty(line.characteristicCode) &&
      isNonEmpty(line.observedValue) &&
      isNonEmpty(line.result) &&
      hasRequiredDefectContext(line),
  ),
)
const canCreateRecord = computed(
  () =>
    isNonEmpty(recordForm.organizationId) &&
    isNonEmpty(recordForm.environmentId) &&
    isNonEmpty(recordForm.sourceType) &&
    isNonEmpty(recordForm.sourceService) &&
    isNonEmpty(recordForm.sourceDocumentId) &&
    isNonEmpty(recordForm.skuCode) &&
    inspectedQuantity.value !== undefined &&
    inspectedQuantity.value > 0 &&
    (!requiresDispositionReason.value || isNonEmpty(recordForm.dispositionReason)) &&
    validResultLines.value.length > 0,
)

type PlanRow = BusinessConsoleQualityItem
const columns: NvDataTableColumn<PlanRow>[] = [
  { key: 'code', header: '方案', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'summary', header: '摘要', accessor: (r) => qualityItemSummary(r) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function emptyLine() {
  return {
    characteristicCode: '',
    result: 'passed',
    observedValue: '',
    unitCode: '',
    defectReason: '',
    defectQuantity: '',
  }
}
function useInspectionPlan(plan: BusinessConsoleQualityItem) {
  recordForm.inspectionPlanId = plan.id ?? ''
  if (plan.skuCode) recordForm.skuCode = plan.skuCode
  recordSuccess.value = ''
  recordSheetOpen.value = true
}
function addCharacteristicRow() {
  recordForm.resultLines.push(emptyLine())
}
function removeCharacteristicRow(index: number) {
  if (recordForm.resultLines.length === 1) {
    recordForm.resultLines[0] = emptyLine()
    return
  }
  recordForm.resultLines.splice(index, 1)
}

async function submitInspectionRecord() {
  if (!canCreateRecord.value) return
  const body: BusinessConsoleCreateInspectionRecordRequest = {
    organizationId: recordForm.organizationId.trim(),
    environmentId: recordForm.environmentId.trim(),
    inspectionPlanId: optionalText(recordForm.inspectionPlanId),
    sourceType: recordForm.sourceType.trim(),
    sourceService: recordForm.sourceService.trim(),
    sourceDocumentId: recordForm.sourceDocumentId.trim(),
    skuCode: recordForm.skuCode.trim(),
    inspectedQuantity: inspectedQuantity.value,
    batchNo: optionalText(recordForm.batchNo),
    serialNo: optionalText(recordForm.serialNo),
    resultLines: toCharacteristicResults(),
    dispositionReason: optionalText(recordForm.dispositionReason),
    dispositionAttachmentFileIds: splitCsv(recordForm.dispositionAttachmentFileIds),
  }
  const response = await createInspectionRecord(body)
  recordSuccess.value = `检验记录 ${response?.data?.inspectionRecordId ?? body.sourceDocumentId} 已提交。`
}

function toCharacteristicResults(): BusinessConsoleInspectionCharacteristicResult[] {
  return validResultLines.value.map((line) => ({
    characteristicCode: line.characteristicCode.trim(),
    result: line.result.trim(),
    observedValue: line.observedValue.trim(),
    unitCode: optionalText(line.unitCode),
    defectReason: optionalText(line.defectReason),
    defectQuantity: toOptionalNumber(line.defectQuantity),
  }))
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
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function hasRequiredDefectContext(line: {
  result: string
  defectReason: string
  defectQuantity: string
}) {
  if (line.result === 'passed') return true
  if (!isNonEmpty(line.defectReason)) return false
  return line.result !== 'conditional-release' || (toOptionalNumber(line.defectQuantity) ?? 0) > 0
}
function qualityItemSummary(item: BusinessConsoleQualityItem) {
  const values = [
    item.category,
    item.skuCode,
    item.partnerId,
    item.workCenterId,
    item.deviceAssetId,
    item.documentType,
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
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="检验任务与记录"
      :breadcrumbs="[{ label: '质量' }]"
      :count="`${inspectionPlansTotal} 个检验方案`"
    >
      <template #actions>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="scanAuditRoute">扫码记录</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" @click="recordSheetOpen = true">
          <ClipboardCheckIcon aria-hidden="true" />
          创建检验记录
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="inspectionPlansPending"
          @click="refreshInspectionPlans"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.status"
          class="h-9 w-32"
          placeholder="状态（可选）"
          aria-label="检验状态"
        />
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>
    <p v-else-if="targetInspectionPlanMissing" class="text-sm text-warning" role="status">
      未找到检验方案 {{ targetInspectionPlanId }}。请确认该方案是否已归档或无权访问。
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="inspectionPlansTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="inspectionPlans"
      :row-key="(r) => r.id ?? r.code ?? '无'"
      :loading="inspectionPlansPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前筛选下没有检验方案。检验记录应从工单、收货或检验任务进入；也可用右上角创建检验记录临时补录。"
    >
      <template #cell-code="{ row }">
        <span class="font-medium">{{ row.code ?? '无' }}</span>
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`检验方案操作 ${row.code ?? ''}`">
          <NvDropdownMenuItem @click="useInspectionPlan(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            创建检验记录
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="recordSheetOpen">
      <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>创建检验记录</NvDialogTitle>
          <NvDialogDescription
            >检验记录尽量从方案、工单、收货或质量任务带出信息，减少手输来源字段。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitInspectionRecord">
          <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">
            {{ createErrorMessage }}
          </p>
          <p v-if="recordSuccess" class="text-sm text-success" role="status">{{ recordSuccess }}</p>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="record-plan">检验方案 ID</NvFieldLabel>
              <NvInput id="record-plan" v-model="recordForm.inspectionPlanId" />
            </NvField>
            <NvField>
              <NvFieldLabel>来源类型</NvFieldLabel>
              <NvSelect v-model="recordForm.sourceType">
                <NvSelectTrigger aria-label="来源类型"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="operation">工序</NvSelectItem>
                  <NvSelectItem value="receiving">收货</NvSelectItem>
                  <NvSelectItem value="final">终检</NvSelectItem>
                  <NvSelectItem value="maintenance">维修</NvSelectItem>
                  <NvSelectItem value="customer-return">客户退货</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel>来源服务</NvFieldLabel>
              <NvSelect v-model="recordForm.sourceService">
                <NvSelectTrigger aria-label="来源服务"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="mes-operation">MES 工序</NvSelectItem>
                  <NvSelectItem value="inventory">库存</NvSelectItem>
                  <NvSelectItem value="wms">WMS</NvSelectItem>
                  <NvSelectItem value="mes">MES</NvSelectItem>
                  <NvSelectItem value="erp">ERP</NvSelectItem>
                  <NvSelectItem value="maintenance">维修</NvSelectItem>
                  <NvSelectItem value="purchase-receipt">采购收货</NvSelectItem>
                  <NvSelectItem value="customer-return">客户退货</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="record-source-document">来源单据</NvFieldLabel>
              <NvInput id="record-source-document" v-model="recordForm.sourceDocumentId" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-sku">SKU</NvFieldLabel>
              <NvInput id="record-sku" v-model="recordForm.skuCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-quantity">检验数量</NvFieldLabel>
              <NvInput
                id="record-quantity"
                v-model="recordForm.inspectedQuantity"
                inputmode="decimal"
                min="0.000001"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-batch">批次</NvFieldLabel>
              <NvInput id="record-batch" v-model="recordForm.batchNo" />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-serial">序列号</NvFieldLabel>
              <NvInput id="record-serial" v-model="recordForm.serialNo" />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <h3 class="text-sm font-semibold text-foreground">检验特性</h3>
              <NvButton size="sm" variant="outline" type="button" @click="addCharacteristicRow">
                <PlusIcon aria-hidden="true" />
                添加行
              </NvButton>
            </div>
            <div class="grid gap-2">
              <div
                v-for="(line, index) in recordForm.resultLines"
                :key="index"
                class="grid gap-2 rounded-lg border p-3 md:grid-cols-[1fr_140px_1fr_110px_auto]"
              >
                <NvField>
                  <NvFieldLabel :for="`characteristic-code-${index}`">特性编码</NvFieldLabel>
                  <NvInput
                    :id="`characteristic-code-${index}`"
                    v-model="line.characteristicCode"
                    required
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel>结果</NvFieldLabel>
                  <NvSelect v-model="line.result">
                    <NvSelectTrigger :aria-label="`第 ${index + 1} 个特性结果`"
                      ><NvSelectValue
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem value="passed">合格</NvSelectItem>
                      <NvSelectItem value="failed">不合格</NvSelectItem>
                      <NvSelectItem value="conditional-release">让步放行</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`observed-value-${index}`">实测值</NvFieldLabel>
                  <NvInput :id="`observed-value-${index}`" v-model="line.observedValue" required />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`unit-code-${index}`">单位</NvFieldLabel>
                  <NvInput :id="`unit-code-${index}`" v-model="line.unitCode" />
                </NvField>
                <NvField class="md:col-span-2">
                  <NvFieldLabel :for="`defect-reason-${index}`">缺陷原因</NvFieldLabel>
                  <NvInput :id="`defect-reason-${index}`" v-model="line.defectReason" />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`defect-quantity-${index}`">缺陷数量</NvFieldLabel>
                  <NvInput
                    :id="`defect-quantity-${index}`"
                    v-model="line.defectQuantity"
                    inputmode="decimal"
                    type="number"
                  />
                </NvField>
                <div class="flex items-end justify-end">
                  <NvButton
                    size="icon-sm"
                    variant="ghost"
                    type="button"
                    @click="removeCharacteristicRow(index)"
                  >
                    <Trash2Icon />
                    <span class="sr-only">移除检验特性</span>
                  </NvButton>
                </div>
              </div>
            </div>
          </div>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="record-disposition"
                >处置原因{{ requiresDispositionReason ? ' *' : '' }}</NvFieldLabel
              >
              <NvInput
                id="record-disposition"
                v-model="recordForm.dispositionReason"
                :required="requiresDispositionReason"
              />
              <NvFieldDescription v-if="requiresDispositionReason"
                >当任一特性不合格或让步放行时必填。</NvFieldDescription
              >
            </NvField>
            <NvField>
              <NvFieldLabel for="record-files">附件文件 ID</NvFieldLabel>
              <NvInput
                id="record-files"
                v-model="recordForm.dispositionAttachmentFileIds"
                placeholder="file-1, file-2"
              />
            </NvField>
          </NvFieldGroup>

          <div class="flex justify-end">
            <NvButton type="submit" :disabled="createInspectionRecordPending || !canCreateRecord">
              <Spinner v-if="createInspectionRecordPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交记录
            </NvButton>
          </div>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
