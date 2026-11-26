<script setup lang="ts">
import SchedulingHorizonFields from '@/components/scheduling/SchedulingHorizonFields.vue'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'
import SchedulingOrderPool from '@/components/scheduling/SchedulingOrderPool.vue'
import SchedulingDraftBoard from '@/components/scheduling/SchedulingDraftBoard.vue'
import ScheduleRevisionReview from '@/components/scheduling/ScheduleRevisionReview.vue'
import SchedulingDowntimeBanner from '@/components/scheduling/SchedulingDowntimeBanner.vue'
import SchedulingCandidatePanel from '@/components/scheduling/SchedulingCandidatePanel.vue'
import SchedulingRushInsertionResult from '@/components/scheduling/SchedulingRushInsertionResult.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import {
  NvButton,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
  NvTabsContent,
} from '@nerv-iip/ui'
import { SendIcon } from '@lucide/vue'
import { ref } from 'vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useSchedulingPageController } from './useSchedulingPageController'

type View = ShallowUnwrapRef<ReturnType<typeof useSchedulingPageController>>

defineProps<{
  attachCandidatePanel: View['attachCandidatePanel']
  canManage: View['canManage']
  candidateDraftBlockedReason: View['candidateDraftBlockedReason']
  downtime: View['downtime']
  downtimeCandidateBlockedReason: View['downtimeCandidateBlockedReason']
  downtimeDisplayModel: View['downtimeDisplayModel']
  draft: View['draft']
  draftDisplayModel: View['draftDisplayModel']
  draftInvalidationReason: View['draftInvalidationReason']
  draftReady: View['draftReady']
  draftSaveStatus: View['draftSaveStatus']
  factoryTimeContext: View['factoryTimeContext']
  firstPlanProgress: View['firstPlanProgress']
  generateBlockedReason: View['generateBlockedReason']
  generateDisabledReason: View['generateDisabledReason']
  generateWorkbenchPlan: View['generateWorkbenchPlan']
  includeWorkbenchOrders: View['includeWorkbenchOrders']
  insertion: View['insertion']
  localCandidates: View['localCandidates']
  onLockedDragAttempt: View['onLockedDragAttempt']
  permissionCodes: View['permissionCodes']
  persistOperationOverride: View['persistOperationOverride']
  persistedDraftPlan: View['persistedDraftPlan']
  persistedOperationKeys: View['persistedOperationKeys']
  persistence: View['persistence']
  previewDowntimeCandidates: View['previewDowntimeCandidates']
  publishCandidateBlockedReason: View['publishCandidateBlockedReason']
  publishCandidateDisabledReason: View['publishCandidateDisabledReason']
  repreviewBlockedReason: View['repreviewBlockedReason']
  repreviewDisabledReason: View['repreviewDisabledReason']
  repreviewLockedDraft: View['repreviewLockedDraft']
  requestPublishCandidate: View['requestPublishCandidate']
  revisionBasePlan: View['revisionBasePlan']
  revisionResult: View['revisionResult']
  saveWorkbenchOrderPriority: View['saveWorkbenchOrderPriority']
  selectInsertionCandidate: View['selectInsertionCandidate']
  selectedInsertionId: View['selectedInsertionId']
  upsertOperationOverridePending: View['upsertOperationOverridePending']
  workbench: View['workbench']
}>()

const clearDraftConfirmOpen = defineModel<View['clearDraftConfirmOpen']>('clearDraftConfirmOpen', {
  required: true,
})

const horizonInput = defineModel<View['horizonInput']>('horizonInput', { required: true })
</script>

