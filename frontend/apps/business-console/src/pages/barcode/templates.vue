<script setup lang="ts">
import type { BusinessConsoleBarcodeTemplateItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBarcodeTemplates } from '@/composables/useBusinessBarcode'
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
import { PencilIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
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
  variableSchemaJson: '{"fields":["skuCode","lotNo","expiryDate"]}',
  status: 'active',
})

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
  { key: 'variableSchemaJson', header: '字段说明' },
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

const errorMessage = computed(() =>
  templatesError.value instanceof Error ? templatesError.value.message : '',
)
// 编辑态由所选行带出的只读上下文（模板编码是身份，不可改）。
const carriedItems = computed(() => [{ label: '模板编码', value: editingTemplateCode.value }])
const canSubmit = computed(
  () =>
    form.templateCode.trim().length > 0 &&
    form.templateName.trim().length > 0 &&
    form.templateFileId.trim().length > 0 &&
    form.variableSchemaJson.trim().length > 0 &&
    isValidJson(form.variableSchemaJson),
)

function isValidJson(value: string) {
  try {
    JSON.parse(value)
    return true
  } catch {
    return false
  }
}

function resetForm() {
  Object.assign(form, {
    templateCode: '',
    templateName: '',
    templateFileId: '',
    variableSchemaJson: '{"fields":["skuCode","lotNo","expiryDate"]}',
    status: 'active',
  })
  editingTemplateCode.value = null
  showErrors.value = false
}

function openEdit(row: BusinessConsoleBarcodeTemplateItem) {
  Object.assign(form, {
    templateCode: row.templateCode ?? '',
    templateName: row.templateName ?? '',
    templateFileId: row.templateFileId ?? '',
    variableSchemaJson: row.variableSchemaJson ?? '{"fields":["skuCode","lotNo","expiryDate"]}',
    status: row.status === 'disabled' ? 'disabled' : 'active',
  })
  editingTemplateCode.value = row.templateCode ?? null
  showErrors.value = false
  open.value = true
}

/**
 * 模板变量名 → 中文说明。
 *
 * 只在模板 JSON 用旧的 `{fields:[...]}` 形态（没带 label）时才需要：种子写的
 * `{variables:[{name,label}]}` 自带中文 label，走 label 就够了。
 */
const TEMPLATE_FIELD_LABELS: Record<string, string> = {
  gtin: 'GTIN',
  skucode: '物料编码',
  skuname: '物料名称',
  lotno: '批次号',
  serialno: '序列号',
  serialprefix: '序列号前缀',
  quantity: '数量',
  uomcode: '计量单位',
  suppliercode: '供应商编码',
  customercode: '客户编码',
  workordercode: '工单号',
  workorderno: '工单号',
  workcentercode: '工作中心编码',
  workcentername: '工作中心名称',
  sitecode: '厂区',
  sourcedocumentid: '来源单号',
  expirydate: '有效期',
  productiondate: '生产日期',
  printedon: '打印日期',
}
function templateFieldLabel(name: string) {
  return TEMPLATE_FIELD_LABELS[name.toLowerCase().replace(/[-_\s]/g, '')] ?? name
}

/**
 * 「字段说明」列的取文。
 *
 * 模板 JSON 有两种形态：现网种子写的是 `{version, variables:[{name,label,type}]}`
 * （`WorldHistoryLabelSpec.BuildSchema`，**label 本来就是中文**），旧表单默认值写的是
 * `{fields:[...]}`。之前只认后者，前者整段落到 `return value`，于是把 `version`、
 * `variables`、`gtin`、`lotNo` 这些 JSON 字段名连同花括号一起摊到了列上。
 * 现在优先取 `variables[].label`（没有 label 才退回 `name`），拿不到结构才认为是未配置。
 */
function fieldSummary(value?: string | null) {
  if (!value) return '未配置字段'
  try {
    const parsed = JSON.parse(value) as {
      fields?: unknown
      variables?: unknown
    }
    if (Array.isArray(parsed.variables)) {
      const names = parsed.variables.flatMap((variable) => {
        if (typeof variable === 'string') return [templateFieldLabel(variable)]
        if (variable && typeof variable === 'object') {
          const entry = variable as { label?: unknown; name?: unknown }
          // label 是模板作者写的中文说明，直接用；只有退回 name 时才查词表。
          if (typeof entry.label === 'string' && entry.label.trim()) return [entry.label.trim()]
          if (typeof entry.name === 'string' && entry.name.trim()) {
            return [templateFieldLabel(entry.name.trim())]
          }
        }
        return []
      })
      if (names.length) return names.join('、')
    }
    if (Array.isArray(parsed.fields) && parsed.fields.length) {
      return parsed.fields.map((field) => templateFieldLabel(String(field))).join('、')
    }
  } catch {
    return '字段说明格式有误'
  }
  return '未配置字段'
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
      variableSchemaJson: form.variableSchemaJson.trim(),
      status: form.status,
    })
    notifySuccess(`标签模板「${form.templateName.trim()}」已保存。`)
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
                请填写模板编码、名称、模板文件，并提供合法 JSON 字段说明。
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
                <NvField
                  class="sm:col-span-2"
                  :data-invalid="showErrors && !isValidJson(form.variableSchemaJson)"
                >
                  <NvFieldLabel for="barcode-template-schema"
                    >字段说明 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <textarea
                    id="barcode-template-schema"
                    v-model="form.variableSchemaJson"
                    class="min-h-24 rounded-md border bg-background px-3 py-2 text-sm"
                  />
                  <!-- JSON 结构不是显然的，保留一行语法示例。 -->
                  <NvFieldDescription
                    >JSON 格式，如 {"fields":["skuCode","lotNo"]}</NvFieldDescription
                  >
                </NvField>
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
      empty-message="暂无标签模板。请先维护模板文件引用和字段说明。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-variableSchemaJson="{ row }">
        <div class="grid gap-1">
          <span class="text-sm">{{ fieldSummary(row.variableSchemaJson) }}</span>
          <span class="text-xs text-muted-foreground">适用对象和字段由模板 JSON 明确声明。</span>
        </div>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status === 'disabled' ? 'disabled' : 'active'"
          :label="statusLabel(row.status)"
        />
      </template>
      <template #cell-actions="{ row }">
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
