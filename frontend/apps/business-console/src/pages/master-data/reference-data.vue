<script setup lang="ts">
import type { BusinessConsoleCreateReferenceDataCodeRequest, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useReferenceDataCodes, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
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
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '数据字典' } })

// 平台预置的受控值分组（CodeSet → 中文名，对齐产品文档 §5.1）。Phase 1 作左侧固定主列表，
// Phase 2 后端字典就绪后可改为动态拉取，UI 不变。
interface CodeSetMeta {
  codeSet: string
  label: string
}
const CODE_SETS: CodeSetMeta[] = [
  { codeSet: 'material-type', label: '物料类型' },
  { codeSet: 'product-category', label: '产品/物料分类' },
  { codeSet: 'uom-dimension', label: '计量量纲' },
  { codeSet: 'batch-tracking-policy', label: '批次策略' },
  { codeSet: 'serial-tracking-policy', label: '序列策略' },
  { codeSet: 'shelf-life-policy', label: '保质期策略' },
  { codeSet: 'storage-condition', label: '仓储条件' },
  { codeSet: 'barcode-rule', label: '条码规则' },
  { codeSet: 'partner-type', label: '伙伴角色' },
  { codeSet: 'quality-reason', label: '质量原因' },
  { codeSet: 'compliance-tag', label: '合规标签' },
  { codeSet: 'device-status', label: '设备状态' },
  { codeSet: 'line-type', label: '产线类型' },
  { codeSet: 'work-center-type', label: '工作中心粒度' },
]

const {
  codes,
  codesError,
  codesPending,
  codesTotal,
  createCode,
  createCodeError,
  createCodePending,
  filters,
  refreshCodes,
} = useReferenceDataCodes()
const codeActions = useMasterDataResourceActions('reference-data')

const selectedCodeSet = ref(CODE_SETS[0]!.codeSet)
const keyword = ref('')
const sort = ref<DataTableSort | null>(null)
const page = ref(1)
const pageSize = ref('10')

const createOpen = shallowRef(false)
const createShowErrors = ref(false)
const createSuccess = shallowRef('')
// 编辑态：null=新建，否则=正在编辑的码值编码（codeSet/code 是身份，编辑态只读）。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)

const selectedLabel = computed(() => codeSetLabel(selectedCodeSet.value))
const activeCount = computed(() => codes.value.filter((r) => r.active !== false).length)

const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return codes.value
  return codes.value.filter((row) =>
    [row.code, row.displayName].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort((a, b) =>
    String(a[key as keyof BusinessConsoleResourceItem] ?? '')
      .localeCompare(String(b[key as keyof BusinessConsoleResourceItem] ?? ''), 'zh-Hans-CN') * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => sortedRows.value)

const listErrorMessage = computed(() => formatError(codesError.value))
const createErrorMessage = computed(() => formatError(createCodeError.value))
const actionErrorMessage = computed(() => formatError(codeActions.actionError.value))

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

const CREATE_FORM_DEFAULTS = {
  codeSet: selectedCodeSet.value,
  code: '',
  name: '',
}
const createForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  ...CREATE_FORM_DEFAULTS,
})
const canCreateCode = computed(() =>
  [createForm.codeSet, createForm.code, createForm.name].every(isNonEmpty),
)

// 选中 CodeSet 即服务端过滤（真分页：codeSet + skip/take 都交给后端）。
watch(selectedCodeSet, (value) => {
  filters.codeSet = value
  page.value = 1
}, { immediate: true })

watch([keyword, pageSize], () => {
  page.value = 1
})

watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

