<script setup lang="ts">
import type { DowntimeTarget } from '../downtimeTarget'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import { toLocalDateTimeInput } from '../downtimeDates'

defineProps<{
  recordShowErrors: boolean
  selectedDowntimeTarget: DowntimeTarget | undefined
  eligibleDowntimeTargets: DowntimeTarget[]
  downtimeReasonOptions: { value: string; label: string }[]
  validStartedAtUtc: () => string | undefined
  toLocalDateTimeInput: (date: Date) => string
  recordPending: boolean
}>()

const recordDialogOpen = defineModel<boolean>('recordDialogOpen', { required: true })

const recordTargetKey = defineModel<string>('recordTargetKey', { required: true })

const recordReasonCode = defineModel<string>('recordReasonCode', { required: true })

const recordStartedAtLocal = defineModel<string>('recordStartedAtLocal', { required: true })

const emit = defineEmits<{
  submit: []
}>()
</script>

<template>
  <NvDialog v-model:open="recordDialogOpen">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>确认登记停机</NvDialogTitle>
        <NvDialogDescription>
          从当前主体可见且可管理的工序中选择真实作业上下文，登记后将刷新停机事件列表。
        </NvDialogDescription>
      </NvDialogHeader>

      <NvFieldGroup class="grid gap-4">
        <NvField :data-invalid="recordShowErrors && !selectedDowntimeTarget">
          <NvFieldLabel>工单与工序</NvFieldLabel>
          <NvSelect v-model="recordTargetKey" aria-label="工单与工序">
            <NvSelectTrigger><NvSelectValue placeholder="选择工单与工序" /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem
                v-for="target in eligibleDowntimeTargets"
                :key="target.key"
                :value="target.key"
              >
                {{ target.label }}
              </NvSelectItem>
            </NvSelectContent>
          </NvSelect>
          <p v-if="recordShowErrors && !selectedDowntimeTarget" class="text-sm text-destructive">
            请选择同时具备工单、工序、工作中心与设备上下文的记录。
          </p>
        </NvField>

        <dl v-if="selectedDowntimeTarget" class="grid gap-2 rounded-md border p-3 text-sm">
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工单</dt>
            <dd>
              {{
                selectedDowntimeTarget.operationTask.workOrderNo ||
                selectedDowntimeTarget.workOrderId
              }}
            </dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工序</dt>
            <dd>
              {{
                selectedDowntimeTarget.operationTask.operationTaskNo ||
                selectedDowntimeTarget.operationTaskId
              }}
            </dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工作中心</dt>
            <dd>
              {{
                selectedDowntimeTarget.operationTask.workCenterName ||
                selectedDowntimeTarget.workCenterId
              }}
            </dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">设备</dt>
            <dd>
              {{
                selectedDowntimeTarget.operationTask.deviceAssetName ||
                selectedDowntimeTarget.operationTask.deviceAssetCode ||
                selectedDowntimeTarget.deviceAssetId
              }}
            </dd>
          </div>
        </dl>

        <NvField :data-invalid="recordShowErrors && !recordReasonCode.trim()">
          <NvFieldLabel>停机原因</NvFieldLabel>
          <NvSelect v-model="recordReasonCode" aria-label="停机原因">
            <NvSelectTrigger><NvSelectValue placeholder="选择停机原因" /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem
                v-for="option in downtimeReasonOptions"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </NvSelectItem>
            </NvSelectContent>
          </NvSelect>
          <p v-if="recordShowErrors && !recordReasonCode.trim()" class="text-sm text-destructive">
            请选择已配置的停机原因。
          </p>
        </NvField>

        <NvField :data-invalid="recordShowErrors && !validStartedAtUtc()">
          <NvFieldLabel>停机开始时间</NvFieldLabel>
          <NvInput
            v-model="recordStartedAtLocal"
            type="datetime-local"
            aria-label="停机开始时间"
            :max="toLocalDateTimeInput(new Date())"
          />
          <p v-if="recordShowErrors && !validStartedAtUtc()" class="text-sm text-destructive">
            请输入有效且不晚于当前时间的停机开始时间。
          </p>
        </NvField>
      </NvFieldGroup>

      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="recordDialogOpen = false">
          取消
        </NvButton>
        <NvButton
          type="button"
          :disabled="recordPending"
          data-testid="record-downtime-submit"
          @click="emit('submit')"
        >
          {{ recordPending ? '登记中…' : '确认登记' }}
        </NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
