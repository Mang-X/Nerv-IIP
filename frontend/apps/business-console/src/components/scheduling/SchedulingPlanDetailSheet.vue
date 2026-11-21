<script setup lang="ts">
import { TaskFacts } from '@nerv-iip/scheduling'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import {
  schedulingPlanStatusLabel,
  schedulingPlanStatusTone,
} from '@/utils/schedulingPlanPresentation'
import SchedulingMaterialShortageSummary from '@/components/scheduling/SchedulingMaterialShortageSummary.vue'
import SchedulingPlanConflicts from '@/components/scheduling/SchedulingPlanConflicts.vue'
import SchedulingPlanRisks from '@/components/scheduling/SchedulingPlanRisks.vue'
import SchedulingResourceLoads from '@/components/scheduling/SchedulingResourceLoads.vue'
import SchedulingWorkOrderLink from '@/components/scheduling/SchedulingWorkOrderLink.vue'
import {
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useSchedulingPageController } from './useSchedulingPageController'

type View = ShallowUnwrapRef<ReturnType<typeof useSchedulingPageController>>

defineProps<{
  assignmentTask: View['assignmentTask']
  assignmentText: View['assignmentText']
  assignmentTime: View['assignmentTime']
  detailFeedback: View['detailFeedback']
  detailSelection: View['detailSelection']
  orderUrgencies: View['orderUrgencies']
  orderedAssignments: View['orderedAssignments']
  planDetail: View['planDetail']
  planDetailPending: View['planDetailPending']
  refreshUrgency: View['refreshUrgency']
  selectedPlanRange: View['selectedPlanRange']
  selectedResourceCount: View['selectedResourceCount']
  targetedAssignmentFound: View['targetedAssignmentFound']
  targetedOrderReference: View['targetedOrderReference']
}>()

const detailOpen = defineModel<View['detailOpen']>('detailOpen', { required: true })

const displayMode = defineModel<View['displayMode']>('displayMode', { required: true })
</script>

