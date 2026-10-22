<script setup lang="ts">
import {
  createSchedulingHorizonInput,
  resolveSchedulingHorizon,
} from '@/composables/schedulingHorizon'
import {
  useCanScheduleSingleOrder,
  useSingleOrderScheduling,
  SINGLE_ORDER_SCHEDULING_DENIED_REASON,
} from '@/composables/useSingleOrderScheduling'
import SchedulingCandidatePicker from './SchedulingCandidatePicker.vue'
import SchedulingHorizonFields from './SchedulingHorizonFields.vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
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
  NvSpinner,
} from '@nerv-iip/ui'
import { AlertTriangleIcon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import type { BusinessConsoleSchedulePlan } from '@nerv-iip/api-client'
import SchedulingInsertionPreview from './SchedulingInsertionPreview.vue'
import SchedulingPreviewResult from './SchedulingPreviewResult.vue'

const props = withDefaults(
  defineProps<{
    /**
     * 固定目标工单。给了就是「对这张 MES 工单排产」，弹窗不再让用户挑单。
     */
    workOrderId?: string | null
    /**
     * 发起来源的人读上下文，例如「销售订单 SO-2026-0001」「计划建议 · 成品净需求」。
     * 只用于文案，不参与任何请求。
     */
    contextLabel?: string
    /**
     * 未给 workOrderId 时的候选工单检索词（例如销售单号）。
     * 只是**检索起点**，不是关联关系：契约里没有 销售订单 → MES 工单 的稳定关联键
     * （见履约追踪「MES 工单」节点），所以最终由排产员确认选哪一张，前端不按相似编号猜。
     */
    initialKeyword?: string
  }>(),
  { workOrderId: null, contextLabel: '', initialKeyword: '' },
)

/** 服务端没有业务原因时使用统一排产反馈。 */
const SUBMIT_FALLBACK = '排产失败，请检查工单生产版本与排程基础数据。'

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ scheduled: [planId: string] }>()

const mode = ref('new')
const generatedPlan = shallowRef<BusinessConsoleSchedulePlan>()
const scheduling = useSingleOrderScheduling()

const horizon = ref(createSchedulingHorizonInput())
const priority = ref(100)
const isRush = ref(false)
const selectedWorkOrderId = shallowRef('')

const fixedWorkOrderId = computed(() => props.workOrderId?.trim() ?? '')
// 候选查询由 SchedulingCandidatePicker 自己持有，并且只在这里为 true 时才挂载：
// 工单详情 / 计划建议行这类已知目标工单的入口，打开弹窗不会白查一页候选。
const needsPicker = computed(() => fixedWorkOrderId.value.length === 0)
// 权限判定与三处入口共用同一处（useCanScheduleSingleOrder），不在组件里各写一份。
const canSchedule = useCanScheduleSingleOrder()
const readOnly = computed(() => !canSchedule.value)

watch(
  open,
  (isOpen) => {
    if (!isOpen) return
    // 每次打开都重置：上一次的窗口/优先级不该悄悄带到下一张单上。
    mode.value = 'new'
    generatedPlan.value = undefined
    horizon.value = createSchedulingHorizonInput()
    priority.value = 100
    isRush.value = false
    selectedWorkOrderId.value = fixedWorkOrderId.value
  },
  { immediate: true },
)

const targetWorkOrderId = computed(() =>
  needsPicker.value ? selectedWorkOrderId.value : fixedWorkOrderId.value,
)
const resolvedHorizon = computed(() => resolveSchedulingHorizon(horizon.value))
const disabledReason = computed(() => {
  if (readOnly.value) return SINGLE_ORDER_SCHEDULING_DENIED_REASON
  if (!scheduling.hasScope.value) return '尚未确定当前组织，暂不能排产。'
  if (!targetWorkOrderId.value) return '请先选择要排产的工单。'
  if (!resolvedHorizon.value.ok) return resolvedHorizon.value.message
  return ''
})
const canSubmit = computed(() => disabledReason.value === '' && !scheduling.pending.value)

