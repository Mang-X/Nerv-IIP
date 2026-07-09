<script setup lang="ts">
import type { BusinessConsoleBarcodeRuleItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBarcodeRules } from '@/composables/useBusinessBarcode'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PencilIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '条码规则',
    requiredPermissions: ['business.barcodes.templates.manage'],
  },
})

const SOURCE_DOCUMENT_OPTIONS = [
  { value: 'inventory.receipt', label: '收货入库' },
  { value: 'production.report', label: '生产报工' },
  { value: 'quality.inspection', label: '质量检验' },
  { value: 'inventory.count', label: '库存盘点' },
  { value: 'wms.receiving', label: '仓储收货' },
]

const BARCODE_TYPE_OPTIONS = [
  { value: 'code128', label: 'Code 128' },
  { value: 'gs1-128', label: 'GS1-128' },
  { value: 'datamatrix', label: 'Data Matrix' },
  { value: 'qr', label: 'QR Code' },
]

const STATUS_OPTIONS = [
  { value: 'active', label: '启用' },
  { value: 'disabled', label: '停用' },
]

const {
  filters,
  refreshRules,
  rules,
  rulesError,
  rulesPending,
  rulesTotal,
  saveRule,
  saveRulePending,
} = useBarcodeRules()

const route = useRoute()
const open = shallowRef(false)
const showErrors = shallowRef(false)
const editingRuleCode = shallowRef<string | null>(null)
const keyword = shallowRef(firstQuery(route.query.ruleCode))
const statusFilter = shallowRef('all')
const page = shallowRef(1)
const pageSize = shallowRef('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)

const form = reactive({
  ruleCode: '',
  barcodeType: 'code128',
  prefix: '',
  length: 18,
  checksumRule: 'none',
  gs1CompanyPrefixLength: '',
  allowedSourceDocumentTypes: [] as string[],
  status: 'active',
})

const columns: NvDataTableColumn<BusinessConsoleBarcodeRuleItem>[] = [
  {
    key: 'ruleCode',
    header: '规则编码',
    cellClass: 'font-medium',
    accessor: (r) => r.ruleCode ?? '无',
  },
  {
    key: 'barcodeType',
    header: '条码类型',
    width: 'w-28',
    accessor: (r) => typeLabel(r.barcodeType),
  },
  { key: 'prefix', header: '前缀', width: 'w-32', accessor: (r) => r.prefix ?? '无' },
  { key: 'length', header: '长度', width: 'w-20', accessor: (r) => String(r.length ?? '无') },
  { key: 'gs1CompanyPrefixLength', header: 'GS1 前缀', width: 'w-32' },
  { key: 'allowedSourceDocumentTypes', header: '适用场景' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'skuLink', header: '物料使用', width: 'w-52' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

watch(
  keyword,
  (value) => {
    filters.keyword = value.trim() || undefined
    filters.skip = 0
    page.value = 1
  },
  { immediate: true },
)

watch(
  () => route.query.ruleCode,
  (value) => {
    const ruleCode = firstQuery(value)
    if (ruleCode) keyword.value = ruleCode
  },
)

watch(
  [page, pageSize],
  () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  },
  { immediate: true },
)

watch(pageSize, () => {
  page.value = 1
})

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
  filters.skip = 0
  page.value = 1
})

const errorMessage = computed(() =>
  rulesError.value instanceof Error ? rulesError.value.message : '',
)
const isGs1 = computed(() => form.barcodeType.toLowerCase().includes('gs1'))
const gs1PrefixNumber = computed(() => Number(form.gs1CompanyPrefixLength))
const canSubmit = computed(
  () =>
    form.ruleCode.trim().length > 0 &&
    form.barcodeType.trim().length > 0 &&
    form.prefix.trim().length > 0 &&
    Number(form.length) > 0 &&
    form.checksumRule.trim().length > 0 &&
    (!isGs1.value || (Number.isInteger(gs1PrefixNumber.value) && gs1PrefixNumber.value > 0)),
)

function typeLabel(value?: string | null) {
  if (!value) return '无'
  return BARCODE_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? value
}

