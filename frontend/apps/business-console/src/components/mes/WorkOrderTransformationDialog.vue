<script setup lang="ts">
import type {
  MesWorkOrderTransformationLine,
  MesWorkOrderTransformationResult,
} from '@/composables/useBusinessMes'
import type {
  MergeValidationInput,
  SplitTargetDraft,
  WorkOrderTransformationSource,
} from '@/composables/mes/workOrderTransformation'
import {
  parsePositiveQuantity,
  validateMergeInput,
  validateSplitInput,
} from '@/composables/mes/workOrderTransformation'
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
  NvStatusBadge,
  Spinner,
} from '@nerv-iip/ui'
import { PlusIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, ref, watch } from 'vue'

export type WorkOrderTransformationMode = 'split' | 'merge'
export type WorkOrderTransformationState =
  | 'idle'
  | 'loading'
  | 'accepted'
  | 'success'
  | 'error'
  | 'conflict'

export type SplitTransformationSubmit = {
  targets: Array<{ workOrderId: string; quantity: number }>
  reason: string
  idempotencyKey: string
}

export type MergeTransformationSubmit = {
  sourceWorkOrderIds: string[]
  targetWorkOrderId: string
  reason: string
  idempotencyKey: string
}

const props = withDefaults(
  defineProps<{
    open: boolean
    mode: WorkOrderTransformationMode
    state?: WorkOrderTransformationState
    pending?: boolean
    source?: WorkOrderTransformationSource & {
      label?: string
      skuLabel?: string
    }
    sources?: Array<WorkOrderTransformationSource & { label?: string; skuLabel?: string }>
    result?: MesWorkOrderTransformationResult | null
    errorMessage?: string
    idempotencyKey: string
  }>(),
  {
    state: 'idle',
    pending: false,
    sources: () => [],
    result: null,
    errorMessage: '',
  },
)

const emit = defineEmits<{
  'update:open': [value: boolean]
  submit: [value: SplitTransformationSubmit | MergeTransformationSubmit]
  retryReadback: []
}>()

const splitTargets = reactive<SplitTargetDraft[]>([
  { workOrderId: '', quantity: '' },
  { workOrderId: '', quantity: '' },
])
const mergeForm = reactive({ targetWorkOrderId: '', reason: '' })
const splitReason = ref('')
const showErrors = ref(false)

const isSplit = computed(() => props.mode === 'split')
const sourceQuantity = computed(() => props.source?.quantity ?? undefined)
const splitErrors = computed(() =>
  validateSplitInput({
    sourceWorkOrderId: props.source?.workOrderId ?? '',
    sourceQuantity: sourceQuantity.value,
    targets: splitTargets,
    reason: splitReason.value,
  }),
)
const mergeInput = computed<MergeValidationInput>(() => ({
  sources: props.sources,
  targetWorkOrderId: mergeForm.targetWorkOrderId,
  reason: mergeForm.reason,
}))
const mergeErrors = computed(() => validateMergeInput(mergeInput.value))
const validationErrors = computed(() => (isSplit.value ? splitErrors.value : mergeErrors.value))

const statusText = computed(() => {
  switch (props.state) {
    case 'loading':
      return '正在提交拆分或合并请求…'
    case 'accepted':
      return '请求已受理，结果尚未完成回读。'
    case 'success':
      return '操作已完成，结果已回读。'
    case 'conflict':
      return '数据冲突（409），请刷新工单后重试。'
    case 'error':
      return props.errorMessage || '操作失败，请检查填写内容后重试。'
    default:
      return ''
  }
})

const statusTone = computed(() => {
  if (props.state === 'success') return 'success' as const
  if (props.state === 'accepted') return 'warning' as const
  if (props.state === 'error' || props.state === 'conflict') return 'danger' as const
  return 'neutral' as const
})

function resetForm() {
  splitTargets.splice(
    0,
    splitTargets.length,
    { workOrderId: '', quantity: '' },
    { workOrderId: '', quantity: '' },
  )
  splitReason.value = ''
  mergeForm.targetWorkOrderId = ''
  mergeForm.reason = ''
  showErrors.value = false
}

watch(
  () => [props.open, props.mode] as const,
  ([open]) => {
    if (open) resetForm()
  },
  { immediate: true },
)

function addTarget() {
  if (splitTargets.length < 10) splitTargets.push({ workOrderId: '', quantity: '' })
}

function removeTarget(index: number) {
  if (splitTargets.length <= 2) return
  splitTargets.splice(index, 1)
}

