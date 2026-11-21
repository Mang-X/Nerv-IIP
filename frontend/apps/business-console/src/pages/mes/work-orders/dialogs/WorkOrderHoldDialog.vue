<script setup lang="ts">
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvField,
  NvFieldLabel,
  NvInput,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  holdReasonMaxLength: View['HOLD_REASON_MAX_LENGTH']
  holdReasonError: View['holdReasonError']
  holdShowErrors: View['holdShowErrors']
  holdWorkOrderPending: View['holdWorkOrderPending']
  submitHold: View['submitHold']
  workOrderLabel: View['workOrderLabel']
}>()

const holdOpen = defineModel<View['holdOpen']>('holdOpen', { required: true })

const holdReason = defineModel<View['holdReason']>('holdReason', { required: true })
</script>

<template>
  <NvAlertDialog v-model:open="holdOpen">
    <NvAlertDialogContent class="sm:max-w-md">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>挂起工单 · {{ workOrderLabel }}</NvAlertDialogTitle>
        <NvAlertDialogDescription
          >挂起后工单暂停后续执行；确认原因后可在工单状态中追溯。</NvAlertDialogDescription
        >
      </NvAlertDialogHeader>
      <p
        v-if="holdShowErrors && holdReasonError"
        class="text-sm text-destructive"
        role="alert"
        data-testid="hold-validation-summary"
      >
        请完整填写带 * 的必填项（已标红）。
      </p>
      <NvField :data-invalid="holdShowErrors && !!holdReasonError">
        <NvFieldLabel for="hold-reason"
          >挂起原因 <span class="text-destructive">*</span></NvFieldLabel
        >
        <NvInput
          id="hold-reason"
          v-model="holdReason"
          :maxlength="holdReasonMaxLength"
          placeholder="请说明挂起原因"
        />
        <p v-if="holdShowErrors && holdReasonError" class="text-xs text-destructive" role="alert">
          {{ holdReasonError }}
        </p>
      </NvField>
      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="holdWorkOrderPending"
          @click="holdOpen = false"
        >
          返回
        </NvButton>
        <NvButton
          type="button"
          :disabled="holdWorkOrderPending"
          data-testid="confirm-hold-work-order"
          @click="submitHold"
        >
          <NvSpinner v-if="holdWorkOrderPending" aria-hidden="true" />
          确认挂起
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
