<script setup lang="ts">
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvEntityPicker,
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
  canSubmitIssue: View['canSubmitIssue']
  createMaterialIssueRequestPending: View['createMaterialIssueRequestPending']
  issueForm: View['issueForm']
  issueUomBlockReason: View['issueUomBlockReason']
  issueUomCode: View['issueUomCode']
  operationTaskOptions: View['operationTaskOptions']
  submitIssue: View['submitIssue']
  workOrderLabel: View['workOrderLabel']
}>()

const issueOpen = defineModel<View['issueOpen']>('issueOpen', { required: true })
</script>

<template>
  <!-- 发起领料 -->
  <NvAlertDialog v-model:open="issueOpen">
    <NvAlertDialogContent class="sm:max-w-md">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>发起领料 · {{ workOrderLabel }}</NvAlertDialogTitle>
        <NvAlertDialogDescription>
          领料发起后由仓库生成出库与拣货任务，出库单号回写到本页；物料到线边后再确认收料。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvFieldGroup class="grid gap-3">
        <NvField>
          <NvFieldLabel for="issue-material">
            物料 <span class="text-destructive">*</span>
          </NvFieldLabel>
          <DirectoryPicker
            id="issue-material"
            v-model="issueForm.materialId"
            directory-type="material"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="issue-uom">单位</NvFieldLabel>
          <!-- 单位来自物料主档，不给用户编辑，也不用占位值冒充。 -->
          <p v-if="issueUomCode" class="text-sm text-foreground" data-testid="issue-uom">
            {{ issueUomCode }}（取自物料主档基本计量单位）
          </p>
          <p v-else class="text-sm text-destructive" data-testid="issue-uom-blocked">
            {{ issueUomBlockReason || '请先选择物料。' }}
          </p>
        </NvField>
        <NvField>
          <NvFieldLabel for="issue-quantity">数量</NvFieldLabel>
          <NvInput
            id="issue-quantity"
            v-model="issueForm.quantity"
            inputmode="decimal"
            placeholder="留空表示按用料需求量领取"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="issue-operation">工序任务</NvFieldLabel>
          <NvEntityPicker
            id="issue-operation"
            v-model="issueForm.operationTaskId"
            :options="operationTaskOptions"
            title="选择工序任务"
            placeholder="可留空，指定后领料归属该工序"
            empty-text="本工单还没有工序任务"
            :show-code="false"
            clearable
            aria-label="工序任务"
          />
        </NvField>
      </NvFieldGroup>
      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="createMaterialIssueRequestPending"
          @click="issueOpen = false"
        >
          返回
        </NvButton>
        <NvButton
          type="button"
          :disabled="!canSubmitIssue || createMaterialIssueRequestPending"
          data-testid="submit-material-issue"
          @click="submitIssue"
        >
          <NvSpinner v-if="createMaterialIssueRequestPending" aria-hidden="true" />
          确认发起领料
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
