<script setup lang="ts">
import type {
  BusinessConsoleCreateBusinessPartnerRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, DataTableSort } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import {
  useBusinessPartners,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
import { PARTNER_TYPE_OPTIONS } from '@/data/masterDataReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
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
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务伙伴',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const {
  createPartner,
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
// 编辑态：null=新建，否则=正在编辑的伙伴编码（编码不可改）。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)
const originalCreditLimitValue = shallowRef('')
const originalCreditCurrencyValue = shallowRef('')

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
      [row.code, row.displayName, rolesLabel(row), row.taxId].some((value) =>
        (value ?? '').toLowerCase().includes(kw),
      )
    return roleMatched && kwMatched
  })
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort(
    (a, b) =>
      String(
        key === 'roles' ? rolesLabel(a) : (a[key as keyof BusinessConsoleResourceItem] ?? ''),
      ).localeCompare(
        String(
          key === 'roles' ? rolesLabel(b) : (b[key as keyof BusinessConsoleResourceItem] ?? ''),
        ),
        'zh-Hans-CN',
      ) * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => sortedRows.value)

const listErrorMessage = computed(() => formatError(partnersError.value))

const PARTNER_FORM_DEFAULTS = {
  code: '',
  name: '',
  partnerType: 'customer',
  taxId: '',
  creditLimit: '',
  creditCurrencyCode: 'CNY',
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
  return PARTNER_TYPE_OPTIONS.map((o) => o.value).filter(
    (value) => extraRoleState[value] && value !== createForm.partnerType,
  )
}
const canCreatePartner = computed(
  () =>
    [createForm.name, createForm.partnerType].every(isNonEmpty) &&
    !creditLimitValidationMessage.value,
)
const hasCustomerRole = computed(
  () => createForm.partnerType === 'customer' || selectedExtraRoles().includes('customer'),
)
const creditLimitValue = computed(() => String(createForm.creditLimit ?? '').trim())
const creditCurrencyValue = computed(() =>
  String(createForm.creditCurrencyCode ?? '')
    .trim()
    .toUpperCase(),
)
const hasOriginalCreditProfile = computed(() => Boolean(originalCreditLimitValue.value))
const shouldShowCreditFields = computed(
  () => hasCustomerRole.value || hasOriginalCreditProfile.value,
)
const creditLimitValidationMessage = computed(() => {
  if (!hasCustomerRole.value || !creditLimitValue.value) return ''
  const amount = Number(creditLimitValue.value)
  if (!Number.isFinite(amount) || amount < 0) return '信用额度必须为不小于 0 的数字。'
  if (!creditCurrencyValue.value) return '填写信用额度时必须填写币种。'
  return ''
})
const creditLimitDescription = computed(() => {
  if (!editingCode.value) return '销售订单信用检查使用的客户额度。'
  if (!hasCustomerRole.value && hasOriginalCreditProfile.value)
    return '移除客户角色后保存会同步清空信用额度。'
  if (hasOriginalCreditProfile.value && !creditLimitValue.value)
    return '留空保存会清空已有信用额度。'
  return '销售订单信用检查使用的客户额度。'
})

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'roles', header: '角色', width: 'w-40' },
  { key: 'taxId', header: '税号', width: 'w-44', accessor: (r) => r.taxId ?? '无' },
  { key: 'creditLimit', header: '信用额度', width: 'w-36', accessor: (r) => formatCreditLimit(r) },
  { key: 'active', header: '状态', width: 'w-24' },
  {
    key: 'snapshotVersion',
    header: '更新时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.snapshotVersion),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function partnerDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '编码', value: row.code ?? '' },
    { label: '名称', value: row.displayName ?? '' },
    { label: '角色', value: rolesLabel(row) },
    { label: '统一社会信用代码', value: row.taxId ?? '' },
    { label: '信用额度', value: formatCreditLimit(row) },
  ]
}

