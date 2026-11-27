<script setup lang="ts">
import SchedulingDraftClearDialog from '@/components/scheduling/SchedulingDraftClearDialog.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvPageHeader,
  NvSpinner,
  NvTabs,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import SchedulingWorkbenchSection from '../components/scheduling/SchedulingWorkbenchSection.vue'
import SchedulingPlanHistorySection from '../components/scheduling/SchedulingPlanHistorySection.vue'
import SchedulingPlanGanttSection from '../components/scheduling/SchedulingPlanGanttSection.vue'
import SchedulingPlanDetailSheet from '../components/scheduling/SchedulingPlanDetailSheet.vue'
import { useSchedulingPageController } from '../components/scheduling/useSchedulingPageController'

definePage({
  meta: {
    requiresAuth: true,
    title: '排产工作台',
    requiredPermissions: ['business.scheduling.plans.read'],
  },
})

const {
  actionablePlans,
  activeView,
  assignmentTask,
  assignmentText,
  assignmentTime,
  associatedWorkOrders,
  attachCandidatePanel,
  canManage,
  canRelease,
  canRevoke,
  candidateDraftBlockedReason,
  clearDraftConfirmOpen,
  clearHistoryFilters,
  columns,
  confirmRevoke,
  csv,
  detailFeedback,
  detailOpen,
  detailSelection,
  displayMode,
  displayWorkOrders,
  downloadPlanCsv,
  downtime,
  downtimeCandidateBlockedReason,
  downtimeDisplayModel,
  draft,
  draftDisplayModel,
  draftInvalidationReason,
  draftReady,
  draftSaveStatus,
  factoryTimeContext,
  firstPlanProgress,
  generateBlockedReason,
  generateDisabledReason,
  generateWorkbenchPlan,
  hasHistoryFilters,
  historyInvalidated,
  historyStatus,
  historyStatuses,
  horizonInput,
  includeWorkbenchOrders,
  insertion,
  localCandidates,
  onLockedDragAttempt,
  openDetail,
  orderUrgencies,
  orderedAssignments,
  page,
  pageSize,
  permissionCodes,
  persistOperationOverride,
  persistedDraftPlan,
  persistedOperationKeys,
  persistence,
  planDetail,
  planDetailError,
  planDetailPending,
  planFactoryTimeContext,
  planHeaderCount,
  plansError,
  plansPending,
  plansTotal,
  previewDowntimeCandidates,
  publish,
  publishCandidate,
  publishCandidateBlockedReason,
  publishCandidateDisabledReason,
  publishConfirmOpen,
  publishTargetChanged,
  publishTargetPlanId,
  refreshPlans,
  refreshUrgency,
  releaseDisabledReason,
  releasePlanPending,
  repreviewBlockedReason,
  repreviewDisabledReason,
  repreviewLockedDraft,
  requestPublishCandidate,
  requestRevoke,
  revisionBasePlan,
  revisionResult,
  revokeConfirmOpen,
  revokePlanPending,
  revokeTargetPlanId,
  rowKey,
  saveWorkbenchOrderPriority,
  schedulingFilters,
  selectInsertionCandidate,
  selectedInsertionId,
  selectedPlanRange,
  selectedPlanSummary,
  selectedResourceCount,
  targetedAssignmentFound,
  targetedOrderReference,
  upsertOperationOverridePending,
  workbench,
} = useSchedulingPageController()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="排产工作台"
      :breadcrumbs="[{ label: '需求与计划' }]"
      :count="planHeaderCount"
    >
      <template #actions>
        <NvButton size="sm" variant="outline" type="button" @click="refreshPlans">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvTabs v-model="activeView">
      <NvTabsList>
        <NvTabsTrigger value="workbench">排程总览</NvTabsTrigger>
        <NvTabsTrigger value="table">表格</NvTabsTrigger>
        <NvTabsTrigger value="gantt">甘特图</NvTabsTrigger>
      </NvTabsList>

      <SchedulingWorkbenchSection
        v-model:clear-draft-confirm-open="clearDraftConfirmOpen"
        v-model:horizon-input="horizonInput"
        :attach-candidate-panel="attachCandidatePanel"
        :can-manage="canManage"
        :candidate-draft-blocked-reason="candidateDraftBlockedReason"
        :downtime="downtime"
        :downtime-candidate-blocked-reason="downtimeCandidateBlockedReason"
        :downtime-display-model="downtimeDisplayModel"
        :draft="draft"
        :draft-display-model="draftDisplayModel"
        :draft-invalidation-reason="draftInvalidationReason"
        :draft-ready="draftReady"
        :draft-save-status="draftSaveStatus"
        :factory-time-context="factoryTimeContext"
        :first-plan-progress="firstPlanProgress"
        :generate-blocked-reason="generateBlockedReason"
        :generate-disabled-reason="generateDisabledReason"
        :generate-workbench-plan="generateWorkbenchPlan"
        :include-workbench-orders="includeWorkbenchOrders"
        :insertion="insertion"
        :local-candidates="localCandidates"
        :on-locked-drag-attempt="onLockedDragAttempt"
        :permission-codes="permissionCodes"
        :persist-operation-override="persistOperationOverride"
        :persisted-draft-plan="persistedDraftPlan"
        :persisted-operation-keys="persistedOperationKeys"
        :persistence="persistence"
        :preview-downtime-candidates="previewDowntimeCandidates"
        :publish-candidate-blocked-reason="publishCandidateBlockedReason"
        :publish-candidate-disabled-reason="publishCandidateDisabledReason"
        :repreview-blocked-reason="repreviewBlockedReason"
        :repreview-disabled-reason="repreviewDisabledReason"
        :repreview-locked-draft="repreviewLockedDraft"
        :request-publish-candidate="requestPublishCandidate"
        :revision-base-plan="revisionBasePlan"
        :revision-result="revisionResult"
        :save-workbench-order-priority="saveWorkbenchOrderPriority"
        :select-insertion-candidate="selectInsertionCandidate"
        :selected-insertion-id="selectedInsertionId"
        :upsert-operation-override-pending="upsertOperationOverridePending"
        :workbench="workbench"
      />

      <SchedulingPlanHistorySection
        v-model:history-status="historyStatus"
        v-model:history-invalidated="historyInvalidated"
        v-model:page="page"
        v-model:page-size="pageSize"
        v-model:active-view="activeView"
        :actionable-plans="actionablePlans"
        :can-release="canRelease"
        :can-revoke="canRevoke"
        :clear-history-filters="clearHistoryFilters"
        :columns="columns"
        :has-history-filters="hasHistoryFilters"
        :history-statuses="historyStatuses"
        :open-detail="openDetail"
        :plans-error="plansError"
        :plans-pending="plansPending"
        :plans-total="plansTotal"
        :publish="publish"
        :refresh-plans="refreshPlans"
        :release-disabled-reason="releaseDisabledReason"
        :release-plan-pending="releasePlanPending"
        :request-revoke="requestRevoke"
        :revoke-plan-pending="revokePlanPending"
        :row-key="rowKey"
        :scheduling-filters="schedulingFilters"
      />

      <SchedulingPlanGanttSection
        v-model:detail-open="detailOpen"
        :actionable-plans="actionablePlans"
        :associated-work-orders="associatedWorkOrders"
        :can-revoke="canRevoke"
        :csv="csv"
        :detail-selection="detailSelection"
        :display-work-orders="displayWorkOrders"
        :download-plan-csv="downloadPlanCsv"
        :plan-detail="planDetail"
        :plan-detail-error="planDetailError"
        :plan-detail-pending="planDetailPending"
        :plan-factory-time-context="planFactoryTimeContext"
        :publish="publish"
        :release-plan-pending="releasePlanPending"
        :request-revoke="requestRevoke"
        :revoke-plan-pending="revokePlanPending"
        :row-key="rowKey"
        :selected-plan-summary="selectedPlanSummary"
      />
    </NvTabs>

    <!-- 撤销发布二次确认：说明 MES 侧后果，避免误触。
         v-if 按需挂载：关闭时完全不渲染 reka AlertDialog 树，也避免与页面测试针对
         NvSheet 的全局 DialogRoot stub 相互干扰（stub 会剥掉 AlertDialog 的注入上下文）。 -->
    <SchedulingDraftClearDialog
      v-model:open="clearDraftConfirmOpen"
      :pending="persistence.status.value === 'clearing' || workbench.generatePending.value"
      :clear="persistence.clear"
    />
    <NvAlertDialog v-if="publishConfirmOpen" v-model:open="publishConfirmOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>确认发布所选方案？</NvAlertDialogTitle>
          <NvAlertDialogDescription
            >将方案
            {{ publishTargetPlanId }} 发布给车间执行，取代当前有效方案。</NvAlertDialogDescription
          >
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <NvButton
            type="button"
            :disabled="Boolean(publishCandidateBlockedReason) || publishTargetChanged"
            @click="publishCandidate"
            >确认发布</NvButton
          >
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
    <NvAlertDialog v-if="revokeConfirmOpen" v-model:open="revokeConfirmOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>确认撤销发布该排程方案？</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            方案 {{ revokeTargetPlanId }} 撤销后将进入「已撤销」终态，MES
            侧会回流撤销由它下达的工序排程；需要重新下达时须生成并发布新方案。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框（confirm-destroy 规则 3），
               撤销失败时框早已消失、用户无从原地重试。 -->
          <NvButton
            type="button"
            variant="destructive"
            :disabled="revokePlanPending"
            @click="confirmRevoke"
          >
            <NvSpinner v-if="revokePlanPending" aria-hidden="true" />
            确认撤销
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <SchedulingPlanDetailSheet
      v-model:detail-open="detailOpen"
      v-model:display-mode="displayMode"
      :assignment-task="assignmentTask"
      :assignment-text="assignmentText"
      :assignment-time="assignmentTime"
      :detail-feedback="detailFeedback"
      :detail-selection="detailSelection"
      :order-urgencies="orderUrgencies"
      :ordered-assignments="orderedAssignments"
      :plan-detail="planDetail"
      :plan-detail-pending="planDetailPending"
      :refresh-urgency="refreshUrgency"
      :selected-plan-range="selectedPlanRange"
      :selected-resource-count="selectedResourceCount"
      :targeted-assignment-found="targetedAssignmentFound"
      :targeted-order-reference="targetedOrderReference"
    />
  </BusinessLayout>
</template>
