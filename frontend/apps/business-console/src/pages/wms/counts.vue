<script setup lang="ts">
import type {
  BusinessConsoleCreateWmsCountExecutionRequest,
  BusinessConsoleWmsCountExecutionItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { useWmsCountExecutions } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
  Spinner,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '盘点执行',
    requiredPermissions: ['business.wms.receipts.read'],
  },
})

const {
  countExecutions,
  countExecutionsError,
  countExecutionsPending,
  countExecutionsTotal,
  refreshCountExecutions,
  createCountExecution,
  createCountExecutionPending,
  createCountExecutionError,
  completeCountExecution,
  completeCountExecutionPending,
  completeCountExecutionError,
  filters,
} = useWmsCountExecutions()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.locationCode],
})

const OPEN_STATUSES = new Set([
  'pending',
  'open',
  'created',
  'counting',
  'inprogress',
  'in-progress',
])
function isOpen(row: BusinessConsoleWmsCountExecutionItem) {
  return OPEN_STATUSES.has((row.status ?? '').toLowerCase())
}
function hasVariance(row: BusinessConsoleWmsCountExecutionItem) {
  return typeof row.varianceQuantity === 'number' && row.varianceQuantity !== 0
}

// 待盘点 / 有差异 是可行动语义指标（驱动复盘与库存调整），非机械总数。
const pendingCount = computed(() => countExecutions.value.filter(isOpen).length)
const varianceCount = computed(() => countExecutions.value.filter(hasVariance).length)

const createOpen = shallowRef(false)
const createForm = reactive({
  countNo: '',
  skuCode: '',
  uomCode: 'EA',
  siteCode: '',
  locationCode: '',
  expectedQuantity: '',
})
const createError = shallowRef('')

const completeOpen = shallowRef(false)
const completeTarget = shallowRef<BusinessConsoleWmsCountExecutionItem>()
const completeForm = reactive({ countedQuantity: '' })
const completeError = shallowRef('')

const listErrorMessage = computed(() => formatError(countExecutionsError.value))
const createErrorMessage = computed(
  () => createError.value || formatError(createCountExecutionError.value),
)
const completeErrorMessage = computed(
  () => completeError.value || formatError(completeCountExecutionError.value),
)

type CountRow = BusinessConsoleWmsCountExecutionItem
const columns: NvDataTableColumn<CountRow>[] = [
  {
    key: 'countNo',
    header: '盘点单号',
    cellClass: 'font-medium',
    accessor: (r) => r.countNo ?? countNo(r),
  },
  {
    key: 'location',
    header: '库位',
    accessor: (r) => `${r.siteCode ?? '—'} / ${r.locationCode ?? '—'}`,
  },
  { key: 'skuCode', header: 'SKU', accessor: (r) => r.skuCode ?? '—' },
  { key: 'inventoryContext', header: '库存上下文', width: 'w-72' },
  {
    key: 'expectedQuantity',
    header: '账面',
    align: 'end',
    accessor: (r) => formatQuantity(r.expectedQuantity),
  },
  {
    key: 'countedQuantity',
    header: '实盘',
    align: 'end',
    accessor: (r) => formatQuantity(r.countedQuantity),
  },
  { key: 'varianceQuantity', header: '差异', align: 'end' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function countNo(row: CountRow) {
  const id = row.countExecutionId ?? ''
  return id ? `CNT-${id.slice(-8).toUpperCase()}` : '盘点执行'
}
function rowKey(row: CountRow) {
  return row.countExecutionId ?? row.countNo ?? '盘点执行'
}
function formatQuantity(value?: number | null) {
  if (value === undefined || value === null) return '—'
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value)
}
function varianceLabel(value?: number | null) {
  if (value === undefined || value === null) return '—'
  const formatted = formatQuantity(Math.abs(value))
  if (value > 0) return `+${formatted}`
  if (value < 0) return `-${formatted}`
  return '0'
}

function openCreate() {
  createForm.countNo = ''
  createForm.skuCode = ''
  createForm.uomCode = 'EA'
  createForm.siteCode = ''
  createForm.locationCode = ''
  createForm.expectedQuantity = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (
    !createForm.countNo.trim() ||
    !createForm.skuCode.trim() ||
    !createForm.siteCode.trim() ||
    !createForm.locationCode.trim()
  ) {
    createError.value = '请填写盘点单号、SKU、工厂与库位。'
    return
  }
  const expected =
    createForm.expectedQuantity === '' ? undefined : Number(createForm.expectedQuantity)
  if (expected !== undefined && !(expected >= 0)) {
    createError.value = '账面数量需为非负数。'
    return
  }
  const body: BusinessConsoleCreateWmsCountExecutionRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    countNo: createForm.countNo.trim(),
    skuCode: createForm.skuCode.trim(),
    uomCode: createForm.uomCode.trim() || 'EA',
    siteCode: createForm.siteCode.trim(),
    locationCode: createForm.locationCode.trim(),
    expectedQuantity: expected,
  }
  try {
    await createCountExecution(body)
    createOpen.value = false
    toast.success('盘点单已创建')
  } catch {
    // 失败信息由对话框错误区呈现。
  }
}

