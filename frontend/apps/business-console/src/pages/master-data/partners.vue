<script setup lang="ts">
import type { BusinessConsoleCreateBusinessPartnerRequest, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useBusinessPartners, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import { PARTNER_TYPE_OPTIONS } from '@/data/masterDataReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
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

definePage({ meta: { requiresAuth: true, title: '业务伙伴' } })

const {
  createPartner,
  createPartnerError,
  createPartnerPending,
  filters,
  partners,
  partnersError,
  partnersPending,
  partnersTotal,
  refreshPartners,
} = useBusinessPartners()
const partnerActions = useMasterDataResourceActions('business-partner')

const createOpen = shallowRef(false)
const createShowErrors = ref(false)
const createSuccess = shallowRef('')
// 编辑态：null=新建，否则=正在编辑的伙伴编码（编码不可改）。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)

const keyword = ref('')
const roleFilter = ref('all')
const sort = ref<DataTableSort | null>(null)
const page = ref(1)
const pageSize = ref('10')

// 取一个伙伴的全部角色（主角色 partnerType + 附加角色 partnerRoles，去重；只取真实 typed 字段）。
function partnerRoles(row: BusinessConsoleResourceItem): string[] {
  const roles = [row.partnerType, ...(row.partnerRoles ?? [])]
    .map((r) => (r ?? '').trim())
    .filter(Boolean)
  return [...new Set(roles)]
}
function roleLabel(value: string) {
  return PARTNER_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? value
}
function rolesLabel(row: BusinessConsoleResourceItem) {
  const labels = partnerRoles(row).map(roleLabel)
  return labels.length ? labels.join(' / ') : '未分配'
}

const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return partners.value.filter((row) => {
    const roles = partnerRoles(row)
    const roleMatched = roleFilter.value === 'all' || roles.includes(roleFilter.value)
    const kwMatched =
      !kw ||
      [row.code, row.displayName, rolesLabel(row), row.taxId]
        .some((value) => (value ?? '').toLowerCase().includes(kw))
    return roleMatched && kwMatched
  })
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort((a, b) =>
    String(key === 'roles' ? rolesLabel(a) : a[key as keyof BusinessConsoleResourceItem] ?? '')
      .localeCompare(String(key === 'roles' ? rolesLabel(b) : b[key as keyof BusinessConsoleResourceItem] ?? ''), 'zh-Hans-CN') * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => sortedRows.value)

const customerCount = computed(() => partners.value.filter((r) => partnerRoles(r).includes('customer')).length)
const supplierCount = computed(() => partners.value.filter((r) => partnerRoles(r).includes('supplier')).length)
const listErrorMessage = computed(() => formatError(partnersError.value))
const createErrorMessage = computed(() => formatError(createPartnerError.value))
const partnerActionErrorMessage = computed(() => formatError(partnerActions.actionError.value))

const PARTNER_FORM_DEFAULTS = {
  code: '',
  name: '',
  partnerType: 'customer',
  taxId: '',
}
const createForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  ...PARTNER_FORM_DEFAULTS,
})
// 附加角色多选；提交时排除已选主角色，避免重复。
const extraRoleState = reactive<Record<string, boolean>>({})
const extraRoleOptions = computed(() =>
  PARTNER_TYPE_OPTIONS.filter((o) => o.value !== createForm.partnerType),
)
function selectedExtraRoles() {
  return PARTNER_TYPE_OPTIONS
    .map((o) => o.value)
    .filter((value) => extraRoleState[value] && value !== createForm.partnerType)
}
const canCreatePartner = computed(() =>
  [createForm.code, createForm.name, createForm.partnerType].every(isNonEmpty),
)

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'roles', header: '角色', width: 'w-40' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function partnerDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '编码', value: row.code ?? '' },
    { label: '名称', value: row.displayName ?? '' },
    { label: '角色', value: rolesLabel(row) },
    { label: '统一社会信用代码', value: row.taxId ?? '' },
  ]
}

watch([keyword, roleFilter, pageSize], () => {
  page.value = 1
})

watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

watch(() => createForm.partnerType, (value) => {
  // 主角色不应同时出现在附加角色里。
  extraRoleState[value] = false
})

