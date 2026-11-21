<script setup lang="ts">
import type { BusinessConsolePlanningSuggestionItem } from '@nerv-iip/api-client'
import {
  NvButton,
  NvDialog,
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
import { SUGGESTION_REJECT_REASON_MAX_LENGTH } from '@/composables/planning/useBusinessPlanning'
import { formatQuantity, suggestionTypeLabel } from './planningFormatters'

defineProps<{
  skuLabel: (code?: string | null) => string
  rejectSuggestionPending: boolean
  canSubmitReject: boolean
  submitRejectSuggestion: () => Promise<void>
}>()

const rejectTarget = defineModel<BusinessConsolePlanningSuggestionItem | null>('rejectTarget', {
  required: true,
})
const rejectReason = defineModel<string>('rejectReason', { required: true })
</script>

<template>
  <NvDialog
    :open="!!rejectTarget"
    @update:open="
      (open) => {
        if (!open) rejectTarget = null
      }
    "
  >
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>拒绝计划建议</NvDialogTitle>
        <NvDialogDescription>
          {{ suggestionTypeLabel(rejectTarget?.suggestionType) }} ·
          {{ skuLabel(rejectTarget?.skuCode) }} ·
          {{ formatQuantity(rejectTarget?.quantity, rejectTarget?.uomCode) }}
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitRejectSuggestion">
        <NvField>
          <NvFieldLabel for="reject-reason">拒绝原因</NvFieldLabel>
          <NvInput
            id="reject-reason"
            v-model="rejectReason"
            :maxlength="SUGGESTION_REJECT_REASON_MAX_LENGTH"
            placeholder="说明为何不采纳该建议（如：库存已另行调拨）"
          />
          <p class="text-xs text-muted-foreground">
            必填，{{ rejectReason.trim().length }}/{{ SUGGESTION_REJECT_REASON_MAX_LENGTH }}
            字。
          </p>
        </NvField>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="rejectTarget = null">取消</NvButton>
          <NvButton type="submit" :disabled="rejectSuggestionPending || !canSubmitReject">
            <NvSpinner v-if="rejectSuggestionPending" aria-hidden="true" />
            确认拒绝
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
