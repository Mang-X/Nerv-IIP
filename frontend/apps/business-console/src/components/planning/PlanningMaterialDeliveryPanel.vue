<script setup lang="ts">
import type {
  BusinessConsoleMaterialDeliveryResponse,
  BusinessConsoleMaterialDeliveryUnknownRequirementSource,
  BusinessConsoleMrpRunItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  NvButton,
  NvDataTable,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { useBusinessPlanningMaterialDeliveries } from '@/composables/useBusinessPlanningMaterialDeliveries'
import { inlineErrorMessage, notifyOperationFailure } from '@/utils/notify'
import PlanningMaterialDeliverySources from './PlanningMaterialDeliverySources.vue'
import {
  materialDeliverySourceLabel as label,
  materialDeliveryStatuses,
  materialDeliveryUtc as utc,
} from './materialDeliveryPresentation'

const props = defineProps<{
  runs: BusinessConsoleMrpRunItem[]
  skuLabel: (code?: string | null) => string
}>()
const emit = defineEmits<{ locateSuggestion: [suggestionId: string, runId: string] }>()
const {
  selection,
  planPage,
  plans,
  plansTotal,
  plansPending,
  plansError,
  deliveries,
  deliveriesPending,
  deliveriesError,
  refresh,
} = useBusinessPlanningMaterialDeliveries()
const completedRuns = computed(() =>
  props.runs.filter((run) => run.status?.toLowerCase() === 'completed'),
)
watch(
  completedRuns,
  (runs) => {
    if (!runs.some((run) => run.runId === selection.runId)) selection.runId = runs[0]?.runId ?? ''
  },
  { immediate: true },
)
watch(deliveriesError, (error) => {
  if (error) notifyOperationFailure('读取物料交付', error, '读取物料交付失败。')
})
watch(plansError, (error) => {
  if (error) notifyOperationFailure('读取 APS 方案', error, '读取 APS 方案失败。')
})
const planChoice = computed({
  get: () => selection.planId || 'none',
  set: (value: string) => {
    selection.planId = value === 'none' ? '' : value
  },
})
const rows = computed(() => deliveries.value?.items ?? [])
const unknown = computed(() => deliveries.value?.unknownRequirementSuggestions ?? [])
const columns: NvDataTableColumn<BusinessConsoleMaterialDeliveryResponse>[] = [
  { key: 'sources', header: '来源销售单 / 追溯' },
  { key: 'skuCode', header: 'SKU / 工厂' },
  { key: 'netRequirementQuantity', header: '净缺口' },
  { key: 'latestProcurementDate', header: '最晚采购日' },
  { key: 'expectedArrivalDate', header: '预计到达' },
  { key: 'expectedStartUtc', header: '预计可开工' },
  { key: 'latestStartUtc', header: '最晚开工' },
  { key: 'status', header: '交付状态' },
]
const unknownColumns: NvDataTableColumn<BusinessConsoleMaterialDeliveryUnknownRequirementSource>[] =
  [
    { key: 'skuCode', header: 'SKU' },
    { key: 'requiredDate', header: '需求日期' },
    { key: 'releaseDate', header: '采购日期' },
    { key: 'sources', header: '建议级来源（不计净缺口）' },
  ]
</script>
<template>
  <section class="grid gap-3" aria-label="物料交付">
    <div class="flex flex-wrap items-end gap-3">
      <div class="grid gap-1">
        <label class="text-sm font-medium">MRP 运行</label>
        <NvSelect v-model="selection.runId">
          <NvSelectTrigger class="w-64" aria-label="物料交付 MRP 运行"
            ><NvSelectValue placeholder="请先完成 MRP"
          /></NvSelectTrigger>
          <NvSelectContent
            ><NvSelectItem
              v-for="(run, index) in completedRuns"
              :key="run.runId"
              :value="run.runId!"
              :title="run.runId"
              >{{ run.horizonStart }} ~ {{ run.horizonEnd }} · 已完成运行
              {{ completedRuns.length - index }}</NvSelectItem
            ></NvSelectContent
          >
        </NvSelect>
      </div>
      <div class="grid gap-1">
        <label class="text-sm font-medium">APS 方案（显式选择）</label>
        <NvSelect v-model="planChoice">
          <NvSelectTrigger class="w-96" aria-label="物料交付 APS 方案" :disabled="plansPending"
            ><NvSelectValue placeholder="选择 APS 方案"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="none">未选择方案</NvSelectItem>
            <NvSelectItem
              v-for="plan in plans"
              :key="plan.planId"
              :value="plan.planId!"
              :title="plan.planId"
              >{{ utc(plan.generatedAtUtc) }} · {{ label(plan.status) }} ·
              {{ plan.assignmentCount ?? 0 }} 条排程{{
                plan.isInvalidated ? ' · 已失效' : ''
              }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </div>
      <div v-if="plansTotal > 20" class="flex items-center gap-2">
        <NvButton
          size="sm"
          variant="outline"
          :disabled="planPage === 1 || plansPending"
          @click="planPage--"
          >上一页方案</NvButton
        >
        <span class="text-xs text-muted-foreground"
          >{{ planPage }} / {{ Math.ceil(plansTotal / 20) }}</span
        >
        <NvButton
          size="sm"
          variant="outline"
          :disabled="planPage * 20 >= plansTotal || plansPending"
          @click="planPage++"
          >下一页方案</NvButton
        >
      </div>
      <NvButton
        size="sm"
        variant="outline"
        :disabled="!selection.runId || deliveriesPending"
        @click="refresh"
        >刷新交付事实</NvButton
      >
    </div>
    <p class="text-xs text-muted-foreground">
      日期沿用来源日期，时间以 UTC 展示。供应覆盖按每条净需求独立评价。<span
        v-if="deliveries?.evaluatedAtUtc"
        :title="deliveries.evaluatedAtUtc"
        >评价时点 {{ utc(deliveries.evaluatedAtUtc) }}</span
      >
    </p>
    <NvDataTable
      :columns="columns"
      :rows="rows"
      row-key="netRequirementReference"
      :loading="deliveriesPending"
      :error="deliveriesError"
      :error-message="inlineErrorMessage(deliveriesError, '读取物料交付失败。')"
      :awaiting-scope="!selection.runId"
      awaiting-scope-message="完成 MRP 后选择运行，查看物料交付。"
      :searchable="false"
      :pagination="false"
      :column-settings="false"
      empty-message="本次运行没有可识别的净需求。"
    >
      <template #cell-sources="{ row }">
        <details>
          <summary class="cursor-pointer whitespace-normal font-medium">
            <span v-if="row.demandSources?.length">{{
              [...new Set(row.demandSources.map((source) => source.sourceReference))].join('、')
            }}</span
            ><span v-else>需求来源缺失</span
            ><span class="block text-xs font-normal text-primary">展开来源与 pegging</span>
          </summary>
          <PlanningMaterialDeliverySources
            :row="row"
            @locate-suggestion="
              (suggestionId, runId) => emit('locateSuggestion', suggestionId, runId)
            "
          />
        </details>
      </template>
      <template #cell-skuCode="{ row }"
        ><p>{{ skuLabel(row.skuCode) }}</p>
        <p class="text-xs text-muted-foreground">
          {{ row.skuCode }} · {{ row.siteCode }}
        </p></template
      >
      <template #cell-netRequirementQuantity="{ row }"
        ><span data-net-requirement-quantity class="tabular-nums font-medium"
          >{{ row.netRequirementQuantity ?? '—' }} {{ row.uomCode }}</span
        ></template
      >
      <template #cell-latestProcurementDate="{ row }"
        ><span :title="row.latestProcurementUtc">{{
          row.latestProcurementDate ?? '—'
        }}</span></template
      >
      <template #cell-expectedArrivalDate="{ row }"
        ><span data-expected-arrival :title="row.expectedArrivalUtc ?? undefined">{{
          row.expectedArrivalDate ?? '—'
        }}</span></template
      >
      <template #cell-expectedStartUtc="{ row }"
        ><span data-expected-start :title="row.expectedStartUtc ?? undefined">{{
          utc(row.expectedStartUtc)
        }}</span></template
      >
      <template #cell-latestStartUtc="{ row }"
        ><span :title="row.latestStartUtc ?? undefined">{{
          utc(row.latestStartUtc)
        }}</span></template
      >
      <template #cell-status="{ row }">
        <NvStatusBadge
          v-if="row.status"
          :label="materialDeliveryStatuses[row.status].label"
          :tone="materialDeliveryStatuses[row.status].tone"
        />
        <ul
          v-if="row.reasons?.length"
          class="mt-1 min-w-40 whitespace-normal text-xs text-muted-foreground"
        >
          <li v-for="reason in row.reasons" :key="reason">{{ label(reason) }}</li>
        </ul>
      </template>
    </NvDataTable>
    <div v-if="unknown.length" class="grid gap-2">
      <h3 class="text-base font-semibold">净需求身份未知 · 历史建议</h3>
      <p class="text-sm text-muted-foreground">
        保留建议级来源与原始净算说明，无法确认精确净需求行和缺口合计。
      </p>
      <NvDataTable
        :columns="unknownColumns"
        :rows="unknown"
        :row-key="(row) => row.suggestionSource!.suggestionId!"
        :searchable="false"
        :pagination="false"
        :column-settings="false"
      >
        <template #cell-skuCode="{ row }">{{ skuLabel(row.skuCode) }} · {{ row.uomCode }}</template>
        <template #cell-sources="{ row }">
          <details>
            <summary class="cursor-pointer">
              {{ label(row.reason) }} · 建议量 {{ row.suggestionSource?.quantity ?? '—' }}
              {{ row.uomCode }}
            </summary>
            <div class="grid gap-1 whitespace-normal py-2">
              <p v-for="(source, i) in row.demandSources" :key="i">
                {{ source.sourceReference }} · 行 {{ source.sourceLineReference ?? '—' }} · 毛需求
                {{ source.grossDemandQuantity ?? '—' }} · 交期 {{ source.dueDate ?? '—' }}
              </p>
              <p>{{ row.rawNetRequirementSource?.formula ?? '原始净算说明缺失' }}</p>
              <p>
                原始说明中的净需求量
                {{ row.rawNetRequirementSource?.netRequirementQuantity ?? '—' }}（不累计）
              </p>
              <NvButton
                v-if="row.suggestionSource?.suggestionId && row.runId"
                size="sm"
                variant="ghost"
                @click="emit('locateSuggestion', row.suggestionSource.suggestionId, row.runId)"
                >定位计划建议</NvButton
              >
            </div>
          </details>
        </template>
      </NvDataTable>
    </div>
  </section>
</template>
