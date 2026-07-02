<script setup lang="ts">
import type { BusinessConsoleBarcodeRuleItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBarcodeRules } from '@/composables/useBusinessBarcode'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  FieldPro,
  FieldProDescription,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '条码规则', requiredPermissions: ['business.barcodes.templates.manage'] } })

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

const open = shallowRef(false)
const showErrors = shallowRef(false)
const keyword = shallowRef('')
const statusFilter = shallowRef('all')

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

const columns: DataTableProColumn<BusinessConsoleBarcodeRuleItem>[] = [
  { key: 'ruleCode', header: '规则编码', cellClass: 'font-medium', accessor: (r) => r.ruleCode ?? '无' },
  { key: 'barcodeType', header: '条码类型', width: 'w-28', accessor: (r) => typeLabel(r.barcodeType) },
  { key: 'prefix', header: '前缀', width: 'w-32', accessor: (r) => r.prefix ?? '无' },
  { key: 'length', header: '长度', width: 'w-20', accessor: (r) => String(r.length ?? '无') },
  { key: 'gs1CompanyPrefixLength', header: 'GS1 前缀', width: 'w-32' },
  { key: 'allowedSourceDocumentTypes', header: '适用场景' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'skuLink', header: '物料使用', align: 'end', width: 'w-40' },
]

watch(keyword, (value) => {
  filters.keyword = value.trim() || undefined
  filters.skip = 0
})

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
  filters.skip = 0
})

const errorMessage = computed(() => rulesError.value instanceof Error ? rulesError.value.message : '')
const isGs1 = computed(() => form.barcodeType.toLowerCase().includes('gs1'))
const gs1PrefixNumber = computed(() => Number(form.gs1CompanyPrefixLength))
const canSubmit = computed(() =>
  form.ruleCode.trim().length > 0
  && form.barcodeType.trim().length > 0
  && form.prefix.trim().length > 0
  && Number(form.length) > 0
  && form.checksumRule.trim().length > 0
  && (!isGs1.value || (Number.isInteger(gs1PrefixNumber.value) && gs1PrefixNumber.value > 0)),
)

function typeLabel(value?: string | null) {
  if (!value) return '无'
  return BARCODE_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? value
}

function sourceLabels(values?: readonly string[] | null) {
  if (!values?.length) return '暂未限定'
  return values.map((value) => SOURCE_DOCUMENT_OPTIONS.find((o) => o.value === value)?.label ?? value).join('、')
}

function statusLabel(value?: string | null) {
  return value === 'disabled' ? '停用' : '启用'
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
  showErrors.value = false
}

function toggleSource(value: string, checked: boolean) {
  if (checked && !form.allowedSourceDocumentTypes.includes(value)) {
    form.allowedSourceDocumentTypes.push(value)
  }
  if (!checked) {
    form.allowedSourceDocumentTypes = form.allowedSourceDocumentTypes.filter((item) => item !== value)
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
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="条码规则" :breadcrumbs="[{ label: '条码标签' }]" :count="`${rulesTotal} 条规则`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="rulesPending" @click="refreshRules">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="open">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="resetForm">
              <PlusIcon aria-hidden="true" />
              新建规则
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>新建或更新条码规则</DialogProTitle>
              <DialogProDescription>规则编码相同则更新。GS1 类型必须填写公司前缀长度。</DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitRule">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                {{ isGs1 && !form.gs1CompanyPrefixLength ? 'GS1 规则必须填写公司前缀长度。' : '请完整填写规则编码、条码类型、前缀、长度和校验规则。' }}
              </p>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !form.ruleCode.trim()">
                  <FieldProLabel for="barcode-rule-code">规则编码 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-rule-code" v-model="form.ruleCode" autocomplete="off" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel>状态</FieldProLabel>
                  <SelectPro v-model="form.status">
                    <SelectProTrigger aria-label="规则状态"><SelectProValue /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="option in STATUS_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro>
                  <FieldProLabel>条码类型 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.barcodeType" aria-label="条码类型">
                    <SelectProTrigger aria-label="条码类型"><SelectProValue /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="option in BARCODE_TYPE_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.prefix.trim()">
                  <FieldProLabel for="barcode-rule-prefix">条码前缀 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-rule-prefix" v-model="form.prefix" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !(Number(form.length) > 0)">
                  <FieldProLabel for="barcode-rule-length">总长度 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-rule-length" v-model.number="form.length" type="number" min="1" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.checksumRule.trim()">
                  <FieldProLabel for="barcode-rule-checksum">校验规则 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-rule-checksum" v-model="form.checksumRule" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && isGs1 && !form.gs1CompanyPrefixLength">
                  <FieldProLabel for="barcode-rule-gs1-prefix">GS1 公司前缀长度</FieldProLabel>
                  <InputPro id="barcode-rule-gs1-prefix" v-model="form.gs1CompanyPrefixLength" type="number" min="1" />
                  <FieldProDescription>仅 GS1-128 / GS1 DataMatrix 等规则必填。</FieldProDescription>
                </FieldPro>
                <FieldPro class="sm:col-span-2">
                  <FieldProLabel>适用场景</FieldProLabel>
                  <div class="grid gap-2 rounded-md border p-3 sm:grid-cols-2">
                    <label v-for="option in SOURCE_DOCUMENT_OPTIONS" :key="option.value" class="flex items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        class="size-4"
                        :aria-label="`适用场景：${option.label}`"
                        :checked="form.allowedSourceDocumentTypes.includes(option.value)"
                        @change="onSourceChange(option.value, $event)"
                      >
                      {{ option.label }}
                    </label>
                  </div>
                </FieldPro>
              </FieldProGroup>
              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="open = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="saveRulePending">
                  <Spinner v-if="saveRulePending" aria-hidden="true" />
                  保存规则
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="keyword" search-placeholder="按规则编码筛选">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-28" aria-label="状态筛选"><SelectProValue placeholder="全部状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部状态</SelectProItem>
            <SelectProItem v-for="option in STATUS_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="rules"
      row-key="barcodeRuleId"
      :loading="rulesPending"
      empty-message="暂无条码规则。请先维护规则，再在物料档案中选为默认条码规则。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-gs1CompanyPrefixLength="{ row }">
        <span class="text-muted-foreground">{{ row.gs1CompanyPrefixLength ? `GS1 公司前缀 ${row.gs1CompanyPrefixLength} 位` : '不适用' }}</span>
      </template>
      <template #cell-allowedSourceDocumentTypes="{ row }">
        {{ sourceLabels(row.allowedSourceDocumentTypes) }}
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro :value="row.status === 'disabled' ? 'disabled' : 'active'" :label="statusLabel(row.status)" />
      </template>
      <template #cell-skuLink="{ row }">
        <RouterLink class="text-sm text-primary underline-offset-4 hover:underline" :to="{ path: '/master-data/skus', query: { barcodeRule: row.ruleCode } }">
          查看使用该规则的物料
        </RouterLink>
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
