<script setup lang="ts">
import type {
  BusinessConsoleRegisterToolingAssetRequest,
  BusinessConsoleToolingAssetItem,
  BusinessConsoleToolingAssetStatus,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { computed, reactive, ref, shallowRef } from 'vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import {
  toolingStatusLabel,
  toolingTypeLabel,
  useBusinessTooling,
} from '@/composables/useBusinessTooling'
import { usePagedList } from '@/composables/usePagedList'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldDescription,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '工装与模具',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const tooling = useBusinessTooling()
const paging = usePagedList(tooling.filters, {
  resetOn: [() => tooling.filters.keyword, () => tooling.filters.status],
})
const auth = useAuthStore()
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.masterDataResourcesManage),
)

const workCenters = useBusinessMasterDataResources('work-center')
const skus = useBusinessMasterDataResources('sku')
const CATALOG_PAGE_SIZE = 50
workCenters.filters.take = CATALOG_PAGE_SIZE
skus.filters.take = CATALOG_PAGE_SIZE
const workCenterSearch = ref('')
const skuSearch = ref('')
watchDebounced(
  workCenterSearch,
  (value) => {
    const keyword = value.trim()
    workCenters.filters.keyword = keyword || undefined
  },
  { debounce: 300, maxWait: 1000 },
)
watchDebounced(
  skuSearch,
  (value) => {
    const keyword = value.trim()
    skus.filters.keyword = keyword || undefined
  },
  { debounce: 300, maxWait: 1000 },
)

const toolingTypes = [
  { value: 'mould', label: '模具' },
  { value: 'fixture', label: '夹具' },
  { value: 'jig', label: '工装夹具' },
  { value: 'cutting', label: '刀具' },
  { value: 'gauge', label: '检具' },
]
const statusOptions: Array<{ value: 'all' | BusinessConsoleToolingAssetStatus; label: string }> = [
  { value: 'all', label: '全部状态' },
  { value: 'available', label: '可用' },
  { value: 'maintenance', label: '保养中' },
  { value: 'retired', label: '已退役' },
]
const statusFilter = computed({
  get: () => tooling.filters.status ?? 'all',
  set: (value: 'all' | BusinessConsoleToolingAssetStatus) => {
    tooling.filters.status = value === 'all' ? undefined : value
  },
})

