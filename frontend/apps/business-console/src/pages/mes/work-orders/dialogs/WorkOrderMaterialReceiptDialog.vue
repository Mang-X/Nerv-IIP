<script setup lang="ts">
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import { readFaceText } from '@/utils/readFace'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  canSubmitReceipt: View['canSubmitReceipt']
  confirmLineSideReceiptPending: View['confirmLineSideReceiptPending']
  receiveError: View['receiveError']
  receiveForm: View['receiveForm']
  receiveRetry: View['receiveRetry']
  submitReceipt: View['submitReceipt']
}>()

const receiveOpen = defineModel<View['receiveOpen']>('receiveOpen', { required: true })
</script>

<template>
  <!-- 线边收料 -->
  <NvAlertDialog v-model:open="receiveOpen">
    <NvAlertDialogContent class="sm:max-w-md">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle
          >线边收料 · {{ readFaceText(receiveForm.requestId) }}</NvAlertDialogTitle
        >
        <NvAlertDialogDescription>
          线边可用量在库存过账后更新。数量留空表示按未收余量一次收齐。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvFieldGroup class="grid gap-3">
        <p
          v-if="receiveError"
          class="rounded-md border border-destructive/40 bg-destructive/5 p-2 text-sm text-destructive"
          data-testid="line-side-receipt-error"
          role="alert"
        >
          {{ receiveError }}
        </p>
        <NvField>
          <NvFieldLabel for="receive-quantity">收料数量</NvFieldLabel>
          <NvInput
            id="receive-quantity"
            v-model="receiveForm.quantity"
            inputmode="decimal"
            :disabled="receiveRetry || confirmLineSideReceiptPending"
            placeholder="留空表示收齐剩余数量"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="receive-lot">物料批次</NvFieldLabel>
          <DirectoryPicker
            id="receive-lot"
            v-model="receiveForm.materialLotId"
            directory-type="batch"
            :sku-code="receiveForm.materialCode"
            :disabled="receiveRetry || confirmLineSideReceiptPending"
            placeholder="退料与追溯依赖批次，建议如实选择"
            clearable
          />
        </NvField>
      </NvFieldGroup>
      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="confirmLineSideReceiptPending"
          @click="receiveOpen = false"
        >
          返回
        </NvButton>
        <NvButton
          type="button"
          :disabled="!canSubmitReceipt || confirmLineSideReceiptPending"
          data-testid="submit-line-side-receipt"
          @click="submitReceipt"
        >
          <NvSpinner v-if="confirmLineSideReceiptPending" aria-hidden="true" />
          {{ receiveRetry ? '重试本次收料' : '确认收料' }}
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
