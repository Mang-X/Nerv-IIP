<script setup lang="ts">
import type {
  BusinessConsoleErpOperationMachineOverheadItem,
  BusinessConsoleErpMachineOverheadReconciliationItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvToolbar,
} from '@nerv-iip/ui'
import { computed, reactive } from 'vue'
import { useErpMachineOverhead } from '@/composables/useErpCostAccounting'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import WorkOrderCostPicker from '@/components/erp/WorkOrderCostPicker.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  allocationDifference,
  machineAmount,
  machineHours,
  machineReason,
  machineStatusLabels,
} from '../machineOverhead'

definePage({
  meta: {
    requiresAuth: true,
    title: '机器制造费用',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

const costs = useErpMachineOverhead()
const draft = reactive({ workOrderId: '', period: '', workCenterId: '' })
const order = computed(() =>
  costs.workOrderError.value || costs.workOrderPending.value
    ? undefined
    : costs.workOrderData.value,
)
const month = computed(() =>
  costs.monthlyError.value || costs.monthlyPending.value ? undefined : costs.monthlyData.value,
)
const orderCells = computed<NvMetricStripCell[]>(() => {
  const data = order.value
  const available = data?.machineCostStatus === 'available'
  return [
    {
      key: 'hours',
      label: '机器实绩工时',
      value: data ? machineHours(data.actualMachineHours) : '—',
    },
    {
      key: 'fixed',
      label: '固定预定分配',
      value: available
        ? machineAmount(data.appliedFixedMachineOverhead, data.machineCurrencyCode)
        : '—',
    },
    {
      key: 'variable',
      label: '变动预定分配',
      value: available
        ? machineAmount(data.appliedVariableMachineOverhead, data.machineCurrencyCode)
        : '—',
    },
    {
      key: 'total',
      label: '预定分配合计',
      value: available
        ? machineAmount(data.appliedMachineOverheadTotal, data.machineCurrencyCode)
        : '—',
    },
  ]
})
const operationColumns: NvDataTableColumn<BusinessConsoleErpOperationMachineOverheadItem>[] = [
  { key: 'operationTaskId', header: '工序任务', accessor: (row) => row.operationTaskId },
  { key: 'workCenterId', header: '工作中心', accessor: (row) => row.workCenterId },
  { key: 'status', header: '状态' },
  {
    key: 'actualMachineHours',
    header: '机器实绩工时',
    accessor: (row) => machineHours(row.actualMachineHours),
  },
  { key: 'amounts', header: '预定分配（固定 / 变动 / 合计）' },
  { key: 'lineage', header: '费率与结算' },
]
const monthlyColumns: NvDataTableColumn<BusinessConsoleErpMachineOverheadReconciliationItem>[] = [
  { key: 'workCenterId', header: '工作中心', accessor: (row) => row.workCenterId },
  { key: 'reconciliationStatus', header: '核对状态' },
  { key: 'actual', header: '月度实际池（固定 / 变动 / 合计）' },
  { key: 'applied', header: '已分配（固定 / 变动 / 合计）' },
  { key: 'difference', header: '未 / 多分配差异' },
  { key: 'revision', header: '期间与版本' },
]

function queryOrder() {
  costs.workOrder.id = draft.workOrderId.trim()
  costs.workOrder.page = 1
  void costs.refreshWorkOrder()
}
function queryMonth() {
  costs.monthly.period = draft.period.trim()
  costs.monthly.workCenterId = draft.workCenterId.trim()
  costs.monthly.page = 1
  void costs.refreshMonthly()
}
function resizeOrder(pageSize: number) {
  costs.workOrder.pageSize = pageSize
  costs.workOrder.page = 1
}
function resizeMonth(pageSize: number) {
  costs.monthly.pageSize = pageSize
  costs.monthly.page = 1
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="机器制造费用" :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]" />
    <section class="space-y-4" aria-labelledby="order-heading">
      <div>
        <h2 id="order-heading" class="text-lg font-semibold">工单预定分配</h2>
        <p class="text-sm text-muted-foreground">按冻结费率分配，不代表已核定实际机器成本。</p>
      </div>
      <form @submit.prevent="queryOrder">
        <NvToolbar :show-search="false">
          <template #filters>
            <WorkOrderCostPicker v-model="draft.workOrderId" class="w-96" />
            <NvButton
              type="submit"
              :disabled="
                !costs.ready.value || !draft.workOrderId.trim() || costs.workOrderPending.value
              "
              >查询工单</NvButton
            >
          </template>
        </NvToolbar>
      </form>
      <p v-if="order" class="text-sm" role="status">
        {{ costs.workOrder.id }} · {{ machineStatusLabels[order.machineCostStatus] }}
        <span v-if="order.machineCostUnavailableReason">
          · {{ machineReason(order.machineCostUnavailableReason) }}</span
        >
      </p>
      <NvMetricStrip :cells="orderCells" />
      <NvDataTable
        manual
        :columns="operationColumns"
        :rows="order?.machineOverheadOperations ?? []"
        :row-key="(row: BusinessConsoleErpOperationMachineOverheadItem) => row.settlementId"
        :page="costs.workOrder.page"
        :page-size="costs.workOrder.pageSize"
        :total-items="order?.totalMachineOverheadOperations ?? 0"
        :loading="costs.workOrderPending.value"
        :error="Boolean(costs.workOrderError.value)"
        error-message="工单费用读取失败，请重试。"
        :searchable="false"
        :column-settings="false"
        :awaiting-scope="!costs.ready.value || !costs.workOrder.id"
        awaiting-scope-message="请选择业务范围并选择工单。"
        :empty-message="order ? '没有有效机器结算明细。' : '尚未取得工单费用。'"
        @retry="costs.refreshWorkOrder"
        @update:page="costs.workOrder.page = $event"
        @update:page-size="resizeOrder"
      >
        <template #cell-status="{ row }">
          <span>{{ machineStatusLabels[row.status] }}</span>
          <p v-if="row.unavailableReason" class="text-xs text-muted-foreground">
            {{ machineReason(row.unavailableReason) }}
          </p>
        </template>
        <template #cell-amounts="{ row }">
          <div v-if="row.status === 'available'" class="space-y-1 tabular-nums">
            <p>固定 {{ machineAmount(row.appliedFixedMachineOverhead, row.currencyCode) }}</p>
            <p>变动 {{ machineAmount(row.appliedVariableMachineOverhead, row.currencyCode) }}</p>
            <p class="font-medium">
              合计 {{ machineAmount(row.appliedMachineOverheadTotal, row.currencyCode) }}
            </p>
          </div>
          <span v-else>—</span>
        </template>
        <template #cell-lineage="{ row }">
          <p>{{ row.accountingPeriodCode }} · 费率版本 {{ row.rateRevision }}</p>
          <details class="mt-1 text-xs">
            <summary class="cursor-pointer">结算追溯</summary>
            <dl class="mt-2 space-y-1 break-all">
              <dt>结算编号 / 版本</dt>
              <dd>{{ row.settlementId }} / {{ row.settlementRevision }}</dd>
              <dt>费率编号</dt>
              <dd>{{ row.workCenterMachineOverheadRateId }}</dd>
              <dt>设备</dt>
              <dd>{{ row.deviceAssetId ?? '—' }}</dd>
              <dt>完成时间</dt>
              <dd>{{ row.completedAtUtc }}</dd>
              <dt>来源事件</dt>
              <dd>{{ row.sourceEventId }}</dd>
            </dl>
          </details>
        </template>
      </NvDataTable>
    </section>

    <section class="mt-8 space-y-4" aria-labelledby="month-heading">
      <div>
        <h2 id="month-heading" class="text-lg font-semibold">工作中心月度分配差异</h2>
        <p class="text-sm text-muted-foreground">
          实际池 − 已分配额：正数为未分配，负数为多分配。金额按各行币种核对。
        </p>
      </div>
      <form @submit.prevent="queryMonth">
        <NvToolbar :show-search="false">
          <template #filters>
            <NvInput
              v-model="draft.period"
              class="w-48"
              aria-label="会计期间"
              placeholder="会计期间"
            />
            <DirectoryPicker
              v-model="draft.workCenterId"
              directory-type="work-center"
              class="w-64"
              placeholder="全部工作中心"
              clearable
            />
            <NvButton
              type="submit"
              :disabled="!costs.ready.value || !draft.period.trim() || costs.monthlyPending.value"
              >查询月度差异</NvButton
            >
          </template>
        </NvToolbar>
      </form>
      <p v-if="month" class="text-sm" role="status">
        {{ month.accountingPeriodCode }} ·
        {{
          month.accountingPeriodStatus === 'closed'
            ? '已关账'
            : month.accountingPeriodStatus === 'open'
              ? '未关账'
              : '期间不存在'
        }}
        · {{ machineStatusLabels[month.reconciliationStatus] }}
        <span v-if="month.reconciliationUnavailableReason">
          · {{ machineReason(month.reconciliationUnavailableReason) }}</span
        >
      </p>
      <NvDataTable
        manual
        :columns="monthlyColumns"
        :rows="month?.items ?? []"
        :row-key="(row: BusinessConsoleErpMachineOverheadReconciliationItem) => row.id"
        :page="costs.monthly.page"
        :page-size="costs.monthly.pageSize"
        :total-items="month?.totalCount ?? 0"
        :loading="costs.monthlyPending.value"
        :error="Boolean(costs.monthlyError.value)"
        error-message="月度差异读取失败，请重试。"
        :searchable="false"
        :column-settings="false"
        :awaiting-scope="!costs.ready.value || !costs.monthly.period"
        awaiting-scope-message="请选择业务范围并输入会计期间。"
        empty-message="没有月度对账记录。"
        @retry="costs.refreshMonthly"
        @update:page="costs.monthly.page = $event"
        @update:page-size="resizeMonth"
      >
        <template #cell-reconciliationStatus="{ row }">
          <p>{{ machineStatusLabels[row.reconciliationStatus] }}</p>
          <p v-if="row.unavailableReason" class="text-xs text-muted-foreground">
            {{ machineReason(row.unavailableReason) }}
          </p>
        </template>
        <template #cell-actual="{ row }">
          <div class="space-y-1 tabular-nums">
            <p>固定 {{ machineAmount(row.actualFixedOverheadAmount, row.currencyCode) }}</p>
            <p>变动 {{ machineAmount(row.actualVariableOverheadAmount, row.currencyCode) }}</p>
            <p class="font-medium">
              合计 {{ machineAmount(row.actualTotalOverheadAmount, row.currencyCode) }}
            </p>
          </div>
        </template>
        <template #cell-applied="{ row }">
          <div class="space-y-1 tabular-nums">
            <p>固定 {{ machineAmount(row.appliedFixedAmount, row.currencyCode) }}</p>
            <p>变动 {{ machineAmount(row.appliedVariableAmount, row.currencyCode) }}</p>
            <p class="font-medium">
              合计 {{ machineAmount(row.appliedTotalAmount, row.currencyCode) }}
            </p>
            <p class="text-xs text-muted-foreground">{{ machineHours(row.appliedMachineHours) }}</p>
          </div>
        </template>
        <template #cell-difference="{ row }">
          <div class="space-y-1 tabular-nums">
            <p>
              固定 {{ allocationDifference(row.underOverAppliedFixedAmount, row.currencyCode) }}
            </p>
            <p>
              变动 {{ allocationDifference(row.underOverAppliedVariableAmount, row.currencyCode) }}
            </p>
            <p class="font-medium">
              合计 {{ allocationDifference(row.underOverAppliedTotalAmount, row.currencyCode) }}
            </p>
          </div>
        </template>
        <template #cell-revision="{ row }">
          <p>{{ row.accountingPeriodCode }}</p>
          <p>对账版本 {{ row.revision }} · 费率版本 {{ row.rateRevision }}</p>
          <p v-if="row.reconciliationStatus !== 'available'" class="text-xs text-muted-foreground">
            此记录不可作为当前关账依据
          </p>
        </template>
      </NvDataTable>
    </section>
  </BusinessLayout>
</template>
