<script setup lang="ts">
import type {
  BusinessConsoleResourceItem,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingPlanSummaryResponse,
} from '@nerv-iip/api-client'
import {
  conflictReasonLabel,
  ResourceSchedulerBoard,
  SchedulingLegend,
  SchedulingToolbar,
  TaskDetailPanel,
  toModel,
  type ScheduleModel,
  type ScheduleTask,
  type DimensionValue,
  type SchedulingDimension,
  type TimeScale,
} from '@nerv-iip/scheduling'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useSkuNames } from '@/composables/useSkuNames'
import { resolveWorkCenterFamily, WORK_CENTER_FAMILY_LIST } from '@/data/workCenterFamilies'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import {
  schedulingPlanStatusLabel,
  schedulingPlanStatusTone,
  schedulingPlanTerminalReleaseReason,
} from '@/utils/schedulingPlanPresentation'
import { NvButton, NvStatusBadge, Spinner } from '@nerv-iip/ui'
import {
  CalendarDaysIcon,
  EyeIcon,
  SendIcon,
  ShieldAlertIcon,
  TriangleAlertIcon,
  XIcon,
} from '@lucide/vue'
import { computed, shallowRef, watch } from 'vue'

const props = defineProps<{
  plan?: BusinessConsoleSchedulePlan
  summary?: BusinessConsoleSchedulingPlanSummaryResponse
  loading?: boolean
  error?: unknown
  releasePending?: boolean
  /**
   * MES 权威工单（与「待排工单池」同一份查询缓存，这里不另发请求）。
   * APS 的 assignment 契约只有工单号/工序号/资源/起止，物料、数量、交期在工单上；
   * 工序详情要展示这些字段就只能在呈现层 join，join 不到的字段一律不上屏。
   */
  workOrders?: BusinessConsoleMesWorkOrderItem[]
}>()

const emit = defineEmits<{
  openDetail: []
  release: []
}>()

type SchedulingGroupKey = 'workCenter' | 'workshop' | 'productionLine'

const SCHEDULING_GROUP_DIMENSIONS: SchedulingDimension[] = [
  { key: 'workCenter', label: '工作中心' },
  { key: 'workshop', label: '车间' },
  { key: 'productionLine', label: '产线' },
]

const UNASSIGNED_GROUP_IDS = {
  workshop: '__unassigned_workshop__',
  productionLine: '__unassigned_production_line__',
} as const

function catalogValues(
  resources: BusinessConsoleResourceItem[],
  missingLabel: string,
): DimensionValue[] {
  return resources.flatMap((resource) => {
    const code = resource.code?.trim()
    if (!code) return []
    return [{ id: code, label: resource.displayName?.trim() || missingLabel }]
  })
}

function uniqueDimensionValues(values: DimensionValue[]) {
  const seen = new Set<string>()
  return values.filter((value) => {
    if (seen.has(value.id)) return false
    seen.add(value.id)
    return true
  })
}

