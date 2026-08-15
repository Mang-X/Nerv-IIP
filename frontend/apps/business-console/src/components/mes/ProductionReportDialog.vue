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
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  Spinner,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon } from '@lucide/vue'
import { computed } from 'vue'

import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  useProductionReportForm,
  type ProductionReportContext,
} from '@/composables/mes/useProductionReportForm'

/**
 * 生产报工 —— 「带出式录入」样板。
 *
 * 规范（owner）：从工单或工序任务进入报工，系统带出必要字段，一线人员只补充数量和完成状态；
 * 工单与工序只能从工单列表行或工序任务行带入。因此本组件：
 * - 不提供任何工单/工序的挑选控件，`context` 为空即不可提交；
 * - 带出的字段走 CarriedContextSummary 只读展示，不做 readonly 输入框；
 * - 录入项只有合格数量、不合格数量、是否完成本工序；
 * - 零说明文案，结果一律 toast。
 */
const props = defineProps<{
  open: boolean
  /** 报工对象，由所选行带出；为空时弹窗不渲染内容也不可提交。 */
  context: ProductionReportContext | null
}>()
const emit = defineEmits<{
  'update:open': [value: boolean]
  reported: []
}>()

const openModel = computed({
  get: () => props.open,
  set: (value: boolean) => emit('update:open', value),
})

const {
  form,
  invalid,
  showErrors,
  canSubmit,
  canCompleteOperation,
  intentLocked,
  recordProductionReportPending,
  quantitySnapshotPending,
  quantityValidationMessage,
  overproductionConfirmationRequired,
  reportScopeMessage,
  reportScopePending,
  reportScopeReady,
  submit,
} = useProductionReportForm(() => props.context, {
  onReported: () => emit('reported'),
  onStateChanged: () => emit('update:open', false),
})

const operationLabel = computed(() => {
  const ctx = props.context
  if (!ctx) return ''
  const sequence = ctx.operationSequence
  const no = ctx.operationTaskNo ?? ctx.operationTaskId
  return sequence !== null && sequence !== undefined ? `第 ${sequence} 道 · ${no}` : no
})
const workOrderLabel = computed(
  () => props.context?.workOrderNo ?? props.context?.workOrderId ?? '',
)
const contextItems = computed(() => {
  const ctx = props.context
  if (!ctx) return []
  return [
    { label: '工单', value: workOrderLabel.value },
    { label: '工序', value: operationLabel.value },
    { label: '工作中心', value: ctx.workCenterLabel },
    { label: '物料', value: ctx.skuLabel },
    {
      label: '计划数量',
      value:
        ctx.plannedQuantity === null || ctx.plannedQuantity === undefined
          ? undefined
          : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(
              ctx.plannedQuantity,
            ),
    },
  ]
})

async function onSubmit() {
  const ok = await submit()
  if (ok) openModel.value = false
}
</script>

<template>
  <NvDialog v-model:open="openModel">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>报工</NvDialogTitle>
        <!-- 报工对象已在下方只读区完整呈现；此处仅供读屏播报，不在界面上再写一遍说明。 -->
        <NvDialogDescription class="sr-only">
          报工对象：工单 {{ workOrderLabel }}，工序 {{ operationLabel }}。
        </NvDialogDescription>
      </NvDialogHeader>

      <form v-if="context" class="grid content-start gap-4" @submit.prevent="onSubmit">
        <CarriedContextSummary label="报工对象" :items="contextItems" />

        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="report-good">
              合格数量 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvInput
              id="report-good"
              v-model="form.goodQuantity"
              inputmode="decimal"
              min="0"
              step="any"
              type="number"
              autofocus
              :disabled="intentLocked || !reportScopeReady"
              :data-invalid="showErrors && invalid.goodQuantity ? '' : undefined"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="report-scrap">不合格数量</NvFieldLabel>
            <NvInput
              id="report-scrap"
              v-model="form.scrapQuantity"
              inputmode="decimal"
              min="0"
              step="any"
              type="number"
              :disabled="intentLocked || !reportScopeReady"
              :data-invalid="showErrors && invalid.scrapQuantity ? '' : undefined"
            />
          </NvField>
          <NvField
            orientation="horizontal"
            class="items-center justify-between rounded-lg border p-3 sm:col-span-2"
          >
            <NvFieldLabel for="report-complete">本工序已完成</NvFieldLabel>
            <NvCheckbox
              id="report-complete"
              v-model="form.completesOperation"
              :disabled="!canCompleteOperation || intentLocked || !reportScopeReady"
            />
          </NvField>
        </NvFieldGroup>

        <p
          v-if="reportScopeMessage"
          data-testid="report-scope-message"
          class="text-sm text-destructive"
          role="alert"
        >
          {{ reportScopeMessage }}
        </p>
        <p
          v-if="quantityValidationMessage"
          data-testid="production-quantity-message"
          class="rounded-lg border border-warning bg-warning/10 p-3 text-sm font-medium text-warning-strong"
          role="alert"
        >
          {{ quantityValidationMessage }}
        </p>
        <!-- 点提交才标红；未通过不发请求。 -->
        <p
          v-if="showErrors && (invalid.goodQuantity || invalid.scrapQuantity)"
          class="text-sm text-destructive"
          role="alert"
        >
          请填写数量：合格与不合格均不可为负，且合计需大于 0。
        </p>
        <p v-if="intentLocked" class="text-sm text-warning-strong">
          提交结果未知，当前内容已锁定；仅可按原内容重试。
        </p>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="openModel = false">取消</NvButton>
          <NvButton
            type="submit"
            :disabled="
              !canSubmit ||
              recordProductionReportPending ||
              reportScopePending ||
              quantitySnapshotPending
            "
          >
            <Spinner
              v-if="recordProductionReportPending || reportScopePending || quantitySnapshotPending"
              aria-hidden="true"
            />
            <ClipboardCheckIcon v-else aria-hidden="true" />
            {{ overproductionConfirmationRequired ? '确认超产并提交' : '提交报工' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
