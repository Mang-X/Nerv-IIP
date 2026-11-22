<script setup lang="ts">
import {
  NvButton,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldError,
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
import type { useApprovalPage } from '../useApprovalPage'

type View = ShallowUnwrapRef<ReturnType<typeof useApprovalPage>>

defineProps<{
  approval: View['approval']
  delegateOptions: View['delegateOptions']
  delegationError: View['delegationError']
  delegationForm: View['delegationForm']
  documentTypeOptions: View['documentTypeOptions']
  submitDelegation: View['submitDelegation']
  workersPending: View['workersPending']
}>()

const delegationOpen = defineModel<View['delegationOpen']>('delegationOpen', { required: true })

const delegationDocumentType = defineModel<View['delegationDocumentType']>(
  'delegationDocumentType',
  { required: true },
)
</script>

<template>
  <NvDialog v-model:open="delegationOpen">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>新建审批委托</NvDialogTitle>
        <NvDialogDescription class="sr-only">设置审批任务的临时代理人。</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitDelegation">
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="approval-delegator">委托人</NvFieldLabel>
            <NvInput
              id="approval-delegator"
              v-model="delegationForm.delegatorActorRef"
              autocomplete="off"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-delegate">代理人</NvFieldLabel>
            <NvEntityPicker
              id="approval-delegate"
              v-model="delegationForm.delegateActorRef"
              :options="delegateOptions"
              :show-code="false"
              title="选择代理人"
              placeholder="选择代理人"
              empty-text="暂无员工，请先在基础数据维护员工"
              :loading="workersPending"
              aria-label="代理人"
              clearable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-delegation-doc">单据范围</NvFieldLabel>
            <NvSelect v-model="delegationDocumentType">
              <NvSelectTrigger id="approval-delegation-doc">
                <NvSelectValue placeholder="全部单据" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="all">全部单据</NvSelectItem>
                <NvSelectItem
                  v-for="option in documentTypeOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-delegation-from">开始时间</NvFieldLabel>
            <NvInput
              id="approval-delegation-from"
              v-model="delegationForm.effectiveFromUtc"
              type="datetime-local"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-delegation-to">截止时间</NvFieldLabel>
            <NvInput
              id="approval-delegation-to"
              v-model="delegationForm.effectiveToUtc"
              type="datetime-local"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-delegation-reason">原因</NvFieldLabel>
            <NvInput
              id="approval-delegation-reason"
              v-model="delegationForm.reason"
              autocomplete="off"
            />
          </NvField>
        </NvFieldGroup>
        <NvFieldError v-if="delegationError" :errors="[delegationError]" />
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline">取消</NvButton>
          </NvDialogClose>
          <NvButton type="submit" :disabled="approval.createDelegationPending.value">
            <NvSpinner v-if="approval.createDelegationPending.value" aria-hidden="true" />
            保存委托
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
