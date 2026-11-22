<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  NvButton,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldLabel,
  NvInput,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  approval: View['approval']
  canManageApprovals: View['canManageApprovals']
  decisionContextItems: View['decisionContextItems']
  decisionForm: View['decisionForm']
  decisionLabel: View['decisionLabel']
  decisionTarget: View['decisionTarget']
  documentLabel: View['documentLabel']
  submitTaskDecision: View['submitTaskDecision']
}>()

const taskDecisionOpen = defineModel<View['taskDecisionOpen']>('taskDecisionOpen', {
  required: true,
})
</script>

<template>
  <NvDialog v-if="canManageApprovals" v-model:open="taskDecisionOpen">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>{{ decisionLabel(decisionForm.decision) }}审批任务</NvDialogTitle>
        <!-- 审批对象已在下方只读区完整呈现；此处仅供读屏播报。 -->
        <NvDialogDescription class="sr-only">
          审批对象：{{ decisionTarget ? documentLabel(decisionTarget) : '' }}。
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitTaskDecision">
        <!-- 单据 / 步骤 / 类型 / 到期由所选任务行带出，只读呈现，审批人只补一条意见。 -->
        <CarriedContextSummary label="审批对象" :items="decisionContextItems" />

        <NvField>
          <NvFieldLabel for="approval-comment">处理意见</NvFieldLabel>
          <NvInput id="approval-comment" v-model="decisionForm.comment" autocomplete="off" />
        </NvField>
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline">取消</NvButton>
          </NvDialogClose>
          <NvButton
            type="submit"
            :variant="decisionForm.decision === 'reject' ? 'destructive' : 'default'"
            :disabled="approval.resolveTaskPending.value"
          >
            <NvSpinner v-if="approval.resolveTaskPending.value" aria-hidden="true" />
            {{ decisionLabel(decisionForm.decision) }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
