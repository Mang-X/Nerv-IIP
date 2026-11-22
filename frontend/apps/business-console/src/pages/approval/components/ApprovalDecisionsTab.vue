<script setup lang="ts">
import { APPROVAL_DECISION_LABELS, labelFor } from '@/data/businessLabels'
import { NvDataTable, NvStatusBadge, NvTabsContent } from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  APPROVAL_AWAITING_MESSAGE: View['APPROVAL_AWAITING_MESSAGE']
  approval: View['approval']
  decisionColumns: View['decisionColumns']
  decisionPager: View['decisionPager']
  decisionsState: View['decisionsState']
  rowKey: View['rowKey']
}>()
</script>

<template>
  <NvTabsContent value="decisions" class="grid gap-3">
    <NvDataTable
      manual
      :page="decisionPager.page.value"
      :page-size="decisionPager.pageSize.value"
      :total-items="approval.decisionsTotal.value"
      :columns="decisionColumns"
      :rows="approval.decisions.value"
      :row-key="rowKey"
      :loading="approval.decisionsPending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="decisionsState.emptyMessage"
      :error="decisionsState.error"
      :error-message="decisionsState.errorMessage"
      :awaiting-scope="decisionsState.awaitingScope"
      :awaiting-scope-message="APPROVAL_AWAITING_MESSAGE"
      @retry="approval.refreshAll"
      @update:page="decisionPager.page.value = $event"
      @update:page-size="(v) => (decisionPager.pageSize.value = String(v))"
    >
      <template #cell-decision="{ row }">
        <NvStatusBadge
          :value="row.decision"
          :label="labelFor(APPROVAL_DECISION_LABELS, row.decision, '—')"
        />
      </template>
    </NvDataTable>
  </NvTabsContent>
</template>
