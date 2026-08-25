<script setup lang="ts">
import type {
  BusinessConsoleMesCreateShiftHandoverRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, ref } from 'vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { makeIdempotencyKey, useMesShiftHandovers } from '@/composables/useBusinessMes'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { usePagedList } from '@/composables/usePagedList'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { mesHandoverStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { labelFor, MES_HANDOVER_STATUS_LABELS } from '@/data/businessLabels'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useAuthStore } from '@/stores/auth'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '班次交接',
    requiredPermissions: ['business.mes.handovers.read'],
  },
})

const {
  acceptShiftHandover,
  createShiftHandover,
  filters,
  handovers,
  handoversError,
  handoversPending,
  handoversTotal,
  refreshHandovers,
} = useMesShiftHandovers()
const { keyword } = useMesKeywordFilter(filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})

const HANDOVERS_MANAGE_PERMISSION = 'business.mes.handovers.manage'
const CATALOG_TAKE = 500
const SYSTEM_ID_PATTERN =
  /^[{(]?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[)}]?$/i
const auth = useAuthStore()
const canManageHandovers = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(HANDOVERS_MANAGE_PERMISSION),
)
const handoverContextReady = computed(() =>
  Boolean(filters.organizationId.trim() && filters.environmentId.trim()),
)

const shiftCatalog = useBusinessMasterDataResources('shift')
const teamCatalog = useBusinessMasterDataResources('team')
shiftCatalog.filters.take = CATALOG_TAKE
teamCatalog.filters.take = CATALOG_TAKE

type DirectoryOption = { value: string; label: string }

function isSystemIdentifier(value: string) {
  return SYSTEM_ID_PATTERN.test(value.trim())
}

function toDirectoryOptions(resources: BusinessConsoleResourceItem[]): DirectoryOption[] {
  return resources
    .map((resource) => {
      const value = resource.code?.trim()
      if (!value || resource.active === false || isSystemIdentifier(value)) return undefined

      const displayName = resource.displayName?.trim()
      const label = displayName && !isSystemIdentifier(displayName) ? displayName : value
      return { value, label }
    })
    .filter((option): option is DirectoryOption => Boolean(option))
    .sort((a, b) => a.label.localeCompare(b.label, 'zh-Hans-CN'))
}

const shiftOptions = computed(() => toDirectoryOptions(shiftCatalog.resources.value))
const teamOptions = computed(() => toDirectoryOptions(teamCatalog.resources.value))
const shiftLabels = computed(
  () => new Map(shiftOptions.value.map((option) => [option.value, option.label])),
)
const teamLabels = computed(
  () => new Map(teamOptions.value.map((option) => [option.value, option.label])),
)

function resolveShiftLabel(value?: string | null) {
  if (!value || isSystemIdentifier(value)) return '未排班'
  return shiftLabels.value.get(value) ?? value
}

function resolveTeamLabel(value?: string | null) {
  if (!value || isSystemIdentifier(value)) return undefined
  return teamLabels.value.get(value) ?? value
}

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const currentPageOpenIssueTotal = computed(() =>
  handovers.value.reduce((s, r) => s + (r.openIssueCount ?? 0), 0),
)
const currentPageAcceptedCount = computed(
  () => handovers.value.filter((r) => (r.handoverStatus ?? '').toLowerCase() === 'accepted').length,
)
const handoverSegments = computed(() =>
  pagedBreakdownSegments(handovers.value.length, [
    {
      key: 'open',
      label: '待接班',
      value: handovers.value.length - currentPageAcceptedCount.value,
      tone: 'warning',
    },
    { key: 'accepted', label: '已接班', value: currentPageAcceptedCount.value, tone: 'success' },
  ]),
)
const errorMessage = computed(() => formatError(handoversError.value))