function buildGroupedTasks(
  tasks: ScheduleTask[],
  catalogs: {
    workCenters: BusinessConsoleResourceItem[]
    workshops: BusinessConsoleResourceItem[]
    lines: BusinessConsoleResourceItem[]
  },
  resolveWorkCenter: (code?: string | null) => string | undefined,
) {
  const workCenterByCode = new Map(
    catalogs.workCenters.flatMap((resource) => {
      const code = resource.code?.trim()
      return code ? [[code, resource] as const] : []
    }),
  )
  const workshopValues = catalogValues(catalogs.workshops, '未命名车间')
  const lineValues = catalogValues(catalogs.lines, '未命名产线')
  const operationTasks = tasks.filter((task) => task.type === 'operation' && !task.blockKind)
  const missingWorkshopCount = operationTasks.filter((task) => {
    const workCenterCode = task.workCenterId ?? task.resourceId
    return !workCenterCode || !workCenterByCode.get(workCenterCode)?.workshopCode?.trim()
  }).length
  const missingLineCount = operationTasks.filter((task) => {
    const workCenterCode = task.workCenterId ?? task.resourceId
    return !workCenterCode || !workCenterByCode.get(workCenterCode)?.lineCode?.trim()
  }).length
  const fallbackWorkshopLabel = `未归属车间（${missingWorkshopCount} 项）`
  const fallbackLineLabel = `未归属产线（${missingLineCount} 项）`

  const groupedTasks = tasks.map((task) => {
    if (task.type !== 'operation') return task
    const workCenterCode = task.workCenterId ?? task.resourceId
    const workCenter = workCenterCode ? workCenterByCode.get(workCenterCode) : undefined
    const workshopCode = workCenter?.workshopCode?.trim()
    const lineCode = workCenter?.lineCode?.trim()
    const dimensions = { ...(task.dimensions ?? {}) }

    if (workCenterCode) {
      dimensions.workCenter = {
        id: workCenterCode,
        label: resolveWorkCenter(workCenterCode) ?? workCenterCode,
      }
    }
    dimensions.workshop = {
      id: workshopCode ?? UNASSIGNED_GROUP_IDS.workshop,
      label: workshopCode
        ? (workshopValues.find((value) => value.id === workshopCode)?.label ?? '未命名车间')
        : fallbackWorkshopLabel,
    }
    dimensions.productionLine = {
      id: lineCode ?? UNASSIGNED_GROUP_IDS.productionLine,
      label: lineCode
        ? (lineValues.find((value) => value.id === lineCode)?.label ?? '未命名产线')
        : fallbackLineLabel,
    }

    return { ...task, dimensions }
  })

  const workCenterValues = uniqueDimensionValues(
    groupedTasks
      .filter((task) => task.type === 'operation' && !task.blockKind)
      .flatMap((task) => (task.dimensions?.workCenter ? [task.dimensions.workCenter] : [])),
  )
  const groupValues: Record<SchedulingGroupKey, DimensionValue[]> = {
    workCenter: workCenterValues,
    workshop: uniqueDimensionValues([
      ...workshopValues,
      ...(missingWorkshopCount
        ? [{ id: UNASSIGNED_GROUP_IDS.workshop, label: fallbackWorkshopLabel }]
        : []),
    ]),
    productionLine: uniqueDimensionValues([
      ...lineValues,
      ...(missingLineCount
        ? [
            {
              id: UNASSIGNED_GROUP_IDS.productionLine,
              label: fallbackLineLabel,
            },
          ]
        : []),
    ]),
  }

  return { tasks: groupedTasks, groupValues }
}

const scale = shallowRef<TimeScale>('auto')
const groupBy = shallowRef<SchedulingGroupKey>('workCenter')

// 工作中心显示名 + 分类（主数据名录，与派工看板同一份缓存）。
const {
  resolveWorkCenter,
  resolveWorkCenterCategory,
  workCenterResources,
  workshopResources,
  lineResources,
} = useMesDisplayNames({ schedulingGroups: true })

// 工序分色按工作中心「工序族」归类，族解析以主数据 category 为准、编码前缀仅作兜底，
// 映射与色槽语义集中在 data/workCenterFamilies.ts（不在页面里写死客户编码）。
function wcFamily(workCenterId?: string | null) {
  if (!workCenterId) return undefined
  return resolveWorkCenterFamily(workCenterId, resolveWorkCenterCategory(workCenterId))
}

// 物料名（SKU 名录，同一份查询缓存）；查不到只显编码，不编造物料名。
const { resolveSkuName } = useSkuNames()

const workOrderById = computed(() => {
  const map = new Map<string, BusinessConsoleMesWorkOrderItem>()
  for (const order of props.workOrders ?? []) {
    if (order.workOrderId) map.set(order.workOrderId, order)
  }
  return map
})

const assignments = computed(() => props.plan?.assignments ?? [])
const invalidTimeAssignments = computed(() => assignments.value.filter(hasInvalidTime))
const missingResourceAssignments = computed(() =>
  assignments.value.filter(
    (assignment) =>
      !hasInvalidTime(assignment) && !assignment.resourceId && !assignment.workCenterId,
  ),
)
const renderableAssignments = computed(() =>
  assignments.value.filter(
    (assignment) =>
      !hasInvalidTime(assignment) && Boolean(assignment.resourceId || assignment.workCenterId),
  ),
)

