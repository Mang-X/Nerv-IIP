<script setup lang="ts">
import EngineeringWorkOrderImpacts from '@/components/mes/EngineeringWorkOrderImpacts.vue'
import WorkOrderSourceFacts from '@/components/mes/WorkOrderSourceFacts.vue'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'
import SingleOrderSchedulingDialog from '@/components/scheduling/SingleOrderSchedulingDialog.vue'
import { describeMesReadinessReasons } from '@/composables/mes'
import WorkOrderTransformationDialog from '@/components/mes/WorkOrderTransformationDialog.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvMetricCard, NvPageHeader, NvStatusBadge } from '@nerv-iip/ui'
import { RouterLink } from 'vue-router'
import WorkOrderLifecycleActionBar from './components/WorkOrderLifecycleActionBar.vue'
import WorkOrderQualityHolds from './components/WorkOrderQualityHolds.vue'
import WorkOrderOperationsSection from './components/WorkOrderOperationsSection.vue'
import WorkOrderKittingMaterialsSection from './components/WorkOrderKittingMaterialsSection.vue'
import WorkOrderMaterialIssueSection from './components/WorkOrderMaterialIssueSection.vue'
import WorkOrderMaterialIssueDialog from './dialogs/WorkOrderMaterialIssueDialog.vue'
import WorkOrderMaterialReceiptDialog from './dialogs/WorkOrderMaterialReceiptDialog.vue'
import WorkOrderMaterialReturnDialog from './dialogs/WorkOrderMaterialReturnDialog.vue'
import WorkOrderCancelDialog from './dialogs/WorkOrderCancelDialog.vue'
import WorkOrderCloseDialog from './dialogs/WorkOrderCloseDialog.vue'
import WorkOrderHoldDialog from './dialogs/WorkOrderHoldDialog.vue'
import WorkOrderEngineeringDecisionDialog from './dialogs/WorkOrderEngineeringDecisionDialog.vue'
import { useWorkOrderDetailPage } from './useWorkOrderDetailPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单详情',
    requiredPermissions: ['business.mes.work-orders.read'],
  },
})

const {
  CANCEL_REASONS,
  ENGINEERING_CHANGE_NUMBER_MAX_LENGTH,
  ENGINEERING_CHANGE_REASON_MAX_LENGTH,
  HOLD_REASON_MAX_LENGTH,
  REASON_MAX_LENGTH,
  activeQualityHoldCount,
  blockingReasonDisplays,
  blockingReasons,
  canCancel,
  canClose,
  canHold,
  canManageMaterials,
  canManageQualityHold,
  canReadInspectionRecords,
  canReadInventory,
  canReadQualityHold,
  canReadReceipts,
  canReadReports,
  canReceive,
  canRecordEngineeringDecision,
  canReturn,
  canSubmitCancel,
  canSubmitIssue,
  canSubmitReceipt,
  canTransformWorkOrder,
  cancelContextItems,
  cancelDisabledReason,
  cancelForm,
  cancelOpen,
  cancelPreviewError,
  cancelPreviewPending,
  cancelWorkOrderPending,
  cancellableReceiptCount,
  cancellableTaskCount,
  closeOpen,
  closeWorkOrderPending,
  completedTaskCount,
  confirmLineSideReceiptPending,
  createMaterialIssueRequestPending,
  decisionFieldErrors,
  decisionForm,
  decisionOpen,
  decisionShowErrors,
  decisionValidationSummary,
  engineeringImpactsRefreshVersion,
  detail,
  detailError,
  detailErrorMessage,
  detailPending,
  filters,
  finalReasonLength,
  formatDateTime,
  formatQuantity,
  formatStatus,
  hasCompensation,
  holdOpen,
  holdReason,
  holdReasonError,
  holdShowErrors,
  holdWorkOrderPending,
  issueForm,
  issueOpen,
  issueUomBlockReason,
  issueUomCode,
  lineSideReturnRows,
  materialColumns,
  materialConclusion,
  materialIssueColumns,
  materialIssueReadForbidden,
  materialIssueRequestsError,
  materialIssueRequestsErrorMessage,
  materialIssueRequestsPending,
  materialIssueRows,
  materialReadForbidden,
  materialReadPermissionMessage,
  materialReadStateLabel,
  materialReadiness,
  materialReadinessError,
  materialReadinessErrorMessage,
  materialReadinessPending,
  materialRowStatus,
  materialRows,
  materialShortageCount,
  materialTone,
  openCancelDialog,
  openDecisionDialog,
  openHoldDialog,
  openIssueDialog,
  openReceiveDialog,
  openReturnDialog,
  openRoute,
  openSplitDialog,
  operationTaskOptions,
  operationTasks,
  operationTasksEmptyMessage,
  qualityHolds,
  receiveError,
  receiveForm,
  receiveRetry,
  receiveOpen,
  recordEngineeringChangeDecisionPending,
  refreshAll,
  refreshDetail,
  refreshMaterialIssueRequests,
  refreshMaterialReadiness,
  remarkMaxLength,
  requiresRemark,
  reservationRows,
  resolveSkuLabel,
  retryCancelPreview,
  retrySplitReadback,
  returnForm,
  returnFormMaxQuantity,
  returnLineSideMaterialPending,
  returnOpen,
  returnableQuantityOf,
  route,
  scheduleDisabledReason,
  scheduleOpen,
  showReasonLengthHint,
  skuLabel,
  sourceSuggestionRoute,
  splitDialogOpen,
  splitError,
  splitIdempotencyKey,
  splitPending,
  splitResult,
  splitSource,
  splitState,
  statusLabel,
  submitCancel,
  submitClose,
  submitDecision,
  submitHold,
  submitIssue,
  submitReceipt,
  submitReturn,
  submitSplit,
  taskColumns,
  taskProgress,
  traceReturn,
  traceSourceMessage,
  unreturnableNoLotRows,
  workOrderLabel,
  workOrderManageScopeReady,
  workOrderReadScope,
  workOrderReadScopeMessage,
  workOrderStatusPill,
  workOrderTone,
} = useWorkOrderDetailPage()
</script>

