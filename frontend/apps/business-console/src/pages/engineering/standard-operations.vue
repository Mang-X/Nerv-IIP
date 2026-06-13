<script setup lang="ts">
import type {
  BusinessConsoleCreateStandardOperationRequest,
  BusinessConsoleStandardOperationItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { useStandardOperations } from '@/composables/useProductEngineering'
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

definePage({ meta: { requiresAuth: true, title: '标准工序' } })

const {
  archiveStandardOperation,
  archivePending,
  createStandardOperation,
  createPending,
  filters,
  refresh,
  standardOperations,
  standardOperationsError,
  standardOperationsPending,
  standardOperationsTotal,
  updateStandardOperation,
  updatePending,
} = useStandardOperations()

// 默认工作中心来自基础数据「工作中心」，选择器显名称、绑定编码。
const { resources: workCenters, resourcesPending: workCentersPending } = useBusinessMasterDataResources('work-center')
const workCenterNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const wc of workCenters.value) {
    if (wc.code) map.set(wc.code, wc.displayName ?? wc.code)
  }
  return map
})
function workCenterLabel(code?: string | null) {
  if (!code) return '未指定'
  return workCenterNameByCode.value.get(code) ?? code
}
const workCenterOptions = computed(() =>
  workCenters.value
    .filter((wc) => wc.code)
    .map((wc) => ({ value: wc.code as string, label: `${wc.displayName ?? wc.code} · ${wc.code}` })),
)

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
  standardOperationsError.value instanceof Error ? standardOperationsError.value.message : '',
)

function formatMinutes(setup?: number | null, run?: number | null) {
  if (setup == null && run == null) return '—'
  return `准备 ${setup ?? 0} / 加工 ${run ?? 0}`
}

