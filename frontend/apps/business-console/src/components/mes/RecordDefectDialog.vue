<script setup lang="ts">
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  Spinner,
  type EntityPickerOption,
} from '@nerv-iip/ui'

defineProps<{
  defectOptions: EntityPickerOption[]
  operationOptions: EntityPickerOption[]
  pending: boolean
  showErrors: boolean
}>()

const emit = defineEmits<{ submit: [] }>()
const open = defineModel<boolean>('open', { required: true })
const targetKey = defineModel<string>('targetKey', { required: true })
const defectCode = defineModel<string>('defectCode', { required: true })
const defectQuantity = defineModel<string>('defectQuantity', { required: true })
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>登记生产过程缺陷</NvDialogTitle>
        <NvDialogDescription>
          从当前主体可见且在质量登记范围内的工单选择上下文，可按需关联具体工序。
        </NvDialogDescription>
      </NvDialogHeader>

      <form class="grid gap-4" @submit.prevent="emit('submit')">
        <p
          v-if="showErrors"
          class="text-sm text-destructive"
          role="alert"
          data-testid="defect-validation-summary"
        >
          请完整填写工单上下文、缺陷码和大于 0 的缺陷数量（已标红）。
        </p>

        <NvFieldGroup class="grid gap-3">
          <NvField>
            <NvFieldLabel for="defect-operation-task">
              工单与可选工序 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvEntityPicker
              id="defect-operation-task"
              v-model="targetKey"
              :options="operationOptions"
              title="选择工单与工序"
              placeholder="选择工单与工序"
              source-text="仅列当前主体可见且在质量登记范围内的工单与工序"
              empty-text="当前授权范围内暂无可登记缺陷的工单"
              aria-label="工单与工序"
              :data-invalid="showErrors && !targetKey"
              :disabled="pending"
            />
          </NvField>

          <NvField>
            <NvFieldLabel for="defect-code">
              缺陷码 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvEntityPicker
              id="defect-code"
              v-model="defectCode"
              :options="defectOptions"
              title="选择缺陷码"
              placeholder="选择缺陷码"
              source-text="数据来自质量原因码目录"
              empty-text="暂无可用缺陷码，请先在质量管理维护"
              aria-label="缺陷码"
              :data-invalid="showErrors && !defectCode.trim()"
              :disabled="pending"
            />
          </NvField>

          <NvField>
            <NvFieldLabel for="defect-quantity">
              缺陷数量 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvInput
              id="defect-quantity"
              v-model="defectQuantity"
              aria-label="缺陷数量"
              inputmode="decimal"
              min="0"
              step="any"
              type="number"
              :data-invalid="showErrors && !(Number(defectQuantity) > 0)"
              :disabled="pending"
            />
          </NvField>
        </NvFieldGroup>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" :disabled="pending" @click="open = false">
            取消
          </NvButton>
          <NvButton type="submit" :disabled="pending">
            <Spinner v-if="pending" aria-hidden="true" />
            {{ pending ? '登记中…' : '确认登记' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