function sourceLabels(values?: readonly string[] | null) {
  if (!values?.length) return '暂未限定'
  return values
    .map((value) => SOURCE_DOCUMENT_OPTIONS.find((o) => o.value === value)?.label ?? value)
    .join('、')
}

function statusLabel(value?: string | null) {
  return value === 'disabled' ? '停用' : '启用'
}

function firstQuery(value: unknown) {
  return Array.isArray(value) ? String(value[0] ?? '') : String(value ?? '')
}

function resetForm() {
  Object.assign(form, {
    ruleCode: '',
    barcodeType: 'code128',
    prefix: '',
    length: 18,
    checksumRule: 'none',
    gs1CompanyPrefixLength: '',
    allowedSourceDocumentTypes: [],
    status: 'active',
  })
  editingRuleCode.value = null
  showErrors.value = false
}

function openEdit(row: BusinessConsoleBarcodeRuleItem) {
  Object.assign(form, {
    ruleCode: row.ruleCode ?? '',
    barcodeType: row.barcodeType ?? 'code128',
    prefix: row.prefix ?? '',
    length: row.length ?? 18,
    checksumRule: row.checksumRule ?? 'none',
    gs1CompanyPrefixLength: row.gs1CompanyPrefixLength ? String(row.gs1CompanyPrefixLength) : '',
    allowedSourceDocumentTypes: [...(row.allowedSourceDocumentTypes ?? [])],
    status: row.status === 'disabled' ? 'disabled' : 'active',
  })
  editingRuleCode.value = row.ruleCode ?? null
  showErrors.value = false
  open.value = true
}

function toggleSource(value: string, checked: boolean) {
  if (checked && !form.allowedSourceDocumentTypes.includes(value)) {
    form.allowedSourceDocumentTypes.push(value)
  }
  if (!checked) {
    form.allowedSourceDocumentTypes = form.allowedSourceDocumentTypes.filter(
      (item) => item !== value,
    )
  }
}

function onSourceChange(value: string, event: Event) {
  toggleSource(value, (event.target as HTMLInputElement).checked)
}