function submit() {
  showErrors.value = true
  if (validationErrors.value.length || props.pending) return
  if (isSplit.value) {
    emit('submit', {
      targets: splitTargets.map((target) => ({
        workOrderId: target.workOrderId.trim(),
        quantity: parsePositiveQuantity(target.quantity) as number,
      })),
      reason: splitReason.value.trim(),
      idempotencyKey: props.idempotencyKey,
    })
    return
  }
  emit('submit', {
    sourceWorkOrderIds: props.sources.map((source) => source.workOrderId.trim()),
    targetWorkOrderId: mergeForm.targetWorkOrderId.trim(),
    reason: mergeForm.reason.trim(),
    idempotencyKey: props.idempotencyKey,
  })
}

function updateReason(value: unknown) {
  if (isSplit.value) splitReason.value = String(value)
  else mergeForm.reason = String(value)
}

function close() {
  if (!props.pending) emit('update:open', false)
}

function lineLabel(line: MesWorkOrderTransformationLine) {
  return `${line.sourceWorkOrderId} → ${line.targetWorkOrderId}`
}
</script>

<template>
  <NvDialog :open="open" @update:open="emit('update:open', $event)">
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>{{ isSplit ? '拆分工单' : '合并工单' }}</NvDialogTitle>
        <NvDialogDescription v-if="isSplit">
          将
          {{ source?.label || source?.workOrderId || '当前工单' }}
          拆成多个新的子工单；数量必须守恒，目标标识不能已存在。
        </NvDialogDescription>
        <NvDialogDescription v-else>
          将选中的同 SKU 工单合并为一个新工单；服务端会按源工单数量合计生成目标数量。
        </NvDialogDescription>
      </NvDialogHeader>

      <div class="grid gap-4">
        <div
          v-if="statusText"
          class="flex items-center gap-2 rounded-md border p-3 text-sm"
          :class="
            props.state === 'error' || props.state === 'conflict'
              ? 'border-destructive/40 bg-destructive/5 text-destructive'
              : 'border-border bg-muted/30 text-foreground'
          "
          role="status"
          :data-state="props.state"
          data-testid="transformation-status"
        >
          <Spinner v-if="props.state === 'loading'" aria-hidden="true" />
          <NvStatusBadge v-else :label="statusText" :tone="statusTone" />
          <span v-if="props.state === 'loading'">{{ statusText }}</span>
        </div>

        <section
          v-if="isSplit"
          class="grid gap-3 rounded-md border bg-muted/20 p-3"
          aria-label="拆分源工单"
        >
          <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
            <span class="font-medium"
              >源工单：{{ source?.label || source?.workOrderId || '未取得' }}</span
            >
            <span class="tabular-nums text-muted-foreground"
              >源数量 {{ sourceQuantity ?? '未取得' }} {{ source?.uomCode || '' }}</span
            >
          </div>
          <span class="text-xs text-muted-foreground"
            >目标工单会继承源工单的 SKU、生产版本和单位；请填写尚未存在的新工单标识。</span
          >
        </section>

        <NvFieldGroup v-if="isSplit" class="grid gap-3">
          <div class="flex items-center justify-between gap-2">
            <NvFieldLabel>子工单与数量</NvFieldLabel>
            <NvButton
              type="button"
              variant="ghost"
              size="sm"
              :disabled="splitTargets.length >= 10"
              @click="addTarget"
            >
              <PlusIcon aria-hidden="true" />
              增加子工单
            </NvButton>
          </div>
          <div
            v-for="(target, index) in splitTargets"
            :key="index"
            class="grid gap-2 sm:grid-cols-[1fr_10rem_auto]"
          >
            <NvField
              :data-invalid="
                showErrors && splitErrors.some((error) => error.includes(`第 ${index + 1} 个`))
              "
            >
              <NvFieldLabel :for="`split-target-id-${index}`"
                >子工单 {{ index + 1 }} 标识</NvFieldLabel
              >
              <NvInput
                :id="`split-target-id-${index}`"
                v-model="target.workOrderId"
                placeholder="请输入新的工单标识"
              />
            </NvField>
            <NvField
              :data-invalid="
                showErrors && splitErrors.some((error) => error.includes(`第 ${index + 1} 个`))
              "
            >
              <NvFieldLabel :for="`split-target-quantity-${index}`">数量</NvFieldLabel>
              <NvInput
                :id="`split-target-quantity-${index}`"
                v-model="target.quantity"
                inputmode="decimal"
                placeholder="例如 10.5"
              />
            </NvField>
            <NvButton
              type="button"
              variant="ghost"
              size="sm"
              class="self-end"
              :disabled="splitTargets.length <= 2"
              :aria-label="`删除子工单 ${index + 1}`"
              @click="removeTarget(index)"
            >
              <Trash2Icon aria-hidden="true" />
            </NvButton>
          </div>
        </NvFieldGroup>

        <section
          v-else
          class="grid gap-2 rounded-md border bg-muted/20 p-3"
          aria-label="合并源工单"
        >
          <div
            v-for="source in sources"
            :key="source.workOrderId"
            class="flex flex-wrap justify-between gap-2 text-sm"
          >
            <span class="font-medium">{{ source.label || source.workOrderId }}</span>
            <span class="tabular-nums text-muted-foreground"
              >{{ source.quantity ?? '未取得' }} {{ source.uomCode || '单位未取得' }} ·
              {{ source.status || '未知状态' }}</span
            >
          </div>
          <p class="text-xs text-muted-foreground">
            目标数量将自动合计为
            {{ sources.reduce((total, source) => total + (source.quantity ?? 0), 0) }}
            {{ sources[0]?.uomCode || '单位未取得' }}。
          </p>
        </section>

        <NvField
          v-if="!isSplit"
          :data-invalid="showErrors && validationErrors.some((error) => error.includes('目标'))"
        >
          <NvFieldLabel for="merge-target-work-order">
            新的目标工单标识 <span class="text-destructive">*</span>
          </NvFieldLabel>
          <NvInput
            id="merge-target-work-order"
            v-model="mergeForm.targetWorkOrderId"
            placeholder="请输入尚未存在的新工单标识"
          />
        </NvField>

        <NvFieldGroup class="grid gap-3">
          <NvField
            :data-invalid="showErrors && validationErrors.some((error) => error.includes('原因'))"
          >
            <NvFieldLabel :for="isSplit ? 'split-reason' : 'merge-reason'">
              {{ isSplit ? '拆分原因' : '合并原因' }} <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvInput
              :id="isSplit ? 'split-reason' : 'merge-reason'"
              :model-value="isSplit ? splitReason : mergeForm.reason"
              maxlength="500"
              :placeholder="isSplit ? '例如：按客户批次拆分' : '例如：同 SKU 小单合并'"
              @update:model-value="updateReason"
            />
          </NvField>
          <div
            v-if="showErrors && validationErrors.length"
            class="grid gap-1 text-sm text-destructive"
            role="alert"
            data-testid="transformation-validation-errors"
          >
            <p>请修正以下填写项：</p>
            <ul class="list-disc ps-5">
              <li v-for="error in validationErrors" :key="error">{{ error }}</li>
            </ul>
          </div>
        </NvFieldGroup>

        <section
          v-if="result?.readback"
          class="grid gap-2 rounded-md border bg-muted/20 p-3 text-sm"
          aria-label="拆分合并回读结果"
          data-testid="transformation-readback"
        >
          <div class="flex flex-wrap items-center justify-between gap-2">
            <span class="font-medium">已回读转换结果</span>
            <span class="text-xs text-muted-foreground"
              >{{ result.readback.type }} · {{ result.readback.occurredAtUtc }}</span
            >
          </div>
          <ul class="grid gap-1">
            <li
              v-for="line in result.readback.lines"
              :key="`${line.sourceWorkOrderId}-${line.targetWorkOrderId}`"
              class="flex justify-between gap-2"
            >
              <span>{{ lineLabel(line) }}</span>
              <span class="tabular-nums"
                >{{ line.quantity }} {{ line.uomCode }} · {{ line.sourceStatus }} →
                {{ line.targetStatus }}</span
              >
            </li>
          </ul>
        </section>
      </div>

      <NvDialogFooter>
        <NvButton type="button" variant="outline" :disabled="pending" @click="close">关闭</NvButton>
        <NvButton
          v-if="props.state === 'accepted' && result?.readbackError"
          type="button"
          variant="outline"
          :disabled="pending"
          data-testid="retry-transformation-readback"
          @click="emit('retryReadback')"
        >
          重试回读
        </NvButton>
        <NvButton
          v-if="props.state !== 'success'"
          type="button"
          :disabled="pending"
          data-testid="submit-work-order-transformation"
          @click="submit"
        >
          <Spinner v-if="pending" aria-hidden="true" />
          {{ isSplit ? '确认拆分' : '确认合并' }}
        </NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