const columns: NvDataTableColumn<BusinessConsoleToolingAssetItem>[] = [
  { key: 'code', header: '工装编码', cellClass: 'font-medium' },
  { key: 'name', header: '工装名称', accessor: (row) => row.name ?? '—' },
  { key: 'type', header: '类型', width: 'w-24' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'life', header: '使用寿命', width: 'w-44' },
  { key: 'schedulable', header: '排程资格', width: 'w-28' },
  { key: 'scope', header: '适用范围', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-44' },
]

function lifeLabel(row: BusinessConsoleToolingAssetItem) {
  const usage = row.usageCount ?? 0
  return row.maintenanceLifeCount == null
    ? `${usage.toLocaleString()} 次 / 未设上限`
    : `${usage.toLocaleString()} / ${row.maintenanceLifeCount.toLocaleString()} 次`
}
function lifeWarning(row: BusinessConsoleToolingAssetItem) {
  const life = row.maintenanceLifeCount
  const usage = row.usageCount ?? 0
  if (life == null) return ''
  if (usage >= life) return '已达寿命'
  return usage >= life * 0.9 ? '即将达寿命' : ''
}
function statusTone(status: BusinessConsoleToolingAssetStatus | undefined) {
  return status === 'available' ? 'success' : status === 'maintenance' ? 'warning' : 'neutral'
}
function toggleCode(codes: string[], code: string, checked: boolean) {
  const index = codes.indexOf(code)
  if (checked && index < 0) codes.push(code)
  if (!checked && index >= 0) codes.splice(index, 1)
}

const detailOpen = shallowRef(false)
const detailTarget = shallowRef<BusinessConsoleToolingAssetItem>()
function openDetail(row: BusinessConsoleToolingAssetItem) {
  detailTarget.value = row
  detailOpen.value = true
}

const registerOpen = shallowRef(false)
const registerShowErrors = shallowRef(false)
const registerForm = reactive({
  idempotencyKey: '',
  code: '',
  name: '',
  toolingType: '',
  maintenanceLifeCount: '',
  workCenterCodes: [] as string[],
  skuCodes: [] as string[],
})
const applicabilityCount = computed(
  () => registerForm.workCenterCodes.length * registerForm.skuCodes.length,
)
const lifeValidationMessage = computed(() => {
  if (!String(registerForm.maintenanceLifeCount).trim()) return ''
  const value = Number(registerForm.maintenanceLifeCount)
  return Number.isInteger(value) && value > 0 ? '' : '使用寿命必须是正整数。'
})
const registerNameError = computed(() =>
  registerShowErrors.value && !registerForm.name.trim() ? '请填写工装名称。' : '',
)
const registerTypeError = computed(() =>
  registerShowErrors.value && !registerForm.toolingType ? '请选择工装类型。' : '',
)
const registerLifeError = computed(() =>
  registerShowErrors.value ? lifeValidationMessage.value : '',
)
const registerWorkCenterError = computed(() =>
  registerShowErrors.value && registerForm.workCenterCodes.length === 0
    ? '请至少选择一个适用工作中心。'
    : '',
)
const registerSkuError = computed(() =>
  registerShowErrors.value && registerForm.skuCodes.length === 0 ? '请至少选择一个适用 SKU。' : '',
)
const registerErrors = computed(() =>
  [
    registerNameError.value,
    registerTypeError.value,
    registerLifeError.value,
    registerWorkCenterError.value,
    registerSkuError.value,
  ].filter(Boolean),
)
const registerValidationMessage = computed(() => {
  return registerErrors.value[0] ?? ''
})
function newRegisterIdempotencyKey() {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto
  const suffix =
    cryptoApi && typeof cryptoApi.randomUUID === 'function'
      ? cryptoApi.randomUUID()
      : `${Date.now()}-${Math.random().toString(16).slice(2)}`
  return `tooling-register-${suffix}`
}
function openRegister() {
  if (!canManage.value) return
  Object.assign(registerForm, {
    idempotencyKey: newRegisterIdempotencyKey(),
    code: '',
    name: '',
    toolingType: '',
    maintenanceLifeCount: '',
    workCenterCodes: [],
    skuCodes: [],
  })
  registerShowErrors.value = false
  workCenterSearch.value = ''
  skuSearch.value = ''
  workCenters.filters.keyword = undefined
  skus.filters.keyword = undefined
  registerOpen.value = true
}
async function submitRegister() {
  if (!canManage.value) return
  registerShowErrors.value = true
  if (registerValidationMessage.value) return
  const body: Omit<BusinessConsoleRegisterToolingAssetRequest, 'organizationId' | 'environmentId'> =
    {
      idempotencyKey: registerForm.idempotencyKey,
      code: registerForm.code.trim() || null,
      name: registerForm.name.trim(),
      toolingType: registerForm.toolingType,
      maintenanceLifeCount: String(registerForm.maintenanceLifeCount).trim()
        ? Number(registerForm.maintenanceLifeCount)
        : null,
      workCenterCodes: [...registerForm.workCenterCodes],
      skuCodes: [...registerForm.skuCodes],
    }
  try {
    await tooling.register(body)
    notifySuccess(`工装「${body.name}」已注册。`)
    registerOpen.value = false
  } catch (error) {
    notifyOperationFailure('注册工装失败', error, '注册工装失败，请稍后重试。')
  }
}

const statusOpen = shallowRef(false)
const retireOpen = shallowRef(false)
const statusTarget = shallowRef<BusinessConsoleToolingAssetItem>()
const nextStatus = shallowRef<BusinessConsoleToolingAssetStatus>('maintenance')
const statusReason = shallowRef('')
const statusShowErrors = shallowRef(false)
const statusActionLabel = computed(() =>
  nextStatus.value === 'maintenance'
    ? '转保养'
    : nextStatus.value === 'available'
      ? '完成保养'
      : '退役',
)
const statusWillClearUsage = computed(() => {
  const life = statusTarget.value?.maintenanceLifeCount
  const usage = statusTarget.value?.usageCount ?? 0
  return nextStatus.value === 'available' && life != null && usage >= life
})
const statusReasonInvalid = computed(() => statusShowErrors.value && !statusReason.value.trim())
function openStatus(
  row: BusinessConsoleToolingAssetItem,
  status: BusinessConsoleToolingAssetStatus,
) {
  if (!canManage.value || row.status === 'retired') return
  statusTarget.value = row
  nextStatus.value = status
  statusReason.value = ''
  statusShowErrors.value = false
  statusOpen.value = status !== 'retired'
  retireOpen.value = status === 'retired'
}
async function submitStatus() {
  const code = statusTarget.value?.code
  const reason = statusReason.value.trim()
  statusShowErrors.value = true
  if (!canManage.value || !code || !reason) return
  try {
    await tooling.changeStatus(code, nextStatus.value, reason)
    notifySuccess(`工装「${code}」已${statusActionLabel.value}。`)
    if (nextStatus.value === 'retired') retireOpen.value = false
    else statusOpen.value = false
  } catch (error) {
    notifyOperationFailure(`${statusActionLabel.value}失败`, error, '状态变更失败，请稍后重试。')
  }
}

const usageOpen = shallowRef(false)
const usageTarget = shallowRef<BusinessConsoleToolingAssetItem>()
const usageCount = shallowRef('')
const usageShowErrors = shallowRef(false)
const usageValidationMessage = computed(() => {
  if (!usageShowErrors.value) return ''
  const value = Number(usageCount.value)
  return Number.isInteger(value) && value > 0 ? '' : '使用次数必须是正整数。'
})
const projectedUsageCount = computed(() => {
  const current = usageTarget.value?.usageCount ?? 0
  const increment = Number(usageCount.value)
  return current + (Number.isInteger(increment) && increment > 0 ? increment : 0)
})
const usageWillReachLife = computed(() => {
  const life = usageTarget.value?.maintenanceLifeCount
  return life != null && projectedUsageCount.value >= life
})
function openUsage(row: BusinessConsoleToolingAssetItem) {
  if (!canManage.value || row.status === 'retired') return
  usageTarget.value = row
  usageCount.value = ''
  usageShowErrors.value = false
  usageOpen.value = true
}
async function submitUsage() {
  usageShowErrors.value = true
  const code = usageTarget.value?.code
  if (!canManage.value || !code || usageValidationMessage.value) return
  try {
    await tooling.recordUsage(code, Number(usageCount.value))
    notifySuccess(`工装「${code}」使用次数已登记。`)
    usageOpen.value = false
  } catch (error) {
    notifyOperationFailure('登记使用失败', error, '登记使用失败，请稍后重试。')
  }
}

const listErrorMessage = computed(() =>
  inlineErrorMessage(tooling.toolingError.value, '工装列表加载失败，请稍后重试。'),
)
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工装与模具"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${tooling.toolingTotal.value} 项工装`"
    >
      <template #actions>
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="tooling.toolingPending.value"
          @click="tooling.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton v-if="canManage" type="button" size="sm" @click="openRegister">
          <PlusIcon aria-hidden="true" />
          注册工装
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar
      v-model:search="tooling.filters.keyword"
      search-placeholder="搜索工装编码或名称"
      search-label="搜索工装"
    >
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="工装状态">
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>
    <NvDataTable
      v-else
      manual
      :page="paging.page.value"
      :page-size="paging.pageSize.value"
      :total-items="tooling.toolingTotal.value"
      :columns="columns"
      :rows="tooling.toolingAssets.value"
      row-key="code"
      :loading="tooling.toolingPending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="当前筛选范围内没有工装档案。"
      @update:page="paging.page.value = $event"
      @update:page-size="(value) => (paging.pageSize.value = String(value))"
    >
      <template #cell-code="{ row }">
        <button
          v-if="row.code"
          type="button"
          class="text-left font-medium text-brand hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          @click="openDetail(row)"
        >
          {{ row.code }}
        </button>
        <span v-else class="font-medium">—</span>
      </template>
      <template #cell-type="{ row }">{{ toolingTypeLabel(row.toolingType) }}</template>
      <template #cell-status="{ row }">
        <NvStatusBadge :label="toolingStatusLabel(row.status)" :tone="statusTone(row.status)" />
      </template>
      <template #cell-life="{ row }">
        <div class="grid gap-1">
          <span>{{ lifeLabel(row) }}</span>
          <span v-if="lifeWarning(row)" class="text-xs text-warning-foreground">
            {{ lifeWarning(row) }}
          </span>
        </div>
      </template>
      <template #cell-schedulable="{ row }">
        <NvStatusBadge
          :label="row.isSchedulable ? '可参与排程' : '不可参与排程'"
          :tone="row.isSchedulable ? 'success' : 'warning'"
        />
      </template>
      <template #cell-scope="{ row }">
        {{ row.workCenterCodes?.length ?? 0 }} 个工作中心 · {{ row.skuCodes?.length ?? 0 }} 个 SKU
      </template>
      <template #cell-actions="{ row }">
        <div class="flex flex-nowrap items-center justify-end gap-1">
          <NvButton
            v-if="canManage && row.status !== 'retired'"
            type="button"
            size="sm"
            variant="ghost"
            @click="openUsage(row)"
          >
            登记使用
          </NvButton>
          <NvRowActions
            v-if="canManage && row.status !== 'retired'"
            :label="`工装操作 ${row.code ?? ''}`"
          >
            <!-- 工装生命周期不是通用 enabled 启停语义，不能改用通用主数据行操作组件。 -->
            <NvDropdownMenuItem
              v-if="row.status === 'available'"
              @click="openStatus(row, 'maintenance')"
            >
              转保养
            </NvDropdownMenuItem>
            <NvDropdownMenuItem
              v-if="row.status === 'maintenance'"
              @click="openStatus(row, 'available')"
            >
              完成保养
            </NvDropdownMenuItem>
            <NvDropdownMenuItem @click="openStatus(row, 'retired')"> 退役 </NvDropdownMenuItem>
          </NvRowActions>
        </div>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent size="lg">
        <NvSheetHeader>
          <NvSheetTitle>{{ detailTarget?.name ?? '工装详情' }}</NvSheetTitle>
          <NvSheetDescription>{{ detailTarget?.code }}</NvSheetDescription>
        </NvSheetHeader>
        <dl v-if="detailTarget" class="grid gap-4 px-4 text-sm sm:grid-cols-2">
          <div>
            <dt class="text-muted-foreground">类型</dt>
            <dd>{{ toolingTypeLabel(detailTarget.toolingType) }}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">状态</dt>
            <dd>{{ toolingStatusLabel(detailTarget.status) }}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">使用寿命</dt>
            <dd>{{ lifeLabel(detailTarget) }}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">排程资格</dt>
            <dd>{{ detailTarget.isSchedulable ? '可参与排程' : '不可参与排程' }}</dd>
          </div>
          <div class="sm:col-span-2">
            <dt class="text-muted-foreground">适用工作中心</dt>
            <dd>{{ detailTarget.workCenterCodes?.join('、') || '—' }}</dd>
          </div>
          <div class="sm:col-span-2">
            <dt class="text-muted-foreground">适用 SKU</dt>
            <dd>{{ detailTarget.skuCodes?.join('、') || '—' }}</dd>
          </div>
        </dl>
      </NvSheetContent>
    </NvSheet>

    <NvSheet v-if="canManage" v-model:open="registerOpen">
      <NvSheetContent size="2xl">
        <form class="grid gap-5" novalidate @submit.prevent="submitRegister">
          <NvSheetHeader>
            <NvSheetTitle>注册工装</NvSheetTitle>
            <NvSheetDescription
              >登记身份、寿命与可用于排程的工作中心 × SKU 组合。</NvSheetDescription
            >
          </NvSheetHeader>
          <NvFieldGroup class="grid gap-4 px-4 sm:grid-cols-2">
            <NvFieldError
              v-if="registerErrors.length"
              class="sm:col-span-2"
              :errors="['请修正已标红的字段，并完整填写带 * 的必填项。']"
            />
            <FormSectionTitle class="sm:col-span-2">基本信息</FormSectionTitle>
            <NvField>
              <NvFieldLabel for="tooling-code">工装编码</NvFieldLabel>
              <NvInput
                id="tooling-code"
                v-model="registerForm.code"
                placeholder="可选，留空由编码规则生成"
              />
            </NvField>
            <NvField :data-invalid="Boolean(registerNameError)">
              <NvFieldLabel for="tooling-name">
                <span :class="registerNameError ? 'text-destructive' : undefined">
                  工装名称 <span class="text-destructive">*</span>
                </span>
              </NvFieldLabel>
              <NvInput
                id="tooling-name"
                v-model="registerForm.name"
                :invalid="Boolean(registerNameError)"
              />
              <NvFieldError v-if="registerNameError" :errors="[registerNameError]" />
            </NvField>
            <NvField :data-invalid="Boolean(registerTypeError)">
              <NvFieldLabel for="tooling-type">
                <span :class="registerTypeError ? 'text-destructive' : undefined">
                  工装类型 <span class="text-destructive">*</span>
                </span>
              </NvFieldLabel>
              <NvSelect v-model="registerForm.toolingType">
                <NvSelectTrigger id="tooling-type" :invalid="Boolean(registerTypeError)">
                  <NvSelectValue placeholder="请选择工装类型" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in toolingTypes"
                    :key="option.value"
                    :value="option.value"
                    >{{ option.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
              <NvFieldError v-if="registerTypeError" :errors="[registerTypeError]" />
            </NvField>
            <NvField :data-invalid="Boolean(registerLifeError)">
              <NvFieldLabel for="tooling-life">
                <span :class="registerLifeError ? 'text-destructive' : undefined">
                  保养使用寿命（次）
                </span>
              </NvFieldLabel>
              <NvInput
                id="tooling-life"
                v-model="registerForm.maintenanceLifeCount"
                type="number"
                min="1"
                step="1"
                :invalid="Boolean(registerLifeError)"
              />
              <NvFieldDescription
                >可留空；达到寿命后转入保养状态并停止参与排程。</NvFieldDescription
              >
              <NvFieldError v-if="registerLifeError" :errors="[registerLifeError]" />
            </NvField>
            <FormSectionTitle class="sm:col-span-2">适用范围</FormSectionTitle>
            <NvField class="sm:col-span-2" :aria-invalid="Boolean(registerWorkCenterError)">
              <NvFieldLabel>
                <span :class="registerWorkCenterError ? 'text-destructive' : undefined">
                  适用工作中心 <span class="text-destructive">*</span>
                </span>
              </NvFieldLabel>
              <NvInput
                id="tooling-work-center-search"
                v-model="workCenterSearch"
                placeholder="搜索工作中心名称 / 编码"
                aria-label="搜索适用工作中心"
              />
              <div
                class="grid gap-2 rounded-lg border p-3 sm:grid-cols-2"
                :data-invalid="Boolean(registerWorkCenterError)"
                :class="registerWorkCenterError ? 'border-destructive' : ''"
              >
                <label
                  v-for="row in workCenters.resources.value.filter(
                    (item) => item.active !== false && item.code,
                  )"
                  :key="row.code"
                  class="flex items-center gap-2 text-sm"
                >
                  <NvCheckbox
                    :model-value="registerForm.workCenterCodes.includes(row.code!)"
                    @update:model-value="
                      toggleCode(registerForm.workCenterCodes, row.code!, Boolean($event))
                    "
                  />
                  <span
                    >{{ row.displayName || row.code }}
                    <span class="text-muted-foreground">{{ row.code }}</span></span
                  >
                </label>
              </div>
              <NvFieldDescription>
                已选择 {{ registerForm.workCenterCodes.length }} 个工作中心；当前匹配
                {{ workCenters.resourcesTotal.value }} 项，请搜索后继续选择。
              </NvFieldDescription>
              <NvFieldError v-if="registerWorkCenterError" :errors="[registerWorkCenterError]" />
            </NvField>
            <NvField class="sm:col-span-2" :aria-invalid="Boolean(registerSkuError)">
              <NvFieldLabel>
                <span :class="registerSkuError ? 'text-destructive' : undefined">
                  适用 SKU <span class="text-destructive">*</span>
                </span>
              </NvFieldLabel>
              <NvInput
                id="tooling-sku-search"
                v-model="skuSearch"
                placeholder="搜索 SKU 名称 / 编码"
                aria-label="搜索适用 SKU"
              />
              <div
                class="grid gap-2 rounded-lg border p-3 sm:grid-cols-2"
                :data-invalid="Boolean(registerSkuError)"
                :class="registerSkuError ? 'border-destructive' : ''"
              >
                <label
                  v-for="row in skus.resources.value.filter(
                    (item) => item.active !== false && item.code,
                  )"
                  :key="row.code"
                  class="flex items-center gap-2 text-sm"
                >
                  <NvCheckbox
                    :model-value="registerForm.skuCodes.includes(row.code!)"
                    @update:model-value="
                      toggleCode(registerForm.skuCodes, row.code!, Boolean($event))
                    "
                  />
                  <span
                    >{{ row.displayName || row.code }}
                    <span class="text-muted-foreground">{{ row.code }}</span></span
                  >
                </label>
              </div>
              <NvFieldDescription>
                已选择 {{ registerForm.skuCodes.length }} 个 SKU；当前匹配
                {{ skus.resourcesTotal.value }} 项，请搜索后继续选择。
              </NvFieldDescription>
              <NvFieldDescription>{{ applicabilityCount }} 个适用组合</NvFieldDescription>
              <NvFieldError v-if="registerSkuError" :errors="[registerSkuError]" />
            </NvField>
          </NvFieldGroup>
          <NvSheetFooter class="px-4">
            <NvButton type="button" variant="outline" @click="registerOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="tooling.registerPending.value">
              <Spinner v-if="tooling.registerPending.value" aria-hidden="true" />
              确认注册
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>

    <NvDialog v-if="canManage" v-model:open="statusOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>{{ statusActionLabel }}</NvDialogTitle>
          <NvDialogDescription>
            <span v-if="statusWillClearUsage">
              完成保养后将清零累计使用次数，并恢复为可用状态。
            </span>
            <span v-else>本次状态变更不会清零累计使用次数。</span>
          </NvDialogDescription>
        </NvDialogHeader>
        <NvField :data-invalid="statusReasonInvalid">
          <NvFieldLabel for="tooling-status-reason">
            <span :class="statusReasonInvalid ? 'text-destructive' : undefined">
              原因 <span class="text-destructive">*</span>
            </span>
          </NvFieldLabel>
          <NvInput
            id="tooling-status-reason"
            v-model="statusReason"
            :invalid="statusReasonInvalid"
          />
          <NvFieldDescription>请说明本次状态变更原因。</NvFieldDescription>
          <NvFieldError v-if="statusReasonInvalid" :errors="['请填写状态变更原因。']" />
        </NvField>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="statusOpen = false">取消</NvButton>
          <NvButton
            type="button"
            :disabled="tooling.changeStatusPending.value"
            @click="submitStatus"
          >
            <Spinner v-if="tooling.changeStatusPending.value" aria-hidden="true" />
            确认{{ statusActionLabel }}
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>

    <NvAlertDialog v-if="canManage" v-model:open="retireOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>退役</NvAlertDialogTitle>
          <NvAlertDialogDescription> 退役为终态，工装将永久退出排程。 </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvField :data-invalid="statusReasonInvalid">
          <NvFieldLabel for="tooling-retire-reason">
            <span :class="statusReasonInvalid ? 'text-destructive' : undefined">
              原因 <span class="text-destructive">*</span>
            </span>
          </NvFieldLabel>
          <NvInput
            id="tooling-retire-reason"
            v-model="statusReason"
            :invalid="statusReasonInvalid"
          />
          <NvFieldDescription>请说明本次退役原因。</NvFieldDescription>
          <NvFieldError v-if="statusReasonInvalid" :errors="['请填写退役原因。']" />
        </NvField>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <!-- 不用 NvAlertDialogAction：请求失败时必须保持确认框打开。 -->
          <NvButton
            type="button"
            variant="destructive"
            :disabled="!statusReason.trim() || tooling.changeStatusPending.value"
            @click="submitStatus"
          >
            <Spinner v-if="tooling.changeStatusPending.value" aria-hidden="true" />
            确认退役
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <NvDialog v-if="canManage" v-model:open="usageOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>登记使用</NvDialogTitle>
          <NvDialogDescription
            >本次使用会累计到寿命计数，达到上限后系统会自动转入保养状态。</NvDialogDescription
          >
        </NvDialogHeader>
        <div v-if="usageTarget" class="grid gap-1 text-sm">
          <span>当前累计：{{ (usageTarget.usageCount ?? 0).toLocaleString() }} 次</span>
          <span>登记后预计：{{ projectedUsageCount.toLocaleString() }} 次</span>
          <p v-if="usageWillReachLife" class="text-warning-foreground" role="status">
            保存后工装将自动转为保养中，并停止参与排程。
          </p>
        </div>
        <NvField :data-invalid="Boolean(usageValidationMessage)">
          <NvFieldLabel for="tooling-usage-count">
            <span :class="usageValidationMessage ? 'text-destructive' : undefined">
              本次使用次数 <span class="text-destructive">*</span>
            </span>
          </NvFieldLabel>
          <NvInput
            id="tooling-usage-count"
            v-model="usageCount"
            type="number"
            min="1"
            step="1"
            :invalid="Boolean(usageValidationMessage)"
          />
          <NvFieldError v-if="usageValidationMessage" :errors="[usageValidationMessage]" />
        </NvField>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="usageOpen = false">取消</NvButton>
          <NvButton type="button" :disabled="tooling.recordUsagePending.value" @click="submitUsage">
            <Spinner v-if="tooling.recordUsagePending.value" aria-hidden="true" />
            确认登记
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
