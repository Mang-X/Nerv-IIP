<script setup lang="ts">
import { runHorizonLabel } from './planningFormatters'
import PlanningTimePhasedPanel from '@/components/planning/PlanningTimePhasedPanel.vue'
import PlanningMaterialDeliveryPanel from '@/components/planning/PlanningMaterialDeliveryPanel.vue'
import PlanningForecastManagement from '@/components/planning/PlanningForecastManagement.vue'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvMetricCard,
  NvPageHeader,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import PlanningMrpRunDialog from './PlanningMrpRunDialog.vue'
import PlanningMpsFormDialog from './PlanningMpsFormDialog.vue'
import PlanningDemandFormDialog from './PlanningDemandFormDialog.vue'
import PlanningDemandsTab from './PlanningDemandsTab.vue'
import PlanningMpsTab from './PlanningMpsTab.vue'
import PlanningMrpRunsTab from './PlanningMrpRunsTab.vue'
import PlanningSuggestionsAndPeggingTab from './PlanningSuggestionsAndPeggingTab.vue'
import { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

const {
  acceptPlanningSuggestion,
  acceptSuggestionPending,
  acceptingSuggestionId,
  activeMrpRun,
  activeTab,
  actorName,
  canReadDeliveries,
  canReadDemands,
  canReadMps,
  canReadMrp,
  canReadWorkOrders,
  canScheduleSingleOrder,
  canSubmitDemand,
  canSubmitMps,
  canSubmitReject,
  cancelDemandPending,
  cancelTarget,
  confirmCancelDemand,
  baseUomBySku,
  coverageRunId,
  coveredSkuCodes,
  createDemandPending,
  createMpsBucketPending,
  defaultTab,
  demandCancelTarget,
  demandColumns,
  demandCoverageForRow,
  demandCoverageRate,
  demandFilterActive,
  demandForm,
  demandKeyword,
  demandOpen,
  demandSkuCodes,
  demandTypeFilter,
  demandTypeFilterOptions,
  demandTypeOptions,
  demandTypeResetNotice,
  demands,
  demandsError,
  demandsPending,
  demandsReady,
  deniedSource,
  displayMode,
  downstreamRoute,
  downstreamStatuses,
  editMpsBucket,
  errorMessage,
  filteredDemands,
  focusPegging,
  focusSuggestion,
  focusedSuggestionId,
  formatMpsQuantity,
  hasPermission,
  hasScope,
  latestCompletedRun,
  latestRun,
  latestRunKpiHint,
  latestRunKpiValue,
  mpsBuckets,
  mpsBucketsError,
  mpsBucketsPending,
  mpsColumns,
  mpsEditingId,
  mpsForm,
  mpsOpen,
  mpsReady,
  mpsSegments,
  mrpOpen,
  mrpRunInProgress,
  mrpRunLabels,
  mrpRuns,
  mrpRunsError,
  mrpRunsPending,
  mrpSuggestionSeries,
  navigateToSuggestion,
  newMpsBucket,
  openBomContext,
  openPeggingSource,
  openRejectDialog,
  openSalesOrderDemand,
  openSuggestionCount,
  orderUrgencies,
  pegging,
  peggingColumns,
  peggingPending,
  planningDataError,
  planningDataPending,
  refreshPlanning,
  refreshUrgency,
  rejectReason,
  rejectSuggestionPending,
  rejectTarget,
  releaseMps,
  releaseMpsBucketPending,
  returnLabel,
  reviewKpiHint,
  reviewMps,
  reviewMpsBucketPending,
  router,
  runChoiceLabel,
  runColumns,
  runMrpPending,
  runRequest,
  runSelection,
  runsReady,
  scheduleTarget,
  scopedSuggestions,
  selectedRun,
  siteLabel,
  siteOptions,
  skuLabel,
  sourceOrder,
  sourceUnresolved,
  staleDemandChangeCount,
  submitDemand,
  submitMpsBucket,
  submitMrpRun,
  submitRejectSuggestion,
  successorRunLabel,
  suggestionColumns,
  suggestionFilters,
  suggestionPage,
  suggestionPageRows,
  suggestionPageSize,
  suggestionRunChoice,
  suggestionScheduleHint,
  suggestionStatusOptions,
  suggestionTypeFilter,
  suggestionTypeFilterOptions,
  suggestionWorkOrderId,
  suggestions,
  suggestionsError,
  suggestionsPending,
  suggestionsReady,
  timePhasedRun,
  uomOptions,
  updateMpsBucketPending,
  visibleSuggestions,
} = usePlanningWorkbenchController()
</script>

<template>
  <NvPageHeader title="需求与计划" :breadcrumbs="[{ label: '需求与计划' }]">
    <template #actions>
      <NvButton
        size="sm"
        type="button"
        variant="outline"
        :disabled="demandsPending"
        @click="refreshPlanning"
      >
        <RefreshCwIcon aria-hidden="true" />
        刷新
      </NvButton>

      <PlanningMrpRunDialog
        v-model:mrp-open="mrpOpen"
        :active-mrp-run="activeMrpRun"
        :has-permission="hasPermission"
        :mrp-run-in-progress="mrpRunInProgress"
        :run-mrp-pending="runMrpPending"
        :run-request="runRequest"
        :submit-mrp-run="submitMrpRun"
      />

      <PlanningMpsFormDialog
        v-model:mps-open="mpsOpen"
        :can-submit-mps="canSubmitMps"
        :create-mps-bucket-pending="createMpsBucketPending"
        :has-permission="hasPermission"
        :mps-editing-id="mpsEditingId"
        :mps-form="mpsForm"
        :base-uom-by-sku="baseUomBySku"
        :new-mps-bucket="newMpsBucket"
        :site-options="siteOptions"
        :submit-mps-bucket="submitMpsBucket"
        :uom-options="uomOptions"
        :update-mps-bucket-pending="updateMpsBucketPending"
      />

      <PlanningDemandFormDialog
        v-model:demand-open="demandOpen"
        :can-submit-demand="canSubmitDemand"
        :create-demand-pending="createDemandPending"
        :demand-form="demandForm"
        :demand-type-options="demandTypeOptions"
        :has-permission="hasPermission"
        :site-options="siteOptions"
        :submit-demand="submitDemand"
        :uom-options="uomOptions"
      />
    </template>
  </NvPageHeader>

  <p
    v-if="staleDemandChangeCount > 0"
    class="rounded-md border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-foreground"
    role="alert"
  >
    MRP 结果已过期（{{ staleDemandChangeCount }} 条需求变更）
  </p>

  <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
    <NvMetricCard
      v-if="demandsReady && suggestionsReady && demandCoverageRate !== null"
      variant="target"
      label="需求物料覆盖率"
      :value="demandCoverageRate"
      unit="%"
      :progress="demandCoverageRate"
      target-label="目标 100%"
      :progress-tone="
        demandCoverageRate >= 100 ? 'success' : demandCoverageRate >= 80 ? 'warning' : 'danger'
      "
      :foot-start="
        coverageRunId ? `有供给建议的物料 ${coveredSkuCodes.size} 个` : '尚未运行 MRP，已覆盖 0 个'
      "
      :foot-end="`需求物料 ${demandSkuCodes.size} 个`"
    />
    <NvMetricCard
      v-else-if="demandsReady && suggestionsReady"
      variant="alert"
      label="需求物料覆盖率"
      value="—"
      tone="neutral"
      :status="{ label: '暂无样本', tone: 'neutral' }"
      foot-start="当前范围没有参与计算的需求物料。"
    />
    <NvMetricCard
      variant="breakdown"
      v-if="mpsReady"
      label="主计划行"
      :value="mpsBuckets.length"
      unit="行"
      :segments="mpsSegments"
    />
    <NvMetricCard
      variant="sparkline"
      v-if="runsReady"
      label="最近一次 MRP"
      :value="latestRunKpiValue"
      :series="mrpSuggestionSeries"
      :series-labels="mrpRunLabels"
      series-unit="条建议"
      :foot-start="latestRun ? runHorizonLabel(latestRun) : '尚未运行 MRP'"
      :foot-end="latestRunKpiHint"
    />
    <NvMetricCard
      variant="alert"
      v-if="suggestionsReady"
      label="待评审建议"
      :value="openSuggestionCount"
      unit="条"
      :tone="openSuggestionCount > 0 ? 'warning' : 'neutral'"
      :status="
        openSuggestionCount > 0
          ? { label: '待接受', tone: 'warning' }
          : { label: '已清空', tone: 'success' }
      "
      :foot-start="reviewKpiHint"
    />
  </div>

  <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

  <div v-if="deniedSource" role="status" class="flex flex-wrap items-center gap-2">
    {{ deniedSource }}
    <RouterLink to="/planning">{{ returnLabel }}</RouterLink>
  </div>
  <p v-else-if="!defaultTab" role="status">当前岗位没有计划数据读取权限。</p>
  <NvTabs v-if="defaultTab && !deniedSource" v-model="activeTab" class="min-w-0 max-w-full">
    <NvTabsList class="max-w-full justify-start overflow-x-auto">
      <!-- 筛选生效时页签显「筛出数/总数」，别让人以为需求池整个缩水了。 -->
      <NvTabsTrigger v-if="canReadDemands" value="demands"
        >需求池
        <span v-if="demandsReady"
          >({{
            sourceOrder
              ? filteredDemands.length
              : demandFilterActive
                ? `${filteredDemands.length}/${demands.length}`
                : demands.length
          }})</span
        ></NvTabsTrigger
      >
      <NvTabsTrigger v-if="canReadDemands" value="forecasts">预测管理</NvTabsTrigger>
      <NvTabsTrigger v-if="canReadMps" value="mps"
        >MPS 主计划 <span v-if="mpsReady">({{ mpsBuckets.length }})</span></NvTabsTrigger
      >
      <NvTabsTrigger v-if="canReadDeliveries" value="material-delivery">物料交付</NvTabsTrigger>
      <NvTabsTrigger v-if="canReadDemands && canReadMps && canReadMrp" value="phasing"
        >时段视图</NvTabsTrigger
      >
      <NvTabsTrigger v-if="canReadMrp" value="runs"
        >MRP 运行 <span v-if="runsReady">({{ mrpRuns.length }})</span></NvTabsTrigger
      >
      <NvTabsTrigger v-if="canReadMrp" value="suggestions"
        >计划建议
        <span v-if="suggestionsReady">({{ scopedSuggestions.length }})</span></NvTabsTrigger
      >
    </NvTabsList>

    <NvTabsContent
      v-if="canReadDeliveries"
      value="material-delivery"
      class="grid min-w-0 grid-cols-1 gap-3"
    >
      <PlanningMaterialDeliveryPanel
        :runs="mrpRuns"
        :sku-label="skuLabel"
        @locate-suggestion="navigateToSuggestion"
      />
    </NvTabsContent>

    <PlanningDemandsTab
      v-model:demand-keyword="demandKeyword"
      v-model:demand-type-filter="demandTypeFilter"
      v-model:display-mode="displayMode"
      v-model:demand-cancel-target="demandCancelTarget"
      :can-read-demands="canReadDemands"
      :can-read-mrp="canReadMrp"
      :demand-columns="demandColumns"
      :demand-coverage-for-row="demandCoverageForRow"
      :demand-filter-active="demandFilterActive"
      :demand-type-filter-options="demandTypeFilterOptions"
      :demand-type-reset-notice="demandTypeResetNotice"
      :demands-error="demandsError"
      :demands-pending="demandsPending"
      :filtered-demands="filteredDemands"
      :has-permission="hasPermission"
      :open-sales-order-demand="openSalesOrderDemand"
      :order-urgencies="orderUrgencies"
      :refresh-urgency="refreshUrgency"
      :run-request="runRequest"
      :site-label="siteLabel"
      :sku-label="skuLabel"
      :source-order="sourceOrder"
    />

    <NvTabsContent v-if="canReadDemands" value="forecasts">
      <PlanningForecastManagement />
    </NvTabsContent>

    <PlanningMpsTab
      :actor-name="actorName"
      :can-read-mps="canReadMps"
      :edit-mps-bucket="editMpsBucket"
      :format-mps-quantity="formatMpsQuantity"
      :has-permission="hasPermission"
      :has-scope="hasScope"
      :mps-buckets="mpsBuckets"
      :mps-buckets-error="mpsBucketsError"
      :mps-buckets-pending="mpsBucketsPending"
      :mps-columns="mpsColumns"
      :release-mps="releaseMps"
      :release-mps-bucket-pending="releaseMpsBucketPending"
      :review-mps="reviewMps"
      :review-mps-bucket-pending="reviewMpsBucketPending"
      :site-label="siteLabel"
      :sku-label="skuLabel"
    />

    <NvTabsContent
      v-if="canReadDemands && canReadMps && canReadMrp"
      value="phasing"
      class="grid min-w-0 grid-cols-1 gap-3"
    >
      <PlanningTimePhasedPanel
        :demands="demands"
        :mps-buckets="mpsBuckets"
        :suggestions="suggestions"
        :suggestion-run-id="timePhasedRun?.runId ?? ''"
        :suggestion-run-label="timePhasedRun ? runHorizonLabel(timePhasedRun) : ''"
        :pending="planningDataPending"
        :error-message="planningDataError"
        :sku-label="skuLabel"
      />
    </NvTabsContent>

    <PlanningMrpRunsTab
      :can-read-mrp="canReadMrp"
      :focus-suggestion="focusSuggestion"
      :focused-suggestion-id="focusedSuggestionId"
      :has-scope="hasScope"
      :mrp-runs="mrpRuns"
      :mrp-runs-error="mrpRunsError"
      :mrp-runs-pending="mrpRunsPending"
      :open-bom-context="openBomContext"
      :open-pegging-source="openPeggingSource"
      :pegging="pegging"
      :pegging-columns="peggingColumns"
      :pegging-pending="peggingPending"
      :run-columns="runColumns"
      :run-selection="runSelection"
      :selected-run="selectedRun"
      :sku-label="skuLabel"
      :suggestions="suggestions"
      :suggestions-pending="suggestionsPending"
    />

    <PlanningSuggestionsAndPeggingTab
      v-model:suggestion-run-choice="suggestionRunChoice"
      v-model:suggestion-page="suggestionPage"
      v-model:suggestion-page-size="suggestionPageSize"
      v-model:schedule-target="scheduleTarget"
      v-model:reject-reason="rejectReason"
      v-model:reject-target="rejectTarget"
      v-model:cancel-target="cancelTarget"
      :accept-planning-suggestion="acceptPlanningSuggestion"
      :accept-suggestion-pending="acceptSuggestionPending"
      :accepting-suggestion-id="acceptingSuggestionId"
      :can-read-mrp="canReadMrp"
      :can-read-work-orders="canReadWorkOrders"
      :can-schedule-single-order="canScheduleSingleOrder"
      :can-submit-reject="canSubmitReject"
      :downstream-route="downstreamRoute"
      :downstream-statuses="downstreamStatuses"
      :focus-pegging="focusPegging"
      :focused-suggestion-id="focusedSuggestionId"
      :has-permission="hasPermission"
      :has-scope="hasScope"
      :latest-completed-run="latestCompletedRun"
      :mrp-runs="mrpRuns"
      :mrp-runs-error="mrpRunsError"
      :mrp-runs-pending="mrpRunsPending"
      :open-reject-dialog="openRejectDialog"
      :reject-suggestion-pending="rejectSuggestionPending"
      :return-label="returnLabel"
      :router="router"
      :run-choice-label="runChoiceLabel"
      :run-request="runRequest"
      :sku-label="skuLabel"
      :source-unresolved="sourceUnresolved"
      :submit-reject-suggestion="submitRejectSuggestion"
      :successor-run-label="successorRunLabel"
      :suggestion-columns="suggestionColumns"
      :suggestion-filters="suggestionFilters"
      :suggestion-page-rows="suggestionPageRows"
      :suggestion-schedule-hint="suggestionScheduleHint"
      :suggestion-status-options="suggestionStatusOptions"
      :suggestion-type-filter="suggestionTypeFilter"
      :suggestion-type-filter-options="suggestionTypeFilterOptions"
      :suggestion-work-order-id="suggestionWorkOrderId"
      :suggestions-error="suggestionsError"
      :suggestions-pending="suggestionsPending"
      :visible-suggestions="visibleSuggestions"
    />
  </NvTabs>
  <NvDialog
    v-if="hasPermission(P.planningDemandsManage)"
    :open="!!demandCancelTarget"
    @update:open="
      (open) => {
        if (!open) demandCancelTarget = null
      }
    "
  >
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>作废计划需求</NvDialogTitle>
        <NvDialogDescription
          >确认作废 {{ demandCancelTarget?.sourceReference }}？需求将退出后续 MRP
          输入，追溯记录保留。</NvDialogDescription
        >
      </NvDialogHeader>
      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="demandCancelTarget = null">返回</NvButton>
        <NvButton type="button" :disabled="cancelDemandPending" @click="confirmCancelDemand"
          >确认作废</NvButton
        >
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
