<script setup lang="ts">
import type {
  BusinessConsoleCreateWmsCountExecutionRequest,
  BusinessConsoleWmsCountExecutionItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useWmsCountExecutions } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProClose,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DropdownMenuProItem,
  FieldPro,
  FieldProError,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Spinner,
  StatusBadgePro,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '盘点执行', requiredPermissions: ['business.wms.receipts.read'] } })

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

const OPEN_STATUSES = new Set(['pending', 'open', 'created', 'counting', 'inprogress', 'in-progress'])
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
const createErrorMessage = computed(() => createError.value || formatError(createCountExecutionError.value))
const completeErrorMessage = computed(() => completeError.value || formatError(completeCountExecutionError.value))

type CountRow = BusinessConsoleWmsCountExecutionItem
const columns: DataTableProColumn<CountRow>[] = [
  { key: 'countNo', header: '盘点单号', cellClass: 'font-medium', accessor: (r) => r.countNo ?? countNo(r) },
  { key: 'location', header: '库位', accessor: (r) => `${r.siteCode ?? '—'} / ${r.locationCode ?? '—'}` },
  { key: 'skuCode', header: 'SKU', accessor: (r) => r.skuCode ?? '—' },
  { key: 'expectedQuantity', header: '账面', align: 'end', accessor: (r) => formatQuantity(r.expectedQuantity) },
  { key: 'countedQuantity', header: '实盘', align: 'end', accessor: (r) => formatQuantity(r.countedQuantity) },
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
  if (!createForm.countNo.trim() || !createForm.skuCode.trim() || !createForm.siteCode.trim() || !createForm.locationCode.trim()) {
    createError.value = '请填写盘点单号、SKU、工厂与库位。'
    return
  }
  const expected = createForm.expectedQuantity === '' ? undefined : Number(createForm.expectedQuantity)
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
    <PageHeader title="盘点执行" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${countExecutionsTotal} 张盘点单`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="countExecutionsPending" @click="refreshCountExecutions">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建盘点单
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="待盘点" :value="pendingCount" hint="本页未完成，待实盘录入" />
      <SectionCard description="有差异" :value="varianceCount" hint="本页账实不符，需复盘或调整" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="filters.locationCode" class="h-9 w-32" placeholder="库位" aria-label="库位" />
        <InputPro v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="盘点状态" />
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
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
        <span :class="hasVariance(row) ? 'font-medium text-warning' : 'text-muted-foreground'">{{ varianceLabel(row.varianceQuantity) }}</span>
      </template>
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <RowActions :label="`盘点操作 ${row.countNo ?? countNo(row)}`">
          <DropdownMenuProItem :disabled="!isOpen(row)" @click="openComplete(row)">
            <CheckCircle2Icon aria-hidden="true" />
            完成盘点
          </DropdownMenuProItem>
        </RowActions>
      </template>
    </DataTablePro>


    <DialogPro v-model:open="createOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>新建盘点单</DialogProTitle>
          <DialogProDescription>按库位与 SKU 登记盘点单，账面数量留空则由系统取值。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro>
              <FieldProLabel for="cnt-no">盘点单号</FieldProLabel>
              <InputPro id="cnt-no" v-model="createForm.countNo" autocomplete="off" placeholder="如 CNT-2026-0003" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="cnt-sku">SKU</FieldProLabel>
              <InputPro id="cnt-sku" v-model="createForm.skuCode" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="cnt-site">工厂</FieldProLabel>
              <InputPro id="cnt-site" v-model="createForm.siteCode" autocomplete="off" placeholder="如 SITE-HD" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="cnt-location">库位</FieldProLabel>
              <InputPro id="cnt-location" v-model="createForm.locationCode" autocomplete="off" placeholder="如 RACK-A-01-01" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="cnt-uom">单位</FieldProLabel>
              <InputPro id="cnt-uom" v-model="createForm.uomCode" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="cnt-expected">账面数量</FieldProLabel>
              <InputPro id="cnt-expected" v-model="createForm.expectedQuantity" type="number" min="0" step="any" placeholder="可选" />
            </FieldPro>
          </FieldProGroup>

          <FieldProError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="createCountExecutionPending">
              <Spinner v-if="createCountExecutionPending" aria-hidden="true" />
              创建盘点单
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro v-model:open="completeOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>完成盘点</DialogProTitle>
          <DialogProDescription>
            {{ completeTarget ? `${completeTarget.countNo ?? countNo(completeTarget)} · 账面 ${formatQuantity(completeTarget.expectedQuantity)}` : '录入实盘数量。' }}
          </DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitComplete">
          <FieldProGroup class="grid gap-3">
            <FieldPro>
              <FieldProLabel for="cnt-counted">实盘数量</FieldProLabel>
              <InputPro id="cnt-counted" v-model="completeForm.countedQuantity" type="number" min="0" step="any" />
            </FieldPro>
          </FieldProGroup>

          <FieldProError v-if="completeErrorMessage" :errors="[completeErrorMessage]" />

          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="completeCountExecutionPending">
              <Spinner v-if="completeCountExecutionPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              完成盘点
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
