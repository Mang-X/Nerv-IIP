<script setup lang="ts">
import type { BusinessConsoleErpOperationLaborVarianceItem } from '@nerv-iip/api-client'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvToolbar,
  type NvDataTableColumn,
} from '@nerv-iip/ui'
import { computed, ref } from 'vue'
import { useErpWorkOrderCostVariance } from '@/composables/useBusinessErp'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { costAmount, costHours, efficiencyDirection, laborReason } from '../costVariance'
import { machineReason } from '../machineOverhead'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单成本差异',
    requiredPermissions: ['business.erp.finance.read'],
  },
})
const costs = useErpWorkOrderCostVariance()
const draft = ref('')
const order = computed(() =>
  costs.workOrderPending.value || costs.workOrderError.value
    ? undefined
    : costs.workOrderData.value,
)
const columns: NvDataTableColumn<BusinessConsoleErpOperationLaborVarianceItem>[] = [
  { key: 'operationTaskId', header: '工序 / 工作中心' },
  { key: 'actual', header: '实际人工' },
  { key: 'standard', header: '标准人工' },
  { key: 'efficiency', header: '效率差异' },
  { key: 'lineage', header: '结算与覆盖报工' },
]
const actualCells = computed(() => [
  { key: 'actualHours', label: '实际人工工时', value: costHours(order.value?.actualLaborHours) },
  {
    key: 'actualAmount',
    label: '实际人工金额',
    value: costAmount(order.value?.actualLaborCost, order.value?.currencyCode),
  },
  {
    key: 'standardHours',
    label: '标准人工工时',
    value: costHours(order.value?.standardLaborHours),
  },
  {
    key: 'standardAmount',
    label: '标准人工金额',
    value: costAmount(order.value?.standardLaborCost, order.value?.currencyCode),
  },
])
const varianceCells = computed(() => [
  {
    key: 'hours',
    label: '效率工时差异',
    value: costHours(order.value?.laborEfficiencyVarianceHours),
  },
  {
    key: 'amount',
    label: '效率金额差异',
    value: costAmount(order.value?.laborEfficiencyVarianceAmount, order.value?.currencyCode),
  },
  {
    key: 'accumulated',
    label: '累计成本',
    value: costAmount(order.value?.totalAccumulatedCost, order.value?.currencyCode),
  },
  {
    key: 'capitalized',
    label: '已资本化金额',
    value: costAmount(order.value?.capitalizedCost, order.value?.currencyCode),
  },
  {
    key: 'capitalization',
    label: '资本化差额',
    value: costAmount(order.value?.capitalizationVarianceAmount, order.value?.currencyCode),
  },
])
function queryOrder() {
  costs.workOrder.id = draft.value.trim()
  costs.workOrder.page = 1
  void costs.refreshWorkOrder()
}
function resize(pageSize: number) {
  costs.workOrder.pageSize = pageSize
  costs.workOrder.page = 1
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="工单成本差异" :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]" />
    <form @submit.prevent="queryOrder">
      <NvToolbar :show-search="false">
        <template #filters>
          <NvInput v-model="draft" aria-label="工单编号" placeholder="工单编号" class="w-72" />
          <NvButton
            type="submit"
            :disabled="!costs.ready.value || !draft.trim() || costs.workOrderPending.value"
            >查询工单</NvButton
          >
        </template>
      </NvToolbar>
    </form>
    <section class="mt-6 space-y-4" aria-labelledby="labor-heading">
      <h2 id="labor-heading" class="text-lg font-semibold">人工实际与标准</h2>
      <p v-if="order" role="status" class="text-sm">
        {{ order.workOrderId }} ·
        {{
          order.laborVarianceStatus === 'available'
            ? '差异可用'
            : laborReason(order.unavailableReason)
        }}
      </p>
      <p class="text-sm text-muted-foreground">实际人工按工序结算冻结费率计价，不等同实际工资。</p>
      <NvMetricStrip :cells="actualCells" />
    </section>
    <section class="mt-6 space-y-4" aria-labelledby="variance-heading">
      <h2 id="variance-heading" class="text-lg font-semibold">效率与资本化</h2>
      <p class="text-sm text-muted-foreground">
        效率差异 = 实际 − 标准；资本化差额 = 累计成本 − 已资本化金额。
      </p>
      <p v-if="order" class="text-sm">
        效率方向：{{ efficiencyDirection(order.laborEfficiencyVarianceDirection) }}
      </p>
      <NvMetricStrip :cells="varianceCells" />
      <p v-if="order" class="text-sm text-muted-foreground">
        {{ laborReason(order.laborRateVarianceReason) }}
      </p>
    </section>
    <section class="mt-6 space-y-4" aria-labelledby="operation-heading">
      <h2 id="operation-heading" class="text-lg font-semibold">工序人工明细</h2>
      <NvDataTable
        manual
        :columns="columns"
        :rows="order?.operations ?? []"
        :row-key="(row: BusinessConsoleErpOperationLaborVarianceItem) => row.operationTaskId!"
        :page="costs.workOrder.page"
        :page-size="costs.workOrder.pageSize"
        :total-items="order?.totalOperations ?? 0"
        :loading="costs.workOrderPending.value"
        :error="Boolean(costs.workOrderError.value)"
        error-message="工单成本读取失败，请重试。"
        :searchable="false"
        :column-settings="false"
        :awaiting-scope="!costs.ready.value || !costs.workOrder.id"
        awaiting-scope-message="请选择业务范围并输入工单编号。"
        empty-message="暂无有效人工结算；冲销后请重新结算。"
        @retry="costs.refreshWorkOrder"
        @update:page="costs.workOrder.page = $event"
        @update:page-size="resize"
      >
        <template #cell-operationTaskId="{ row }"
          ><p>{{ row.operationTaskId }}</p>
          <p class="text-xs text-muted-foreground">{{ row.workCenterId }}</p></template
        >
        <template #cell-actual="{ row }"
          ><p>{{ costHours(row.actualLaborHours) }}</p>
          <p>{{ costAmount(row.actualLaborCost, row.currencyCode) }}</p></template
        >
        <template #cell-standard="{ row }"
          ><p>{{ costHours(row.standardLaborHours) }}</p>
          <p>{{ costAmount(row.standardLaborCost, row.currencyCode) }}</p>
          <p v-if="row.unavailableReason" class="text-xs text-muted-foreground">
            {{ laborReason(row.unavailableReason) }}
          </p></template
        >
        <template #cell-efficiency="{ row }"
          ><p>{{ efficiencyDirection(row.laborEfficiencyVarianceDirection) }}</p>
          <p>{{ costHours(row.laborEfficiencyVarianceHours) }}</p>
          <p>{{ costAmount(row.laborEfficiencyVarianceAmount, row.currencyCode) }}</p></template
        >
        <template #cell-lineage="{ row }">
          <p>结算版本 {{ row.settlementRevision }} · 费率版本 {{ row.rateRevision }}</p>
          <details class="mt-1 text-xs">
            <summary class="cursor-pointer">费率与覆盖报工</summary>
            <dl class="mt-2 space-y-1 break-all">
              <dt>费率编号 / 每小时</dt>
              <dd>
                {{ row.workCenterCostRateId }} / {{ costAmount(row.hourlyRate, row.currencyCode) }}
              </dd>
              <dt>费率基准时间</dt>
              <dd>{{ row.rateBasisAtUtc }}</dd>
            </dl>
            <ul class="mt-3 space-y-3">
              <li v-for="report in row.coveredReports" :key="report.reportNo">
                <p class="font-medium">
                  {{ report.reportNo }} · {{ report.isReversal ? '冲销' : '报工' }}
                </p>
                <p v-if="report.isReversal">原报工：{{ report.reversedReportNo }}</p>
                <p>
                  良品 {{ report.goodQuantity }} / 废品 {{ report.scrapQuantity }} / 返工
                  {{ report.reworkQuantity }} {{ report.uomCode }}
                </p>
                <p>
                  理论速率 {{ report.theoreticalRatePerHour ?? '未维护' }} {{ report.uomCode }} /
                  小时
                </p>
                <p>{{ report.reportedAtUtc }}</p>
              </li>
            </ul>
          </details>
        </template>
      </NvDataTable>
    </section>
    <section v-if="order" class="mt-6 space-y-2" aria-labelledby="machine-heading">
      <h2 id="machine-heading" class="text-lg font-semibold">机器工时与费用</h2>
      <p>机器工时：{{ costHours(order.actualMachineHours) }}</p>
      <p v-if="order.machineCostStatus === 'available'">
        机器预定分配：{{
          costAmount(order.appliedMachineOverheadTotal, order.machineCurrencyCode)
        }}（非已核定实际成本）
      </p>
      <p
        v-else-if="
          order.machineCostStatus === 'notApplicable' ||
          order.machineCostUnavailableReason === 'machine_overhead_rate_not_configured'
        "
      >
        未启用机器成本
      </p>
      <p v-else>机器费用不可用：{{ machineReason(order.machineCostUnavailableReason) }}</p>
      <RouterLink to="/erp/finance/machine-overhead" class="text-sm underline"
        >核对机器制造费用</RouterLink
      >
    </section>
  </BusinessLayout>
</template>
