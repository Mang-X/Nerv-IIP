<script setup lang="ts">
import type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleInspectionCharacteristicResult,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useQualityInspectionPlans } from '@/composables/useBusinessQuality'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DropdownMenuItem,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '检验任务与记录' } })

const route = useRoute()
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
} = useQualityInspectionPlans()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const recordSuccess = shallowRef('')
const recordSheetOpen = shallowRef(false)

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
watch(
  () => route.query,
  (query) => {
    const source = firstQuery(query.sourceDocumentId) || firstQuery(query.workOrderId) || firstQuery(query.operationTaskId)
    const batch = firstQuery(query.batchNo) || firstQuery(query.materialLotId)
    const serial = firstQuery(query.serialNo)
    if (source) recordForm.sourceDocumentId = source
    if (batch) recordForm.batchNo = batch
    if (serial) recordForm.serialNo = serial
    if (source) recordSheetOpen.value = true
  },
  { immediate: true },
)

const listErrorMessage = computed(() => formatError(inspectionPlansError.value))
const createErrorMessage = computed(() => formatError(createInspectionRecordError.value))
const inspectedQuantity = computed(() => toOptionalNumber(recordForm.inspectedQuantity))
const requiresDispositionReason = computed(() =>
  recordForm.resultLines.some((line) => line.result === 'failed' || line.result === 'conditional-release'),
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
const columns: DataTableColumn<PlanRow>[] = [
  { key: 'code', header: '方案', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'summary', header: '摘要', accessor: (r) => qualityItemSummary(r) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function emptyLine() {
  return { characteristicCode: '', result: 'passed', observedValue: '', unitCode: '', defectReason: '', defectQuantity: '' }
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
  const values = value.split(',').map((item) => item.trim()).filter(Boolean)
  return values.length ? values : undefined
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function hasRequiredDefectContext(line: { result: string, defectReason: string, defectQuantity: string }) {
  if (line.result === 'passed') return true
  if (!isNonEmpty(line.defectReason)) return false
  return line.result !== 'conditional-release' || (toOptionalNumber(line.defectQuantity) ?? 0) > 0
}
function qualityItemSummary(item: BusinessConsoleQualityItem) {
  const values = [item.category, item.skuCode, item.partnerId, item.workCenterId, item.deviceAssetId, item.documentType].filter(isPresent)
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
    <PageHeader title="检验任务与记录" :breadcrumbs="[{ label: '质量' }]" :count="`${inspectionPlansTotal} 个检验方案`">
      <template #actions>
        <Button v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </Button>
        <Button size="sm" type="button" @click="recordSheetOpen = true">
          <ClipboardCheckIcon aria-hidden="true" />
          创建检验记录
        </Button>
        <Button size="sm" type="button" variant="outline" :disabled="inspectionPlansPending" @click="refreshInspectionPlans">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="检验方案" :value="inspectionPlansTotal" hint="后端筛选总数" />
      <SectionCard description="本页方案" :value="inspectionPlans.length" hint="当前页数量" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="检验状态" />
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="inspectionPlans"
      :row-key="(r) => r.id ?? r.code ?? '无'"
      :loading="inspectionPlansPending"
      empty-message="当前筛选下没有检验方案。检验记录应从工单、收货或检验任务进入；也可用右上角创建检验记录临时补录。"
    >
      <template #cell-code="{ row }">
        <div class="flex flex-col gap-0.5">
          <span class="font-medium">{{ row.code ?? '无' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.id ?? '无方案 ID' }}</span>
        </div>
      </template>
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <RowActions :label="`检验方案操作 ${row.code ?? ''}`">
          <DropdownMenuItem @click="useInspectionPlan(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            创建检验记录
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="inspectionPlansTotal" />

    <Dialog v-model:open="recordSheetOpen">
      <DialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>创建检验记录</DialogTitle>
          <DialogDescription>检验记录尽量从方案、工单、收货或质量任务带出信息，减少手输来源字段。</DialogDescription>
        </DialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitInspectionRecord">
          <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>
          <p v-if="recordSuccess" class="text-sm text-success" role="status">{{ recordSuccess }}</p>

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="record-plan">检验方案 ID</FieldLabel>
              <Input id="record-plan" v-model="recordForm.inspectionPlanId" />
            </Field>
            <Field>
              <FieldLabel>来源类型</FieldLabel>
              <Select v-model="recordForm.sourceType">
                <SelectTrigger aria-label="来源类型"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="operation">工序</SelectItem>
                  <SelectItem value="receiving">收货</SelectItem>
                  <SelectItem value="final">终检</SelectItem>
                  <SelectItem value="maintenance">维修</SelectItem>
                  <SelectItem value="customer-return">客户退货</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel>来源服务</FieldLabel>
              <Select v-model="recordForm.sourceService">
                <SelectTrigger aria-label="来源服务"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="mes-operation">MES 工序</SelectItem>
                  <SelectItem value="inventory">库存</SelectItem>
                  <SelectItem value="wms">WMS</SelectItem>
                  <SelectItem value="mes">MES</SelectItem>
                  <SelectItem value="erp">ERP</SelectItem>
                  <SelectItem value="maintenance">维修</SelectItem>
                  <SelectItem value="purchase-receipt">采购收货</SelectItem>
                  <SelectItem value="customer-return">客户退货</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="record-source-document">来源单据</FieldLabel>
              <Input id="record-source-document" v-model="recordForm.sourceDocumentId" required />
            </Field>
            <Field>
              <FieldLabel for="record-sku">SKU</FieldLabel>
              <Input id="record-sku" v-model="recordForm.skuCode" required />
            </Field>
            <Field>
              <FieldLabel for="record-quantity">检验数量</FieldLabel>
              <Input id="record-quantity" v-model="recordForm.inspectedQuantity" inputmode="decimal" min="0.000001" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="record-batch">批次</FieldLabel>
              <Input id="record-batch" v-model="recordForm.batchNo" />
            </Field>
            <Field>
              <FieldLabel for="record-serial">序列号</FieldLabel>
              <Input id="record-serial" v-model="recordForm.serialNo" />
            </Field>
          </FieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <h3 class="text-sm font-semibold text-foreground">检验特性</h3>
              <Button size="sm" variant="outline" type="button" @click="addCharacteristicRow">
                <PlusIcon aria-hidden="true" />
                添加行
              </Button>
            </div>
            <div class="grid gap-2">
              <div
                v-for="(line, index) in recordForm.resultLines"
                :key="index"
                class="grid gap-2 rounded-lg border p-3 md:grid-cols-[1fr_140px_1fr_110px_auto]"
              >
                <Field>
                  <FieldLabel :for="`characteristic-code-${index}`">特性编码</FieldLabel>
                  <Input :id="`characteristic-code-${index}`" v-model="line.characteristicCode" required />
                </Field>
                <Field>
                  <FieldLabel>结果</FieldLabel>
                  <Select v-model="line.result">
                    <SelectTrigger :aria-label="`第 ${index + 1} 个特性结果`"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="passed">合格</SelectItem>
                      <SelectItem value="failed">不合格</SelectItem>
                      <SelectItem value="conditional-release">让步放行</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel :for="`observed-value-${index}`">实测值</FieldLabel>
                  <Input :id="`observed-value-${index}`" v-model="line.observedValue" required />
                </Field>
                <Field>
                  <FieldLabel :for="`unit-code-${index}`">单位</FieldLabel>
                  <Input :id="`unit-code-${index}`" v-model="line.unitCode" />
                </Field>
                <Field class="md:col-span-2">
                  <FieldLabel :for="`defect-reason-${index}`">缺陷原因</FieldLabel>
                  <Input :id="`defect-reason-${index}`" v-model="line.defectReason" />
                </Field>
                <Field>
                  <FieldLabel :for="`defect-quantity-${index}`">缺陷数量</FieldLabel>
                  <Input :id="`defect-quantity-${index}`" v-model="line.defectQuantity" inputmode="decimal" type="number" />
                </Field>
                <div class="flex items-end justify-end">
                  <Button size="icon-sm" variant="ghost" type="button" @click="removeCharacteristicRow(index)">
                    <Trash2Icon />
                    <span class="sr-only">移除检验特性</span>
                  </Button>
                </div>
              </div>
            </div>
          </div>

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="record-disposition">处置原因{{ requiresDispositionReason ? ' *' : '' }}</FieldLabel>
              <Input id="record-disposition" v-model="recordForm.dispositionReason" :required="requiresDispositionReason" />
              <FieldDescription v-if="requiresDispositionReason">当任一特性不合格或让步放行时必填。</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="record-files">附件文件 ID</FieldLabel>
              <Input id="record-files" v-model="recordForm.dispositionAttachmentFileIds" placeholder="file-1, file-2" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createInspectionRecordPending || !canCreateRecord">
              <Spinner v-if="createInspectionRecordPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交记录
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