function openComplete(row: CountRow) {
  completeTarget.value = row
  completeForm.countedQuantity = row.countedQuantity != null ? String(row.countedQuantity) : ''
  completeError.value = ''
  completeOpen.value = true
}
async function submitComplete() {
  const target = completeTarget.value
  if (!target?.countExecutionId) return
  if (completeForm.countedQuantity === '') {
    completeError.value = '请填写实盘数量。'
    return
  }
  const counted = Number(completeForm.countedQuantity)
  if (!(counted >= 0)) {
    completeError.value = '实盘数量需为非负数。'
    return
  }
  try {
    await completeCountExecution(target.countExecutionId, counted)
    completeOpen.value = false
    toast.success(`盘点单 ${target.countNo ?? countNo(target)} 已完成`)
  } catch {
    // 失败信息由对话框错误区呈现。
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="盘点执行"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${countExecutionsTotal} 张盘点单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="countExecutionsPending"
          @click="refreshCountExecutions"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建盘点单
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="待盘点" :value="pendingCount" hint="本页未完成，待实盘录入" />
      <NvSectionCard
        description="有差异"
        :value="varianceCount"
        hint="本页账实不符，需复盘或调整"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.locationCode"
          class="h-9 w-32"
          placeholder="库位"
          aria-label="库位"
        />
        <NvInput
          v-model="filters.status"
          class="h-9 w-28"
          placeholder="状态（可选）"
          aria-label="盘点状态"
        />
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="countExecutionsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="countExecutions"
      :row-key="rowKey"
      :loading="countExecutionsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无盘点单。按库位 × SKU 新建盘点单，实盘后完成以触发库存调整。"
    >
      <template #cell-varianceQuantity="{ row }">
        <span :class="hasVariance(row) ? 'font-medium text-warning' : 'text-muted-foreground'">{{
          varianceLabel(row.varianceQuantity)
        }}</span>
      </template>
      <template #cell-inventoryContext="{ row }">
        <WmsInventoryContextPanel
          compact
          :sku-code="row.skuCode"
          :uom-code="row.uomCode"
          :site-code="row.siteCode"
          :location-code="row.locationCode"
          source-workflow="inventory.count"
          source-label="扫码记录"
          :source-document-id="row.countNo ?? row.countExecutionId"
          gap-message="后端缺口：盘点执行列表未返回冻结、预留和批次/序列号明细；可带盘点范围到 Inventory 查看账面上下文。"
        />
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`盘点操作 ${row.countNo ?? countNo(row)}`">
          <NvDropdownMenuItem :disabled="!isOpen(row)" @click="openComplete(row)">
            <CheckCircle2Icon aria-hidden="true" />
            完成盘点
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建盘点单</NvDialogTitle>
          <NvDialogDescription
            >按库位与 SKU 登记盘点单，账面数量留空则由系统取值。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="cnt-no">盘点单号</NvFieldLabel>
              <NvInput
                id="cnt-no"
                v-model="createForm.countNo"
                autocomplete="off"
                placeholder="如 CNT-2026-0003"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-sku">SKU</NvFieldLabel>
              <NvInput id="cnt-sku" v-model="createForm.skuCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-site">工厂</NvFieldLabel>
              <NvInput
                id="cnt-site"
                v-model="createForm.siteCode"
                autocomplete="off"
                placeholder="如 SITE-HD"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-location">库位</NvFieldLabel>
              <NvInput
                id="cnt-location"
                v-model="createForm.locationCode"
                autocomplete="off"
                placeholder="如 RACK-A-01-01"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-uom">单位</NvFieldLabel>
              <NvInput id="cnt-uom" v-model="createForm.uomCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-expected">账面数量</NvFieldLabel>
              <NvInput
                id="cnt-expected"
                v-model="createForm.expectedQuantity"
                type="number"
                min="0"
                step="any"
                placeholder="可选"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createCountExecutionPending">
              <Spinner v-if="createCountExecutionPending" aria-hidden="true" />
              创建盘点单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="completeOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>完成盘点</NvDialogTitle>
          <NvDialogDescription>
            {{
              completeTarget
                ? `${completeTarget.countNo ?? countNo(completeTarget)} · 账面 ${formatQuantity(completeTarget.expectedQuantity)}`
                : '录入实盘数量。'
            }}
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitComplete">
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="cnt-counted">实盘数量</NvFieldLabel>
              <NvInput
                id="cnt-counted"
                v-model="completeForm.countedQuantity"
                type="number"
                min="0"
                step="any"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="completeErrorMessage" :errors="[completeErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="completeCountExecutionPending">
              <Spinner v-if="completeCountExecutionPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              完成盘点
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