const columns: DataTableColumn<BusinessConsoleStandardOperationItem>[] = [
  { key: 'operationCode', header: '编码', width: 'w-32' },
  { key: 'operationName', header: '工序名', cellClass: 'font-medium' },
  { key: 'defaultWorkCenter', header: '默认工作中心' },
  { key: 'standardMinutes', header: '标准工时（分）', width: 'w-40' },
  { key: 'control', header: '控制', width: 'w-52' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

// ── 新建 / 编辑表单 ─────────────────────────────────────────────
interface StandardOperationForm {
  operationCode: string
  operationName: string
  defaultWorkCenterCode: string
  controlKey: string
  standardSetupMinutes: string
  standardRunMinutes: string
  requiresReporting: boolean
  requiresQualityInspection: boolean
  isOutsourced: boolean
  description: string
}

function blankForm(): StandardOperationForm {
  return {
    operationCode: '',
    operationName: '',
    defaultWorkCenterCode: '',
    controlKey: 'INHOUSE',
    standardSetupMinutes: '',
    standardRunMinutes: '',
    requiresReporting: true,
    requiresQualityInspection: false,
    isOutsourced: false,
    description: '',
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑工序的 operationCode（编码即身份，编辑态只读）。
const editingCode = shallowRef<string | null>(null)
const form = reactive<StandardOperationForm>(blankForm())

function parseNumber(value: string): number | undefined {
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}
const setupMinutes = computed(() => parseNumber(form.standardSetupMinutes))
const runMinutes = computed(() => parseNumber(form.standardRunMinutes))

const codeValid = computed(() => !!editingCode.value || form.operationCode.trim().length > 0)
const nameValid = computed(() => form.operationName.trim().length > 0)
const workCenterValid = computed(() => form.defaultWorkCenterCode.trim().length > 0)
const controlKeyValid = computed(() => form.controlKey.trim().length > 0)
const setupValid = computed(() => form.standardSetupMinutes.trim() === '' || (setupMinutes.value != null && setupMinutes.value >= 0))
const runValid = computed(() => form.standardRunMinutes.trim() === '' || (runMinutes.value != null && runMinutes.value >= 0))
const canSubmit = computed(() =>
  codeValid.value && nameValid.value && workCenterValid.value && controlKeyValid.value && setupValid.value && runValid.value,
)

function openCreate() {
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: BusinessConsoleStandardOperationItem) {
  if (!row.operationCode) return
  editingCode.value = row.operationCode
  showErrors.value = false
  Object.assign(form, {
    operationCode: row.operationCode ?? '',
    operationName: row.operationName ?? '',
    defaultWorkCenterCode: row.defaultWorkCenterCode ?? '',
    controlKey: row.controlKey ?? 'INHOUSE',
    standardSetupMinutes: row.standardSetupMinutes == null ? '' : String(row.standardSetupMinutes),
    standardRunMinutes: row.standardRunMinutes == null ? '' : String(row.standardRunMinutes),
    requiresReporting: row.requiresReporting ?? true,
    requiresQualityInspection: row.requiresQualityInspection ?? false,
    isOutsourced: row.isOutsourced ?? false,
    description: row.description ?? '',
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const shared = {
    operationName: form.operationName.trim(),
    defaultWorkCenterCode: form.defaultWorkCenterCode.trim(),
    controlKey: form.controlKey.trim(),
    standardSetupMinutes: setupMinutes.value,
    standardRunMinutes: runMinutes.value,
    requiresReporting: form.requiresReporting,
    requiresQualityInspection: form.requiresQualityInspection,
    isOutsourced: form.isOutsourced,
    description: form.description.trim() || null,
  }
  try {
    if (editingCode.value) {
      await updateStandardOperation(editingCode.value, {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...shared,
      })
      notifySuccess(`工序「${shared.operationName}」已更新。`)
    }
    else {
      const body: BusinessConsoleCreateStandardOperationRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        operationCode: form.operationCode.trim(),
        ...shared,
      }
      await createStandardOperation(body)
      notifySuccess(`已创建标准工序「${shared.operationName}」。`)
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
const archiveTarget = shallowRef<BusinessConsoleStandardOperationItem | null>(null)
function openArchive(row: BusinessConsoleStandardOperationItem) {
  if (!row.operationCode) return
  archiveTarget.value = row
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.operationCode) return
  try {
    await archiveStandardOperation(target.operationCode, '不再使用')
    notifySuccess(`工序「${target.operationName}」已停用。`)
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
      title="标准工序"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${standardOperationsTotal} 个工序`"
    >
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="standardOperationsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="formOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建工序
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ editingCode ? '编辑标准工序' : '新建标准工序' }}</DialogTitle>
              <DialogDescription>
                标准工序是可复用的工程主数据：预设默认工作中心、控制键与标准工时，工艺路线选用时自动带出，避免逐行重填。带 * 为必填项。
              </DialogDescription>
            </DialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写带 * 的必填项，并确保标准工时为非负数（已标红）。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="showErrors && !codeValid">
                  <FieldLabel for="op-code">工序编码 <span class="text-destructive">*</span></FieldLabel>
                  <Input
                    v-if="!editingCode"
                    id="op-code"
                    v-model="form.operationCode"
                    placeholder="例如：OP-CNC-ROUGH"
                  />
                  <Input v-else :model-value="editingCode" readonly disabled />
                  <FieldDescription>{{ editingCode ? '编码是工序身份，不可更改。' : '由工厂自定义、需唯一。' }}</FieldDescription>
                </Field>
                <Field :data-invalid="showErrors && !nameValid">
                  <FieldLabel for="op-name">工序名 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="op-name" v-model="form.operationName" placeholder="例如：CNC 粗加工" />
                </Field>
                <Field :data-invalid="showErrors && !workCenterValid">
                  <FieldLabel for="op-wc">默认工作中心 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="form.defaultWorkCenterCode" :disabled="workCentersPending">
                    <SelectTrigger id="op-wc"><SelectValue placeholder="选择默认工作中心" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in workCenterOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>来自基础数据工作中心；工艺路线选此工序时自动带出。</FieldDescription>
                </Field>
                <Field :data-invalid="showErrors && !controlKeyValid">
                  <FieldLabel for="op-control">控制键 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="op-control" v-model="form.controlKey" placeholder="例如：INHOUSE / INHOUSE-QC" />
                  <FieldDescription>决定报工/质检/外协等执行行为的控制键。</FieldDescription>
                </Field>
              </FieldGroup>

              <FormSectionTitle>标准工时</FormSectionTitle>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="showErrors && !setupValid">
                  <FieldLabel for="op-setup">准备工时（分）</FieldLabel>
                  <Input id="op-setup" v-model="form.standardSetupMinutes" type="number" min="0" placeholder="0" />
                </Field>
                <Field :data-invalid="showErrors && !runValid">
                  <FieldLabel for="op-run">加工工时（分）</FieldLabel>
                  <Input id="op-run" v-model="form.standardRunMinutes" type="number" min="0" placeholder="0" />
                  <FieldDescription>单件加工标准时间；与准备工时一并带入工艺路线。</FieldDescription>
                </Field>
              </FieldGroup>

              <FormSectionTitle>控制标志</FormSectionTitle>
              <FieldGroup class="grid gap-2">
                <label for="op-report" class="flex cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 py-2 text-sm">
                  <span>需要报工</span>
                  <Checkbox id="op-report" v-model:checked="form.requiresReporting" />
                </label>
                <label for="op-inspect" class="flex cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 py-2 text-sm">
                  <span>需要质检</span>
                  <Checkbox id="op-inspect" v-model:checked="form.requiresQualityInspection" />
                </label>
                <label for="op-outsource" class="flex cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 py-2 text-sm">
                  <span>外协工序</span>
                  <Checkbox id="op-outsource" v-model:checked="form.isOutsourced" />
                </label>
              </FieldGroup>

              <FormSectionTitle>其它</FormSectionTitle>
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel for="op-desc">说明</FieldLabel>
                  <Input id="op-desc" v-model="form.description" placeholder="可选，工序用途或注意事项" />
                </Field>
              </FieldGroup>

              <DialogFooter>
                <Button type="button" variant="outline" @click="formOpen = false">取消</Button>
                <Button type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '创建工序' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <Toolbar v-model:search="search" search-placeholder="按工序名或编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="standardOperations"
      row-key="operationCode"
      :loading="standardOperationsPending"
      empty-message="标准工序目录为空。新建可复用工序（默认工作中心 + 标准工时 + 控制键），工艺路线即可选用。"
    >
      <template #cell-defaultWorkCenter="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ workCenterLabel(row.defaultWorkCenterCode) }}</span>
          <span v-if="row.defaultWorkCenterCode" class="text-xs text-muted-foreground">{{ row.defaultWorkCenterCode }}</span>
        </div>
      </template>
      <template #cell-standardMinutes="{ row }">
        <span class="tabular-nums text-muted-foreground">{{ formatMinutes(row.standardSetupMinutes, row.standardRunMinutes) }}</span>
      </template>
      <template #cell-control="{ row }">
        <div class="flex flex-col gap-1">
          <div class="flex flex-wrap gap-1">
            <StatusBadge v-if="row.requiresReporting" label="报工" tone="info" />
            <StatusBadge v-if="row.requiresQualityInspection" label="质检" tone="warning" />
            <StatusBadge v-if="row.isOutsourced" label="外协" tone="neutral" />
          </div>
          <span v-if="row.controlKey" class="text-xs text-muted-foreground">{{ row.controlKey }}</span>
        </div>
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

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="standardOperationsTotal" />

    <AlertDialog v-model:open="archiveOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>停用标准工序</AlertDialogTitle>
          <AlertDialogDescription>
            停用后工序「{{ archiveTarget?.operationName }}」将不可在新的工艺路线中选用，已发布路线不受影响。
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