const model = computed<ScheduleModel | undefined>(() => {
  if (!props.plan) return undefined
  const mapped = toModel({ ...props.plan, assignments: renderableAssignments.value })
  const grouped = buildGroupedTasks(
    mapped.tasks.map((task) => {
      // 资源时间块(维护/停机/换线/换型)不是工序:只把工作中心换成人话名,不套工序标题。
      if (task.blockKind) {
        return task.workCenterId
          ? {
              ...task,
              dimensions: {
                ...task.dimensions,
                workCenter: {
                  id: task.workCenterId,
                  label: resolveWorkCenter(task.workCenterId) ?? task.workCenterId,
                },
              },
            }
          : task
      }
      if (task.type !== 'operation') return task
      const sequence = task.operationSequence > 0 ? `第 ${task.operationSequence} 道` : '工序'
      const family = wcFamily(task.workCenterId)
      const workOrder = workOrderById.value.get(task.orderId)
      const skuCode = workOrder?.skuCode ?? undefined
      // 参考实现成例（dev/SchedulingPreview.vue）：toModel 之后充实展示模型——
      // 工序分色 colorKey + 工作中心维度人话名（泳道名从 WC-ROD-01 变「活塞杆加工中心一线」）。
      // 物料/数量/交期来自 MES 工单（工单级事实），join 不到就保持缺省、详情里不显示该行。
      return {
        ...task,
        text: [task.orderId, sequence, task.operationId].filter(Boolean).join(' · '),
        colorKey: family?.key,
        product: skuCode ? (resolveSkuName(skuCode) ?? skuCode) : undefined,
        quantity: workOrder?.quantity,
        dueUtc: workOrder?.dueUtc,
        dimensions: task.workCenterId
          ? {
              ...task.dimensions,
              workCenter: {
                id: task.workCenterId,
                label: resolveWorkCenter(task.workCenterId) ?? task.workCenterId,
              },
            }
          : task.dimensions,
      }
    }),
    {
      workCenters: workCenterResources.value,
      workshops: workshopResources.value,
      lines: lineResources.value,
    },
    resolveWorkCenter,
  )
  return {
    ...mapped,
    tasks: grouped.tasks,
    groupDimensions: SCHEDULING_GROUP_DIMENSIONS,
    groupValues: grouped.groupValues,
  }
})

// 图例只列本方案实际用到的分类（不罗列全色板）。
const legendCategories = computed(() => {
  const used = new Set(
    (model.value?.tasks ?? [])
      .filter((t) => t.type === 'operation')
      .map((t) => t.colorKey)
      .filter(Boolean),
  )
  return WORK_CENTER_FAMILY_LIST.filter((f) => used.has(f.key)).map((f) => ({
    key: f.key,
    label: f.label,
  }))
})

// 选中态：点甘特上的条 → 右侧并排展开该条的详情（甘特不被遮挡、仍可点）。
// 方案级抽屉只由「方案明细」按钮打开——两个入口对应两种粒度，不再混为一谈。
const selectedTaskId = shallowRef('')
const selectedTask = computed<ScheduleTask | undefined>(() =>
  selectedTaskId.value
    ? model.value?.tasks.find((task) => task.id === selectedTaskId.value)
    : undefined,
)
const detailPanelOpen = computed(() => Boolean(selectedTask.value))
/**
 * 选中条的粒度。资源排产板只铺工序条，但工单甘特会有工单汇总行、资源时间块，
 * 引擎也可能把它们回给 taskSelect——粒度不同，标题和字段就得不同：
 * 工单行没有"工序号"，资源时间块没有工单，硬套工序模板会显示「工序号：无」这种假事实。
 */
const selectedKind = computed<'order' | 'block' | 'operation' | undefined>(() => {
  const task = selectedTask.value
  if (!task) return undefined
  if (task.blockKind) return 'block'
  return task.type === 'order' ? 'order' : 'operation'
})
const detailPanelTitle = computed(() => {
  if (selectedKind.value === 'order') return '工单详情'
  if (selectedKind.value === 'block') return '资源时间块'
  return '工序详情'
})
// 换方案就收起详情；同方案内模型刷新后选中的条不在了也收起，
// 避免停在一条已经不存在的工序上（两种情形一个 watch 处理，不重复设值）。
watch(
  () => [props.plan?.planId, model.value] as const,
  ([planId], previous) => {
    if (!selectedTaskId.value) return
    if (planId !== previous?.[0] || !selectedTask.value) selectedTaskId.value = ''
  },
)

