<script setup lang="ts">
/**
 * 交接单三类明细 + 附件的只读呈现（接班确认页用）。
 *
 * 「没登记」和「没取到」必须分开说：本组件只在 `loaded` 为真时才把空数组说成
 * 「交班时点没有登记…」。详情取数失败时由调用方渲染错误横幅，不要把空列表当成空登记
 * ——那会和同屏的错误横幅、列表行自己的计数三方矛盾。
 */
import {
  shiftHandoverIssueCategoryLabel,
  shiftHandoverIssueSeverityLabel,
  shiftHandoverUnfinishedWorkOrderStatusLabel,
} from '@nerv-iip/business-core'
import { NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import {
  formatAttachmentSize,
  type ShiftHandoverAttachment,
  type ShiftHandoverOpenIssue,
  type ShiftHandoverUnfinishedWorkOrder,
  type ShiftHandoverWipItem,
} from '@/composables/useBusinessShiftHandover'

defineProps<{
  loaded: boolean
  wipItems: ShiftHandoverWipItem[]
  unfinishedWorkOrders: ShiftHandoverUnfinishedWorkOrder[]
  openIssues: ShiftHandoverOpenIssue[]
  attachments: ShiftHandoverAttachment[]
  openingAttachmentFileId?: string
  attachmentError?: string
}>()

const emit = defineEmits<{ openAttachment: [attachment: ShiftHandoverAttachment] }>()

const SEVERITY_TONES: Record<string, 'default' | 'warning' | 'danger'> = {
  low: 'default',
  medium: 'warning',
  high: 'danger',
}
function severityTone(value?: string | null) {
  return SEVERITY_TONES[(value ?? '').trim().toLowerCase()] ?? 'default'
}
</script>

<template>
  <div class="space-y-4">
    <section data-testid="detail-wip">
      <h2 class="mb-2 text-sm font-medium text-muted-foreground">在制清点</h2>
      <ul v-if="wipItems.length" class="space-y-2">
        <li
          v-for="(item, index) in wipItems"
          :key="`wip-${index}`"
          class="rounded-xl border border-border bg-card px-3 py-2"
        >
          <p class="text-sm font-medium text-foreground">{{ item.workOrderId }}</p>
          <p class="text-xs text-muted-foreground">
            {{ item.operationTaskId || '按工单登记' }} · 在制 {{ item.quantity }}
          </p>
        </li>
      </ul>
      <p v-else-if="loaded" class="text-sm text-muted-foreground">交班时点没有登记在制清点。</p>
    </section>

    <section data-testid="detail-unfinished">
      <h2 class="mb-2 text-sm font-medium text-muted-foreground">未完工单</h2>
      <ul v-if="unfinishedWorkOrders.length" class="space-y-2">
        <li
          v-for="(item, index) in unfinishedWorkOrders"
          :key="`unfinished-${index}`"
          class="rounded-xl border border-border bg-card px-3 py-2"
        >
          <p class="text-sm font-medium text-foreground">{{ item.workOrderId }}</p>
          <p class="text-xs text-muted-foreground">
            {{ shiftHandoverUnfinishedWorkOrderStatusLabel(item.workOrderStatus) }} · 完成
            {{ item.completedQuantity }} / 计划 {{ item.plannedQuantity }}
          </p>
        </li>
      </ul>
      <p v-else-if="loaded" class="text-sm text-muted-foreground">交班时点没有登记未完工单。</p>
    </section>

    <section data-testid="detail-issues">
      <h2 class="mb-2 text-sm font-medium text-muted-foreground">设备与质量遗留问题</h2>
      <ul v-if="openIssues.length" class="space-y-2">
        <li
          v-for="(item, index) in openIssues"
          :key="`issue-${index}`"
          class="rounded-xl border border-border bg-card px-3 py-2"
        >
          <div class="flex items-center gap-2">
            <span class="text-sm font-medium text-foreground">{{
              shiftHandoverIssueCategoryLabel(item.category)
            }}</span>
            <NvMobileTag size="sm" :variant="severityTone(item.severity)">{{
              shiftHandoverIssueSeverityLabel(item.severity)
            }}</NvMobileTag>
          </div>
          <p class="mt-1 text-sm text-foreground">{{ item.description }}</p>
          <p v-if="item.referenceId" class="text-xs text-muted-foreground">
            关联单据 {{ item.referenceId }}
          </p>
        </li>
      </ul>
      <p v-else-if="loaded" class="text-sm text-muted-foreground">交班时点没有登记遗留问题。</p>
    </section>

    <section data-testid="detail-attachments">
      <h2 class="mb-2 text-sm font-medium text-muted-foreground">现场照片</h2>
      <ul v-if="attachments.length" class="space-y-2" data-testid="detail-attachment-rows">
        <li
          v-for="(attachment, index) in attachments"
          :key="attachment.fileId ?? `attachment-${index}`"
          class="rounded-xl border border-border bg-card px-3 py-2"
        >
          <p class="truncate text-sm font-medium text-foreground">{{ attachment.fileName }}</p>
          <p class="text-xs text-muted-foreground">
            {{ attachment.contentType }} · {{ formatAttachmentSize(attachment.sizeBytes) }}
          </p>
          <NvMobileButton
            variant="text"
            size="sm"
            class="mt-1 px-0"
            :data-testid="`open-attachment-${index}`"
            :disabled="openingAttachmentFileId === attachment.fileId"
            @click="emit('openAttachment', attachment)"
            >{{
              openingAttachmentFileId === attachment.fileId ? '打开中…' : '查看照片'
            }}</NvMobileButton
          >
        </li>
      </ul>
      <p v-else-if="loaded" class="text-sm text-muted-foreground">交班时点没有附照片。</p>
      <p
        v-if="attachmentError"
        role="alert"
        data-testid="detail-attachment-error"
        class="mt-2 text-sm text-destructive"
      >
        {{ attachmentError }}
      </p>
    </section>
  </div>
</template>