function resetFilters() {
  keyword.value = ''
  roleFilter.value = 'all'
}
function rowKey(row: BusinessConsoleResourceItem) {
  return `${row.resourceType ?? 'business-partner'}:${row.code || row.displayName || ''}`
}
function resetCreateForm() {
  Object.assign(createForm, { ...PARTNER_FORM_DEFAULTS })
  for (const key of Object.keys(extraRoleState)) extraRoleState[key] = false
}
function openCreate() {
  editingCode.value = null
  resetCreateForm()
  createShowErrors.value = false
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
  createOpen.value = true
}
async function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  editingCode.value = row.code
  createShowErrors.value = false
  editLoading.value = true
  createOpen.value = true
  try {
    const d = await partnerActions.fetchDetail(row.code)
    const type = d?.partnerType ?? row.partnerType ?? PARTNER_FORM_DEFAULTS.partnerType
    Object.assign(createForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      partnerType: type,
      taxId: d?.taxId ?? row.taxId ?? '',
    })
    const extras = new Set((d?.partnerRoles ?? row.partnerRoles ?? []).map((r) => (r ?? '').trim()).filter(Boolean))
    for (const o of PARTNER_TYPE_OPTIONS) {
      extraRoleState[o.value] = extras.has(o.value) && o.value !== type
    }
  }
  finally {
    editLoading.value = false
  }
}
async function submitPartner() {
  if (!canCreatePartner.value) {
    createShowErrors.value = true
    return
  }
  const roles = selectedExtraRoles()
  const taxId = createForm.taxId.trim()
  if (editingCode.value) {
    await partnerActions.update(editingCode.value, {
      name: createForm.name.trim(),
      partnerType: createForm.partnerType.trim(),
      partnerRoles: roles,
      taxId: taxId || null,
    })
    createSuccess.value = `业务伙伴「${createForm.name.trim()}」已更新。`
  }
  else {
    const body: BusinessConsoleCreateBusinessPartnerRequest = {
      organizationId: createForm.organizationId.trim(),
      environmentId: createForm.environmentId.trim(),
      code: createForm.code.trim(),
      name: createForm.name.trim(),
      partnerType: createForm.partnerType.trim(),
      ...(roles.length ? { partnerRoles: roles } : {}),
      ...(taxId ? { taxId } : {}),
    }
    await createPartner(body)
    createSuccess.value = `业务伙伴「${body.name}」已创建。`
  }
  resetCreateForm()
  editingCode.value = null
  createShowErrors.value = false
  createOpen.value = false
}
function syncContextFromFilters(open: boolean) {
  if (open) createShowErrors.value = false
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
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
    <PageHeader title="业务伙伴" :breadcrumbs="[{ label: '基础数据' }]" :count="`${partnersTotal} 个伙伴`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="partnersPending" @click="refreshPartners">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="createOpen" @update:open="syncContextFromFilters">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建伙伴
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ editingCode ? `编辑业务伙伴 · ${editingCode}` : '新建业务伙伴' }}</DialogTitle>
              <DialogDescription>{{ editingCode ? '修改伙伴档案（编码不可修改）。一个伙伴可兼具多个角色。带 * 为必填项。' : '客户、供应商、承运商统一建档。一个伙伴可兼具多个角色。带 * 为必填项。' }}</DialogDescription>
            </DialogHeader>
            <form class="grid gap-4" @submit.prevent="submitPartner">
              <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>

              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.code)">
                  <FieldLabel for="partner-code">编码 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="partner-code" v-model="createForm.code" autocomplete="off" aria-required="true" :disabled="!!editingCode" required />
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <FieldLabel for="partner-name">名称 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="partner-name" v-model="createForm.name" autocomplete="off" aria-required="true" required />
                </Field>
                <Field>
                  <FieldLabel for="partner-type">主角色 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.partnerType">
                    <SelectTrigger id="partner-type"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in PARTNER_TYPE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>该伙伴的主要业务角色。</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="partner-tax">统一社会信用代码</FieldLabel>
                  <Input id="partner-tax" v-model="createForm.taxId" autocomplete="off" placeholder="可留空" />
                  <FieldDescription>用于开票与对账，可后续补录。</FieldDescription>
                </Field>
              </FieldGroup>

              <Field>
                <FieldLabel>附加角色</FieldLabel>
                <div class="flex flex-wrap gap-4">
                  <label
                    v-for="o in extraRoleOptions"
                    :key="o.value"
                    class="flex items-center gap-2 text-sm"
                  >
                    <Checkbox v-model:checked="extraRoleState[o.value]" :aria-label="o.label" />
                    {{ o.label }}
                  </label>
                </div>
                <FieldDescription>除主角色外，该伙伴还承担的角色，可不选。</FieldDescription>
              </Field>

              <DialogFooter>
                <Button type="button" variant="outline" @click="createOpen = false">取消</Button>
                <Button type="submit" :disabled="createPartnerPending || partnerActions.updatePending.value || editLoading">
                  <Spinner v-if="createPartnerPending || partnerActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存伙伴' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="伙伴总数" :value="partnersTotal" hint="客户、供应商、承运商档案" />
      <SectionCard description="本页客户" :value="customerCount" hint="支撑销售需求与发货" />
      <SectionCard description="本页供应商" :value="supplierCount" hint="支撑采购与收货检验" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="在当前页内筛选编码、名称、角色">
      <template #filters>
        <Select v-model="roleFilter">
          <SelectTrigger class="h-9 w-32" aria-label="伙伴角色"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部角色</SelectItem>
            <SelectItem v-for="o in PARTNER_TYPE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
      <template #actions>
        <Button type="button" variant="ghost" size="sm" @click="resetFilters">重置</Button>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>
    <p v-else-if="partnerActionErrorMessage" class="text-sm text-destructive" role="alert">{{ partnerActionErrorMessage }}</p>
    <p v-else-if="createSuccess" class="text-sm text-success" role="status">{{ createSuccess }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="partnersPending"
      empty-message="未找到业务伙伴。可清空筛选或新建伙伴。"
    >
      <template #cell-roles="{ row }">
        <span class="inline-flex flex-wrap gap-1">
          <StatusBadge v-for="r in partnerRoles(row)" :key="r" :label="roleLabel(r)" tone="neutral" />
          <span v-if="!partnerRoles(row).length" class="text-muted-foreground">未分配</span>
        </span>
      </template>
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="伙伴" :detail-fields="partnerDetailFields(row)" :actions="partnerActions" @edit="openEdit" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="partnersTotal" />
  </BusinessLayout>
</template>
