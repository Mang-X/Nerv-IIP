<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  useBusinessMasterDataResources,
  useBusinessWorkers,
} from '@/composables/useBusinessMasterData'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { resolveDispatchAffordance } from '@/composables/mes/useMesTaskSemantics'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvBadge,
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldLabel,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { UserCheckIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'

/**
 * 派工弹窗：给一道工序挑人、挑设备、挑班次。
 *
 * 班组长的心智是「这道工序谁来干、用哪台设备」，所以候选默认收敛到该工序工作中心下
 * 的在册班组成员，并把每个人的技能摆在候选行上——不用记谁会干什么。找不到人时可以
 * 显式放宽到全部在岗员工，但**不做静默兜底**：范围切换必须是操作员自己按的。
 *
 * 后端目前不下发「该工序要求的技能」（见 MES 后端移交清单），所以这里给的是**按技能
 * 筛选候选**的主动筛选，而不是假装知道工序要求什么技能。
 */

export interface DispatchAssignTarget {
  operationTaskId?: string | null
  operationTaskNo?: string | null
  operationCode?: string | null
  workOrderId?: string | null
  workOrderNo?: string | null
  workCenterId?: string | null
  workCenterCode?: string | null
  workCenterName?: string | null
  deviceAssetId?: string | null
  deviceAssetCode?: string | null
  deviceAssetName?: string | null
  shiftId?: string | null
  assignedUserId?: string | null
  assignedUserName?: string | null
  plannedStartUtc?: string | null
  status?: string | null
  blockingReasons?: string[] | null
}

const props = defineProps<{
  target: DispatchAssignTarget | null
  /** 派工提交函数——由页面注入（各页共用 useMesDispatchTasks 的 assignDispatchTask）。 */
  assign: (
    operationTaskId: string,
    body: {
      assignedUserId: string
      deviceAssetId?: string
      shiftId?: string
      idempotencyKey: string
    },
  ) => Promise<unknown>
  pending?: boolean
}>()

const emit = defineEmits<{ assigned: [] }>()

const open = defineModel<boolean>('open', { required: true })

const assignedUserId = ref('')
const deviceAssetId = ref('')
const shiftId = ref('')
const skillFilter = ref('all')
const candidateScope = ref<'work-center' | 'all'>('work-center')
// 点提交才标红：未选操作员时不禁用按钮，而是标红 + 提示且不发请求。
const showErrors = ref(false)

const {
  workers,
  workersPending,
  filters: workerFilters,
} = useBusinessWorkers({ employmentStatus: 'active' })
const { resources: devices, resourcesPending: devicesPending } =
  useBusinessMasterDataResources('device-asset')
const { resources: shifts } = useBusinessMasterDataResources('shift')
const { resources: skills } = useBusinessMasterDataResources('personnel-skill')
const { resolveShiftLabel, resolveWorkCenter } = useMesDisplayNames()

const targetWorkCenter = computed(
  () => props.target?.workCenterCode ?? props.target?.workCenterId ?? undefined,
)

// 候选查询交给服务端收敛（工作中心 → 所辖班组 → 在册成员），技能筛选同样走服务端，
// 避免在前端对一页候选做二次过滤而把翻页外的人漏掉。
watch(
  [open, candidateScope, targetWorkCenter, skillFilter],
  () => {
    const active = open.value
    workerFilters.workCenterCode =
      active && candidateScope.value === 'work-center' ? targetWorkCenter.value : undefined
    workerFilters.skillCode = active && skillFilter.value !== 'all' ? skillFilter.value : undefined
  },
  { immediate: true },
)

// 有技能登记的排在前面——同一工作中心内优先派给有对应技能的人。
const workerOptions = computed(() =>
  workers.value
    .filter((w) => w.userId)
    .slice()
    .sort((a, b) => (b.skills?.length ?? 0) - (a.skills?.length ?? 0))
    .map((w) => {
      const skillNames = (w.skills ?? []).map((s) => s.skillName).filter(Boolean)
      return {
        value: w.userId as string,
        label: w.employeeNo
          ? `${w.displayName ?? w.employeeNo} · ${w.employeeNo}`
          : (w.displayName ?? ''),
        hint: skillNames.length > 0 ? skillNames.join('、') : '未登记技能',
      }
    }),
)

const selectedWorker = computed(() => workers.value.find((w) => w.userId === assignedUserId.value))
const selectedWorkerSkills = computed(() =>
  (selectedWorker.value?.skills ?? []).filter((s) => s.skillName),
)

const deviceOptions = computed(() =>
  devices.value
    .filter((d) => d.code)
    .map((d) => ({
      value: d.code as string,
      label: d.displayName ?? (d.code as string),
      // 提示位给「所属工作中心」——挑设备时最要紧的是它在不在这道工序的工作中心。
      hint: d.workCenterCode ?? undefined,
    })),
)

const affordance = computed(() =>
  props.target ? resolveDispatchAffordance(props.target) : { label: '派工', enabled: false },
)

const contextItems = computed(() => {
  const row = props.target
  if (!row) return []
  return [
    { label: '工序任务', value: row.operationTaskNo ?? row.operationTaskId },
    { label: '工序', value: row.operationCode },
    { label: '工单', value: row.workOrderNo ?? row.workOrderId },
    {
      label: '工作中心',
      value: row.workCenterName ?? resolveWorkCenter(row.workCenterCode ?? row.workCenterId),
    },
    { label: '计划开始', value: formatDateTime(row.plannedStartUtc) },
    { label: '当前受派', value: row.assignedUserName ?? undefined },
  ]
})

// 每次打开都从所选行重新带出设备/班次，并清掉上一次的选择——
// 否则会把上一道工序的人和设备带进这一次派工。
watch(open, (isOpen) => {
  if (!isOpen) return
  const row = props.target
  assignedUserId.value = ''
  skillFilter.value = 'all'
  candidateScope.value = 'work-center'
  showErrors.value = false
  deviceAssetId.value = row?.deviceAssetCode ?? row?.deviceAssetId ?? ''
  shiftId.value = row?.shiftId ?? ''
})

// 候选是服务端按工作中心收敛后才回来的，所以「只有一个人就直接选中」必须等列表落地再判。
watch(workerOptions, (options) => {
  if (!open.value) return
  if (options.length === 1) {
    assignedUserId.value = options[0]!.value
    return
  }
  // 切换候选范围/技能后原选中项可能已不在候选内，清掉避免提交一个不在范围里的人。
  if (assignedUserId.value && !options.some((o) => o.value === assignedUserId.value)) {
    assignedUserId.value = ''
  }
})

async function submit() {
  showErrors.value = true
  const operationTaskId = props.target?.operationTaskId
  if (!operationTaskId || !assignedUserId.value) return
  try {
    await props.assign(operationTaskId, {
      assignedUserId: assignedUserId.value,
      deviceAssetId: deviceAssetId.value || undefined,
      shiftId: shiftId.value || undefined,
      idempotencyKey: `dispatch-assign-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    })
    notifySuccess(
      selectedWorker.value?.displayName
        ? `已派工：${selectedWorker.value.displayName} 负责该工序。`
        : '已派工。',
    )
    open.value = false
    emit('assigned')
  } catch (error) {
    notifyError(error)
  }
}

function formatDateTime(value?: string | null) {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ target?.assignedUserId ? '改派工序' : '派工' }}</NvDialogTitle>
        <!-- 派工对象已在下方只读区完整呈现；此处仅供读屏播报。 -->
        <NvDialogDescription class="sr-only">
          为工序任务
          {{ target?.operationTaskNo ?? target?.operationTaskId }} 指派操作员、设备与班次。
        </NvDialogDescription>
      </NvDialogHeader>

      <form
        class="grid max-h-[70vh] content-start gap-4 overflow-y-auto px-1"
        @submit.prevent="submit"
      >
        <CarriedContextSummary label="派工对象" :items="contextItems" />

        <p
          v-if="!affordance.enabled && affordance.blockedReason"
          class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-foreground"
          role="alert"
        >
          {{ affordance.blockedReason }}
        </p>

        <template v-else>
          <div class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="assign-scope">候选范围</NvFieldLabel>
              <NvSelect v-model="candidateScope">
                <NvSelectTrigger id="assign-scope"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="work-center">本工作中心班组</NvSelectItem>
                  <NvSelectItem value="all">全部在岗员工</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="assign-skill">按技能筛选</NvFieldLabel>
              <NvSelect v-model="skillFilter">
                <NvSelectTrigger id="assign-skill"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="all">不限技能</NvSelectItem>
                  <NvSelectItem v-for="s in skills" :key="s.code ?? ''" :value="s.code ?? ''">
                    {{ s.displayName ?? s.code }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
          </div>

          <NvField>
            <NvFieldLabel for="assign-operator">
              操作员 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvSearchSelect
              id="assign-operator"
              v-model="assignedUserId"
              :options="workerOptions"
              :loading="workersPending"
              :disabled="workerOptions.length === 0"
              placeholder="选择操作员"
              search-placeholder="搜索姓名 / 工号 / 技能…"
              empty-text="没有符合条件的候选人"
              aria-label="操作员"
              :class="showErrors && !assignedUserId ? 'border-destructive' : undefined"
            />
            <div v-if="selectedWorkerSkills.length" class="flex flex-wrap gap-1.5">
              <NvBadge v-for="s in selectedWorkerSkills" :key="s.skillCode ?? ''" variant="neutral">
                {{ s.skillName }}<template v-if="s.level"> · {{ s.level }}</template>
              </NvBadge>
            </div>
            <p
              v-else-if="!workersPending && workerOptions.length === 0"
              class="text-sm text-muted-foreground"
            >
              {{
                candidateScope === 'work-center'
                  ? '该工作中心暂无符合条件的在岗班组成员，可放宽到「全部在岗员工」或取消技能筛选。'
                  : '暂无符合条件的在岗员工，请先在「基础数据 · 员工」维护人员与技能。'
              }}
            </p>
          </NvField>

          <div class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="assign-device">设备</NvFieldLabel>
              <NvEntityPicker
                id="assign-device"
                v-model="deviceAssetId"
                :options="deviceOptions"
                title="选择设备"
                placeholder="沿用排程设备"
                source-text="数据来自基础数据设备台账"
                empty-text="暂无设备台账，请先在「基础数据 · 设备」维护"
                :loading="devicesPending"
                aria-label="设备"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="assign-shift">班次</NvFieldLabel>
              <NvSelect v-model="shiftId">
                <NvSelectTrigger id="assign-shift">
                  <NvSelectValue :placeholder="resolveShiftLabel(target?.shiftId)" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="s in shifts" :key="s.code ?? ''" :value="s.code ?? ''">
                    {{ s.displayName ?? s.code }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
          </div>

          <!-- 点提交才标红；未选操作员不发请求。 -->
          <p v-if="showErrors && !assignedUserId" class="text-sm text-destructive" role="alert">
            请选择操作员（已标红）。
          </p>
        </template>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="pending || !affordance.enabled">
            <Spinner v-if="pending" aria-hidden="true" />
            <UserCheckIcon v-else aria-hidden="true" />
            {{ target?.assignedUserId ? '确认改派' : '确认派工' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