// 选中的是工单汇总行时，给一个工单级读数（本方案里这张工单排了几道工序）。
const selectedOrderOperationCount = computed(() => {
  const task = selectedTask.value
  if (!task || selectedKind.value !== 'order') return 0
  return (model.value?.tasks ?? []).filter(
    (candidate) => candidate.type === 'operation' && candidate.orderId === task.orderId,
  ).length
})

const selectedWorkOrder = computed(() =>
  selectedTask.value ? workOrderById.value.get(selectedTask.value.orderId) : undefined,
)
const selectedWorkCenterLabel = computed(() => {
  const workCenterId = selectedTask.value?.workCenterId
  if (!workCenterId) return ''
  const name = resolveWorkCenter(workCenterId)
  return name && name !== workCenterId ? `${name}（${workCenterId}）` : workCenterId
})
// 选中工序命中的冲突明细（方案级 conflicts 里按工序过滤，说明文本原样呈现）。
const selectedConflicts = computed(() =>
  selectedTask.value
    ? (model.value?.conflicts ?? []).filter(
        (conflict) => conflict.taskId === selectedTask.value?.id,
      )
    : [],
)

// ── 时间线控件与搜索(#1399 M4 / M5)────────────────────────────────────────────
// 引擎侧 4 档刻度、放大缩小、定位到当前、适配窗口早就实现了,只是这个页面从没挂过工具栏,
// 自己手搓了「自动适配/班次级/日级」三个按钮。改为直接用包内 SchedulingToolbar。
const boardRef = shallowRef<InstanceType<typeof ResourceSchedulerBoard>>()
function sendCommand(cmd: Parameters<NonNullable<typeof boardRef.value>['command']>[0]) {
  boardRef.value?.command(cmd)
}

const search = shallowRef('')
/**
 * 命中判定跨「工单号 / 工序号 / 条上标题 / 资源 / 工作中心人话名」五个字段。
 * 调度员嘴里的"找 03008"可能是工单号,"找精磨"是工作中心名,两种都得中——只匹配工单号
 * 等于把一半的找法判死。大小写与首尾空格一律归一。
 */
const searchMatches = computed<ScheduleTask[]>(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return []
  return (model.value?.tasks ?? []).filter((task) => {
    if (task.type !== 'operation' || task.blockKind) return false
    return [
      task.orderId,
      task.operationId,
      task.text,
      task.resourceId,
      task.workCenterId,
      task.dimensions?.workCenter?.label,
      task.product,
    ].some((field) => field && String(field).toLowerCase().includes(q))
  })
})
const matchCount = computed(() => searchMatches.value.length)
// 1 基游标;0 表示还没定位过。
const matchCursor = shallowRef(0)

function revealMatch(index: number) {
  const list = searchMatches.value
  if (!list.length) return
  // 环形步进:搜索到底再按"下一个"应回到第一条,而不是卡住不动。
  const next = ((index % list.length) + list.length) % list.length
  matchCursor.value = next + 1
  const task = list[next]
  sendCommand({ kind: 'revealTask', taskId: task.id })
  selectedTaskId.value = task.id
}

// 输入即高亮 + 自动跳到第一条命中。清空则退出搜索态(高亮全撤)。
watch([search, searchMatches], () => {
  sendCommand({ kind: 'setSearchHighlight', taskIds: searchMatches.value.map((t) => t.id) })
  matchCursor.value = 0
  if (searchMatches.value.length) {
    const selectedIndex = searchMatches.value.findIndex((task) => task.id === selectedTaskId.value)
    if (selectedIndex >= 0) {
      matchCursor.value = selectedIndex + 1
      sendCommand({ kind: 'revealTask', taskId: selectedTaskId.value })
    } else {
      revealMatch(0)
    }
  }
})
// 换方案时清空搜索:上一版方案的关键词留在框里,配着新方案的「无匹配」最容易被当成 bug。
watch(
  () => props.plan?.planId,
  () => {
    search.value = ''
  },
)

