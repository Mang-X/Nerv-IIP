<script setup lang="ts">
import type { BusinessConsoleBarcodeTemplateItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBarcodeTemplates } from '@/composables/useBusinessBarcode'
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

definePage({ meta: { requiresAuth: true, title: '标签模板', requiredPermissions: ['business.barcodes.templates.manage'] } })

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
const statusFilter = shallowRef('all')

const form = reactive({
  templateCode: '',
  templateName: '',
  templateFileId: '',
  variableSchemaJson: '{"fields":["skuCode","lotNo","expiryDate"]}',
  status: 'active',
})

const columns: DataTableProColumn<BusinessConsoleBarcodeTemplateItem>[] = [
  { key: 'templateCode', header: '模板编码', cellClass: 'font-medium', accessor: (r) => r.templateCode ?? '无' },
  { key: 'templateName', header: '模板名称', accessor: (r) => r.templateName ?? '无' },
  { key: 'templateFileId', header: '模板文件', width: 'w-40', accessor: (r) => r.templateFileId ?? '无' },
  { key: 'variableSchemaJson', header: '字段说明' },
  { key: 'status', header: '状态', width: 'w-24' },
]

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
  filters.skip = 0
})

const errorMessage = computed(() => templatesError.value instanceof Error ? templatesError.value.message : '')
const canSubmit = computed(() =>
  form.templateCode.trim().length > 0
  && form.templateName.trim().length > 0
  && form.templateFileId.trim().length > 0
  && form.variableSchemaJson.trim().length > 0
  && isValidJson(form.variableSchemaJson),
)

function isValidJson(value: string) {
  try {
    JSON.parse(value)
    return true
  }
  catch {
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
  showErrors.value = false
}

function fieldSummary(value?: string | null) {
  if (!value) return '未配置字段'
  try {
    const parsed = JSON.parse(value) as { fields?: unknown }
    if (Array.isArray(parsed.fields)) {
      return parsed.fields.map(String).join('、')
    }
  }
  catch {
    return value
  }
  return value
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
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="标签模板" :breadcrumbs="[{ label: '条码标签' }]" :count="`${templatesTotal} 个模板`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="templatesPending" @click="refreshTemplates">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="open">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="resetForm">
              <PlusIcon aria-hidden="true" />
              新建模板
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>新建或更新标签模板</DialogProTitle>
              <DialogProDescription>模板编码相同则更新。模板文件由文件服务管理，本页只维护引用和字段结构。</DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitTemplate">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写模板编码、名称、模板文件，并提供合法 JSON 字段说明。
              </p>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !form.templateCode.trim()">
                  <FieldProLabel for="barcode-template-code">模板编码 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-template-code" v-model="form.templateCode" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.templateName.trim()">
                  <FieldProLabel for="barcode-template-name">模板名称 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-template-name" v-model="form.templateName" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.templateFileId.trim()">
                  <FieldProLabel for="barcode-template-file">模板文件 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-template-file" v-model="form.templateFileId" autocomplete="off" />
                  <FieldProDescription>填写文件服务返回的模板文件标识。</FieldProDescription>
                </FieldPro>
                <FieldPro>
                  <FieldProLabel>状态</FieldProLabel>
                  <SelectPro v-model="form.status">
                    <SelectProTrigger aria-label="模板状态"><SelectProValue /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="option in STATUS_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro class="sm:col-span-2" :data-invalid="showErrors && !isValidJson(form.variableSchemaJson)">
                  <FieldProLabel for="barcode-template-schema">字段说明 <span class="text-destructive">*</span></FieldProLabel>
                  <textarea id="barcode-template-schema" v-model="form.variableSchemaJson" class="min-h-24 rounded-md border bg-background px-3 py-2 text-sm" />
                  <FieldProDescription>建议包含适用对象和字段数组，例如 SKU、批次、有效期或 SSCC。</FieldProDescription>
                </FieldPro>
              </FieldProGroup>
              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="open = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="saveTemplatePending">
                  <Spinner v-if="saveTemplatePending" aria-hidden="true" />
                  保存模板
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar>
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
        <StatusBadgePro :value="row.status === 'disabled' ? 'disabled' : 'active'" :label="statusLabel(row.status)" />
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
