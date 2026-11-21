<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  closeWorkOrderPending: View['closeWorkOrderPending']
  detail: View['detail']
  formatStatus: View['formatStatus']
  submitClose: View['submitClose']
  workOrderLabel: View['workOrderLabel']
}>()

const closeOpen = defineModel<View['closeOpen']>('closeOpen', { required: true })
</script>

<template>
  <NvAlertDialog v-model:open="closeOpen">
    <NvAlertDialogContent class="sm:max-w-md">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>关闭工单 · {{ workOrderLabel }}</NvAlertDialogTitle>
        <NvAlertDialogDescription>关闭后工单进入终态，不能再继续执行。</NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <CarriedContextSummary
        label="关闭对象"
        :items="[
          { label: '工单', value: workOrderLabel },
          { label: '当前状态', value: formatStatus(detail?.status) },
        ]"
      />
      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="closeWorkOrderPending"
          @click="closeOpen = false"
        >
          返回
        </NvButton>
        <NvButton
          type="button"
          :disabled="closeWorkOrderPending"
          data-testid="confirm-close-work-order"
          @click="submitClose"
        >
          <NvSpinner v-if="closeWorkOrderPending" aria-hidden="true" />
          确认关闭
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
