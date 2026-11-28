<script setup lang="ts">
import { demandTypeLabel, formatDate, formatQuantity } from './planningFormatters'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import { inlineErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvTabsContent,
  NvToolbar,
} from '@nerv-iip/ui'
import { ExternalLinkIcon } from '@lucide/vue'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type { ShallowUnwrapRef } from 'vue'
import type { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

type View = ShallowUnwrapRef<ReturnType<typeof usePlanningWorkbenchController>>

defineProps<{
  canReadDemands: View['canReadDemands']
  canReadMrp: View['canReadMrp']
  carryDemandIntoMps: View['carryDemandIntoMps']
  demandColumns: View['demandColumns']
  demandCoverageForRow: View['demandCoverageForRow']
  demandFilterActive: View['demandFilterActive']
  demandTypeFilterOptions: View['demandTypeFilterOptions']
  demandTypeResetNotice: View['demandTypeResetNotice']
  demandsError: View['demandsError']
  demandsPending: View['demandsPending']
  filteredDemands: View['filteredDemands']
  hasPermission: View['hasPermission']
  openSalesOrderDemand: View['openSalesOrderDemand']
  orderUrgencies: View['orderUrgencies']
  refreshUrgency: View['refreshUrgency']
  runRequest: View['runRequest']
  siteLabel: View['siteLabel']
  skuLabel: View['skuLabel']
  sourceOrder: View['sourceOrder']
}>()

const demandKeyword = defineModel<View['demandKeyword']>('demandKeyword', { required: true })

const demandTypeFilter = defineModel<View['demandTypeFilter']>('demandTypeFilter', {
  required: true,
})

const displayMode = defineModel<View['displayMode']>('displayMode', { required: true })

const demandCancelTarget = defineModel<View['demandCancelTarget']>('demandCancelTarget', {
  required: true,
})
</script>

<template>
  <NvTabsContent v-if="canReadDemands" value="demands" class="grid min-w-0 grid-cols-1 gap-3">
    <p v-if="sourceOrder" class="text-sm text-muted-foreground">
      来源销售订单 {{ sourceOrder }} · 核对前 200 条搜索结果
      <RouterLink to="/planning">查看普通需求池</RouterLink>
    </p>
    <NvToolbar
      v-model:search="demandKeyword"
      search-placeholder="搜来源单号 / 物料 / 客户 / 工厂"
      search-label="需求池关键字"
    >
      <template #filters>
        <NvSelect v-model="demandTypeFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="需求类型筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in demandTypeFilterOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <UrgencyDisplayModeSelect v-model="displayMode" />
      </template>
    </NvToolbar>
    <p v-if="demandTypeResetNotice" class="text-sm text-muted-foreground" role="status">
      {{ demandTypeResetNotice }}
    </p>
    <NvDataTable
      :columns="
        canReadMrp ? demandColumns : demandColumns.filter((column) => column.key !== 'coverage')
      "
      :rows="filteredDemands"
      row-key="demandSourceId"
      :loading="demandsPending"
      :awaiting-scope="!hasBusinessContext(runRequest)"
      awaiting-scope-message="请先选择组织与环境。"
      :error="demandsError"
      :error-message="inlineErrorMessage(demandsError, '读取需求失败。')"
      :searchable="false"
      :column-settings="false"
      :empty-message="
        sourceOrder
          ? '当前搜索范围内未找到符合条件的销售需求。'
          : demandFilterActive
            ? '没有符合当前筛选条件的需求，换个关键字或类型试试。'
            : '当前范围没有计划需求。'
      "
    >
      <template #cell-sourceReference="{ row }">
        <NvButton
          v-if="row.demandType === 'sales-order' && hasPermission(P.erpSalesRead)"
          type="button"
          size="sm"
          variant="ghost"
          class="max-w-full"
          :title="row.sourceReference"
          :aria-label="`查看销售订单 ${row.sourceReference}`"
          @click="openSalesOrderDemand(row)"
        >
          <span class="min-w-0 truncate">{{ row.sourceReference }}</span>
          <ExternalLinkIcon aria-hidden="true" />
        </NvButton>
        <span v-else class="block truncate" :title="row.sourceReference">{{
          row.sourceReference
        }}</span>
      </template>
      <template #cell-demandType="{ row }">{{ demandTypeLabel(row.demandType) }}</template>
      <template #cell-sourceStatus="{ row }">
        <NvStatusBadge
          :label="row.sourceStatus === 'cancelled' ? '已取消' : '有效'"
          :tone="row.sourceStatus === 'cancelled' ? 'neutral' : 'success'"
        />
      </template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="hasPermission(P.planningMpsManage) && row.sourceStatus === 'active'"
          type="button"
          size="sm"
          variant="outline"
          @click="carryDemandIntoMps(row)"
        >
          带入 MPS
        </NvButton>
        <NvButton
          v-if="
            hasPermission(P.planningDemandsManage) &&
            row.demandType !== 'sales-order' &&
            row.sourceStatus === 'active'
          "
          type="button"
          size="sm"
          variant="ghost"
          @click="demandCancelTarget = row"
          >作废</NvButton
        >
      </template>
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span class="truncate" :title="skuLabel(row.skuCode)">{{ skuLabel(row.skuCode) }}</span>
          <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
        </div>
      </template>
      <template #cell-siteCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span class="truncate" :title="siteLabel(row.siteCode)">{{
            siteLabel(row.siteCode)
          }}</span>
          <span v-if="row.siteCode" class="text-xs text-muted-foreground">{{ row.siteCode }}</span>
        </div>
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity, row.uomCode) }}</span></template
      >
      <template #cell-dueDate="{ row }">{{ formatDate(row.dueDate) }}</template>
      <template #cell-urgency="{ row }">
        <OrderUrgencyBadge
          :order-reference="row.sourceReference ?? ''"
          :mode="displayMode"
          :urgency="
            row.sourceReference
              ? orderUrgencies.byReference.value.get(row.sourceReference)
              : undefined
          "
          :source-unavailable="orderUrgencies.error?.value != null"
          @refresh="refreshUrgency"
        />
      </template>
      <template #cell-coverage="{ row }"
        ><NvStatusBadge
          :label="demandCoverageForRow(row).label"
          :tone="demandCoverageForRow(row).tone"
      /></template>
    </NvDataTable>
  </NvTabsContent>
</template>