const resourceCount = computed(() => model.value?.resources.length ?? 0)
const planRange = computed(() => {
  const horizon = model.value?.horizon
  if (!horizon?.startUtc || !horizon.endUtc) return '暂无有效时间范围'
  return `${formatDateTime(horizon.startUtc)} 至 ${formatDateTime(horizon.endUtc)}`
})
const invalidationReason = computed(() =>
  props.summary?.isInvalidated
    ? describeScheduleInvalidationReason(props.summary.latestInvalidationReasonCode)
    : '',
)
const releaseDisabled = computed(
  () =>
    props.releasePending ||
    props.summary?.isInvalidated ||
    Boolean(schedulingPlanTerminalReleaseReason(props.plan?.status ?? props.summary?.status ?? '')),
)
const feedback = computed(() => {
  if (isForbidden(props.error))
    return '权限不足，无法查看该排程方案。请联系管理员确认排程读取权限。'
  if (props.error) return '排程甘特加载失败，请稍后重试。'
  if (!props.plan) return '请选择一个排程方案查看甘特。'
  return ''
})

function hasInvalidTime(assignment: BusinessConsoleSchedulingAssignment) {
  const start = Date.parse(assignment.startUtc ?? '')
  const end = Date.parse(assignment.endUtc ?? '')
  return !Number.isFinite(start) || !Number.isFinite(end) || end <= start
}

function isForbidden(error: unknown, visited = new Set<object>()): boolean {
  if (!error || typeof error !== 'object') return false
  if (visited.has(error)) return false
  visited.add(error)
  const record = error as Record<string, unknown>
  if (
    record.status === 401 ||
    record.status === 403 ||
    record.statusCode === 401 ||
    record.statusCode === 403
  ) {
    return true
  }
  return isForbidden(record.response, visited) || isForbidden(record.cause, visited)
}

function formatDateTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

function setGroupBy(value: string) {
  if (SCHEDULING_GROUP_DIMENSIONS.some((dimension) => dimension.key === value)) {
    groupBy.value = value as SchedulingGroupKey
    if (selectedTaskId.value) sendCommand({ kind: 'revealTask', taskId: selectedTaskId.value })
  }
}
</script>

