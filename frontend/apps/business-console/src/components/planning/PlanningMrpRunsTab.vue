<script setup lang="ts">
import {
  suggestionsPerDemand,
  runHorizonLabel,
  peggingTypeLabel,
  isComponentRow,
  planningStatus,
  sourceTypeLabel,
  formatDate,
  inputDegradationLabel,
  inputSourcesLabel,
  inputCoverageLabel,
} from './planningFormatters'
import type { BusinessConsoleMrpPeggingItem } from '@nerv-iip/api-client'
import PlanningRunSuggestionChart from '@/components/planning/PlanningRunSuggestionChart.vue'
import { inlineErrorMessage } from '@/utils/notify'
import { NvButton, NvDataTable, NvStatusBadge, NvTabsContent } from '@nerv-iip/ui'
import { CornerDownRightIcon, ExternalLinkIcon, NetworkIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

type View = ShallowUnwrapRef<ReturnType<typeof usePlanningWorkbenchController>>

defineProps<{
  canReadMrp: View['canReadMrp']
  focusSuggestion: View['focusSuggestion']
  focusedSuggestionId: View['focusedSuggestionId']
  hasScope: View['hasScope']
  mrpRuns: View['mrpRuns']
  mrpRunsError: View['mrpRunsError']
  mrpRunsPending: View['mrpRunsPending']
  openBomContext: View['openBomContext']
  openPeggingSource: View['openPeggingSource']
  pegging: View['pegging']
  peggingColumns: View['peggingColumns']
  peggingPending: View['peggingPending']
  runColumns: View['runColumns']
  runSelection: View['runSelection']
  selectedRun: View['selectedRun']
  skuLabel: View['skuLabel']
  suggestions: View['suggestions']
  suggestionsPending: View['suggestionsPending']
}>()
</script>

<template>
  <NvTabsContent v-if="canReadMrp" value="runs" class="grid min-w-0 grid-cols-1 gap-4">
    <NvDataTable
      :columns="runColumns"
      :rows="mrpRuns"
      row-key="runId"
      :loading="mrpRunsPending"
      :error="mrpRunsError"
      :error-message="inlineErrorMessage(mrpRunsError, '读取 MRP 运行失败。')"
      :awaiting-scope="!hasScope"
      awaiting-scope-message="请先选择组织与环境。"
      :searchable="false"
      :column-settings="false"
      empty-message="尚未运行 MRP。"
    >
      <template #cell-horizon="{ row }"
        >{{ formatDate(row.horizonStart) }} ~ {{ formatDate(row.horizonEnd) }}</template
      >
      <template #cell-status="{ row }">
        <div class="flex flex-col gap-0.5">
          <NvStatusBadge
            :label="planningStatus(row.status).label"
            :tone="planningStatus(row.status).tone"
          />
          <span
            v-if="row.failureReason"
            class="max-w-48 truncate text-xs text-muted-foreground"
            :title="row.failureReason"
            >{{ row.failureReason }}</span
          >
        </div>
      </template>
      <template #cell-inputSources="{ row }">
        <span class="block truncate" :title="inputSourcesLabel(row.inputSources)">
          {{ inputSourcesLabel(row.inputSources) }}
        </span>
      </template>
      <template #cell-inputCoverage="{ row }">{{ inputCoverageLabel(row) }}</template>
      <template #cell-demandCount="{ row }"
        ><span class="tabular-nums">{{ row.demandCount ?? 0 }}</span></template
      >
      <template #cell-inputDegradationSources="{ row }">
        <NvStatusBadge
          :label="inputDegradationLabel(row.inputDegradationSources)"
          :tone="row.hasInputDegradation ? 'warning' : 'success'"
        />
      </template>
      <template #cell-suggestionCount="{ row }"
        ><span class="tabular-nums">{{ row.suggestionCount ?? 0 }}</span></template
      >
      <template #cell-suggestionsPerDemand="{ row }"
        ><span class="tabular-nums font-medium">{{ suggestionsPerDemand(row) }}</span></template
      >
      <template #cell-availabilityCount="{ row }"
        ><span class="tabular-nums">{{ row.availabilityCount ?? 0 }}</span></template
      >
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          type="button"
          :variant="runSelection.runId === row.runId ? 'secondary' : 'ghost'"
          @click="runSelection.runId = row.runId ?? ''"
        >
          查看追溯
        </NvButton>
      </template>
    </NvDataTable>

    <div class="grid min-w-0 grid-cols-1 gap-2">
      <div class="flex items-center gap-2">
        <span class="text-sm font-medium text-foreground">需求追溯</span>
        <span class="text-sm text-muted-foreground">{{ runHorizonLabel(selectedRun) }}</span>
        <NvStatusBadge
          v-if="selectedRun"
          :label="planningStatus(selectedRun.status).label"
          :tone="planningStatus(selectedRun.status).tone"
        />
      </div>
      <PlanningRunSuggestionChart
        :run="selectedRun"
        :suggestions="suggestions"
        :pending="suggestionsPending"
      />
      <NvDataTable
        :columns="peggingColumns"
        :rows="pegging"
        :row-class="
          (row: BusinessConsoleMrpPeggingItem) =>
            row.suggestionId === focusedSuggestionId ? 'bg-primary/10' : ''
        "
        :row-key="(r) => `${r.suggestionId}:${r.componentSkuCode}`"
        :loading="peggingPending"
        :searchable="false"
        :column-settings="false"
        empty-message="选择一条 MRP 运行查看需求与物料来源。"
      >
        <template #cell-peggingType="{ row }">
          <NvStatusBadge
            :label="peggingTypeLabel(row.peggingType).label"
            :tone="peggingTypeLabel(row.peggingType).tone"
          />
        </template>
        <template #cell-sourceType="{ row }">
          <NvStatusBadge :label="sourceTypeLabel(row.sourceType)" tone="neutral" />
        </template>
        <template #cell-demandSourceReference="{ row }">
          <div class="flex items-center justify-between gap-2">
            <span class="min-w-0 truncate">{{ row.demandSourceReference }}</span>
            <NvButton size="sm" type="button" variant="ghost" @click="openPeggingSource(row)">
              <ExternalLinkIcon aria-hidden="true" />
            </NvButton>
          </div>
        </template>
        <template #cell-suggestion="{ row }">
          <NvButton
            v-if="row.suggestionId"
            size="sm"
            type="button"
            variant="ghost"
            @click="focusSuggestion(row)"
            >定位建议</NvButton
          >
        </template>
        <template #cell-sku="{ row }">
          <div
            :class="isComponentRow(row) ? 'flex items-start gap-1.5 pl-5' : 'flex flex-col gap-0.5'"
          >
            <CornerDownRightIcon
              v-if="isComponentRow(row)"
              class="mt-0.5 size-3.5 shrink-0 text-muted-foreground"
              aria-hidden="true"
            />
            <div class="flex flex-col gap-0.5">
              <template v-if="isComponentRow(row)">
                <span>{{ skuLabel(row.componentSkuCode) }}</span>
                <span class="text-xs text-muted-foreground">
                  {{ row.componentSkuCode }} · 属 {{ skuLabel(row.parentSkuCode) }}
                </span>
              </template>
              <template v-else>
                <span class="font-medium">{{ skuLabel(row.parentSkuCode) }}</span>
                <span v-if="row.parentSkuCode" class="text-xs text-muted-foreground">{{
                  row.parentSkuCode
                }}</span>
              </template>
            </div>
          </div>
        </template>
        <template #cell-quantity="{ row }"
          ><span class="tabular-nums">{{ row.quantity ?? 0 }}</span></template
        >
        <template #cell-engineeringRef="{ row }">
          <div class="flex items-center justify-between gap-2">
            <div class="flex min-w-0 flex-col gap-0.5">
              <span>{{
                row.manufacturingBomReference ?? row.productionVersionReference ?? '—'
              }}</span>
              <span v-if="row.routingReference" class="text-xs text-muted-foreground"
                >工艺 {{ row.routingReference }}</span
              >
            </div>
            <NvButton
              v-if="row.parentSkuCode || row.componentSkuCode"
              size="sm"
              type="button"
              variant="ghost"
              @click="openBomContext(row)"
            >
              <NetworkIcon aria-hidden="true" />
              查看 BOM
            </NvButton>
          </div>
        </template>
      </NvDataTable>
    </div>
  </NvTabsContent>
</template>
