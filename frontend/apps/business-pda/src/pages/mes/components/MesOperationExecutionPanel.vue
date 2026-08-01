<script setup lang="ts">
import type {
  BusinessConsoleCurrentSopDocumentItem,
  BusinessConsoleMesOperationTaskRow,
} from '@nerv-iip/api-client'
import { describeMesReadinessReasons, operationTaskStatusLabel } from '@nerv-iip/business-core'
import RetryableListError from '@/components/RetryableListError.vue'
import { NvBottomSheet, NvMobileResult } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

import {
  actionsForOperationTask,
  deviceLabel,
  formatOperationDate,
  formatOperationDateTime,
  operationTaskLabel,
  operationTaskRowTitle,
  OPERATION_ACTION_LABELS,
  type OperationActionKind,
  type OperationResultState,
  workOrderLabel,
} from './operationPresentation'

const props = defineProps<{
  result: OperationResultState | null
  selected: BusinessConsoleMesOperationTaskRow | null
  open: boolean
  actionPending: boolean
  operationScopeReady: boolean
  confirmingComplete: boolean
  currentSops: BusinessConsoleCurrentSopDocumentItem[]
  sopsPending: boolean
  sopsError?: unknown
  openingSopFileId: string | null
  sopFileError: string
  operationResultUnknown?: boolean
}>()

const emit = defineEmits<{
  action: [action: OperationActionKind]
  retry: []
  continue: []
  back: []
  'update:open': [open: boolean]
  cancelComplete: []
  refreshSops: []
  openSop: [sop: BusinessConsoleCurrentSopDocumentItem]
}>()

const availableActions = computed(() => actionsForOperationTask(props.selected))
const blockReasonDisplays = computed(() =>
  describeMesReadinessReasons(props.selected?.blockReasons),
)
</script>

