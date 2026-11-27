<script setup lang="ts">
import { schedulingPlanStatusLabel } from '@/utils/schedulingPlanPresentation'
import SchedulingPlanGantt from '@/components/scheduling/SchedulingPlanGantt.vue'
import SchedulingDaySchedule from '@/components/scheduling/SchedulingDaySchedule.vue'
import {
  NvButton,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvTabsContent,
} from '@nerv-iip/ui'
import { Undo2Icon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useSchedulingPageController } from './useSchedulingPageController'

type View = ShallowUnwrapRef<ReturnType<typeof useSchedulingPageController>>

defineProps<{
  actionablePlans: View['actionablePlans']
  associatedWorkOrders: View['associatedWorkOrders']
  noPlans: boolean
  canReadProductionSuggestions: boolean
  canRevoke: View['canRevoke']
  csv: View['csv']
  detailSelection: View['detailSelection']
  displayWorkOrders: View['displayWorkOrders']
  downloadPlanCsv: View['downloadPlanCsv']
  planDetail: View['planDetail']
  planDetailError: View['planDetailError']
  planDetailPending: View['planDetailPending']
  planFactoryTimeContext: View['planFactoryTimeContext']
  publish: View['publish']
  releasePlanPending: View['releasePlanPending']
  requestRevoke: View['requestRevoke']
  revokePlanPending: View['revokePlanPending']
  rowKey: View['rowKey']
  selectedPlanSummary: View['selectedPlanSummary']
}>()

const emit = defineEmits<{ 'open-workbench': [] }>()

const detailOpen = defineModel<View['detailOpen']>('detailOpen', { required: true })
</script>

<template>
  <NvTabsContent value="gantt">
    <div v-if="noPlans" class="grid justify-items-start gap-3 rounded-lg border bg-card p-4">
      <h2 class="font-semibold">暂无排程方案</h2>
      <div class="flex flex-wrap gap-2">
        <NvButton type="button" variant="outline" @click="emit('open-workbench')">
          返回排程总览
        </NvButton>
        <NvButton v-if="canReadProductionSuggestions" as-child variant="outline">
          <RouterLink :to="{ path: '/planning', query: { suggestionType: 'planned-work-order' } }">
            查看生产建议
          </RouterLink>
        </NvButton>
      </div>
    </div>
    <template v-else>
      <div class="mb-4 flex flex-wrap items-center gap-3 rounded-lg border bg-card p-3">
        <label for="gantt-plan-select" class="text-sm font-medium text-foreground">排程方案</label>
        <NvSelect v-model="detailSelection.planId">
          <NvSelectTrigger id="gantt-plan-select" class="w-full sm:w-80" aria-label="排程方案">
            <NvSelectValue placeholder="选择排程方案" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="plan in actionablePlans" :key="rowKey(plan)" :value="plan.planId">
              {{ plan.planId }} · {{ schedulingPlanStatusLabel(plan.status) }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvButton
          v-if="canRevoke(planDetail)"
          size="sm"
          variant="destructive"
          type="button"
          :disabled="revokePlanPending"
          title="撤销该已发布方案，MES 侧回流撤销对应工序排程"
          @click="requestRevoke(planDetail)"
        >
          <Undo2Icon aria-hidden="true" />
          撤销发布
        </NvButton>
      </div>
      <NvButton
        class="mb-4"
        type="button"
        variant="outline"
        :disabled="!detailSelection.planId || csv.pending.value"
        @click="downloadPlanCsv"
        >下载方案 CSV</NvButton
      >
      <SchedulingPlanGantt
        :time-context="planFactoryTimeContext.items.value"
        :plan="planDetail"
        :summary="selectedPlanSummary"
        :work-orders="displayWorkOrders"
        :work-order-facts-unavailable="Boolean(associatedWorkOrders.error.value)"
        :loading="planDetailPending"
        :error="planDetailError"
        :release-pending="releasePlanPending"
        @open-detail="detailOpen = true"
        @release="publish(selectedPlanSummary)"
      />
      <SchedulingDaySchedule
        v-if="planDetail && !planDetailPending && !planDetailError"
        :plan="planDetail"
      />
    </template>
  </NvTabsContent>
</template>