<template>
  <NvTabsContent value="workbench" class="grid gap-4">
    <div class="grid gap-4 rounded-lg border bg-card p-4">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p class="font-semibold">批量待排 → 编辑锁定 → 重预览 → 对比发布</p>
          <p class="text-sm text-muted-foreground">
            已选择 {{ draft.includedOrders.value.length }} 个工单，锁定
            {{ draft.lockedAssignments.value.length }} 道工序。
          </p>
        </div>
        <div class="flex flex-wrap gap-2">
          <NvButton
            size="sm"
            variant="ghost"
            type="button"
            :disabled="!draftReady || workbench.generatePending.value || !draft.canUndo.value"
            :title="draft.canUndo.value ? '撤销上一步草案修改' : '没有可撤销的草案修改'"
            @click="draft.undo"
            >撤销</NvButton
          >
          <NvButton
            size="sm"
            variant="ghost"
            type="button"
            :disabled="!draftReady || workbench.generatePending.value || !draft.canRedo.value"
            :title="draft.canRedo.value ? '重做刚撤销的草案修改' : '没有可重做的草案修改'"
            @click="draft.redo"
            >重做</NvButton
          >
          <NvButton
            size="sm"
            variant="outline"
            type="button"
            :disabled="Boolean(generateBlockedReason)"
            :title="generateDisabledReason"
            @click="generateWorkbenchPlan"
          >
            <NvSpinner v-if="workbench.generatePending.value" aria-hidden="true" />生成首版
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            :disabled="Boolean(publishCandidateBlockedReason)"
            :title="publishCandidateDisabledReason"
            @click="requestPublishCandidate"
          >
            <SendIcon aria-hidden="true" />发布新版
          </NvButton>
        </div>
      </div>

      <div class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
        <span role="status" aria-live="polite">{{ draftSaveStatus }}</span>
        <NvSelect
          v-if="persistence.savedDrafts.value.length > 0"
          :model-value="draft.model.value?.meta.planId ?? ''"
          :disabled="persistence.busy.value || workbench.generatePending.value"
          @update:model-value="(value) => persistence.select(String(value))"
        >
          <NvSelectTrigger class="w-64" aria-label="已保存草稿方案"
            ><NvSelectValue placeholder="恢复已保存草稿"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="saved in persistence.savedDrafts.value"
              :key="saved.planId"
              :value="saved.planId!"
              >{{ saved.planId }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvButton
          v-if="persistence.status.value === 'error'"
          size="sm"
          variant="outline"
          :disabled="workbench.generatePending.value"
          @click="persistence.retry"
          >重试草稿操作</NvButton
        >
        <NvButton
          size="sm"
          variant="ghost"
          :disabled="
            !canManage ||
            persistence.busy.value ||
            workbench.generatePending.value ||
            !draft.model.value
          "
          title="清空当前方案的个人草稿，方案和其它草稿仍保留"
          @click="clearDraftConfirmOpen = true"
          >清空草稿</NvButton
        >
      </div>

      <!-- 排程窗口由排产员指定（MAN-694 / #1262）；「生成首版」按这个窗口求解。 -->
      <SchedulingHorizonFields
        v-model="horizonInput"
        id-prefix="workbench-horizon"
        :disabled="!canManage || !draftReady || workbench.generatePending.value"
      />
    </div>

    <p
      v-if="draft.model.value && publishCandidateBlockedReason"
      role="status"
      class="text-sm text-warning-strong"
    >
      发布受阻 · {{ publishCandidateBlockedReason }}
    </p>
    <p
      v-if="!canManage"
      class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
      role="status"
    >
      当前账号只有读取权限，可查看历史方案但不能编辑或生成新版本。
    </p>
    <p
      v-if="draftInvalidationReason"
      class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
      role="status"
    >
      {{ draftInvalidationReason }}
    </p>
    <p v-if="firstPlanProgress" role="status" class="text-sm text-muted-foreground">
      {{ firstPlanProgress }}
    </p>
    <details :open="!draft.model.value" class="rounded-lg border bg-card p-3">
      <summary class="cursor-pointer text-sm font-medium">
        待排工单 · 已选 {{ draft.includedOrders.value.length }} 个（展开调整）
      </summary>
      <SchedulingOrderPool
        :candidates="workbench.schedulableCandidates.value"
        :draft-orders="draft.orders.value"
        :loading="workbench.candidatesPending.value"
        :error="workbench.candidatesError.value"
        :scope-ready="workbench.candidatesScopeReady.value"
        :scope-message="workbench.candidatesScopeMessage.value"
        :read-only="!canManage || !draftReady || workbench.generatePending.value"
        @include="includeWorkbenchOrders"
        :can-edit-priority="
          permissionCodes.includes(P.mesWorkOrdersManage) && workbench.priorityScopeReady.value
        "
        :save-order="saveWorkbenchOrderPriority"
        @retry="workbench.refreshCandidates"
      >
        <template #scope>
          <MesWorkScopeSelect permission-code="business.mes.work-orders.read" />
        </template>
      </SchedulingOrderPool>
    </details>
    <SchedulingDowntimeBanner
      :impact="downtime.impact.value"
      :model="draftDisplayModel"
      :now="downtime.now.value"
      :blocked-reason="downtimeCandidateBlockedReason"
      :pending="localCandidates.pending.value"
      :has-candidates="Boolean(localCandidates.candidates.value)"
      @preview="previewDowntimeCandidates"
    />
    <SchedulingCandidatePanel
      :ref="attachCandidatePanel"
      :candidates="localCandidates.candidates.value"
      :baseline-plan-id="persistedDraftPlan?.planId"
      :can-manage="canManage && draftReady && !workbench.generatePending.value"
      :pending="localCandidates.pending.value"
      :blocked-reason="candidateDraftBlockedReason"
      @preview="localCandidates.preview"
      @select="localCandidates.select"
    />
    <SchedulingRushInsertionResult
      :job="insertion.task.job.value"
      :message="insertion.message.value"
      :pending="insertion.task.pending.value"
      :failed="Boolean(insertion.task.error.value)"
      :saving="workbench.revisionPending.value"
      :save-error="insertion.saveError.value"
      :selected="
        Boolean(
          selectedInsertionId &&
          selectedInsertionId === insertion.task.result.value?.candidatePlanId,
        )
      "
      @retry="insertion.retry"
      @select="selectInsertionCandidate"
    />
    <section
      aria-label="草稿编辑操作"
      class="sticky top-14 z-20 flex flex-wrap items-center justify-between gap-3 rounded-md border bg-background p-3 shadow-sm"
    >
      <div
        v-if="draft.modifiedUnlockedTaskIds.value.length > 0"
        class="flex min-w-0 flex-1 flex-wrap items-center gap-3 text-sm"
        role="status"
      >
        <span class="text-muted-foreground">
          {{ draft.modifiedUnlockedTaskIds.value.length }}
          道人工修改尚未锁定；重预览前需锁定，避免修改被候选方案覆盖。
        </span>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="!canManage || !draftReady || workbench.generatePending.value"
          :title="
            canManage
              ? '把这些人工修改锁定为约束，重预览时不会被覆盖'
              : '当前账号没有排产管理权限，不能锁定修改'
          "
          @click="draft.lockModifiedTasks"
          >锁定全部修改</NvButton
        >
      </div>
      <p
        v-if="draft.modifiedUnlockedTaskIds.value.length === 0"
        class="text-sm text-muted-foreground"
      >
        本次草案 · 已选 {{ draft.includedOrders.value.length }} 个工单 · 已锁定
        {{ draft.lockedAssignments.value.length }} 道工序；重预览后查看变化再发布。
      </p>
      <NvButton
        size="sm"
        variant="default"
        type="button"
        :disabled="Boolean(repreviewBlockedReason)"
        :title="repreviewDisabledReason"
        @click="repreviewLockedDraft"
      >
        <NvSpinner v-if="workbench.revisionPending.value" aria-hidden="true" />锁定重预览
      </NvButton>
    </section>
    <ScheduleRevisionReview :revision="revisionResult" :base-plan="revisionBasePlan" />
    <SchedulingDraftBoard
      :time-context="factoryTimeContext.items.value"
      :model="downtimeDisplayModel"
      :feedback="draft.feedback.value"
      :material-shortage-summary="persistedDraftPlan?.materialShortageSummary"
      :pending-operations="draft.pendingOperations.value"
      :read-only="!canManage || !draftReady || workbench.generatePending.value"
      :persisted-operation-keys="persistedOperationKeys"
      :persist-pending="upsertOperationOverridePending"
      @persist-override="persistOperationOverride"
      @move="draft.moveTask"
      @update="draft.updateTask"
      @lock="draft.setLocked"
      @locked-attempt="onLockedDragAttempt"
      @move-to-pending="draft.moveTaskToPending"
      @restore-pending="draft.restorePendingTask"
    />
  </NvTabsContent>
</template>
