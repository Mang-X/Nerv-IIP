<script setup lang="ts">
import {
  NvButton,
  NvCheckbox,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrdersPage } from '../useWorkOrdersPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrdersPage>>

defineProps<{
  canSubmitRelease: View['canSubmitRelease']
  clearReleaseIntent: View['clearReleaseIntent']
  productionVersionLabel: View['productionVersionLabel']
  releaseIntent: View['releaseIntent']
  releaseIntentOrder: View['releaseIntentOrder']
  releaseRetroactiveNotice: View['releaseRetroactiveNotice']
  releaseValidationMessage: View['releaseValidationMessage']
  releaseWorkOrderPending: View['releaseWorkOrderPending']
  statusLabel: View['statusLabel']
  submitReleaseWorkOrder: View['submitReleaseWorkOrder']
}>()

const releaseDialogOpen = defineModel<View['releaseDialogOpen']>('releaseDialogOpen', {
  required: true,
})

const releaseWarningsConfirmed = defineModel<View['releaseWarningsConfirmed']>(
  'releaseWarningsConfirmed',
  { required: true },
)
</script>

<template>
  <NvDialog v-model:open="releaseDialogOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>确认下达工单</NvDialogTitle>
        <NvDialogDescription>
          下达时系统会再次校验物料、设备、质量与工序就绪条件，未通过则不会下达。
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitReleaseWorkOrder">
        <dl v-if="releaseIntentOrder" class="grid gap-2 rounded-md border p-3 text-sm">
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工单</dt>
            <dd class="font-medium">{{ releaseIntent?.workOrderLabel }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">当前状态</dt>
            <dd>{{ statusLabel(releaseIntentOrder.status) }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">生产版本</dt>
            <dd>{{ productionVersionLabel(releaseIntentOrder.productionVersionId) }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工序任务</dt>
            <dd>{{ releaseIntentOrder.operationTasks?.length ?? 0 }} 道</dd>
          </div>
        </dl>
        <p
          v-if="releaseRetroactiveNotice"
          class="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs text-muted-foreground"
          data-testid="release-retroactive-notice"
        >
          {{ releaseRetroactiveNotice }}
        </p>
        <p
          v-if="releaseValidationMessage"
          class="text-sm text-destructive"
          role="alert"
          data-testid="release-validation-message"
        >
          {{ releaseValidationMessage }}
        </p>
        <label class="flex items-start gap-2 text-sm">
          <NvCheckbox
            v-model="releaseWarningsConfirmed"
            :disabled="releaseWorkOrderPending"
            aria-label="确认已核对工单下达警告"
          />
          <span>我已核对当前工单、生产版本与工序信息，确认下达。</span>
        </label>
        <NvDialogFooter>
          <NvButton
            type="button"
            variant="outline"
            :disabled="releaseWorkOrderPending"
            @click="clearReleaseIntent"
          >
            取消
          </NvButton>
          <NvButton type="submit" :disabled="!canSubmitRelease">
            <NvSpinner v-if="releaseWorkOrderPending" aria-hidden="true" />
            确认下达
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
