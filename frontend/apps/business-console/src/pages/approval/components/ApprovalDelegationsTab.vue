<script setup lang="ts">
import { NvButton, NvDataTable, NvStatusBadge, NvTabsContent } from '@nerv-iip/ui'
import { RotateCcwIcon, UserRoundPlusIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  APPROVAL_AWAITING_MESSAGE: View['APPROVAL_AWAITING_MESSAGE']
  approval: View['approval']
  canManageApprovals: View['canManageApprovals']
  delegationColumns: View['delegationColumns']
  delegationPager: View['delegationPager']
  delegationsState: View['delegationsState']
  openDelegation: View['openDelegation']
  revokeDelegation: View['revokeDelegation']
  rowKey: View['rowKey']
}>()
</script>

<template>
  <NvTabsContent value="delegations" class="grid gap-3">
    <div class="flex justify-end">
      <NvButton v-if="canManageApprovals" size="sm" type="button" @click="openDelegation">
        <UserRoundPlusIcon aria-hidden="true" />
        新建委托
      </NvButton>
    </div>
    <NvDataTable
      manual
      :page="delegationPager.page.value"
      :page-size="delegationPager.pageSize.value"
      :total-items="approval.delegationsTotal.value"
      :columns="delegationColumns"
      :rows="approval.delegations.value"
      :row-key="rowKey"
      :loading="approval.delegationsPending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="delegationsState.emptyMessage"
      :error="delegationsState.error"
      :error-message="delegationsState.errorMessage"
      :awaiting-scope="delegationsState.awaitingScope"
      :awaiting-scope-message="APPROVAL_AWAITING_MESSAGE"
      @retry="approval.refreshAll"
      @update:page="delegationPager.page.value = $event"
      @update:page-size="(v) => (delegationPager.pageSize.value = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="canManageApprovals && (row.status ?? '').toLowerCase() === 'active'"
          size="sm"
          type="button"
          variant="ghost"
          @click="revokeDelegation(row)"
        >
          <RotateCcwIcon aria-hidden="true" />
          撤销
        </NvButton>
      </template>
    </NvDataTable>
  </NvTabsContent>
</template>
