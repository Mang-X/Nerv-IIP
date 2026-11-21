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
  NvCombobox,
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
  approverOptions: View['approverOptions']
  departmentsPending: View['departmentsPending']
  documentTypeOptions: View['documentTypeOptions']
  submitTemplate: View['submitTemplate']
  templateCodeSuggestions: View['templateCodeSuggestions']
  templateError: View['templateError']
  templateForm: View['templateForm']
  workersPending: View['workersPending']
}>()

const templateOpen = defineModel<View['templateOpen']>('templateOpen', { required: true })
</script>

<template>
  <NvDialog v-model:open="templateOpen">
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>维护审批模板</NvDialogTitle>
        <NvDialogDescription class="sr-only">维护审批模板与首个步骤。</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitTemplate">
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="approval-template-code">模板</NvFieldLabel>
            <NvCombobox
              id="approval-template-code"
              v-model="templateForm.templateCode"
              :suggestions="templateCodeSuggestions"
              placeholder="选择已有模板或填写新模板编码"
              empty-text="暂无已有模板，请填写新模板编码"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-doc">单据类型</NvFieldLabel>
            <NvSelect v-model="templateForm.documentType">
              <NvSelectTrigger id="approval-template-doc">
                <NvSelectValue placeholder="选择单据类型" />
              </NvSelectTrigger>
              <NvSelectContent>
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
            <NvFieldLabel for="approval-template-version">版本</NvFieldLabel>
            <NvInput
              id="approval-template-version"
              v-model="templateForm.version"
              type="number"
              min="1"
              step="1"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-active">状态</NvFieldLabel>
            <NvSelect v-model="templateForm.isActive">
              <NvSelectTrigger id="approval-template-active"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="true">启用</NvSelectItem>
                <NvSelectItem value="false">停用</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-step-no">步骤序号</NvFieldLabel>
            <NvInput
              id="approval-template-step-no"
              v-model="templateForm.stepNo"
              type="number"
              min="1"
              step="1"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-step-name">步骤名称</NvFieldLabel>
            <NvInput
              id="approval-template-step-name"
              v-model="templateForm.stepName"
              autocomplete="off"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-actor-type">审批人类型</NvFieldLabel>
            <NvSelect v-model="templateForm.approverType">
              <NvSelectTrigger id="approval-template-actor-type"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="role">角色</NvSelectItem>
                <NvSelectItem value="user">人员</NvSelectItem>
                <NvSelectItem value="department">部门</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-actor-ref">审批人</NvFieldLabel>
            <NvEntityPicker
              v-if="templateForm.approverType !== 'role'"
              id="approval-template-actor-ref"
              v-model="templateForm.approverRef"
              :options="approverOptions"
              :show-code="templateForm.approverType !== 'user'"
              :title="templateForm.approverType === 'user' ? '选择人员' : '选择部门'"
              :placeholder="templateForm.approverType === 'user' ? '选择人员' : '选择部门'"
              :empty-text="
                templateForm.approverType === 'user'
                  ? '暂无员工，请先在基础数据维护员工'
                  : '暂无部门，请先在基础数据维护组织架构'
              "
              :loading="templateForm.approverType === 'user' ? workersPending : departmentsPending"
              aria-label="审批人"
              clearable
            />
            <!-- 角色目录在平台管理台（IAM）维护，业务网关暂无角色读面，保留手工录入。 -->
            <NvInput
              v-else
              id="approval-template-actor-ref"
              v-model="templateForm.approverRef"
              autocomplete="off"
              placeholder="填写角色标识"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="approval-template-due">处理时限（小时）</NvFieldLabel>
            <NvInput
              id="approval-template-due"
              v-model="templateForm.dueInHours"
              type="number"
              min="1"
              step="1"
              placeholder="可选"
            />
          </NvField>
        </NvFieldGroup>
        <NvFieldError v-if="templateError" :errors="[templateError]" />
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline">取消</NvButton>
          </NvDialogClose>
          <NvButton type="submit" :disabled="approval.saveTemplatePending.value">
            <NvSpinner v-if="approval.saveTemplatePending.value" aria-hidden="true" />
            保存模板
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
