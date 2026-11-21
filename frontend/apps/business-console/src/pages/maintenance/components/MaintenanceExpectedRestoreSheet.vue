<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  NvButton,
  NvField,
  NvFieldDescription,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useMaintenanceWorkOrdersPage } from '../useMaintenanceWorkOrdersPage'

type View = ShallowUnwrapRef<ReturnType<typeof useMaintenanceWorkOrdersPage>>

defineProps<{
  deviceLabel: View['deviceLabel']
  expectedRestorePending: View['expectedRestorePending']
  expectedRestoreScope: View['expectedRestoreScope']
  expectedRestoreScopeOptions: View['expectedRestoreScopeOptions']
  expectedRestoreScopes: View['expectedRestoreScopes']
  expectedRestoreTarget: View['expectedRestoreTarget']
  submitExpectedRestore: View['submitExpectedRestore']
  workOrderNo: View['workOrderNo']
}>()

const expectedRestoreOpen = defineModel<View['expectedRestoreOpen']>('expectedRestoreOpen', {
  required: true,
})

const expectedRestoreScopeKey = defineModel<View['expectedRestoreScopeKey']>(
  'expectedRestoreScopeKey',
  { required: true },
)

const expectedRestoreAtLocal = defineModel<View['expectedRestoreAtLocal']>(
  'expectedRestoreAtLocal',
  { required: true },
)
</script>

<template>
  <NvSheet v-model:open="expectedRestoreOpen">
    <NvSheetContent class="flex w-full flex-col sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>更新预计恢复时间</NvSheetTitle>
        <NvSheetDescription
          >填写维修预测，留空可清除；不改变实际恢复或工单状态。</NvSheetDescription
        >
      </NvSheetHeader>
      <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitExpectedRestore">
        <CarriedContextSummary
          v-if="expectedRestoreTarget"
          label="维修工单"
          :items="[
            { label: '工单号', value: workOrderNo(expectedRestoreTarget) },
            { label: '设备', value: deviceLabel(expectedRestoreTarget.deviceAssetId) },
          ]"
        />
        <NvField v-if="expectedRestoreScopes.length > 1">
          <NvFieldLabel for="mwo-expected-restore-scope">维修作业范围</NvFieldLabel>
          <NvSearchSelect
            id="mwo-expected-restore-scope"
            v-model="expectedRestoreScopeKey"
            :options="expectedRestoreScopeOptions"
            :disabled="expectedRestorePending"
            aria-label="维修作业范围"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="mwo-expected-restore">预计恢复时间</NvFieldLabel>
          <NvInput
            id="mwo-expected-restore"
            v-model="expectedRestoreAtLocal"
            type="datetime-local"
            step="1"
            :disabled="expectedRestorePending"
          />
          <NvFieldDescription>按本机时区填写。仅已登记设备不可用的工单可更新。</NvFieldDescription>
        </NvField>
        <NvSheetFooter class="px-0">
          <NvButton
            type="button"
            variant="outline"
            :disabled="expectedRestorePending"
            @click="expectedRestoreOpen = false"
            >取消</NvButton
          >
          <NvButton type="submit" :disabled="expectedRestorePending || !expectedRestoreScope">
            <NvSpinner v-if="expectedRestorePending" aria-hidden="true" />保存预计恢复时间
          </NvButton>
        </NvSheetFooter>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
