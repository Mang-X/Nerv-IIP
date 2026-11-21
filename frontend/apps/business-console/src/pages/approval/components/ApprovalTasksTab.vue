<script setup lang="ts">
import { NvDataTable, NvDropdownMenuItem, NvRowActions, NvTabsContent } from '@nerv-iip/ui'
import { CheckCircle2Icon, EyeIcon, RotateCcwIcon, XCircleIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  APPROVAL_AWAITING_MESSAGE: View['APPROVAL_AWAITING_MESSAGE']
  approval: View['approval']
  canManageApprovals: View['canManageApprovals']
  documentLabel: View['documentLabel']
  openTaskDecision: View['openTaskDecision']
  quickResolveTask: View['quickResolveTask']
  rowKey: View['rowKey']
  taskColumns: View['taskColumns']
  taskPager: View['taskPager']
  tasksState: View['tasksState']
  viewChain: View['viewChain']
}>()
</script>

<template>
  <NvTabsContent value="tasks" class="grid gap-3">
    <NvDataTable
      manual
      :page="taskPager.page.value"
      :page-size="taskPager.pageSize.value"
      :total-items="approval.tasksTotal.value"
      :columns="taskColumns"
      :rows="approval.tasks.value"
      :row-key="rowKey"
      :loading="approval.tasksPending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="tasksState.emptyMessage"
      :error="tasksState.error"
      :error-message="tasksState.errorMessage"
      :awaiting-scope="tasksState.awaitingScope"
      :awaiting-scope-message="APPROVAL_AWAITING_MESSAGE"
      @retry="approval.refreshAll"
      @update:page="taskPager.page.value = $event"
      @update:page-size="(v) => (taskPager.pageSize.value = String(v))"
    >
      <template #cell-actions="{ row }">
        <NvRowActions :label="`审批任务 ${documentLabel(row)}`">
          <NvDropdownMenuItem @click="viewChain(row)">
            <EyeIcon aria-hidden="true" />
            查看步骤
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canManageApprovals" @click="quickResolveTask(row, 'approve')">
            <CheckCircle2Icon aria-hidden="true" />
            通过
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canManageApprovals" @click="openTaskDecision(row, 'reject')">
            <XCircleIcon aria-hidden="true" />
            驳回
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canManageApprovals" @click="openTaskDecision(row, 'return')">
            <RotateCcwIcon aria-hidden="true" />
            退回
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>
  </NvTabsContent>
</template>
