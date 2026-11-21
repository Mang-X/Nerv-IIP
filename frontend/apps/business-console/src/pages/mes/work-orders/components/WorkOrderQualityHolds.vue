<script setup lang="ts">
import QualityHoldPanel from '@/components/mes/QualityHoldPanel.vue'
import { LockIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  activeQualityHoldCount: View['activeQualityHoldCount']
  canManageQualityHold: View['canManageQualityHold']
  canReadInspectionRecords: View['canReadInspectionRecords']
  canReadQualityHold: View['canReadQualityHold']
  filters: View['filters']
  qualityHolds: View['qualityHolds']
  refreshDetail: View['refreshDetail']
}>()
</script>

<template>
  <!-- 质量保留（hold）区块：施加/释放时间线、来源检验单互链、人工强制释放（#886 读面 + 既有 force-release）。
       活跃保留显示锁定 + 强制释放；已释放周期保留展示释放时间/方式 + 完整时间线（释放后不卸载）。 -->
  <div v-if="qualityHolds.length" class="grid gap-2">
    <div class="flex items-center gap-2">
      <LockIcon v-if="activeQualityHoldCount" class="size-4 text-destructive" aria-hidden="true" />
      <span class="text-sm font-semibold text-foreground">质量保留</span>
      <span v-if="activeQualityHoldCount" class="text-xs text-muted-foreground"
        >该工单存在 {{ activeQualityHoldCount }} 项有效质量保留，需处理后才能放行或开工</span
      >
      <span v-else class="text-xs text-muted-foreground"
        >质量保留已全部解除，以下为最近保留周期与时间线</span
      >
    </div>
    <QualityHoldPanel
      v-for="hold in qualityHolds"
      :key="`${hold.sourceService}-${hold.sourceDocumentId}`"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
      :source-service="hold.sourceService"
      :source-document-id="hold.sourceDocumentId"
      :scope="hold.scope"
      :is-active="hold.isActive"
      :operation-task-id="hold.operationTaskId"
      :hold-reason="hold.holdReason"
      :held-at-utc="hold.heldAtUtc"
      :held-by="hold.heldBy"
      :released-at-utc="hold.releasedAtUtc"
      :released-by="hold.releasedBy"
      :release-reason="hold.releaseReason"
      :release-source="hold.releaseSource"
      :can-manage="canManageQualityHold"
      :can-read-timeline="canReadQualityHold"
      :can-read-inspection-records="canReadInspectionRecords"
      @released="refreshDetail"
    />
  </div>
</template>
