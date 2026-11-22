<script setup lang="ts">
import { NvButton, NvDataTable, NvStatusBadge, NvTabsContent } from '@nerv-iip/ui'
import { FilePlus2Icon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  APPROVAL_AWAITING_MESSAGE: View['APPROVAL_AWAITING_MESSAGE']
  approval: View['approval']
  canManageApprovals: View['canManageApprovals']
  formatStatus: View['formatStatus']
  openTemplate: View['openTemplate']
  rowKey: View['rowKey']
  templateColumns: View['templateColumns']
  templatePager: View['templatePager']
  templatesState: View['templatesState']
}>()
</script>

<template>
  <NvTabsContent value="templates" class="grid gap-3">
    <div class="flex justify-end">
      <NvButton v-if="canManageApprovals" size="sm" type="button" @click="openTemplate">
        <FilePlus2Icon aria-hidden="true" />
        维护模板
      </NvButton>
    </div>
    <NvDataTable
      manual
      :page="templatePager.page.value"
      :page-size="templatePager.pageSize.value"
      :total-items="approval.templatesTotal.value"
      :columns="templateColumns"
      :rows="approval.templates.value"
      :row-key="rowKey"
      :loading="approval.templatesPending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="templatesState.emptyMessage"
      :error="templatesState.error"
      :error-message="templatesState.errorMessage"
      :awaiting-scope="templatesState.awaitingScope"
      :awaiting-scope-message="APPROVAL_AWAITING_MESSAGE"
      @retry="approval.refreshAll"
      @update:page="templatePager.page.value = $event"
      @update:page-size="(v) => (templatePager.pageSize.value = String(v))"
    >
      <template #cell-isActive="{ row }"
        ><NvStatusBadge
          :value="row.isActive ? 'active' : 'disabled'"
          :label="formatStatus(row.isActive)"
      /></template>
    </NvDataTable>
  </NvTabsContent>
</template>