watch([keyword, roleFilter, pageSize], () => {
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

watch(
  () => createForm.partnerType,
  (value) => {
    // 主角色不应同时出现在附加角色里。
    extraRoleState[value] = false
  },
)

function resetFilters() {
  keyword.value = ''
  roleFilter.value = 'all'
}
function rowKey(row: BusinessConsoleResourceItem) {
  return `${row.resourceType ?? 'business-partner'}:${row.code || row.displayName || ''}`
}
function resetCreateForm() {
  Object.assign(createForm, { ...PARTNER_FORM_DEFAULTS })
  originalCreditLimitValue.value = ''
  originalCreditCurrencyValue.value = ''
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
    originalCreditLimitValue.value = d?.creditLimit?.toString() ?? row.creditLimit?.toString() ?? ''
    originalCreditCurrencyValue.value = d?.creditCurrencyCode ?? row.creditCurrencyCode ?? ''
    Object.assign(createForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      partnerType: type,
      taxId: d?.taxId ?? row.taxId ?? '',
      creditLimit: originalCreditLimitValue.value,
      creditCurrencyCode: originalCreditCurrencyValue.value || 'CNY',
    })
    const extras = new Set(
      (d?.partnerRoles ?? row.partnerRoles ?? []).map((r) => (r ?? '').trim()).filter(Boolean),
    )
    for (const o of PARTNER_TYPE_OPTIONS) {
      extraRoleState[o.value] = extras.has(o.value) && o.value !== type
    }
  } finally {
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
  const creditPatch = customerCreditPatch()
  try {
    if (editingCode.value) {
      await partnerActions.update(editingCode.value, {
        name: createForm.name.trim(),
        partnerType: createForm.partnerType.trim(),
        partnerRoles: roles,
        taxId: taxId || null,
        ...creditPatch,
      })
      notifySuccess(`业务伙伴「${createForm.name.trim()}」已更新。`)
    } else {
      const body: BusinessConsoleCreateBusinessPartnerRequest = {
        organizationId: createForm.organizationId.trim(),
        environmentId: createForm.environmentId.trim(),
        name: createForm.name.trim(),
        partnerType: createForm.partnerType.trim(),
        ...(roles.length ? { partnerRoles: roles } : {}),
        ...(taxId ? { taxId } : {}),
        ...creditPatch,
      }
      await createPartner(body)
      notifySuccess(`业务伙伴「${body.name}」已创建。`)
    }
    resetCreateForm()
    editingCode.value = null
    createShowErrors.value = false
    createOpen.value = false
  } catch (error) {
    notifyError(error)
  }
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
function customerCreditPatch() {
  if (
    editingCode.value &&
    hasOriginalCreditProfile.value &&
    (!hasCustomerRole.value || !creditLimitValue.value)
  ) {
    return { clearCreditLimit: true }
  }

  if (!hasCustomerRole.value || !creditLimitValue.value) {
    return {}
  }

  return {
    creditLimit: Number(creditLimitValue.value),
    creditCurrencyCode: creditCurrencyValue.value,
  }
}
function formatCreditLimit(
  row: Pick<BusinessConsoleResourceItem, 'creditLimit' | 'creditCurrencyCode'>,
) {
  if (row.creditLimit === undefined || row.creditLimit === null) return '无'
  return `${row.creditCurrencyCode ?? ''} ${row.creditLimit}`.trim()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="业务伙伴"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${partnersTotal} 个伙伴`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="partnersPending"
          @click="refreshPartners"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="createOpen" @update:open="syncContextFromFilters">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建伙伴
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>{{
                editingCode ? `编辑业务伙伴 · ${editingCode}` : '新建业务伙伴'
              }}</NvDialogTitle>
              <NvDialogDescription>{{
                editingCode
                  ? '修改伙伴档案（编码不可修改）。一个伙伴可兼具多个角色。带 * 为必填项。'
                  : '客户、供应商、承运商统一建档。一个伙伴可兼具多个角色。带 * 为必填项。'
              }}</NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-4" @submit.prevent="submitPartner">
              <p
                v-if="createShowErrors && !canCreatePartner"
                class="text-sm text-destructive"
                role="alert"
              >
                请完整填写带 * 的必填项（已标红）。
              </p>

              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField v-if="editingCode">
                  <NvFieldLabel for="partner-code">编码</NvFieldLabel>
                  <NvInput id="partner-code" :model-value="createForm.code" disabled />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <NvFieldLabel for="partner-name"
                    >名称 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="partner-name"
                    v-model="createForm.name"
                    autocomplete="off"
                    aria-required="true"
                    required
                  />
                  <NvFieldDescription v-if="!editingCode">编码由系统自动生成。</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.partnerType)">
                  <NvFieldLabel for="partner-type"
                    >主角色 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.partnerType">
                    <NvSelectTrigger id="partner-type"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="o in PARTNER_TYPE_OPTIONS"
                        :key="o.value"
                        :value="o.value"
                        >{{ o.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                  <NvFieldDescription>该伙伴的主要业务角色。</NvFieldDescription>
                </NvField>
                <NvField>
                  <NvFieldLabel for="partner-tax">统一社会信用代码</NvFieldLabel>
                  <NvInput
                    id="partner-tax"
                    v-model="createForm.taxId"
                    autocomplete="off"
                    placeholder="可留空"
                  />
                  <NvFieldDescription>用于开票与对账，可后续补录。</NvFieldDescription>
                </NvField>
                <NvField
                  v-if="shouldShowCreditFields"
                  :data-invalid="createShowErrors && Boolean(creditLimitValidationMessage)"
                >
                  <NvFieldLabel for="partner-credit-limit">信用额度</NvFieldLabel>
                  <NvInput
                    id="partner-credit-limit"
                    v-model="createForm.creditLimit"
                    type="number"
                    min="0"
                    step="0.01"
                    inputmode="decimal"
                    autocomplete="off"
                    placeholder="可留空"
                  />
                  <NvFieldDescription>{{
                    creditLimitValidationMessage || creditLimitDescription
                  }}</NvFieldDescription>
                </NvField>
                <NvField
                  v-if="shouldShowCreditFields"
                  :data-invalid="createShowErrors && Boolean(creditLimitValidationMessage)"
                >
                  <NvFieldLabel for="partner-credit-currency">信用币种</NvFieldLabel>
                  <NvInput
                    id="partner-credit-currency"
                    v-model="createForm.creditCurrencyCode"
                    autocomplete="off"
                    maxlength="10"
                  />
                  <NvFieldDescription>填写信用额度时使用，默认 CNY。</NvFieldDescription>
                </NvField>
              </NvFieldGroup>

              <NvField>
                <NvFieldLabel>附加角色</NvFieldLabel>
                <div class="flex flex-wrap gap-4">
                  <label
                    v-for="o in extraRoleOptions"
                    :key="o.value"
                    class="flex items-center gap-2 text-sm"
                  >
                    <NvCheckbox v-model:checked="extraRoleState[o.value]" :aria-label="o.label" />
                    {{ o.label }}
                  </label>
                </div>
                <NvFieldDescription>除主角色外，该伙伴还承担的角色，可不选。</NvFieldDescription>
              </NvField>

              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="createOpen = false"
                  >取消</NvButton
                >
                <NvButton
                  type="submit"
                  :disabled="
                    createPartnerPending || partnerActions.updatePending.value || editLoading
                  "
                >
                  <Spinner
                    v-if="createPartnerPending || partnerActions.updatePending.value"
                    aria-hidden="true"
                  />
                  {{ editingCode ? '保存修改' : '保存伙伴' }}
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="在当前页内筛选编码、名称、角色">
      <template #filters>
        <NvSelect v-model="roleFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="伙伴角色"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部角色</NvSelectItem>
            <NvSelectItem v-for="o in PARTNER_TYPE_OPTIONS" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="partnersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="partnersPending"
      :searchable="false"
      :column-settings="false"
      empty-message="未找到业务伙伴。可清空筛选或新建伙伴。"
    >
      <template #cell-roles="{ row }">
        <span class="inline-flex flex-wrap gap-1">
          <NvStatusBadge
            v-for="r in partnerRoles(row)"
            :key="r"
            :label="roleLabel(r)"
            tone="neutral"
          />
          <span v-if="!partnerRoles(row).length" class="text-muted-foreground">未分配</span>
        </span>
      </template>
      <template #cell-active="{ row }">
        <NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions
          :row="row"
          entity-label="伙伴"
          :detail-fields="partnerDetailFields(row)"
          :actions="partnerActions"
          @edit="openEdit"
        />
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
