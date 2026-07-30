<script setup lang="ts">
import {
  createSchedulingHorizonInput,
  resolveSchedulingHorizon,
} from '@/composables/schedulingHorizon'
import {
  singleOrderSchedulingResultRoute,
  useCanScheduleSingleOrder,
  useSingleOrderScheduling,
  SINGLE_ORDER_SCHEDULING_DENIED_REASON,
} from '@/composables/useSingleOrderScheduling'
import SchedulingCandidatePicker from './SchedulingCandidatePicker.vue'
import SchedulingHorizonFields from './SchedulingHorizonFields.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
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
import { AlertTriangleIcon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'

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

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ scheduled: [planId: string] }>()

const router = useRouter()
const scheduling = useSingleOrderScheduling()

const horizon = ref(createSchedulingHorizonInput())
const priority = ref(100)
const isRush = ref(false)
const selectedWorkOrderId = shallowRef('')
const submitError = shallowRef('')

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
    horizon.value = createSchedulingHorizonInput()
    priority.value = 100
    isRush.value = false
    submitError.value = ''
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
  if (!scheduling.hasScope.value) return '请先在顶部选择组织与环境。'
  if (!targetWorkOrderId.value) return '请先选择要排产的工单。'
  if (!resolvedHorizon.value.ok) return resolvedHorizon.value.message
  return ''
})
const canSubmit = computed(() => disabledReason.value === '' && !scheduling.pending.value)

async function submit() {
  const resolved = resolvedHorizon.value
  if (!canSubmit.value || !resolved.ok) return
  submitError.value = ''
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
    open.value = false
    if (planId) {
      await router.push(singleOrderSchedulingResultRoute(planId, targetWorkOrderId.value))
    }
  } catch (error) {
    // 失败留在弹窗里说清楚，用户改窗口就能重试；不要关窗后只剩一句 toast。
    submitError.value =
      error instanceof Error && error.message
        ? error.message
        : '排产失败，请检查工单生产版本与排程基础数据。'
    notifyError(error, submitError.value)
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-2xl" data-testid="single-order-scheduling-dialog">
      <NvDialogHeader>
        <NvDialogTitle>对该单排产</NvDialogTitle>
        <NvDialogDescription>
          {{
            contextLabel ? `${contextLabel} · ` : ''
          }}生成一个只含该单的新排程方案，不会改动任何已有方案。
        </NvDialogDescription>
      </NvDialogHeader>

      <!-- 语义必须写在界面上：新建只含该单的方案 ≠ 插进现有方案。 -->
      <p
        class="flex gap-2 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
        role="status"
        data-testid="single-order-scheduling-semantics"
      >
        <AlertTriangleIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>
          本次排产<strong>新建一个只含该单的排程方案</strong>；现有方案保持不变，两者需要人工取舍后再发布。
          「把该单插入现有方案」尚不可用——插单预览需要后端能力（MAN-674 /
          #1241），到位后再在此处提供。
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
          v-model="horizon"
          id-prefix="single-order-scheduling"
          :disabled="readOnly"
        />

        <NvFieldGroup>
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

        <p v-if="submitError" class="text-sm text-destructive" role="alert">{{ submitError }}</p>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="!canSubmit" :title="disabledReason || undefined">
            <Spinner v-if="scheduling.pending.value" aria-hidden="true" />
            生成只含该单的方案
          </NvButton>
        </NvDialogFooter>
        <p v-if="disabledReason" class="text-sm text-muted-foreground" role="status">
          {{ disabledReason }}
        </p>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