async function submit() {
  const resolved = resolvedHorizon.value
  if (mode.value !== 'new' || !canSubmit.value || !resolved.ok) return
  try {
    const plan = await scheduling.scheduleSingleOrder({
      workOrderId: targetWorkOrderId.value,
      priority: Number(priority.value) || 0,
      isRush: isRush.value,
      horizonStartUtc: resolved.horizonStartUtc,
      horizonEndUtc: resolved.horizonEndUtc,
    })
    const planId = plan.planId ?? ''
    notifySuccess(`已生成只含工单 ${targetWorkOrderId.value} 的排程方案。`)
    emit('scheduled', planId)
    generatedPlan.value = plan
  } catch (error) {
    notifyOperationFailure('排产失败', error, SUBMIT_FALLBACK)
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent
      class="max-h-[90vh] overflow-y-auto sm:max-w-4xl"
      data-testid="single-order-scheduling-dialog"
    >
      <NvDialogHeader>
        <NvDialogTitle>对该单排产</NvDialogTitle>
        <NvDialogDescription>
          {{ contextLabel ? `${contextLabel} · ` : '' }}在当前页面新建方案或插入现有方案重预览。
        </NvDialogDescription>
      </NvDialogHeader>

      <div class="flex gap-2" aria-label="排产方式">
        <NvButton
          type="button"
          :variant="mode === 'new' ? 'default' : 'outline'"
          :disabled="scheduling.pending.value"
          :aria-pressed="mode === 'new'"
          @click="mode = 'new'"
          >新建方案</NvButton
        >
        <NvButton
          type="button"
          :variant="mode === 'insert' ? 'default' : 'outline'"
          :disabled="scheduling.pending.value"
          :aria-pressed="mode === 'insert'"
          @click="mode = 'insert'"
          >插入现有方案</NvButton
        >
      </div>
      <p
        v-if="mode === 'new'"
        class="flex gap-2 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
        role="status"
        data-testid="single-order-scheduling-semantics"
      >
        <AlertTriangleIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>
          本次排产<strong>新建一个只含该单的排程方案</strong>；现有方案保持不变，两者需要人工取舍后再发布。
        </span>
      </p>

      <form class="grid gap-4" @submit.prevent="submit">
        <NvFieldGroup>
          <NvField v-if="!needsPicker">
            <NvFieldLabel for="single-order-scheduling-target">目标工单</NvFieldLabel>
            <NvInput id="single-order-scheduling-target" :model-value="fixedWorkOrderId" readonly />
          </NvField>
          <SchedulingCandidatePicker
            v-else
            v-model="selectedWorkOrderId"
            :initial-keyword="initialKeyword"
            :disabled="readOnly"
          />
        </NvFieldGroup>

        <SchedulingHorizonFields
          v-if="mode === 'new'"
          v-model="horizon"
          id-prefix="single-order-scheduling"
          :disabled="readOnly"
        />

        <NvFieldGroup v-if="mode === 'new'">
          <NvField>
            <NvFieldLabel for="single-order-scheduling-priority">优先级</NvFieldLabel>
            <NvInput
              id="single-order-scheduling-priority"
              v-model="priority"
              type="number"
              min="0"
              max="9999"
              :disabled="readOnly"
            />
          </NvField>
          <NvField>
            <label class="flex items-center gap-2 text-sm">
              <NvCheckbox v-model="isRush" :disabled="readOnly" aria-label="按加急单排产" />
              按加急单排产
            </label>
          </NvField>
        </NvFieldGroup>

        <SchedulingInsertionPreview
          v-if="open && mode === 'insert'"
          :key="`${scheduling.context.organizationId}:${scheduling.context.environmentId}:${targetWorkOrderId}`"
          :work-order-id="targetWorkOrderId"
          :context="scheduling.context"
          :can-manage="canSchedule"
        />
        <SchedulingPreviewResult v-if="mode === 'new' && generatedPlan" :plan="generatedPlan" />

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">关闭</NvButton>
          <NvButton
            v-if="mode === 'new'"
            type="submit"
            :disabled="!canSubmit"
            :title="disabledReason || undefined"
          >
            <NvSpinner v-if="scheduling.pending.value" aria-hidden="true" />
            生成只含该单的方案
          </NvButton>
        </NvDialogFooter>
        <p
          v-if="mode === 'new' && disabledReason"
          class="text-sm text-muted-foreground"
          role="status"
        >
          {{ disabledReason }}
        </p>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
