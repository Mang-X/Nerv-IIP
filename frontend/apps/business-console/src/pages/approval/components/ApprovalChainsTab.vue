<script setup lang="ts">
import { NvButton, NvDataTable, NvStatusBadge, NvTabsContent } from '@nerv-iip/ui'
import { EyeIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  APPROVAL_AWAITING_MESSAGE: View['APPROVAL_AWAITING_MESSAGE']
  approval: View['approval']
  approverLabel: View['approverLabel']
  chainColumns: View['chainColumns']
  chainPager: View['chainPager']
  chainsState: View['chainsState']
  formatDateTime: View['formatDateTime']
  rowKey: View['rowKey']
  viewChain: View['viewChain']
}>()
</script>

<template>
  <NvTabsContent value="chains" class="grid gap-3">
    <NvDataTable
      manual
      :page="chainPager.page.value"
      :page-size="chainPager.pageSize.value"
      :total-items="approval.chainsTotal.value"
      :columns="chainColumns"
      :rows="approval.chains.value"
      :row-key="rowKey"
      :loading="approval.chainsPending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="chainsState.emptyMessage"
      :error="chainsState.error"
      :error-message="chainsState.errorMessage"
      :awaiting-scope="chainsState.awaitingScope"
      :awaiting-scope-message="APPROVAL_AWAITING_MESSAGE"
      @retry="approval.refreshAll"
      @update:page="chainPager.page.value = $event"
      @update:page-size="(v) => (chainPager.pageSize.value = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvButton size="sm" type="button" variant="ghost" @click="viewChain(row)">
          <EyeIcon aria-hidden="true" />
          步骤
        </NvButton>
      </template>
    </NvDataTable>

    <section v-if="approval.chainDetail.value" class="rounded-md border bg-card p-4">
      <div class="mb-3 flex items-center justify-between gap-3">
        <h2 class="text-base font-semibold">
          流程步骤 · {{ approval.chainDetail.value.documentId }}
        </h2>
        <NvStatusBadge :value="approval.chainDetail.value.status" />
      </div>
      <ol class="grid gap-2">
        <li
          v-for="step in approval.chainDetail.value.steps ?? []"
          :key="step.stepNo"
          class="rounded-md border bg-background p-3"
        >
          <div class="flex items-center justify-between gap-3">
            <span class="font-medium">{{ step.stepName ?? `第 ${step.stepNo} 步` }}</span>
            <NvStatusBadge :value="step.status" />
          </div>
          <p class="mt-1 text-sm text-muted-foreground">
            {{ approverLabel(step.approverType, step.approverRef) }} · 到期
            {{ formatDateTime(step.dueAtUtc) }}
          </p>
        </li>
      </ol>
    </section>
  </NvTabsContent>
</template>