<template>
  <section class="grid gap-4" data-testid="scheduling-plan-gantt">
    <div
      v-if="loading"
      class="flex min-h-80 items-center justify-center gap-2 rounded-lg border bg-card text-sm text-muted-foreground"
    >
      <Spinner aria-hidden="true" />
      正在读取方案时间轴
    </div>

    <div
      v-else-if="feedback"
      class="flex min-h-64 flex-col items-center justify-center gap-3 rounded-lg border border-dashed bg-card p-8 text-center"
      :class="isForbidden(error) ? 'border-warning/40 bg-warning/5' : ''"
      role="status"
    >
      <ShieldAlertIcon
        v-if="isForbidden(error)"
        class="size-9 text-warning-strong"
        aria-hidden="true"
      />
      <CalendarDaysIcon v-else class="size-9 text-muted-foreground" aria-hidden="true" />
      <p class="max-w-xl text-sm text-muted-foreground">{{ feedback }}</p>
    </div>

    <template v-else-if="plan && model">
      <div
        v-if="summary?.isInvalidated"
        class="flex items-start gap-3 rounded-lg border border-warning/40 bg-warning/10 p-4 text-sm"
        role="alert"
      >
        <TriangleAlertIcon class="mt-0.5 size-4 flex-none text-warning-strong" aria-hidden="true" />
        <div>
          <p class="font-semibold text-foreground">方案已失效，不能从甘特发布</p>
          <p class="mt-1 text-muted-foreground">
            {{ invalidationReason }}。请重新排程并生成新方案后再发布。
          </p>
        </div>
      </div>

      <div
        class="grid gap-3 rounded-lg border bg-card p-4 xl:grid-cols-[minmax(0,1fr)_auto] xl:items-center"
      >
        <div class="min-w-0">
          <div class="flex flex-wrap items-center gap-2">
            <h2 class="truncate text-base font-semibold text-foreground">
              {{ plan.planId || '未命名方案' }}
            </h2>
            <NvStatusBadge
              :label="schedulingPlanStatusLabel(plan.status)"
              :tone="schedulingPlanStatusTone(plan.status)"
            />
            <NvStatusBadge v-if="summary?.isInvalidated" label="已失效" tone="warning" />
          </div>
          <p class="mt-1 text-sm text-muted-foreground">{{ planRange }}</p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <NvButton size="sm" variant="outline" type="button" @click="emit('openDetail')">
            <EyeIcon aria-hidden="true" />方案明细
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            :disabled="releaseDisabled"
            :title="summary?.isInvalidated ? `方案已失效（${invalidationReason}）` : '发布当前方案'"
            @click="emit('release')"
          >
            <Spinner v-if="releasePending" aria-hidden="true" />
            <SendIcon v-else aria-hidden="true" />
            发布当前方案
          </NvButton>
        </div>
      </div>

      <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">可视工序</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ renderableAssignments.length }}
          </p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">资源</p>
          <p class="mt-1 text-lg font-semibold text-foreground">{{ resourceCount }}</p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">冲突</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ plan.conflicts?.length ?? 0 }}
          </p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">未排工序</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ plan.unscheduledOperations?.length ?? 0 }}
          </p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">锁定分配</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ renderableAssignments.filter((item) => item.isLocked).length }}
          </p>
        </div>
      </div>

      <div
        v-if="invalidTimeAssignments.length || missingResourceAssignments.length"
        class="flex flex-wrap gap-2 rounded-lg border border-warning/30 bg-warning/5 p-3"
        role="status"
      >
        <NvStatusBadge
          v-if="invalidTimeAssignments.length"
          :label="`${invalidTimeAssignments.length} 项时间异常`"
          tone="warning"
        />
        <NvStatusBadge
          v-if="missingResourceAssignments.length"
          :label="`${missingResourceAssignments.length} 项缺少资源`"
          tone="warning"
        />
        <span class="text-sm text-muted-foreground"
          >部分分配无法绘制，请在方案明细中核对这些工序。</span
        >
      </div>

      <!-- 甘特与详情并排：详情是同一行里的一列（不是覆盖层），
           打开时甘特只是变窄，仍然可见、可点、可继续换选。 -->
      <div class="flex h-[34rem] min-h-[28rem] gap-3">
        <div class="flex min-w-0 flex-1 flex-col overflow-hidden rounded-lg border bg-card">
          <!-- 时间线工具栏。这是只读面:不给撤销/重做/解锁(canEdit=false),也不在这里发布
               (发布走上方带失效守卫与禁用原因的「发布当前方案」,不要在工具栏再放一个)。 -->
          <SchedulingToolbar
            :scale="scale"
            :read-only="true"
            :can-undo="false"
            :can-redo="false"
            :dirty="false"
            :busy="false"
            :can-edit="false"
            :can-repreview="false"
            :can-release="false"
            :group-dimensions="SCHEDULING_GROUP_DIMENSIONS"
            :group-by="groupBy"
            searchable
            :search="search"
            :match-count="matchCount"
            :match-index="matchCursor"
            search-placeholder="搜工单 / 工序 / 工作中心"
            @scale-change="scale = $event"
            @group-change="setGroupBy"
            @zoom-in="sendCommand({ kind: 'zoomIn' })"
            @zoom-out="sendCommand({ kind: 'zoomOut' })"
            @today="sendCommand({ kind: 'scrollToToday' })"
            @fit="sendCommand({ kind: 'fitToScreen' })"
            @update:search="search = $event"
            @search-prev="revealMatch(matchCursor - 2)"
            @search-next="revealMatch(matchCursor)"
          />
          <div class="min-h-0 flex-1 p-2">
            <ResourceSchedulerBoard
              ref="boardRef"
              :model="model"
              :scale="scale"
              :read-only="true"
              :group-by="groupBy"
              @task-select="selectedTaskId = $event"
            />
          </div>
        </div>

        <aside
          v-if="detailPanelOpen && selectedTask"
          class="flex w-[21rem] flex-none flex-col overflow-y-auto rounded-lg border bg-card"
          :aria-label="detailPanelTitle"
          data-testid="scheduling-task-detail"
          :data-detail-kind="selectedKind"
        >
          <div class="flex items-center justify-between gap-2 px-4 pt-3 pb-1">
            <h3 class="text-sm font-semibold text-foreground">{{ detailPanelTitle }}</h3>
            <NvButton
              size="icon"
              variant="ghost"
              type="button"
              class="size-7 text-muted-foreground"
              :aria-label="`关闭${detailPanelTitle}`"
              @click="selectedTaskId = ''"
            >
              <XIcon class="size-4" aria-hidden="true" />
            </NvButton>
          </div>

          <TaskDetailPanel :task="selectedTask" :read-only="true" />

          <div class="grid gap-3 px-4 py-3">
            <dl class="grid gap-2 text-xs">
              <div v-if="selectedWorkCenterLabel" class="flex justify-between gap-3">
                <dt class="text-muted-foreground">工作中心</dt>
                <dd class="text-right font-medium text-foreground">
                  {{ selectedWorkCenterLabel }}
                </dd>
              </div>
              <!-- 工序号/锁定状态是工序级事实：工单汇总行与资源时间块上没有这两样，
                   不套模板显示「工序号：无」。 -->
              <template v-if="selectedKind === 'operation'">
                <div class="flex justify-between gap-3">
                  <dt class="text-muted-foreground">工序号</dt>
                  <dd class="text-right font-medium text-foreground">
                    {{ selectedTask.operationId || '无' }}
                  </dd>
                </div>
                <div class="flex justify-between gap-3">
                  <dt class="text-muted-foreground">锁定状态</dt>
                  <dd class="text-right font-medium text-foreground">
                    {{ selectedTask.locked ? '已锁定（重排程保持不变）' : '未锁定' }}
                  </dd>
                </div>
              </template>
              <div v-else-if="selectedKind === 'order'" class="flex justify-between gap-3">
                <dt class="text-muted-foreground">本方案工序数</dt>
                <dd class="text-right font-medium text-foreground">
                  {{ selectedOrderOperationCount }} 道
                </dd>
              </div>
            </dl>

            <div v-if="selectedConflicts.length" class="grid gap-1.5">
              <p class="text-xs font-semibold text-foreground">冲突说明</p>
              <p
                v-for="conflict in selectedConflicts"
                :key="conflict.id || conflict.message"
                class="rounded-md border border-warning/30 bg-warning/10 px-2.5 py-1.5 text-xs text-foreground"
              >
                {{ conflict.message || conflictReasonLabel[conflict.reason] }}
              </p>
            </div>

            <!-- 齐套率当前没有权威来源：APS 方案契约与 MES 工单读面都不返回它。
                 与其显示一个编出来的百分比，不如说明去哪儿看。
                 资源时间块（维护/停机/换线）不关联工单，这段说明对它不成立。 -->
            <p
              v-if="selectedKind !== 'block'"
              class="rounded-md border border-dashed bg-muted/30 px-2.5 py-2 text-xs leading-5 text-muted-foreground"
            >
              物料 / 数量 / 交期取自 MES 工单{{
                selectedWorkOrder ? '' : '（当前工单不在已加载的工单窗口内，故未显示）'
              }}。齐套率排程契约未返回，请在物料齐套页核对，此处不做估算。
            </p>

            <NvButton size="sm" variant="outline" type="button" @click="emit('openDetail')">
              <EyeIcon aria-hidden="true" />查看整方案明细
            </NvButton>
          </div>
        </aside>
      </div>

      <div class="overflow-hidden rounded-lg border bg-card">
        <!-- 图例要知道当前刻度:班次边界在日级视图下一条都画不出来,不能照列(台账 #41)。 -->
        <SchedulingLegend
          :categories="legendCategories"
          view="resource"
          :model="model"
          :scale="scale"
        />
        <p class="border-t border-border/50 px-4 py-2 text-xs text-muted-foreground">
          点击工序块在右侧查看该工序详情；整方案信息走「方案明细」。只读视图不支持拖拽或改派，编辑请回「排程总览」草案工作区。
        </p>
      </div>

      <div
        v-if="plan.unscheduledOperations?.length"
        class="grid gap-2 rounded-lg border bg-card p-4"
      >
        <h3 class="text-sm font-semibold text-foreground">未排工序</h3>
        <p
          v-for="item in plan.unscheduledOperations"
          :key="`${item.orderId}:${item.operationId}`"
          class="rounded-md border border-warning/30 bg-warning/5 p-3 text-sm text-foreground"
        >
          {{ item.orderId || '未关联工单' }} · {{ item.operationId || '工序' }} ·
          {{ item.message || '未返回不可排说明' }}
        </p>
      </div>
    </template>
  </section>
</template>