<template>
  <NvMobileResult
    v-if="result"
    :status="result.status"
    :title="result.title"
    :description="result.description"
  >
    <template #actions>
      <button
        v-if="result.status === 'success'"
        type="button"
        class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
        @click="emit('continue')"
      >
        继续
      </button>
      <button
        v-else
        type="button"
        data-testid="retry-action"
        class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
        @click="emit('retry')"
      >
        重试
      </button>
      <button
        type="button"
        data-testid="back-to-list"
        :disabled="operationResultUnknown"
        class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
        @click="emit('back')"
      >
        返回列表
      </button>
    </template>
  </NvMobileResult>

  <NvBottomSheet
    :open="open"
    :title="selected ? operationTaskRowTitle(selected) : ''"
    @update:open="emit('update:open', $event)"
  >
    <div v-if="selected" class="space-y-3 pb-2">
      <p class="text-sm text-muted-foreground">
        当前状态：{{ operationTaskStatusLabel(selected.status) }}
      </p>
      <dl
        class="grid grid-cols-[5rem_minmax(0,1fr)] gap-x-3 gap-y-2 rounded-lg border border-border px-3 py-3 text-sm"
      >
        <dt class="text-muted-foreground">工单</dt>
        <dd class="min-w-0 break-all text-foreground">{{ workOrderLabel(selected) }}</dd>
        <dt class="text-muted-foreground">工序任务</dt>
        <dd class="min-w-0 break-all text-foreground">{{ operationTaskLabel(selected) }}</dd>
        <dt class="text-muted-foreground">设备</dt>
        <dd class="min-w-0 break-all text-foreground">{{ deviceLabel(selected) }}</dd>
        <dt class="text-muted-foreground">门禁评估</dt>
        <dd class="text-foreground">{{ formatOperationDateTime(selected.evaluatedAtUtc) }}</dd>
      </dl>
      <p v-if="selected.assignedUserName" class="text-sm text-muted-foreground">
        受派工人：{{ selected.assignedUserName }}
      </p>

      <section
        v-if="blockReasonDisplays.length"
        data-testid="operation-block-reasons"
        class="space-y-2 rounded-lg border border-destructive/40 bg-destructive/5 px-3 py-3"
      >
        <h2 class="text-sm font-semibold text-foreground">当前不能开始</h2>
        <div
          v-for="reason in blockReasonDisplays"
          :key="reason.code"
          class="rounded-md bg-card px-3 py-2"
        >
          <p class="text-sm font-medium text-foreground">{{ reason.category }}</p>
          <p class="mt-1 text-sm text-muted-foreground">{{ reason.detail || reason.label }}</p>
        </div>
      </section>

      <section class="space-y-2 rounded-lg border border-border px-3 py-3">
        <div class="flex items-center justify-between gap-3">
          <h2 class="text-sm font-semibold text-foreground">当前SOP</h2>
          <span v-if="selected.operationCode" class="font-mono text-xs text-muted-foreground">
            {{ selected.operationCode }}
          </span>
        </div>
        <p v-if="!selected.operationCode" class="text-sm text-muted-foreground">
          当前任务未绑定标准工序。
        </p>
        <RetryableListError
          v-else-if="sopsError"
          :error="sopsError"
          :pending="sopsPending"
          fallback="加载SOP失败，请稍后重试。"
          test-id="sops-error"
          @retry="emit('refreshSops')"
        />
        <template v-else>
          <p v-if="sopsPending" class="text-sm text-muted-foreground">正在加载SOP...</p>
          <div v-else-if="currentSops.length" class="space-y-2">
            <div
              v-for="sop in currentSops"
              :key="`${sop.documentNumber}-${sop.revision}-${sop.fileId}`"
              class="rounded-md bg-muted px-3 py-2 text-sm"
            >
              <p class="font-medium text-foreground">{{ sop.fileName || sop.documentNumber }}</p>
              <p class="text-xs text-muted-foreground">
                {{ sop.documentNumber }} · rev {{ sop.revision }} · 生效
                {{ formatOperationDate(sop.effectiveDate) }}
              </p>
              <button
                type="button"
                class="mt-2 min-h-touch rounded-md border border-border bg-card px-3 text-sm font-medium text-foreground disabled:opacity-60"
                :disabled="openingSopFileId === sop.fileId"
                @click="emit('openSop', sop)"
              >
                查看SOP
              </button>
            </div>
          </div>
          <p v-else class="text-sm text-muted-foreground">当前没有已生效SOP。</p>
          <p
            v-if="sopFileError"
            data-testid="sop-file-error"
            class="text-sm text-destructive"
            role="alert"
          >
            {{ sopFileError }}
          </p>
        </template>
      </section>

      <div v-if="confirmingComplete" class="space-y-3">
        <p class="text-sm text-foreground">完成后该工序将进入终态，确认完成？</p>
        <button
          type="button"
          data-testid="confirm-complete"
          :disabled="actionPending || !operationScopeReady"
          class="min-h-touch w-full rounded-lg bg-destructive text-base font-medium text-destructive-foreground disabled:opacity-60"
          @click="emit('action', 'complete')"
        >
          确认完成
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="emit('cancelComplete')"
        >
          取消
        </button>
      </div>

      <div v-else class="space-y-2">
        <button
          v-for="action in availableActions"
          :key="action"
          type="button"
          :data-testid="`action-${action}`"
          :disabled="actionPending || !operationScopeReady"
          class="min-h-touch w-full rounded-lg text-base font-medium disabled:opacity-60"
          :class="
            action === 'complete'
              ? 'bg-destructive text-destructive-foreground'
              : 'bg-primary text-primary-foreground'
          "
          @click="emit('action', action)"
        >
          {{ OPERATION_ACTION_LABELS[action] }}
        </button>
        <p
          v-if="availableActions.length === 0"
          class="rounded-lg border border-dashed border-border px-4 py-4 text-center text-sm text-muted-foreground"
        >
          当前状态无可执行动作
        </p>
      </div>
    </div>
  </NvBottomSheet>
</template>
