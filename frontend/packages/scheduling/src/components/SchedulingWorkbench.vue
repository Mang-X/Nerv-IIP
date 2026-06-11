<script setup lang="ts">
import type { ScheduleAssignmentContract } from '@nerv-iip/api-client'
import { Tabs, TabsContent, TabsList, TabsTrigger, toast } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import type { EngineCommand, TaskDragPayload, TimeScale } from '../engine/engine'
import { useSchedulingEdits } from '../composables/useSchedulingEdits'
import { cloneModel } from '../model/clone'
import type { ScheduleModel, ScheduleTask } from '../model/types'
import GanttChart from './GanttChart.vue'
import ResourceSchedulerBoard from './ResourceSchedulerBoard.vue'
import ChangeSummaryPanel from './panels/ChangeSummaryPanel.vue'
import ConflictPanel from './panels/ConflictPanel.vue'
import InspectorSheet from './panels/InspectorSheet.vue'
import SchedulingToolbar from './panels/SchedulingToolbar.vue'
import UnscheduledPanel from './panels/UnscheduledPanel.vue'

// 排产工作台:工具栏 + 视图切换(工单甘特/资源排产板)+ 主图 + 右侧面板 + 检视。
// 编辑走「锁定—重预览」闭环;preview/release 由业务层注入(默认 preview 返回当前模型、release 空操作)。
const props = withDefaults(
  defineProps<{
    model?: ScheduleModel
    loading?: boolean
    readOnly?: boolean
    engineKind?: 'auto' | 'native' | 'dhtmlx'
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
const scale = ref<TimeScale>('auto')
const readOnly = ref(props.readOnly)
watch(() => props.readOnly, (v) => (readOnly.value = v))

const selectedTask = ref<ScheduleTask>()
const inspectorOpen = ref(false)

const previewFn = props.preview ?? (async () => workingModel.value)
const releaseFn = props.release ?? (async () => {})
const edits = useSchedulingEdits(workingModel, { preview: previewFn, release: releaseFn })

const ganttRef = ref<InstanceType<typeof GanttChart>>()
const boardRef = ref<InstanceType<typeof ResourceSchedulerBoard>>()
function sendCommand(cmd: EngineCommand) {
  ;(view.value === 'order' ? ganttRef.value : boardRef.value)?.command(cmd)
}

const conflicts = computed(() => workingModel.value.conflicts)
const unscheduled = computed(() => workingModel.value.unscheduled)
const changes = computed(() => workingModel.value.changes)

function onTaskSelect(taskId: string) {
  selectedTask.value = workingModel.value.tasks.find((t) => t.id === taskId)
  inspectorOpen.value = true
}
function focusTask(taskId: string) {
  sendCommand({ kind: 'selectTask', taskId })
  onTaskSelect(taskId)
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
      <Tabs v-model="view">
        <TabsList>
          <TabsTrigger value="order">工单甘特</TabsTrigger>
          <TabsTrigger value="resource">资源排产板</TabsTrigger>
        </TabsList>
      </Tabs>
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
        />
      </div>

      <aside class="flex w-[320px] shrink-0 flex-col border-l border-border/60 bg-card/60">
        <Tabs default-value="conflicts" class="flex min-h-0 flex-1 flex-col">
          <TabsList class="mx-3 mt-3">
            <TabsTrigger value="conflicts">冲突 {{ conflicts.length }}</TabsTrigger>
            <TabsTrigger value="unscheduled">未排 {{ unscheduled.length }}</TabsTrigger>
            <TabsTrigger value="changes">变更 {{ changes.length }}</TabsTrigger>
          </TabsList>
          <TabsContent value="conflicts" class="min-h-0 flex-1">
            <ConflictPanel :conflicts="conflicts" @select="focusTask" />
          </TabsContent>
          <TabsContent value="unscheduled" class="min-h-0 flex-1">
            <UnscheduledPanel :items="unscheduled" @fix="(o, op) => emit('fix', o, op)" />
          </TabsContent>
          <TabsContent value="changes" class="min-h-0 flex-1">
            <ChangeSummaryPanel :changes="changes" @select="focusTask" />
          </TabsContent>
        </Tabs>
      </aside>
    </div>

    <InspectorSheet v-model:open="inspectorOpen" :task="selectedTask" />
  </div>
</template>