type HandoverRow = (typeof handovers)['value'][number]
const columns: NvDataTableColumn<HandoverRow>[] = [
  {
    key: 'handoverId',
    header: '交接单',
    cellClass: 'font-medium',
    accessor: () => '—',
  },
  { key: 'shiftId', header: '班次', accessor: (r) => resolveShiftLabel(r.shiftId) },
  {
    key: 'teamId',
    header: '班组',
    accessor: (r) => r.teamName?.trim() || resolveTeamLabel(r.teamId) || '未指派',
  },
  { key: 'handoverStatus', header: '状态', width: 'w-24' },
  { key: 'openIssueCount', header: '未结事项', align: 'end', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

const createDialogOpen = ref(false)
const createShowErrors = ref(false)
const createPending = ref(false)
const createIdempotencyKey = ref('')
const createForm = reactive({ shiftId: '', teamId: '' })
const selectedShift = computed(() =>
  shiftOptions.value.find((option) => option.value === createForm.shiftId),
)
const selectedTeam = computed(() =>
  teamOptions.value.find((option) => option.value === createForm.teamId),
)
const createFormReady = computed(
  () =>
    canManageHandovers.value &&
    handoverContextReady.value &&
    Boolean(selectedShift.value && selectedTeam.value),
)

function resetCreateForm() {
  createForm.shiftId = ''
  createForm.teamId = ''
  createShowErrors.value = false
}

function openCreateDialog() {
  if (!canManageHandovers.value || !handoverContextReady.value) return
  resetCreateForm()
  createIdempotencyKey.value = makeIdempotencyKey('mes-handover-create')
  createDialogOpen.value = true
}

function readReceiptOutcome(response: unknown, action: string): 'accepted' | 'confirmed' {
  if (!isRecord(response) || !isRecord(response.data) || response.data.accepted !== true) {
    throw new Error(`${action}未返回有效回执，请刷新列表核实后再重试。`)
  }

  const receipt = response.data.operationReceipt
  if (!isRecord(receipt) || (receipt.outcome !== 'accepted' && receipt.outcome !== 'confirmed')) {
    throw new Error(`${action}未返回有效回执，请刷新列表核实后再重试。`)
  }

  return receipt.outcome
}

function receiptMessage(action: 'create' | 'accept', outcome: 'accepted' | 'confirmed') {
  const status = outcome === 'confirmed' ? '确认' : '受理'
  return action === 'create'
    ? `班次交接创建成功，服务端已${status}。`
    : `接班已${status}，服务端已${status}。`
}

async function refreshAfterWrite() {
  try {
    await refreshHandovers()
  } catch (error) {
    notifyError(error, '班次交接列表刷新失败，请稍后手动刷新。')
  }
}

async function submitCreate() {
  createShowErrors.value = true
  if (!createFormReady.value || createPending.value) return

  const body: BusinessConsoleMesCreateShiftHandoverRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    shiftId: createForm.shiftId,
    teamId: createForm.teamId,
    teamName: selectedTeam.value?.label,
    idempotencyKey:
      createIdempotencyKey.value ||
      (createIdempotencyKey.value = makeIdempotencyKey('mes-handover-create')),
  }

  createPending.value = true
  try {
    const response = await createShiftHandover(body)
    const outcome = readReceiptOutcome(response, '创建班次交接')
    createDialogOpen.value = false
    createShowErrors.value = false
    notifySuccess(receiptMessage('create', outcome))
    await refreshAfterWrite()
  } catch (error) {
    notifyOperationFailure('创建班次交接失败', error, '创建班次交接失败，请稍后重试。')
  } finally {
    createPending.value = false
  }
}

const acceptDialogOpen = ref(false)
const acceptTarget = ref<HandoverRow | null>(null)
const acceptPendingId = ref<string | null>(null)
const acceptIdempotencyKeys = new Map<string, string>()

function isOpenHandover(row: HandoverRow) {
  return (row.handoverStatus ?? '').toLowerCase() === 'open'
}

function canAcceptRow(row: HandoverRow) {
  return (
    canManageHandovers.value &&
    handoverContextReady.value &&
    isOpenHandover(row) &&
    Boolean(row.handoverId?.trim()) &&
    acceptPendingId.value !== row.handoverId
  )
}

function openAcceptDialog(row: HandoverRow) {
  if (!canAcceptRow(row)) return
  const handoverId = row.handoverId?.trim()
  if (!handoverId) return

  acceptTarget.value = row
  if (!acceptIdempotencyKeys.has(handoverId)) {
    acceptIdempotencyKeys.set(handoverId, makeIdempotencyKey('mes-handover-accept'))
  }
  acceptDialogOpen.value = true
}

const acceptTargetShiftLabel = computed(() => resolveShiftLabel(acceptTarget.value?.shiftId))
const acceptTargetTeamLabel = computed(
  () =>
    acceptTarget.value?.teamName?.trim() ||
    resolveTeamLabel(acceptTarget.value?.teamId) ||
    '未指派',
)

async function submitAccept() {
  const target = acceptTarget.value
  const handoverId = target?.handoverId?.trim()
  if (!target || !handoverId || !canAcceptRow(target)) return

  const idempotencyKey = acceptIdempotencyKeys.get(handoverId)
  if (!idempotencyKey) return

  acceptPendingId.value = handoverId
  try {
    const response = await acceptShiftHandover(handoverId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      idempotencyKey,
    })
    const outcome = readReceiptOutcome(response, '接班')
    acceptDialogOpen.value = false
    acceptTarget.value = null
    acceptIdempotencyKeys.delete(handoverId)
    notifySuccess(receiptMessage('accept', outcome))
    await refreshAfterWrite()
  } catch (error) {
    notifyOperationFailure('接班失败', error, '接班失败，请稍后重试。')
  } finally {
    acceptPendingId.value = null
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="班次交接"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${handoversTotal} 条交接`"
    >
      <template #actions>
        <NvButton
          v-if="canManageHandovers"
          aria-label="新建班次交接"
          size="sm"
          type="button"
          :disabled="!handoverContextReady"
          :title="handoverContextReady ? undefined : '请先完成业务上下文选择'"
          @click="openCreateDialog"
        >
          <PlusIcon aria-hidden="true" />
          新建交接
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="handoversPending"
          @click="refreshHandovers"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="breakdown"
        label="当前列表交接单"
        :value="handovers.length"
        unit="张"
        :segments="handoverSegments"
      />
      <NvMetricCard
        variant="alert"
        label="当前列表未结事项"
        :value="currentPageOpenIssueTotal"
        unit="项"
        :tone="currentPageOpenIssueTotal > 0 ? 'warning' : 'neutral'"
        :status="
          currentPageOpenIssueTotal > 0
            ? { label: '当前页需跟进', tone: 'warning' }
            : { label: '当前页无遗留', tone: 'success' }
        "
        :foot-start="
          currentPageOpenIssueTotal > 0
            ? '仅统计当前列表页返回的交接单；详情以交接单为准。'
            : '当前列表页没有未结事项。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="交接单 / 班次 / 班组"
          aria-label="搜索班次交接"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="交接状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesHandoverStatusOptions"
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
      :total-items="handoversTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="handovers"
      row-key="handoverId"
      :loading="handoversPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无班次交接。先在班次结束时创建交接单登记未完成事项，再由接班人在这里确认接收。"
    >
      <template #cell-handoverStatus="{ row }">
        <NvStatusBadge
          :value="row.handoverStatus"
          :label="labelFor(MES_HANDOVER_STATUS_LABELS, row.handoverStatus) || '未知'"
        />
      </template>
      <template #cell-openIssueCount="{ row }"
        ><span class="tabular-nums">{{ row.openIssueCount ?? 0 }}</span></template
      >
      <template #cell-createdAtUtc="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvRowActions v-if="canManageHandovers && isOpenHandover(row)" label="班次交接操作">
          <NvDropdownMenuItem
            data-testid="accept-handover"
            :disabled="!canAcceptRow(row)"
            @click="openAcceptDialog(row)"
          >
            <CheckCircle2Icon aria-hidden="true" />
            接班
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-if="canManageHandovers" v-model:open="createDialogOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>创建班次交接</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >选择当前可见的班次和班组，创建班次交接单。</NvDialogDescription
          >
        </NvDialogHeader>
        <form data-testid="create-handover-form" class="grid gap-4" @submit.prevent="submitCreate">
          <p
            v-if="createShowErrors && !createFormReady"
            class="text-sm text-destructive"
            role="alert"
          >
            请选择班次和班组，并确认当前可见业务范围完整。
          </p>
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="handover-create-shift-trigger">交接班次</NvFieldLabel>
              <NvSelect
                v-model="createForm.shiftId"
                data-testid="handover-create-shift"
                :data-invalid="createShowErrors && !selectedShift ? '' : undefined"
                :disabled="shiftOptions.length === 0"
              >
                <NvSelectTrigger id="handover-create-shift-trigger" aria-label="选择交接班次">
                  <NvSelectValue placeholder="选择班次" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in shiftOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <p v-if="createShowErrors && !selectedShift" class="text-xs text-destructive">
                请选择交接班次。
              </p>
            </NvField>
            <NvField>
              <NvFieldLabel for="handover-create-team-trigger">交接班组</NvFieldLabel>
              <NvSelect
                v-model="createForm.teamId"
                data-testid="handover-create-team"
                :data-invalid="createShowErrors && !selectedTeam ? '' : undefined"
                :disabled="teamOptions.length === 0"
              >
                <NvSelectTrigger id="handover-create-team-trigger" aria-label="选择交接班组">
                  <NvSelectValue placeholder="选择班组" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in teamOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <p v-if="createShowErrors && !selectedTeam" class="text-xs text-destructive">
                请选择交接班组。
              </p>
            </NvField>
          </NvFieldGroup>
          <p class="text-xs text-muted-foreground">未结事项由服务端按当前可见范围生成快照。</p>
          <NvDialogFooter>
            <NvButton
              type="button"
              variant="outline"
              :disabled="createPending"
              @click="createDialogOpen = false"
            >
              取消
            </NvButton>
            <NvButton type="submit" :disabled="createPending">
              <Spinner v-if="createPending" aria-hidden="true" />
              <PlusIcon v-else aria-hidden="true" />
              创建交接单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-if="canManageHandovers" v-model:open="acceptDialogOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认接班</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >确认接收当前待接班交接单，服务端将按当前权限核验状态。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" data-testid="accept-handover-form" @submit.prevent="submitAccept">
          <dl class="grid gap-2 text-sm">
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">班次</dt>
              <dd class="font-medium">{{ acceptTargetShiftLabel }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">班组</dt>
              <dd class="font-medium">{{ acceptTargetTeamLabel }}</dd>
            </div>
          </dl>
          <p class="text-sm text-muted-foreground">
            确认接收当前待接班交接单；服务端将按当前权限核验状态。
          </p>
          <NvDialogFooter>
            <NvButton
              type="button"
              variant="outline"
              :disabled="acceptPendingId !== null"
              @click="acceptDialogOpen = false"
            >
              取消
            </NvButton>
            <NvButton type="submit" :disabled="acceptPendingId !== null">
              <Spinner v-if="acceptPendingId !== null" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              确认接班
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