function selectCodeSet(codeSet: string) {
  selectedCodeSet.value = codeSet
  keyword.value = ''
}
function codeSetLabel(codeSet: string) {
  return CODE_SETS.find((s) => s.codeSet === codeSet)?.label ?? codeSet
}
function rowKey(row: BusinessConsoleResourceItem) {
  return `${row.codeSet ?? selectedCodeSet.value}:${row.code ?? row.displayName ?? ''}`
}
function codeDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '所属字典', value: codeSetLabel(row.codeSet ?? selectedCodeSet.value) },
    { label: '编码', value: row.code ?? '' },
    { label: '名称', value: row.displayName ?? '' },
  ]
}
function resetCreateForm() {
  Object.assign(createForm, { ...CREATE_FORM_DEFAULTS, codeSet: selectedCodeSet.value })
}
function openCreate() {
  editingCode.value = null
  resetCreateForm()
  createShowErrors.value = false
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
  createForm.codeSet = selectedCodeSet.value
  createOpen.value = true
}
async function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  editingCode.value = row.code
  createShowErrors.value = false
  editLoading.value = true
  createOpen.value = true
  try {
    const d = await codeActions.fetchDetail(row.code)
    Object.assign(createForm, {
      codeSet: row.codeSet ?? selectedCodeSet.value,
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
    })
  }
  finally {
    editLoading.value = false
  }
}
async function submitCode() {
  if (!canCreateCode.value) {
    createShowErrors.value = true
    return
  }
  if (editingCode.value) {
    await codeActions.update(editingCode.value, { name: createForm.name.trim() })
    createSuccess.value = `字典条目「${createForm.name.trim()}」已更新。`
  }
  else {
    const body: BusinessConsoleCreateReferenceDataCodeRequest = {
      organizationId: createForm.organizationId.trim(),
      environmentId: createForm.environmentId.trim(),
      codeSet: createForm.codeSet.trim(),
      code: createForm.code.trim(),
      name: createForm.name.trim(),
    }
    await createCode(body)
    createSuccess.value = `字典条目「${body.name}」已创建。`
    selectedCodeSet.value = body.codeSet
  }
  resetCreateForm()
  editingCode.value = null
  createShowErrors.value = false
  createOpen.value = false
}
function syncFormOnOpen(open: boolean) {
  if (!open) return
  createShowErrors.value = false
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
  createForm.codeSet = selectedCodeSet.value
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="数据字典" :breadcrumbs="[{ label: '基础数据' }]" :count="`${CODE_SETS.length} 个字典分组`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="codesPending" @click="refreshCodes">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="createOpen" @update:open="syncFormOnOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建字典条目
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-lg">
            <DialogHeader>
              <DialogTitle>{{ editingCode ? `编辑字典条目 · ${editingCode}` : '新建字典条目' }}</DialogTitle>
              <DialogDescription>{{ editingCode ? '修改字典条目名称（所属字典与编码不可修改）。带 * 为必填项。' : '选择所属字典，填写编码与名称。带 * 为必填项。' }}</DialogDescription>
            </DialogHeader>
            <form class="grid gap-4" @submit.prevent="submitCode">
              <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>
              <p v-if="createShowErrors && !canCreateCode" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>

              <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.codeSet)">
                <FieldLabel for="ref-code-set">所属字典 <span class="text-destructive">*</span></FieldLabel>
                <Select v-model="createForm.codeSet" :disabled="!!editingCode">
                  <SelectTrigger id="ref-code-set"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="s in CODE_SETS" :key="s.codeSet" :value="s.codeSet">{{ s.label }}</SelectItem>
                  </SelectContent>
                </Select>
                <FieldDescription>该条目归属的字典分组。</FieldDescription>
              </Field>

              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.code)">
                  <FieldLabel for="ref-code">编码 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="ref-code" v-model="createForm.code" autocomplete="off" aria-required="true" :disabled="!!editingCode" required />
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <FieldLabel for="ref-name">名称 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="ref-name" v-model="createForm.name" autocomplete="off" aria-required="true" required />
                </Field>
              </FieldGroup>

              <DialogFooter>
                <Button type="button" variant="outline" @click="createOpen = false">取消</Button>
                <Button type="submit" :disabled="createCodePending || codeActions.updatePending.value || editLoading">
                  <Spinner v-if="createCodePending || codeActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存条目' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="字典分组" :value="CODE_SETS.length" hint="平台预置的受控值分组" />
      <SectionCard description="当前分组条目" :value="codesTotal" hint="选中字典的码值数量" />
      <SectionCard description="本页启用" :value="activeCount" hint="当前页可用于新建与计划的码值" />
    </SectionCards>

    <p class="text-sm text-muted-foreground">
      字典是平台受控值来源；物料等表单的分类、单位、存储条件等下拉取自这里（完整联动随初始数据接入）。
    </p>

    <div class="grid gap-4 md:grid-cols-[220px_minmax(0,1fr)]">
      <nav class="grid h-fit gap-1 rounded-lg border p-2" aria-label="字典分组">
        <Button
          v-for="s in CODE_SETS"
          :key="s.codeSet"
          type="button"
          variant="ghost"
          size="sm"
          class="justify-start"
          :class="s.codeSet === selectedCodeSet ? 'bg-accent text-accent-foreground' : ''"
          :aria-pressed="s.codeSet === selectedCodeSet"
          @click="selectCodeSet(s.codeSet)"
        >
          {{ s.label }}
        </Button>
      </nav>

      <div class="grid gap-4">
        <Toolbar v-model:search="keyword" :search-placeholder="`在「${selectedLabel}」内筛选编码、名称`" />

        <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>
        <p v-else-if="actionErrorMessage" class="text-sm text-destructive" role="alert">{{ actionErrorMessage }}</p>
        <p v-else-if="createSuccess" class="text-sm text-success" role="status">{{ createSuccess }}</p>

        <DataTable
          v-model:sort="sort"
          :columns="columns"
          :rows="pagedRows"
          :row-key="rowKey"
          :client-sort="false"
          :loading="codesPending"
          :empty-message="`「${selectedLabel}」暂无条目。可新建字典条目。`"
        >
          <template #cell-active="{ row }">
            <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="字典条目" :detail-fields="codeDetailFields(row)" :actions="codeActions" @edit="openEdit" />
          </template>
        </DataTable>

        <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="codesTotal" />
      </div>
    </div>
  </BusinessLayout>
</template>
