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
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  engineeringChangeNumberMaxLength: View['ENGINEERING_CHANGE_NUMBER_MAX_LENGTH']
  engineeringChangeReasonMaxLength: View['ENGINEERING_CHANGE_REASON_MAX_LENGTH']
  decisionFieldErrors: View['decisionFieldErrors']
  decisionForm: View['decisionForm']
  decisionShowErrors: View['decisionShowErrors']
  decisionValidationSummary: View['decisionValidationSummary']
  recordEngineeringChangeDecisionPending: View['recordEngineeringChangeDecisionPending']
  submitDecision: View['submitDecision']
  workOrderLabel: View['workOrderLabel']
}>()

const decisionOpen = defineModel<View['decisionOpen']>('decisionOpen', { required: true })
</script>

<template>
  <NvAlertDialog v-model:open="decisionOpen">
    <NvAlertDialogContent class="sm:max-w-md">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>记录工程变更决策 · {{ workOrderLabel }}</NvAlertDialogTitle>
        <NvAlertDialogDescription
          >填写变更单号和处理意见，决策会回写到该工单。</NvAlertDialogDescription
        >
      </NvAlertDialogHeader>
      <NvFieldGroup class="grid gap-3">
        <p
          v-if="decisionShowErrors && decisionValidationSummary.length"
          class="text-sm text-destructive"
          role="alert"
          data-testid="engineering-change-decision-errors"
        >
          请修正标红的必填项后再保存。
        </p>
        <NvField :data-invalid="decisionShowErrors && !!decisionFieldErrors.changeNumber">
          <NvFieldLabel for="engineering-change-number"
            >变更单号 <span class="text-destructive">*</span></NvFieldLabel
          >
          <NvInput
            id="engineering-change-number"
            v-model="decisionForm.changeNumber"
            :maxlength="engineeringChangeNumberMaxLength"
            placeholder="请输入变更单号"
          />
          <p
            v-if="decisionShowErrors && decisionFieldErrors.changeNumber"
            class="text-xs text-destructive"
          >
            {{ decisionFieldErrors.changeNumber }}
          </p>
        </NvField>
        <NvField :data-invalid="decisionShowErrors && !!decisionFieldErrors.decision">
          <NvFieldLabel for="engineering-change-decision"
            >处理意见 <span class="text-destructive">*</span></NvFieldLabel
          >
          <NvSelect v-model="decisionForm.decision">
            <NvSelectTrigger id="engineering-change-decision"
              ><NvSelectValue placeholder="请选择处理意见"
            /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem value="continue-with-archived-version">继续使用现行版本</NvSelectItem>
              <NvSelectItem value="abort-work-order">停止工单</NvSelectItem>
            </NvSelectContent>
          </NvSelect>
          <p
            v-if="decisionShowErrors && decisionFieldErrors.decision"
            class="text-xs text-destructive"
          >
            {{ decisionFieldErrors.decision }}
          </p>
        </NvField>
        <NvField :data-invalid="decisionShowErrors && !!decisionFieldErrors.reason">
          <NvFieldLabel for="engineering-change-reason"
            >决策说明 <span class="text-destructive">*</span></NvFieldLabel
          >
          <NvInput
            id="engineering-change-reason"
            v-model="decisionForm.reason"
            :maxlength="engineeringChangeReasonMaxLength"
            placeholder="请说明决策依据"
          />
          <p
            v-if="decisionShowErrors && decisionFieldErrors.reason"
            class="text-xs text-destructive"
          >
            {{ decisionFieldErrors.reason }}
          </p>
        </NvField>
      </NvFieldGroup>
      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="recordEngineeringChangeDecisionPending"
          @click="decisionOpen = false"
          >返回</NvButton
        >
        <NvButton
          type="button"
          :disabled="recordEngineeringChangeDecisionPending"
          data-testid="confirm-engineering-change-decision"
          @click="submitDecision"
        >
          <NvSpinner v-if="recordEngineeringChangeDecisionPending" aria-hidden="true" />
          保存决策
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
