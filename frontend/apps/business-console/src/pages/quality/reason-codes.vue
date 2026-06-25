<script setup lang="ts">
import type {
  CreateQualityReasonRequest,
  QualityReasonItem,
} from '@/composables/usePromotedCatalogs'
import type { DataTableProColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useQualityReasonCodes } from '@/composables/usePromotedCatalogs'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  ButtonPro,
  DataTablePagination,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
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
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '原因码目录' } })

const {
  archiveReason,
  archivePending,
  createReason,
  createPending,
  filters,
  refresh,
  reasons,
  reasonsError,
  reasonsPending,
  reasonsTotal,
  updateReason,
  updatePending,
} = useQualityReasonCodes()

// 严重度常量：value 存英文枚举，label 显示中文。
const severityOptions = [
  { label: '轻微', value: 'minor' },
  { label: '一般', value: 'major' },
  { label: '严重', value: 'critical' },
]
function severityLabel(value?: string | null) {
  if (!value) return '—'
  return severityOptions.find((o) => o.value === value)?.label ?? value
}
function severityTone(value?: string | null) {
  if (value === 'critical') return 'danger'
  if (value === 'major') return 'warning'
  if (value === 'minor') return 'neutral'
  return 'neutral'
}

// Toolbar 搜索绑定到 search 筛选（空串不污染查询）。
const search = computed({
  get: () => filters.search ?? '',
  set: (value: string) => { filters.search = value.trim() ? value : undefined },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

const listErrorMessage = computed(() =>
  reasonsError.value instanceof Error ? reasonsError.value.message : '',
)

const columns: DataTableProColumn<QualityReasonItem>[] = [
  { key: 'reasonCode', header: '编码', width: 'w-32' },
  { key: 'reasonName', header: '原因', cellClass: 'font-medium' },
  { key: 'groupName', header: '原因组' },
  { key: 'severity', header: '严重度', width: 'w-28' },
  { key: 'defaultDisposition', header: '默认处置' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

// ── 新建 / 编辑表单 ─────────────────────────────────────────────
interface QualityReasonForm {
  reasonCode: string
  reasonName: string
  groupName: string
  severity: string
  defaultDisposition: string
}

function blankForm(): QualityReasonForm {
  return {
    reasonCode: '',
    reasonName: '',
    groupName: '',
    severity: '',
    defaultDisposition: '',
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑原因的 reasonCode（编码即身份，编辑态只读）。
const editingCode = shallowRef<string | null>(null)
const form = reactive<QualityReasonForm>(blankForm())

const codeValid = computed(() => !!editingCode.value || form.reasonCode.trim().length > 0)
const nameValid = computed(() => form.reasonName.trim().length > 0)
const groupValid = computed(() => form.groupName.trim().length > 0)
const severityValid = computed(() => form.severity.trim().length > 0)
const canSubmit = computed(() =>
  codeValid.value && nameValid.value && groupValid.value && severityValid.value,
)

function openCreate() {
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: QualityReasonItem) {
  if (!row.reasonCode) return
  editingCode.value = row.reasonCode
  showErrors.value = false
  Object.assign(form, {
    reasonCode: row.reasonCode ?? '',
    reasonName: row.reasonName ?? '',
    groupName: row.groupName ?? '',
    severity: row.severity ?? '',
    defaultDisposition: row.defaultDisposition ?? '',
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const shared = {
    reasonName: form.reasonName.trim(),
    groupName: form.groupName.trim(),
    severity: form.severity,
    defaultDisposition: form.defaultDisposition.trim() || null,
  }
  try {
    if (editingCode.value) {
      await updateReason(editingCode.value, shared)
      notifySuccess(`原因「${shared.reasonName}」已更新。`)
    }
    else {
      const body: CreateQualityReasonRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        reasonCode: form.reasonCode.trim(),
        ...shared,
      }
      await createReason(body)
      notifySuccess(`已创建质量原因「${shared.reasonName}」。`)
    }
    showErrors.value = false
    formOpen.value = false
    editingCode.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 停用 ────────────────────────────────────────────────────────
const archiveOpen = shallowRef(false)
const archiveTarget = shallowRef<QualityReasonItem | null>(null)
function openArchive(row: QualityReasonItem) {
  if (!row.reasonCode) return
  archiveTarget.value = row
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.reasonCode) return
  try {
    await archiveReason(target.reasonCode)
    notifySuccess(`原因「${target.reasonName}」已停用。`)
    archiveOpen.value = false
    archiveTarget.value = null
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="原因码目录"
      :breadcrumbs="[{ label: '质量管理' }]"
      :count="`${reasonsTotal} 个原因`"
    >
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="reasonsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建原因
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>{{ editingCode ? '编辑质量原因' : '新建质量原因' }}</DialogProTitle>
              <DialogProDescription>
                质量原因是可复用的质量主数据：按原因组归类，预设严重度与默认处置，供检验 / 不合格品记录引用。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写带 * 的必填项（已标红）。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="showErrors && !codeValid">
                  <FieldLabel for="reason-code">原因编码 <span class="text-destructive">*</span></FieldLabel>
                  <InputPro
                    v-if="!editingCode"
                    id="reason-code"
                    v-model="form.reasonCode"
                    placeholder="例如：DEF-SCRATCH"
                  />
                  <InputPro v-else :model-value="editingCode" readonly disabled />
                  <FieldDescription>{{ editingCode ? '编码是原因身份，不可更改。' : '由工厂自定义、需唯一。' }}</FieldDescription>
                </Field>
                <Field :data-invalid="showErrors && !nameValid">
                  <FieldLabel for="reason-name">原因 <span class="text-destructive">*</span></FieldLabel>
                  <InputPro id="reason-name" v-model="form.reasonName" placeholder="例如：尺寸超差" />
                </Field>
                <Field :data-invalid="showErrors && !groupValid">
                  <FieldLabel for="reason-group">原因组 <span class="text-destructive">*</span></FieldLabel>
                  <InputPro id="reason-group" v-model="form.groupName" placeholder="例如：外观缺陷" />
                </Field>
                <Field :data-invalid="showErrors && !severityValid">
                  <FieldLabel for="reason-severity">严重度 <span class="text-destructive">*</span></FieldLabel>
                  <SelectPro v-model="form.severity">
                    <SelectProTrigger id="reason-severity"><SelectProValue placeholder="选择严重度" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in severityOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </Field>
              </FieldGroup>

              <FormSectionTitle>处置</FormSectionTitle>
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel for="reason-disposition">默认处置</FieldLabel>
                  <InputPro id="reason-disposition" v-model="form.defaultDisposition" placeholder="例如：返工 / 报废 / 让步接收" />
                </Field>
              </FieldGroup>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '创建原因' }}
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="search" search-placeholder="按原因或编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="reasons"
      row-key="reasonCode"
      :loading="reasonsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="原因码目录为空。新建可复用质量原因（按原因组归类、预设严重度与默认处置），供检验 / 不合格品记录引用。"
    >
      <template #cell-groupName="{ row }">
        <span v-if="row.groupName">{{ row.groupName }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-severity="{ row }">
        <StatusBadgePro v-if="row.severity" :label="severityLabel(row.severity)" :tone="severityTone(row.severity)" />
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-defaultDisposition="{ row }">
        <span v-if="row.defaultDisposition">{{ row.defaultDisposition }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro
          :label="row.enabled === false ? '停用' : '启用'"
          :tone="row.enabled === false ? 'neutral' : 'success'"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <ButtonPro type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</ButtonPro>
          <ButtonPro type="button" variant="ghost" size="sm" :disabled="row.enabled === false" @click="openArchive(row)">停用</ButtonPro>
        </div>
      </template>
    </DataTablePro>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="reasonsTotal" />

    <AlertDialog v-model:open="archiveOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>停用质量原因</AlertDialogTitle>
          <AlertDialogDescription>
            停用后原因「{{ archiveTarget?.reasonName }}」将不可在新的检验 / 不合格品记录中引用，历史记录不受影响。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction :disabled="archivePending" @click="confirmArchive">
            <Spinner v-if="archivePending" aria-hidden="true" />
            确认停用
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </BusinessLayout>
</template>
