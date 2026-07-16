<script setup lang="ts">
import type { ScheduleAssignmentContract } from '@nerv-iip/api-client'
import { NvButton, NvTabs, NvTabsContent, NvTabsList, NvTabsTrigger, toast } from '@nerv-iip/ui'
import { ListFilterIcon, PanelRightCloseIcon, PanelRightOpenIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import type { EngineCommand, TaskDragPayload, TimeScale } from '../engine/engine'
import { useSchedulingEdits } from '../composables/useSchedulingEdits'
import { cloneModel } from '../model/clone'
import type { ScheduleModel, ScheduleTask } from '../model/types'
import GanttChart from './GanttChart.vue'
import ResourceSchedulerBoard from './ResourceSchedulerBoard.vue'
import ChangeSummaryPanel from './panels/ChangeSummaryPanel.vue'
import ConflictPanel from './panels/ConflictPanel.vue'
import SchedulingLegend from './panels/SchedulingLegend.vue'
import SchedulingToolbar from './panels/SchedulingToolbar.vue'
import TaskDetailPanel from './panels/TaskDetailPanel.vue'
import UnscheduledPanel from './panels/UnscheduledPanel.vue'

// 排产工作台:工具栏 + 视图切换(工单甘特/资源排产板)+ 主图 + 右侧面板 + 检视。
// 编辑走「锁定—重预览」闭环;preview/release 由业务层注入(默认 preview 返回当前模型、release 空操作)。
const props = withDefaults(
  defineProps<{
    model?: ScheduleModel
    loading?: boolean
    readOnly?: boolean
    engineKind?: 'auto' | 'dhtmlx'
    defaultView?: 'order' | 'resource'
    preview?: (locked: ScheduleAssignmentContract[]) => Promise<ScheduleModel>
    release?: (planId: string) => Promise<void>
  }>(),
  { loading: false, readOnly: false, engineKind: 'auto', defaultView: 'order' },
)

const emit = defineEmits<{ fix: [orderId: string, operationId: string] }>()

const EMPTY_MODEL: ScheduleModel = {
  tasks: [], links: [], resources: [], loads: [], conflicts: [], unscheduled: [], changes: [],
  horizon: { startUtc: '', endUtc: '' }, meta: { planId: '', status: 'preview', algorithmVersion: '' },
}

const workingModel = ref<ScheduleModel>(props.model ?? EMPTY_MODEL)
watch(
  () => props.model,
  (m) => { if (m) workingModel.value = cloneModel(m) },
  { immediate: true },
)

const view = ref<'order' | 'resource'>(props.defaultView)
const showLegend = ref(true)
const scale = ref<TimeScale>('auto')
const readOnly = ref(props.readOnly)
watch(() => props.readOnly, (v) => (readOnly.value = v))

const selectedTask = ref<ScheduleTask>()
const sidebarOpen = ref(true)

const previewFn = props.preview ?? (async () => workingModel.value)
const releaseFn = props.release ?? (async () => {})
const edits = useSchedulingEdits(workingModel, { preview: previewFn, release: releaseFn })

// 只有业务层注入了 preview/release 才展示对应动作,避免只读/演示挂载时出现假成功提示。
const canRepreview = computed(() => !!props.preview)
const canRelease = computed(() => !!props.release)

const ganttRef = ref<InstanceType<typeof GanttChart>>()
const boardRef = ref<InstanceType<typeof ResourceSchedulerBoard>>()
function sendCommand(cmd: EngineCommand) {
  ;(view.value === 'order' ? ganttRef.value : boardRef.value)?.command(cmd)
}

const conflicts = computed(() => workingModel.value.conflicts)
const unscheduled = computed(() => workingModel.value.unscheduled)
const changes = computed(() => workingModel.value.changes)
const legendCategories = computed(() => {
  const seen = new Map<string, string>()
  for (const t of workingModel.value.tasks) {
    if (t.type !== 'operation' || !t.colorKey || seen.has(t.colorKey)) continue
    seen.set(t.colorKey, t.dimensions?.workCenter?.label ?? t.workCenterId ?? t.colorKey)
  }
  return [...seen].map(([key, label]) => ({ key, label }))
})

function onTaskSelect(taskId: string) {
  selectedTask.value = workingModel.value.tasks.find((t) => t.id === taskId)
  sidebarOpen.value = true // 选中即在侧栏展示详情(不再弹抽屉)
}
function onToggleLock(taskId: string, locked: boolean) {
  edits.setLocked(taskId, locked)
  selectedTask.value = workingModel.value.tasks.find((t) => t.id === taskId)
  toast.success(locked ? '已锁定该工序' : '已解锁,可拖拽调整')
}
function focusTask(taskId: string) {
  sendCommand({ kind: 'selectTask', taskId })
  onTaskSelect(taskId)
}
// 锁定块拖拽尝试:节流提示(拖动会连发)并把详情面板聚焦到该块,让「解锁」送到眼前。
let lastLockedToastAt = 0
function onLockedDragAttempt(taskId: string) {
  selectedTask.value = workingModel.value.tasks.find((t) => t.id === taskId)
  sidebarOpen.value = true
  const now = Date.now()
  if (now - lastLockedToastAt < 1500) return
  lastLockedToastAt = now
  toast.info('该工序已锁定,先解锁再拖拽')
}
function onDrag(p: TaskDragPayload) {
  edits.onTaskDragEnd(p)
}
async function onRepreview() {
  try {
    await edits.repreview()
    toast.success('已按锁定项重新排程')
  } catch {
    toast.error('重新排程失败,请稍后重试')
  }
}
async function onRelease() {
  try {
    await edits.release()
    toast.success('计划已发布')
  } catch {
    toast.error('发布失败,请稍后重试')
  }
}
</script>

<template>
  <div class="flex h-full min-h-[480px] flex-col overflow-hidden rounded-xl border border-border/70 bg-card shadow-sm">
    <SchedulingToolbar
      :scale="scale"
      :read-only="readOnly"
      :can-undo="edits.canUndo.value"
      :can-redo="edits.canRedo.value"
      :dirty="edits.dirty.value"
      :busy="edits.busy.value"
      :can-repreview="canRepreview"
      :can-release="canRelease"
      @scale-change="scale = $event"
      @zoom-in="sendCommand({ kind: 'zoomIn' })"
      @zoom-out="sendCommand({ kind: 'zoomOut' })"
      @today="sendCommand({ kind: 'scrollToToday' })"
      @fit="sendCommand({ kind: 'fitToScreen' })"
      @undo="edits.undo()"
      @redo="edits.redo()"
      @toggle-read-only="readOnly = !readOnly"
      @repreview="onRepreview"
      @release="onRelease"
    />

    <div class="flex items-center gap-2 border-b border-border/60 px-4 py-2.5">
      <NvTabs v-model="view">
        <NvTabsList>
          <NvTabsTrigger value="order">工单甘特</NvTabsTrigger>
          <NvTabsTrigger value="resource">资源排产板</NvTabsTrigger>
        </NvTabsList>
      </NvTabs>
      <NvButton
        size="sm"
        variant="ghost"
        class="ml-auto h-8 gap-1.5"
        :class="showLegend ? 'text-foreground' : 'text-muted-foreground'"
        @click="showLegend = !showLegend"
      >
        <ListFilterIcon aria-hidden="true" />
        图例
      </NvButton>
    </div>

    <div class="flex min-h-0 flex-1">
      <div class="min-w-0 flex-1">
        <GanttChart
          v-if="view === 'order'"
          ref="ganttRef"
          :model="workingModel"
          :scale="scale"
          :read-only="readOnly"
          :loading="loading"
          :engine-kind="engineKind"
          @task-select="onTaskSelect"
          @task-drag-end="onDrag"
          @conflict-click="focusTask"
          @locked-drag-attempt="onLockedDragAttempt"
        />
        <ResourceSchedulerBoard
          v-else
          ref="boardRef"
          :model="workingModel"
          :scale="scale"
          :read-only="readOnly"
          :loading="loading"
          :engine-kind="engineKind"
          @task-select="onTaskSelect"
          @task-drag-end="onDrag"
          @conflict-click="focusTask"
          @locked-drag-attempt="onLockedDragAttempt"
        />
      </div>

      <!-- 单元素侧栏:宽度在 展开(320) ↔ 折叠(细条 w-9) 间过渡,内容随 sidebarOpen 淡入淡出。
           overflow-hidden 防止收窄时内容溢出;prefers-reduced-motion 下过渡降级为无。 -->
      <aside
        class="relative shrink-0 overflow-hidden border-l border-border/60 bg-card/60 transition-[width] duration-200 ease-[cubic-bezier(0.22,1,0.36,1)] motion-reduce:transition-none"
        :class="sidebarOpen ? 'w-[320px]' : 'w-9'"
      >
        <!-- 折叠态:细条上的展开按钮(淡入,收起时不可交互) -->
        <button
          type="button"
          class="absolute inset-0 flex items-center justify-center text-muted-foreground transition-[opacity,color] duration-200 ease-[cubic-bezier(0.22,1,0.36,1)] hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring motion-reduce:transition-none"
          :class="sidebarOpen ? 'pointer-events-none opacity-0' : 'opacity-100'"
          :tabindex="sidebarOpen ? -1 : 0"
          :aria-hidden="sidebarOpen"
          aria-label="展开侧栏"
          @click="sidebarOpen = true"
        >
          <PanelRightOpenIcon class="size-4" aria-hidden="true" />
        </button>

        <!-- 展开态:详情 + 面板(固定 320 宽,收窄时被 overflow-hidden 裁掉并淡出) -->
        <div
          class="flex h-full w-[320px] flex-col transition-opacity duration-200 ease-[cubic-bezier(0.22,1,0.36,1)] motion-reduce:transition-none"
          :class="sidebarOpen ? 'opacity-100' : 'pointer-events-none opacity-0'"
          :aria-hidden="!sidebarOpen"
        >
          <div class="flex items-center justify-between px-3 pt-2.5">
            <span class="text-xs font-semibold tracking-wide text-muted-foreground">详情与排程</span>
            <NvButton size="icon" variant="ghost" class="size-7 text-muted-foreground" :tabindex="sidebarOpen ? 0 : -1" aria-label="收起侧栏" @click="sidebarOpen = false">
              <PanelRightCloseIcon class="size-4" aria-hidden="true" />
            </NvButton>
          </div>

          <!-- 选中详情(常驻,取代弹出抽屉) -->
          <TaskDetailPanel :task="selectedTask" @toggle-lock="onToggleLock" />

          <NvTabs default-value="conflicts" class="flex min-h-0 flex-1 flex-col">
            <NvTabsList class="mx-3 mt-3">
              <NvTabsTrigger value="conflicts">冲突 {{ conflicts.length }}</NvTabsTrigger>
              <NvTabsTrigger value="unscheduled">未排 {{ unscheduled.length }}</NvTabsTrigger>
              <NvTabsTrigger value="changes">变更 {{ changes.length }}</NvTabsTrigger>
            </NvTabsList>
            <NvTabsContent value="conflicts" class="min-h-0 flex-1">
              <ConflictPanel :conflicts="conflicts" @select="focusTask" />
            </NvTabsContent>
            <NvTabsContent value="unscheduled" class="min-h-0 flex-1">
              <UnscheduledPanel :items="unscheduled" @fix="(o, op) => emit('fix', o, op)" />
            </NvTabsContent>
            <NvTabsContent value="changes" class="min-h-0 flex-1">
              <ChangeSummaryPanel :changes="changes" @select="focusTask" />
            </NvTabsContent>
          </NvTabs>
        </div>
      </aside>
    </div>

    <SchedulingLegend v-if="showLegend" :categories="legendCategories" :view="view" />
  </div>
</template>
