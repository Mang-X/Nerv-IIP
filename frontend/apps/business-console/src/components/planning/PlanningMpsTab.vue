<script setup lang="ts">
import { mpsStatus, formatDate } from './planningFormatters'
import { inlineErrorMessage } from '@/utils/notify'
import { NvButton, NvDataTable, NvSpinner, NvStatusBadge, NvTabsContent } from '@nerv-iip/ui'
import { CheckIcon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type { ShallowUnwrapRef } from 'vue'
import type { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

type View = ShallowUnwrapRef<ReturnType<typeof usePlanningWorkbenchController>>

defineProps<{
  actorName: View['actorName']
  canReadMps: View['canReadMps']
  editMpsBucket: View['editMpsBucket']
  formatMpsQuantity: View['formatMpsQuantity']
  hasPermission: View['hasPermission']
  hasScope: View['hasScope']
  mpsBuckets: View['mpsBuckets']
  mpsBucketsError: View['mpsBucketsError']
  mpsBucketsPending: View['mpsBucketsPending']
  mpsColumns: View['mpsColumns']
  releaseMps: View['releaseMps']
  releaseMpsBucketPending: View['releaseMpsBucketPending']
  reviewMps: View['reviewMps']
  reviewMpsBucketPending: View['reviewMpsBucketPending']
  siteLabel: View['siteLabel']
  skuLabel: View['skuLabel']
}>()
</script>

<template>
  <NvTabsContent v-if="canReadMps" value="mps">
    <NvDataTable
      :columns="mpsColumns"
      :rows="mpsBuckets"
      row-key="mpsId"
      :loading="mpsBucketsPending"
      :error="mpsBucketsError"
      :error-message="inlineErrorMessage(mpsBucketsError, '读取主计划失败。')"
      :awaiting-scope="!hasScope"
      awaiting-scope-message="请先选择组织与环境。"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围还没有主计划行。"
    >
      <template #cell-bucketDate="{ row }">{{ formatDate(row.bucketDate) }}</template>
      <template #cell-skuCode="{ row }">
        <div class="flex min-w-0 flex-col gap-0.5 whitespace-normal [overflow-wrap:anywhere]">
          <span>{{ skuLabel(row.skuCode) }}</span>
          <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
        </div>
      </template>
      <template #cell-siteCode="{ row }">
        <div class="flex min-w-0 flex-col gap-0.5 whitespace-normal [overflow-wrap:anywhere]">
          <span>{{ siteLabel(row.siteCode) }}</span>
          <span v-if="row.siteCode" class="text-xs text-muted-foreground">{{ row.siteCode }}</span>
        </div>
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{
          formatMpsQuantity(row.quantity, row.uomCode)
        }}</span></template
      >
      <template #cell-status="{ row }"
        ><NvStatusBadge :label="mpsStatus(row.status).label" :tone="mpsStatus(row.status).tone"
      /></template>
      <template #cell-reviewRelease="{ row }">
        <div
          class="flex min-w-0 flex-col gap-0.5 text-sm whitespace-normal [overflow-wrap:anywhere]"
        >
          <span>{{ row.reviewedBy ? `评审 ${actorName(row.reviewedBy)}` : '未评审' }}</span>
          <span class="text-muted-foreground">{{
            row.releasedBy ? `发布 ${actorName(row.releasedBy)}` : '未发布'
          }}</span>
        </div>
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-2">
          <NvButton
            v-if="hasPermission(P.planningMpsManage) && row.status?.toLowerCase() !== 'released'"
            size="sm"
            type="button"
            variant="outline"
            :aria-label="`编辑主计划行 ${row.skuCode}`"
            @click="editMpsBucket(row)"
            >编辑</NvButton
          >
          <NvButton
            v-if="hasPermission(P.planningMpsManage) && row.status?.toLowerCase() === 'draft'"
            size="sm"
            type="button"
            variant="outline"
            :disabled="reviewMpsBucketPending"
            @click="reviewMps(row)"
          >
            <NvSpinner v-if="reviewMpsBucketPending" aria-hidden="true" />
            <CheckIcon v-else aria-hidden="true" />
            评审
          </NvButton>
          <NvButton
            v-if="hasPermission(P.planningMpsRelease) && row.status?.toLowerCase() === 'reviewed'"
            size="sm"
            type="button"
            variant="outline"
            :disabled="releaseMpsBucketPending"
            @click="releaseMps(row)"
          >
            <NvSpinner v-if="releaseMpsBucketPending" aria-hidden="true" />
            <CheckIcon v-else aria-hidden="true" />
            发布
          </NvButton>
        </div>
      </template>
    </NvDataTable>
  </NvTabsContent>
</template>