<template>
  <NvSheet v-model:open="detailOpen">
    <NvSheetContent side="right" class="w-full overflow-y-auto sm:max-w-3xl">
      <NvSheetHeader>
        <NvSheetTitle>排程方案明细</NvSheetTitle>
        <NvSheetDescription>
          {{ detailSelection.planId || '未选择方案' }} · 只读查阅，调整排程请回草案工作区
        </NvSheetDescription>
      </NvSheetHeader>

      <p
        v-if="targetedOrderReference"
        class="mt-4 rounded-md border border-primary/30 bg-primary/5 px-3 py-2 text-sm text-foreground"
        role="status"
      >
        {{
          targetedAssignmentFound
            ? `已定位订单 ${targetedOrderReference}`
            : `正在定位订单 ${targetedOrderReference}`
        }}
      </p>

      <div
        v-if="planDetailPending"
        class="mt-6 flex items-center gap-2 text-sm text-muted-foreground"
      >
        <NvSpinner aria-hidden="true" />
        正在读取方案明细
      </div>

      <div v-else-if="planDetail" class="mt-6 grid gap-6">
        <section class="grid gap-3 rounded-lg border bg-background p-4">
          <div class="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h3 class="text-sm font-semibold text-foreground">计划概览</h3>
              <p class="mt-1 text-sm text-muted-foreground">{{ selectedPlanRange }}</p>
            </div>
            <NvStatusBadge
              :label="schedulingPlanStatusLabel(planDetail.status)"
              :tone="schedulingPlanStatusTone(planDetail.status)"
            />
          </div>
          <!-- 6 个指标塞进 5 列：第 6 个「负荷分钟」必然孤悬到第二行独占一格，而 1/5 的
               列宽又装不下「设备状态未知工序」这种 7 字标签，只能断行并贴到格子边缘
               （#1418）。改 3 列 × 2 行：列宽翻倍，长标签一行放得下，也不再有落单格子。 -->
          <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <p class="text-xs text-muted-foreground">资源数</p>
              <p class="text-sm font-medium text-foreground">{{ selectedResourceCount }}</p>
            </div>
            <div>
              <p class="text-xs text-muted-foreground">已排工序</p>
              <p class="text-sm font-medium text-foreground">
                {{
                  planDetail.metrics?.scheduledOperationCount ?? planDetail.assignments?.length ?? 0
                }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted-foreground">未排工序</p>
              <p class="text-sm font-medium text-foreground">
                {{
                  planDetail.metrics?.unscheduledOperationCount ??
                  planDetail.unscheduledOperations?.length ??
                  0
                }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted-foreground">物料风险工序</p>
              <p class="text-sm font-medium text-foreground">
                {{
                  planDetail.metrics?.materialRiskOperationCount ??
                  planDetail.materialRisks?.length ??
                  0
                }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted-foreground">设备状态未知工序</p>
              <p class="text-sm font-medium text-foreground">
                {{
                  planDetail.metrics?.equipmentRiskOperationCount ??
                  planDetail.equipmentRisks?.length ??
                  0
                }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted-foreground">负荷分钟</p>
              <p class="text-sm font-medium text-foreground">
                {{ planDetail.metrics?.assignedMinutes ?? 0 }}
              </p>
            </div>
          </div>
        </section>

        <SchedulingPlanConflicts
          :conflicts="planDetail.conflicts ?? []"
          :unscheduled-operations="planDetail.unscheduledOperations ?? []"
          :generated-at-utc="planDetail.generatedAtUtc"
        />

        <section class="grid gap-3">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <h3 class="text-sm font-semibold text-foreground">资源分配</h3>
            <UrgencyDisplayModeSelect v-model="displayMode" />
          </div>
          <div v-if="orderedAssignments.length" class="grid gap-2">
            <div
              v-for="assignment in orderedAssignments"
              :key="assignment.assignmentId ?? assignmentText(assignment)"
              class="rounded-md border bg-background p-3"
              :class="{
                'border-primary/50 bg-primary/5': assignment.orderId === targetedOrderReference,
              }"
              :data-targeted-order="
                assignment.orderId === targetedOrderReference ? 'true' : undefined
              "
            >
              <div class="flex items-center justify-between gap-3">
                <p class="text-sm font-medium text-foreground">
                  <SchedulingWorkOrderLink
                    :work-order-id="assignment.orderId"
                    :operation-task-id="assignment.operationId"
                    >{{ assignmentText(assignment) }}</SchedulingWorkOrderLink
                  >
                </p>
                <OrderUrgencyBadge
                  :order-reference="assignment.orderId ?? ''"
                  :mode="displayMode"
                  :urgency="
                    assignment.orderId
                      ? orderUrgencies.byReference.value.get(assignment.orderId)
                      : undefined
                  "
                  :source-unavailable="orderUrgencies.error?.value != null"
                  @refresh="refreshUrgency"
                />
              </div>
              <TaskFacts
                v-if="assignmentTask(assignment)"
                :task="assignmentTask(assignment)!"
                class="mt-3"
              />
              <p class="mt-1 text-sm text-muted-foreground">
                <span class="block">{{ assignmentTask(assignment)?.factoryTimeLabel }}</span>
                <template v-if="assignment.segments?.length">
                  <span v-for="(segment, index) in assignment.segments" :key="index" class="block">
                    第 {{ index + 1 }} 段 · {{ assignmentTime(assignment, segment.startUtc) }} 至
                    {{ assignmentTime(assignment, segment.endUtc) }}
                  </span>
                </template>
                <template v-else>
                  {{ assignmentTime(assignment, assignment.startUtc) }} 至
                  {{ assignmentTime(assignment, assignment.endUtc) }}
                </template>
              </p>
            </div>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            暂无资源分配。
          </p>
        </section>

        <SchedulingResourceLoads :loads="planDetail.resourceLoads ?? []" />
        <SchedulingMaterialShortageSummary :shortages="planDetail.materialShortageSummary ?? []" />
        <SchedulingPlanRisks
          :material-risks="planDetail.materialRisks ?? []"
          :equipment-risks="planDetail.equipmentRisks ?? []"
        />
      </div>

      <div
        v-else
        class="mt-6 rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ detailFeedback }}
      </div>
    </NvSheetContent>
  </NvSheet>
</template>