<template>
  <BusinessLayout class="max-sm:overflow-x-clip">
    <NvPageHeader
      :title="`工单 ${workOrderLabel}`"
      :breadcrumbs="[{ label: '制造执行' }, { label: '工单与派工' }]"
    >
      <template #actions>
        <NvButton v-if="traceReturn" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="traceReturn">返回追溯</RouterLink>
        </NvButton>
        <NvButton v-if="sourceSuggestionRoute" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="sourceSuggestionRoute">返回来源建议</RouterLink>
        </NvButton>
        <WorkOrderLifecycleActionBar
          v-model:close-open="closeOpen"
          v-model:schedule-open="scheduleOpen"
          :can-cancel="canCancel"
          :can-close="canClose"
          :can-hold="canHold"
          :can-read-inspection-records="canReadInspectionRecords"
          :can-read-receipts="canReadReceipts"
          :can-read-reports="canReadReports"
          :can-record-engineering-decision="canRecordEngineeringDecision"
          :can-transform-work-order="canTransformWorkOrder"
          :cancel-disabled-reason="cancelDisabledReason"
          :detail="detail"
          :detail-error="detailError"
          :detail-pending="detailPending"
          :material-readiness-pending="materialReadinessPending"
          :open-cancel-dialog="openCancelDialog"
          :open-decision-dialog="openDecisionDialog"
          :open-hold-dialog="openHoldDialog"
          :open-route="openRoute"
          :open-split-dialog="openSplitDialog"
          :refresh-all="refreshAll"
          :schedule-disabled-reason="scheduleDisabledReason"
          :split-pending="splitPending"
          :work-order-manage-scope-ready="workOrderManageScopeReady"
        />
      </template>
    </NvPageHeader>

    <p
      v-if="traceSourceMessage"
      role="alert"
      data-testid="trace-source-message"
      class="text-sm text-destructive"
    >
      {{ traceSourceMessage }}
    </p>

    <!-- v-if 按需挂载：关闭时不持有候选工单查询，也不残留上一次的窗口输入。 -->
    <SingleOrderSchedulingDialog
      v-if="scheduleOpen"
      v-model:open="scheduleOpen"
      :work-order-id="detail?.workOrderId ?? filters.workOrderId"
      :context-label="`MES 工单 ${workOrderLabel}`"
    />

    <WorkOrderTransformationDialog
      v-model:open="splitDialogOpen"
      mode="split"
      :state="splitState"
      :pending="splitPending"
      :source="splitSource"
      :result="splitResult"
      :error-message="splitError"
      :idempotency-key="splitIdempotencyKey"
      @submit="submitSplit"
      @retry-readback="retrySplitReadback"
    />

    <!-- 作业范围未就绪：说明缺什么，并直接给选择入口，不让整页只剩一句拒载（#1288）。 -->
    <div
      v-if="workOrderReadScopeMessage"
      class="flex flex-wrap items-center gap-3 rounded-md border border-destructive/30 bg-destructive/[0.04] p-3"
      role="alert"
      data-testid="work-order-read-scope-message"
    >
      <p class="text-sm text-destructive">{{ workOrderReadScopeMessage }}</p>
      <MesWorkScopeSelect permission-code="business.mes.work-orders.read" />
    </div>

    <WorkOrderSourceFacts
      v-if="detail"
      :source="detail.sourcePlanReference"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
    />

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <NvMetricCard
        variant="alert"
        label="工单状态"
        :value="formatStatus(detail?.status)"
        :tone="workOrderTone"
        :status="workOrderStatusPill"
        :foot-start="`生产物料：${skuLabel}`"
      />
      <NvMetricCard label="计划数量" :value="formatQuantity(detail?.quantity)" />
      <NvMetricCard
        variant="target"
        label="工序完成"
        :value="completedTaskCount"
        unit="道"
        :progress="taskProgress"
        :target-label="`共 ${operationTasks.length} 道`"
      />
      <!-- 「已齐套」同样是安全结论：齐套读面没取到时只说未取得，不反显齐套假文案（#1288）。 -->
      <NvMetricCard
        variant="alert"
        label="用料齐套"
        :value="
          materialConclusion
            ? formatStatus(materialConclusion.readinessStatus)
            : materialReadStateLabel
        "
        :action="
          materialReadinessError && !materialReadForbidden ? { label: '重试更新齐套' } : undefined
        "
        @action="refreshMaterialReadiness"
        :tone="materialTone"
        :status="
          materialConclusion
            ? materialConclusion.readinessStatus === 'Ready'
              ? { label: '已齐套', tone: 'success' }
              : {
                  label: materialShortageCount > 0 ? `${materialShortageCount} 项缺料` : '阻塞',
                  tone: 'danger',
                }
            : { label: materialReadStateLabel, tone: 'neutral' }
        "
        :foot-start="
          materialConclusion
            ? materialConclusion.readinessStatus === 'Ready'
              ? '用料已备齐，可按工序顺序开工。'
              : materialShortageCount > 0
                ? '缺料项需先发起领料，物料到线边后确认收料，否则无法开工。'
                : describeMesReadinessReasons(materialConclusion.blockingReasons)
                    .map((reason) => reason.detail || reason.label)
                    .join('；')
            : materialReadForbidden
              ? materialReadPermissionMessage
              : materialReadinessError
                ? '读取未完成，请重试更新齐套，无需重复收料。'
                : materialReadinessPending
                  ? '正在等待收料过账与最新齐套结果。'
                  : '尚未取得用料齐套结论，可在下方「用料齐套」表查看读取状态并重试。'
        "
      />
    </div>

    <div v-if="blockingReasons.length" class="rounded-lg border bg-background p-4">
      <h2 class="text-sm font-semibold text-foreground">开工阻塞</h2>
      <div class="mt-3 grid gap-2">
        <div
          v-for="reason in blockingReasonDisplays"
          :key="reason.code"
          class="rounded-md border border-warning/30 bg-warning/10 p-3"
        >
          <NvStatusBadge :label="reason.label" tone="warning" />
          <p class="mt-2 text-sm text-muted-foreground">{{ reason.nextStep }}</p>
        </div>
      </div>
    </div>

    <EngineeringWorkOrderImpacts
      v-if="detail?.workOrderId && workOrderReadScope"
      :work-order-id="detail.workOrderId"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
      :scope="workOrderReadScope"
      :refresh-version="engineeringImpactsRefreshVersion"
    />

    <WorkOrderQualityHolds
      :active-quality-hold-count="activeQualityHoldCount"
      :can-manage-quality-hold="canManageQualityHold"
      :can-read-inspection-records="canReadInspectionRecords"
      :can-read-quality-hold="canReadQualityHold"
      :filters="filters"
      :quality-holds="qualityHolds"
      :refresh-detail="refreshDetail"
    />

    <WorkOrderOperationsSection
      :detail-error="detailError"
      :detail-error-message="detailErrorMessage"
      :detail-pending="detailPending"
      :format-date-time="formatDateTime"
      :operation-tasks="operationTasks"
      :operation-tasks-empty-message="operationTasksEmptyMessage"
      :refresh-detail="refreshDetail"
      :route="route"
      :status-label="statusLabel"
      :task-columns="taskColumns"
    />

    <WorkOrderKittingMaterialsSection
      :can-manage-materials="canManageMaterials"
      :can-read-inventory="canReadInventory"
      :format-quantity="formatQuantity"
      :material-columns="materialColumns"
      :material-read-forbidden="materialReadForbidden"
      :material-read-permission-message="materialReadPermissionMessage"
      :material-readiness="materialReadiness"
      :material-readiness-error="materialReadinessError"
      :material-readiness-error-message="materialReadinessErrorMessage"
      :material-readiness-pending="materialReadinessPending"
      :material-row-status="materialRowStatus"
      :material-rows="materialRows"
      :material-shortage-count="materialShortageCount"
      :open-issue-dialog="openIssueDialog"
      :refresh-material-readiness="refreshMaterialReadiness"
    />

    <WorkOrderMaterialIssueSection
      :can-receive="canReceive"
      :can-return="canReturn"
      :format-quantity="formatQuantity"
      :material-issue-columns="materialIssueColumns"
      :material-issue-read-forbidden="materialIssueReadForbidden"
      :material-issue-requests-error="materialIssueRequestsError"
      :material-issue-requests-error-message="materialIssueRequestsErrorMessage"
      :material-issue-requests-pending="materialIssueRequestsPending"
      :material-issue-rows="materialIssueRows"
      :material-read-permission-message="materialReadPermissionMessage"
      :open-receive-dialog="openReceiveDialog"
      :open-return-dialog="openReturnDialog"
      :refresh-material-issue-requests="refreshMaterialIssueRequests"
      :status-label="statusLabel"
    />

    <WorkOrderMaterialIssueDialog
      v-model:issue-open="issueOpen"
      :can-submit-issue="canSubmitIssue"
      :create-material-issue-request-pending="createMaterialIssueRequestPending"
      :issue-form="issueForm"
      :issue-uom-block-reason="issueUomBlockReason"
      :issue-uom-code="issueUomCode"
      :operation-task-options="operationTaskOptions"
      :submit-issue="submitIssue"
      :work-order-label="workOrderLabel"
    />

    <WorkOrderMaterialReceiptDialog
      v-model:receive-open="receiveOpen"
      :can-submit-receipt="canSubmitReceipt"
      :confirm-line-side-receipt-pending="confirmLineSideReceiptPending"
      :receive-error="receiveError"
      :receive-form="receiveForm"
      :receive-retry="receiveRetry"
      :submit-receipt="submitReceipt"
    />

    <WorkOrderMaterialReturnDialog
      v-model:return-open="returnOpen"
      :return-form="returnForm"
      :return-form-max-quantity="returnFormMaxQuantity"
      :return-line-side-material-pending="returnLineSideMaterialPending"
      :submit-return="submitReturn"
    />

    <WorkOrderCancelDialog
      v-model:cancel-open="cancelOpen"
      :cancel-reasons="CANCEL_REASONS"
      :reason-max-length="REASON_MAX_LENGTH"
      :can-submit-cancel="canSubmitCancel"
      :cancel-context-items="cancelContextItems"
      :cancel-form="cancelForm"
      :cancel-preview-error="cancelPreviewError"
      :cancel-preview-pending="cancelPreviewPending"
      :cancel-work-order-pending="cancelWorkOrderPending"
      :cancellable-receipt-count="cancellableReceiptCount"
      :cancellable-task-count="cancellableTaskCount"
      :final-reason-length="finalReasonLength"
      :format-quantity="formatQuantity"
      :has-compensation="hasCompensation"
      :line-side-return-rows="lineSideReturnRows"
      :remark-max-length="remarkMaxLength"
      :requires-remark="requiresRemark"
      :reservation-rows="reservationRows"
      :resolve-sku-label="resolveSkuLabel"
      :retry-cancel-preview="retryCancelPreview"
      :returnable-quantity-of="returnableQuantityOf"
      :show-reason-length-hint="showReasonLengthHint"
      :submit-cancel="submitCancel"
      :unreturnable-no-lot-rows="unreturnableNoLotRows"
      :work-order-label="workOrderLabel"
    />

    <WorkOrderCloseDialog
      v-model:close-open="closeOpen"
      :close-work-order-pending="closeWorkOrderPending"
      :detail="detail"
      :format-status="formatStatus"
      :submit-close="submitClose"
      :work-order-label="workOrderLabel"
    />

    <WorkOrderHoldDialog
      v-model:hold-open="holdOpen"
      v-model:hold-reason="holdReason"
      :hold-reason-max-length="HOLD_REASON_MAX_LENGTH"
      :hold-reason-error="holdReasonError"
      :hold-show-errors="holdShowErrors"
      :hold-work-order-pending="holdWorkOrderPending"
      :submit-hold="submitHold"
      :work-order-label="workOrderLabel"
    />

    <WorkOrderEngineeringDecisionDialog
      v-model:decision-open="decisionOpen"
      :engineering-change-number-max-length="ENGINEERING_CHANGE_NUMBER_MAX_LENGTH"
      :engineering-change-reason-max-length="ENGINEERING_CHANGE_REASON_MAX_LENGTH"
      :decision-field-errors="decisionFieldErrors"
      :decision-form="decisionForm"
      :decision-show-errors="decisionShowErrors"
      :decision-validation-summary="decisionValidationSummary"
      :record-engineering-change-decision-pending="recordEngineeringChangeDecisionPending"
      :submit-decision="submitDecision"
      :work-order-label="workOrderLabel"
    />
  </BusinessLayout>
</template>
