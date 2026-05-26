<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useQualityInspectionPlans } from '@/composables/useBusinessQuality'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleInspectionCharacteristicResult,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.inspections',
  },
})

const {
  createInspectionRecord,
  createInspectionRecordError,
  createInspectionRecordPending,
  filters,
  inspectionPlans,
  inspectionPlansError,
  inspectionPlansPending,
  refreshInspectionPlans,
} = useQualityInspectionPlans()

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
  resultLines: [
    {
      characteristicCode: '',
      result: 'passed',
      observedValue: '',
      unitCode: '',
      defectReason: '',
      defectQuantity: '',
    },
  ],
})

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

function syncContextFromFilters() {
  recordForm.organizationId = filters.organizationId
  recordForm.environmentId = filters.environmentId
}

function useInspectionPlan(plan: BusinessConsoleQualityItem) {
  recordForm.inspectionPlanId = plan.id ?? ''
  if (plan.skuCode) {
    recordForm.skuCode = plan.skuCode
  }
  recordSheetOpen.value = true
}

function addCharacteristicRow() {
  recordForm.resultLines.push({
    characteristicCode: '',
    result: 'passed',
    observedValue: '',
    unitCode: '',
    defectReason: '',
    defectQuantity: '',
  })
}

function removeCharacteristicRow(index: number) {
  if (recordForm.resultLines.length === 1) {
    recordForm.resultLines[0] = {
      characteristicCode: '',
      result: 'passed',
      observedValue: '',
      unitCode: '',
      defectReason: '',
      defectQuantity: '',
    }
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

function hasRequiredDefectContext(line: { result: string; defectReason: string; defectQuantity: string }) {
  if (line.result === 'passed') {
    return true
  }

  if (!isNonEmpty(line.defectReason)) {
    return false
  }

  return line.result !== 'conditional-release' || (toOptionalNumber(line.defectQuantity) ?? 0) > 0
}

function rowKey(item: BusinessConsoleQualityItem, index: number) {
  return `${item.id ?? item.code ?? 'plan'}:${index}`
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

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
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
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="质量"
        title="检验任务与记录"
        summary="先确认检验方案和来源上下文，再进入抽屉提交检验记录。"
      >
        <template #actions>
          <Button size="sm" type="button" @click="recordSheetOpen = true">
            <ClipboardCheckIcon data-icon="inline-start" />
            创建检验记录
          </Button>
          <Button
            size="sm"
            variant="outline"
            type="button"
            :disabled="inspectionPlansPending"
            @click="refreshInspectionPlans"
          >
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="inspection-org">组织</FieldLabel>
            <Input id="inspection-org" v-model="filters.organizationId" @change="syncContextFromFilters" />
          </Field>
          <Field>
            <FieldLabel for="inspection-env">环境</FieldLabel>
            <Input id="inspection-env" v-model="filters.environmentId" @change="syncContextFromFilters" />
          </Field>
          <Field>
            <FieldLabel for="inspection-status">状态</FieldLabel>
            <Input id="inspection-status" v-model="filters.status" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="inspection-take">返回条数</FieldLabel>
            <Input id="inspection-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </div>

      <div class="grid gap-4">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">检验方案</h2>
            <span class="text-sm text-muted-foreground">返回 {{ inspectionPlans.length }} 条</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>方案</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>摘要</TableHead>
                  <TableHead class="text-right">使用</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="(plan, index) in inspectionPlans" :key="rowKey(plan, index)">
                  <TableCell>
                    <div class="flex flex-col gap-0.5">
                      <span class="font-medium">{{ plan.code ?? '无' }}</span>
                      <span class="text-xs text-muted-foreground">{{ plan.id ?? '无方案 ID' }}</span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{{ plan.status ?? '未知' }}</Badge>
                  </TableCell>
                  <TableCell>{{ qualityItemSummary(plan) }}</TableCell>
                  <TableCell class="text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      type="button"
                      @click="useInspectionPlan(plan)"
                    >
                      选择
                    </Button>
                  </TableCell>
                </TableRow>
                <TableEmpty v-if="!inspectionPlans.length && !inspectionPlansPending" :colspan="4">
                  <BusinessEmptyState
                    title="当前筛选下没有检验方案"
                    description="检验记录应从工单、收货或检验任务上下文进入；缺少方案时请先维护质量规则。"
                    action="也可以使用右上角创建检验记录进行临时补录。"
                  />
                </TableEmpty>
                <TableEmpty v-if="inspectionPlansPending" :colspan="4">正在加载检验方案...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>
      </div>

      <BusinessActionSheet
        v-model:open="recordSheetOpen"
        title="创建检验记录"
        description="检验记录应尽量从方案、工单、收货或质量任务带出上下文，减少手输来源字段。"
      >
        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitInspectionRecord">
          <div>
            <p class="text-xs font-bold uppercase text-primary">记录</p>
            <h2 class="text-base font-semibold text-foreground">创建检验记录</h2>
          </div>

          <BusinessFormStatus :error="createErrorMessage" :success="recordSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="record-org">组织</FieldLabel>
              <Input id="record-org" v-model="recordForm.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="record-env">环境</FieldLabel>
              <Input id="record-env" v-model="recordForm.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="record-plan">检验方案 ID</FieldLabel>
              <Input id="record-plan" v-model="recordForm.inspectionPlanId" />
            </Field>
            <Field>
              <FieldLabel>来源类型</FieldLabel>
              <Select v-model="recordForm.sourceType">
                <SelectTrigger aria-label="来源类型">
                  <SelectValue />
                </SelectTrigger>
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
                  <SelectTrigger aria-label="来源服务">
                    <SelectValue />
                  </SelectTrigger>
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
              <Input
                id="record-quantity"
                v-model="recordForm.inspectedQuantity"
                inputmode="decimal"
                min="0.000001"
                required
                type="number"
              />
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
                <PlusIcon data-icon="inline-start" />
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
                    <SelectTrigger :aria-label="`第 ${index + 1} 个特性结果`">
                      <SelectValue />
                    </SelectTrigger>
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
                  <Input
                    :id="`defect-quantity-${index}`"
                    v-model="line.defectQuantity"
                    inputmode="decimal"
                    type="number"
                  />
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
              <FieldLabel for="record-disposition">
                处置原因{{ requiresDispositionReason ? ' *' : '' }}
              </FieldLabel>
              <Input id="record-disposition" v-model="recordForm.dispositionReason" :required="requiresDispositionReason" />
              <FieldDescription v-if="requiresDispositionReason">
                当任一特性不合格或让步放行时必填。
              </FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="record-files">附件文件 ID</FieldLabel>
              <Input id="record-files" v-model="recordForm.dispositionAttachmentFileIds" placeholder="file-1, file-2" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createInspectionRecordPending || !canCreateRecord">
              <Spinner v-if="createInspectionRecordPending" data-icon="inline-start" />
              <ClipboardCheckIcon v-else data-icon="inline-start" />
              提交记录
            </Button>
          </div>
        </form>
      </BusinessActionSheet>
    </section>
  </BusinessLayout>
</template>
