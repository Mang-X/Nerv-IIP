<script setup lang="ts">
import type { BusinessConsoleBarcodeTemplateItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import TemplateAssetRetirement from '@/components/barcode/TemplateAssetRetirement.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBarcodeTemplates } from '@/composables/useBusinessBarcode'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  emptyVariableRow,
  LABEL_DATA_ITEMS,
  parseVariableRows,
  rowDisplayLabel,
  serializeVariableRows,
  variableRowsError,
  variableSummary,
  type LabelVariableRow,
} from '@/components/barcode/labelTemplateVariables'
import {
  NvButton,
  NvCheckbox,
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
import { PencilIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '标签模板',
    requiredPermissions: ['business.barcodes.templates.manage'],
  },
})

const STATUS_OPTIONS = [
  { value: 'active', label: '启用' },
  { value: 'disabled', label: '停用' },
]

const {
  filters,
  refreshTemplates,
  saveTemplate,
  saveTemplatePending,
  templates,
  templatesError,
  templatesPending,
  templatesTotal,
} = useBarcodeTemplates()

const open = shallowRef(false)
const showErrors = shallowRef(false)
const editingTemplateCode = shallowRef<string | null>(null)
const statusFilter = shallowRef('all')
const page = shallowRef(1)
const pageSize = shallowRef('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)

const form = reactive({
  templateCode: '',
  templateName: '',
  templateFileId: '',
  status: 'active',
})
const variableRows = shallowRef<LabelVariableRow[]>([emptyVariableRow()])

const columns: NvDataTableColumn<BusinessConsoleBarcodeTemplateItem>[] = [
  {
    key: 'templateCode',
    header: '模板编码',
    cellClass: 'font-medium',
    accessor: (r) => r.templateCode ?? '无',
  },
  { key: 'templateName', header: '模板名称', accessor: (r) => r.templateName ?? '无' },
  {
    key: 'templateFileId',
    header: '模板文件',
    width: 'w-40',
    accessor: (r) => r.templateFileId ?? '无',
  },
  { key: 'variableSchemaJson', header: '标签数据项' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
  filters.skip = 0
  page.value = 1
})

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

const errorMessage = computed(() => inlineErrorMessage(templatesError.value))
// 编辑态由所选行带出的只读上下文（模板编码是身份，不可改）。
const carriedItems = computed(() => [{ label: '模板编码', value: editingTemplateCode.value }])
const canSubmit = computed(
  () =>
    form.templateCode.trim().length > 0 &&
    form.templateName.trim().length > 0 &&
    form.templateFileId.trim().length > 0 &&
    !variableError.value,
)
const variableError = computed(() => variableRowsError(variableRows.value))

// 每行的数据项选项：平台目录 + 本行已存的目录外数据项（只显示它的中文名称）。
function dataItemOptions(row: LabelVariableRow) {
  const options = LABEL_DATA_ITEMS.map((item) => ({ value: item.name, label: item.label }))
  if (row.name && !options.some((option) => option.value === row.name)) {
    options.unshift({ value: row.name, label: rowDisplayLabel(row.name, row.label) })
  }
  return options
}
function selectDataItem(index: number, value: unknown) {
  const name = typeof value === 'string' ? value : ''
  const rows = [...variableRows.value]
  const previous = rows[index]!
  const previousDefault = rowDisplayLabel(previous.name, '')
  // 显示名称没被手改过时，跟着数据项换成新的中文名。
  const label =
    !previous.label.trim() || previous.label === previousDefault
      ? (LABEL_DATA_ITEMS.find((item) => item.name === name)?.label ?? '')
      : previous.label
  rows[index] = { ...previous, name, label }
  variableRows.value = rows
}
function updateRow(index: number, patch: Partial<LabelVariableRow>) {
  const rows = [...variableRows.value]
  rows[index] = { ...rows[index]!, ...patch }
  variableRows.value = rows
}
function addVariableRow() {
  variableRows.value = [...variableRows.value, emptyVariableRow()]
}
function removeVariableRow(index: number) {
  variableRows.value = variableRows.value.filter((_, i) => i !== index)
}

function resetForm() {
  Object.assign(form, {
    templateCode: '',
    templateName: '',
    templateFileId: '',
    status: 'active',
  })
  variableRows.value = [emptyVariableRow()]
  editingTemplateCode.value = null
  showErrors.value = false
}

function openEdit(row: BusinessConsoleBarcodeTemplateItem) {
  Object.assign(form, {
    templateCode: row.templateCode ?? '',
    templateName: row.templateName ?? '',
    templateFileId: row.templateFileId ?? '',
    status: row.status === 'disabled' ? 'disabled' : 'active',
  })
  const rows = parseVariableRows(row.variableSchemaJson)
  variableRows.value = rows.length ? rows : [emptyVariableRow()]
  editingTemplateCode.value = row.templateCode ?? null
  showErrors.value = false
  open.value = true
}

function statusLabel(value?: string | null) {
  return value === 'disabled' ? '停用' : '启用'
}

async function submitTemplate() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }

  try {
    await saveTemplate({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      templateCode: form.templateCode.trim(),
      templateName: form.templateName.trim(),
      templateFileId: form.templateFileId.trim(),
      variableSchemaJson: serializeVariableRows(variableRows.value),
      status: form.status,
    })
    notifySuccess(`标签模板「${form.templateName.trim()}」已保存。`)
    open.value = false
    resetForm()
  } catch (error) {
    notifyOperationFailure('保存标签模板失败', error, '保存标签模板失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="标签模板"
      :breadcrumbs="[{ label: '条码标签' }]"
      :count="`${templatesTotal} 个模板`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="templatesPending"
          @click="refreshTemplates"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="open">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="resetForm">
              <PlusIcon aria-hidden="true" />
              新建模板
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>{{
                editingTemplateCode ? `编辑标签模板 · ${editingTemplateCode}` : '新建标签模板'
              }}</NvDialogTitle>
              <!-- 说明不上界面：仅供读屏播报。 -->
              <NvDialogDescription class="sr-only">{{
                editingTemplateCode
                  ? `标签模板 ${editingTemplateCode} 的配置。`
                  : '登记一个标签模板。'
              }}</NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitTemplate">
              <!-- 编辑态：模板编码由所选行带出，只读展示，不做成 readonly 输入框。 -->
              <CarriedContextSummary
                v-if="editingTemplateCode"
                label="编辑对象"
                :items="carriedItems"
              />
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写模板编码、名称、模板文件，并补全标签数据项。
              </p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField
                  v-if="!editingTemplateCode"
                  :data-invalid="showErrors && !form.templateCode.trim()"
                >
                  <NvFieldLabel for="barcode-template-code"
                    >模板编码 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-template-code"
                    v-model="form.templateCode"
                    autocomplete="off"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !form.templateName.trim()">
                  <NvFieldLabel for="barcode-template-name"
                    >模板名称 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-template-name"
                    v-model="form.templateName"
                    autocomplete="off"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !form.templateFileId.trim()">
                  <NvFieldLabel for="barcode-template-file"
                    >模板文件 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-template-file"
                    v-model="form.templateFileId"
                    autocomplete="off"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel>状态</NvFieldLabel>
                  <NvSelect v-model="form.status">
                    <NvSelectTrigger aria-label="模板状态"><NvSelectValue /></NvSelectTrigger>
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
                <div class="grid gap-2 sm:col-span-2">
                  <span class="text-sm font-medium">
                    标签数据项 <span class="text-destructive">*</span>
                  </span>
                  <div
                    v-for="(row, index) in variableRows"
                    :key="index"
                    class="grid items-end gap-2 rounded-md border p-3 sm:grid-cols-[1fr_1fr_auto_6rem_auto]"
                  >
                    <NvField>
                      <NvFieldLabel :for="`barcode-template-item-${index}`">数据项</NvFieldLabel>
                      <NvSelect
                        :model-value="row.name"
                        @update:model-value="(value) => selectDataItem(index, value)"
                      >
                        <NvSelectTrigger :id="`barcode-template-item-${index}`">
                          <NvSelectValue placeholder="选择数据项" />
                        </NvSelectTrigger>
                        <NvSelectContent>
                          <NvSelectItem
                            v-for="option in dataItemOptions(row)"
                            :key="option.value"
                            :value="option.value"
                            >{{ option.label }}</NvSelectItem
                          >
                        </NvSelectContent>
                      </NvSelect>
                    </NvField>
                    <NvField>
                      <NvFieldLabel :for="`barcode-template-label-${index}`">显示名称</NvFieldLabel>
                      <NvInput
                        :id="`barcode-template-label-${index}`"
                        :model-value="row.label"
                        autocomplete="off"
                        @update:model-value="
                          (value) => updateRow(index, { label: String(value ?? '') })
                        "
                      />
                    </NvField>
                    <label class="flex h-9 items-center gap-2 text-sm">
                      <NvCheckbox
                        :model-value="row.required"
                        :aria-label="`第 ${index + 1} 行必填`"
                        @update:model-value="
                          (value) => updateRow(index, { required: value === true })
                        "
                      />
                      必填
                    </label>
                    <NvField>
                      <NvFieldLabel :for="`barcode-template-max-${index}`">最大长度</NvFieldLabel>
                      <NvInput
                        :id="`barcode-template-max-${index}`"
                        :model-value="row.maxLength"
                        type="number"
                        min="1"
                        step="1"
                        @update:model-value="
                          (value) => updateRow(index, { maxLength: String(value ?? '') })
                        "
                      />
                    </NvField>
                    <NvButton
                      type="button"
                      variant="ghost"
                      size="sm"
                      :aria-label="`删除第 ${index + 1} 行数据项`"
                      @click="removeVariableRow(index)"
                    >
                      <Trash2Icon aria-hidden="true" />
                    </NvButton>
                  </div>
                  <NvButton
                    type="button"
                    variant="outline"
                    size="sm"
                    class="justify-self-start"
                    @click="addVariableRow"
                  >
                    <PlusIcon aria-hidden="true" />
                    添加数据项
                  </NvButton>
                  <p
                    v-if="showErrors && variableError"
                    class="text-sm text-destructive"
                    role="alert"
                  >
                    {{ variableError }}
                  </p>
                </div>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
                <NvButton type="submit" :disabled="saveTemplatePending">
                  <Spinner v-if="saveTemplatePending" aria-hidden="true" />
                  保存模板
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar>
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
      :total-items="templatesTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="templates"
      row-key="templateId"
      :loading="templatesPending"
      empty-message="暂无标签模板。请先维护模板文件和标签数据项。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-variableSchemaJson="{ row }">
        <div class="grid gap-1">
          <span class="text-sm">{{ variableSummary(row.variableSchemaJson) }}</span>
        </div>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status === 'disabled' ? 'disabled' : 'active'"
          :label="statusLabel(row.status)"
        />
      </template>
      <template #cell-actions="{ row }">
        <TemplateAssetRetirement
          v-if="
            row.templateId && row.templateFileId && filters.organizationId && filters.environmentId
          "
          :key="`${filters.organizationId}/${filters.environmentId}/${row.templateId}/${row.templateFileId}`"
          :organization-id="filters.organizationId"
          :environment-id="filters.environmentId"
          :template-id="row.templateId"
          :file-id="row.templateFileId"
          :template-name="row.templateName ?? row.templateCode ?? ''"
          :inactive="row.status === 'disabled'"
        />
        <NvButton
          size="sm"
          variant="ghost"
          type="button"
          :disabled="!row.templateCode"
          @click="openEdit(row)"
        >
          <PencilIcon aria-hidden="true" />
          编辑
        </NvButton>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