async function submitRule() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }

  try {
    await saveRule({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      ruleCode: form.ruleCode.trim(),
      barcodeType: form.barcodeType,
      prefix: form.prefix.trim(),
      length: Number(form.length),
      checksumRule: form.checksumRule.trim(),
      allowedSourceDocumentTypes: [...form.allowedSourceDocumentTypes],
      status: form.status,
      gs1CompanyPrefixLength: isGs1.value ? gs1PrefixNumber.value : undefined,
    })
    notifySuccess(`条码规则「${form.ruleCode.trim()}」已保存。`)
    open.value = false
    resetForm()
  } catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="条码规则"
      :breadcrumbs="[{ label: '条码标签' }]"
      :count="`${rulesTotal} 条规则`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="rulesPending"
          @click="refreshRules"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="open">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="resetForm">
              <PlusIcon aria-hidden="true" />
              新建规则
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>{{
                editingRuleCode ? `编辑条码规则 · ${editingRuleCode}` : '新建条码规则'
              }}</NvDialogTitle>
              <NvDialogDescription>{{
                editingRuleCode
                  ? '修改条码规则配置，规则编码不可修改。'
                  : '创建条码规则。GS1 类型必须填写公司前缀长度。'
              }}</NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitRule">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                {{
                  isGs1 && !form.gs1CompanyPrefixLength
                    ? 'GS1 规则必须填写公司前缀长度。'
                    : '请完整填写规则编码、条码类型、前缀、长度和校验规则。'
                }}
              </p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField :data-invalid="showErrors && !form.ruleCode.trim()">
                  <NvFieldLabel for="barcode-rule-code"
                    >规则编码 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-rule-code"
                    v-model="form.ruleCode"
                    autocomplete="off"
                    :readonly="Boolean(editingRuleCode)"
                  />
                  <NvFieldDescription v-if="editingRuleCode"
                    >规则编码由后端作为更新键，不可在编辑时修改。</NvFieldDescription
                  >
                </NvField>
                <NvField>
                  <NvFieldLabel>状态</NvFieldLabel>
                  <NvSelect v-model="form.status">
                    <NvSelectTrigger aria-label="规则状态"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in STATUS_OPTIONS"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField>
                  <NvFieldLabel>条码类型 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="form.barcodeType" aria-label="条码类型">
                    <NvSelectTrigger aria-label="条码类型"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in BARCODE_TYPE_OPTIONS"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="showErrors && !form.prefix.trim()">
                  <NvFieldLabel for="barcode-rule-prefix"
                    >条码前缀 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="barcode-rule-prefix" v-model="form.prefix" autocomplete="off" />
                </NvField>
                <NvField :data-invalid="showErrors && !(Number(form.length) > 0)">
                  <NvFieldLabel for="barcode-rule-length"
                    >总长度 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-rule-length"
                    v-model.number="form.length"
                    type="number"
                    min="1"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !form.checksumRule.trim()">
                  <NvFieldLabel for="barcode-rule-checksum"
                    >校验规则 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-rule-checksum"
                    v-model="form.checksumRule"
                    autocomplete="off"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && isGs1 && !form.gs1CompanyPrefixLength">
                  <NvFieldLabel for="barcode-rule-gs1-prefix">GS1 公司前缀长度</NvFieldLabel>
                  <NvInput
                    id="barcode-rule-gs1-prefix"
                    v-model="form.gs1CompanyPrefixLength"
                    type="number"
                    min="1"
                  />
                  <NvFieldDescription>仅 GS1-128 / GS1 DataMatrix 等规则必填。</NvFieldDescription>
                </NvField>
                <NvField class="sm:col-span-2">
                  <NvFieldLabel>适用场景</NvFieldLabel>
                  <div class="grid gap-2 rounded-md border p-3 sm:grid-cols-2">
                    <label
                      v-for="option in SOURCE_DOCUMENT_OPTIONS"
                      :key="option.value"
                      class="flex items-center gap-2 text-sm"
                    >
                      <input
                        type="checkbox"
                        class="size-4"
                        :aria-label="`适用场景：${option.label}`"
                        :checked="form.allowedSourceDocumentTypes.includes(option.value)"
                        @change="onSourceChange(option.value, $event)"
                      />
                      {{ option.label }}
                    </label>
                  </div>
                </NvField>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
                <NvButton type="submit" :disabled="saveRulePending">
                  <Spinner v-if="saveRulePending" aria-hidden="true" />
                  保存规则
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="按规则编码筛选">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-28" aria-label="状态筛选"
            ><NvSelectValue placeholder="全部状态"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部状态</NvSelectItem>
            <NvSelectItem
              v-for="option in STATUS_OPTIONS"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="rulesTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="rules"
      row-key="barcodeRuleId"
      :loading="rulesPending"
      empty-message="暂无条码规则。请先维护规则，再在物料档案中选为默认条码规则。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-gs1CompanyPrefixLength="{ row }">
        <span class="text-muted-foreground">{{
          row.gs1CompanyPrefixLength ? `GS1 公司前缀 ${row.gs1CompanyPrefixLength} 位` : '不适用'
        }}</span>
      </template>
      <template #cell-allowedSourceDocumentTypes="{ row }">
        {{ sourceLabels(row.allowedSourceDocumentTypes) }}
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status === 'disabled' ? 'disabled' : 'active'"
          :label="statusLabel(row.status)"
        />
      </template>
      <template #cell-skuLink="{ row }">
        <div class="grid gap-1 text-sm">
          <RouterLink class="text-primary underline-offset-4 hover:underline" to="/master-data/skus"
            >打开物料页</RouterLink
          >
          <span class="text-xs text-muted-foreground">按默认条码规则反查待 SKU facade 支持。</span>
        </div>
      </template>
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          variant="ghost"
          type="button"
          :disabled="!row.ruleCode"
          @click="openEdit(row)"
        >
          <PencilIcon aria-hidden="true" />
          编辑
        </NvButton>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
