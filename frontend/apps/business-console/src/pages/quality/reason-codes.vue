<script setup lang="ts">
import type {
  CreateQualityReasonRequest,
  QualityReasonItem,
} from '@/composables/usePromotedCatalogs'
import type { DataTableColumn } from '@nerv-iip/ui'
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
  Button,
  Checkbox,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '原因码目录' } })

const {
  archiveReason,
  archivePending,
  backendReady,
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

const columns: DataTableColumn<QualityReasonItem>[] = [
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
  reasonName: string
  groupName: string
  severity: string
  defaultDisposition: string
  enabled: boolean
}

function blankForm(): QualityReasonForm {
  return {
    reasonName: '',
    groupName: '',
    severity: '',
    defaultDisposition: '',
    enabled: true,
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑原因的记录标识。
const editingId = shallowRef<string | null>(null)
const editingCode = shallowRef<string | null>(null)
const form = reactive<QualityReasonForm>(blankForm())

const nameValid = computed(() => form.reasonName.trim().length > 0)
const canSubmit = computed(() => nameValid.value)

function openCreate() {
  editingId.value = null
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: QualityReasonItem) {
  if (!row.id) return
  editingId.value = row.id
  editingCode.value = row.reasonCode ?? null
  showErrors.value = false
  Object.assign(form, {
    reasonName: row.reasonName ?? '',
    groupName: row.groupName ?? '',
    severity: row.severity ?? '',
    defaultDisposition: row.defaultDisposition ?? '',
    enabled: row.enabled ?? true,
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const payload = {
    reasonName: form.reasonName.trim(),
    groupName: form.groupName.trim() || null,
    severity: form.severity || null,
    defaultDisposition: form.defaultDisposition.trim() || null,
    enabled: form.enabled,
  }
  try {
    if (editingId.value) {
      await updateReason(editingId.value, payload)
      notifySuccess(`原因「${payload.reasonName}」已更新。`)
    }
    else {
      const body: CreateQualityReasonRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...payload,
      }
      await createReason(body)
      notifySuccess(`已创建质量原因「${payload.reasonName}」。`)
    }
    showErrors.value = false
    formOpen.value = false
    editingId.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 停用 ────────────────────────────────────────────────────────
const archiveOpen = shallowRef(false)
const archiveTarget = shallowRef<QualityReasonItem | null>(null)
function openArchive(row: QualityReasonItem) {
  if (!row.id) return
  archiveTarget.value = row
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.id) return
  try {
    await archiveReason(target.id, '不再使用')
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
        <Button size="sm" variant="outline" type="button" :disabled="reasonsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="formOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建原因
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ editingId ? '编辑质量原因' : '新建质量原因' }}</DialogTitle>
              <DialogDescription>
                质量原因是可复用的质量主数据：按原因组归类，预设严重度与默认处置，供检验 / 不合格品记录引用。带 * 为必填项。
              </DialogDescription>
            </DialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写原因（已标红）。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="showErrors && !nameValid">
                  <FieldLabel for="reason-name">原因 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="reason-name" v-model="form.reasonName" placeholder="例如：尺寸超差" />
                </Field>
                <Field v-if="editingCode">
                  <FieldLabel>编码</FieldLabel>
                  <Input :model-value="editingCode" readonly disabled />
                  <FieldDescription>编码由系统自动生成，不可更改。</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="reason-group">原因组</FieldLabel>
                  <Input id="reason-group" v-model="form.groupName" placeholder="例如：外观缺陷" />
                </Field>
                <Field>
                  <FieldLabel for="reason-severity">严重度</FieldLabel>
                  <Select v-model="form.severity">
                    <SelectTrigger id="reason-severity"><SelectValue placeholder="选择严重度" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in severityOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </FieldGroup>

              <FormSectionTitle>处置</FormSectionTitle>
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel for="reason-disposition">默认处置</FieldLabel>
                  <Input id="reason-disposition" v-model="form.defaultDisposition" placeholder="例如：返工 / 报废 / 让步接收" />
                </Field>
                <Field class="self-start">
                  <FieldLabel>启用</FieldLabel>
                  <label for="reason-enabled" class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm">
                    <span>停用后不可在检验 / 不合格品记录中引用</span>
                    <Checkbox id="reason-enabled" v-model:checked="form.enabled" />
                  </label>
                </Field>
              </FieldGroup>

              <DialogFooter>
                <Button type="button" variant="outline" @click="formOpen = false">取消</Button>
                <Button type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingId ? '保存修改' : '创建原因' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <p
      v-if="!backendReady"
      class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
      role="status"
    >
      页面建设中：质量原因目录正在后端实现（#397）。当前为 IA / 表单预览，列表为空、保存暂不可用；后端交付后此处显示真实原因码（按原因组归类）。
    </p>

    <Toolbar v-model:search="search" search-placeholder="按原因或编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="reasons"
      row-key="id"
      :loading="reasonsPending"
      empty-message="原因码目录为空。后端 #397 交付后，在此按原因组维护质量原因（供检验/不合格品引用）。"
    >
      <template #cell-groupName="{ row }">
        <span v-if="row.groupName">{{ row.groupName }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-severity="{ row }">
        <StatusBadge v-if="row.severity" :label="severityLabel(row.severity)" :tone="severityTone(row.severity)" />
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-defaultDisposition="{ row }">
        <span v-if="row.defaultDisposition">{{ row.defaultDisposition }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadge
          :label="row.enabled === false ? '停用' : '启用'"
          :tone="row.enabled === false ? 'neutral' : 'success'"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <Button type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</Button>
          <Button type="button" variant="ghost" size="sm" :disabled="row.enabled === false" @click="openArchive(row)">停用</Button>
        </div>
      </template>
    </DataTable>

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
