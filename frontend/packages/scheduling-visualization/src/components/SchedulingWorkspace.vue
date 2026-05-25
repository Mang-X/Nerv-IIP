<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'

import type { GanttFixture, GanttSelection } from '../model/gantt'
import type { ScheduleFixture, ScheduleSelection } from '../model/schedule'
import type { SchedulingPreviewCommand, SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom } from '../time-scale/timeScale'
import type { SchedulingLinkMode, SchedulingWorkspaceMode, SchedulingWorkspaceSelection } from './types'
import GanttChart from './GanttChart.vue'
import ScheduleChart from './ScheduleChart.vue'
import SchedulingDetailSheet from './SchedulingDetailSheet.vue'
import SchedulingLegend from './SchedulingLegend.vue'
import SchedulingToolbar from './SchedulingToolbar.vue'
import { createMockGanttFixture, createMockScheduleFixture } from '../model/fixtures'
import { createSchedulingCommandStack } from '../state/useSchedulingCommands'

interface Props {
  ganttFixture?: GanttFixture
  scheduleFixture?: ScheduleFixture
  initialMode?: SchedulingWorkspaceMode
  previewById?: Record<string, SchedulingPreviewWindow>
}

interface Emits {
  selectionChange: [selection: SchedulingWorkspaceSelection | undefined]
  previewCommand: [command: SchedulingPreviewCommand]
  commitPreview: [previewById: Record<string, SchedulingPreviewWindow>]
  resetPreview: []
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()
defineSlots<{
  toolbar?: (props: {
    mode: SchedulingWorkspaceMode
    zoom: SchedulingZoom
    query: string
    dependencyMode: SchedulingLinkMode
  }) => unknown
  ganttTaskRow?: (props: Record<string, unknown>) => unknown
  ganttTaskBar?: (props: Record<string, unknown>) => unknown
  ganttTooltip?: (props: Record<string, unknown>) => unknown
  scheduleResourceRow?: (props: Record<string, unknown>) => unknown
  scheduleCalendarHighlight?: (props: Record<string, unknown>) => unknown
  scheduleOperationBar?: (props: Record<string, unknown>) => unknown
  scheduleTooltip?: (props: Record<string, unknown>) => unknown
  detail?: (props: { selection: SchedulingWorkspaceSelection }) => unknown
  legend?: () => unknown
}>()

const fallbackGanttFixture = createMockGanttFixture()
const fallbackScheduleFixture = createMockScheduleFixture()
const commandStack = createSchedulingCommandStack()

const mode = shallowRef<SchedulingWorkspaceMode>(props.initialMode ?? 'gantt')
const zoom = shallowRef<SchedulingZoom>('day')
const query = shallowRef('')
const dependencyMode = shallowRef<SchedulingLinkMode>('all')
const showBaselines = shallowRef(true)
const showCapacity = shallowRef(true)
const showConflicts = shallowRef(true)
const expandedTaskIds = shallowRef<string[]>(['phase-engineering'])
const activeSelection = shallowRef<SchedulingWorkspaceSelection>()

const ganttFixture = computed(() => props.ganttFixture ?? fallbackGanttFixture)
const scheduleFixture = computed(() => props.scheduleFixture ?? fallbackScheduleFixture)
const defaultExpandedTaskIds = computed(() =>
  ganttFixture.value.tasks
    .filter((task) => (task.children?.length ?? 0) > 0)
    .map((task) => task.id),
)
const ganttSelection = computed<GanttSelection | undefined>(() =>
  activeSelection.value?.source === 'gantt' ? activeSelection.value.selection : undefined,
)
const scheduleSelection = computed<ScheduleSelection | undefined>(() =>
  activeSelection.value?.source === 'schedule' ? activeSelection.value.selection : undefined,
)
const effectivePreviewById = computed(() => ({
  ...(props.previewById ?? {}),
  ...commandStack.previewById.value,
}))

function toggleExpandedTask(taskId: string) {
  expandedTaskIds.value = expandedTaskIds.value.includes(taskId)
    ? expandedTaskIds.value.filter((id) => id !== taskId)
    : [...expandedTaskIds.value, taskId]
}

function selectGantt(selection: GanttSelection) {
  activeSelection.value = { source: 'gantt', selection }
  emit('selectionChange', activeSelection.value)
}

function selectSchedule(selection: ScheduleSelection) {
  activeSelection.value = { source: 'schedule', selection }
  emit('selectionChange', activeSelection.value)
}

function clearSelection() {
  activeSelection.value = undefined
  emit('selectionChange', undefined)
}

function applyPreviewCommand(command: SchedulingPreviewCommand) {
  commandStack.execute(command)
  emit('previewCommand', command)
}

function resetPreview() {
  commandStack.reset()
  emit('resetPreview')
}

function commitPreview() {
  emit('commitPreview', effectivePreviewById.value)
}

watch(
  () => props.ganttFixture?.id,
  () => {
    expandedTaskIds.value = props.ganttFixture ? defaultExpandedTaskIds.value : ['phase-engineering']
  },
  { flush: 'sync', immediate: true },
)
</script>

<template>
  <section class="scheduling-workspace" data-test="scheduling-workspace">
    <slot
      name="toolbar"
      :mode="mode"
      :zoom="zoom"
      :query="query"
      :dependency-mode="dependencyMode"
    >
      <SchedulingToolbar
        :mode="mode"
        :zoom="zoom"
        :query="query"
        :dependency-mode="dependencyMode"
        :show-baselines="showBaselines"
        :show-capacity="showCapacity"
        :show-conflicts="showConflicts"
        :can-undo="commandStack.canUndo.value"
        :can-redo="commandStack.canRedo.value"
        @update:mode="mode = $event"
        @update:zoom="zoom = $event"
        @update:query="query = $event"
        @update:dependency-mode="dependencyMode = $event"
        @update:show-baselines="showBaselines = $event"
        @update:show-capacity="showCapacity = $event"
        @update:show-conflicts="showConflicts = $event"
        @undo="commandStack.undo()"
        @redo="commandStack.redo()"
        @reset="resetPreview"
        @commit="commitPreview"
      />
    </slot>

    <div
      class="scheduling-workspace__main"
      :class="{ 'scheduling-workspace__main--with-detail': activeSelection }"
    >
      <div class="scheduling-workspace__chart">
        <GanttChart
          v-if="mode === 'gantt'"
          :fixture="ganttFixture"
          :expanded-task-ids="expandedTaskIds"
          :selected="ganttSelection"
          :zoom="zoom"
          :dependency-mode="dependencyMode"
          :show-baselines="showBaselines"
          :show-conflicts="showConflicts"
          :preview-by-id="effectivePreviewById"
          :query="query"
          @select="selectGantt"
          @toggle-expand="toggleExpandedTask"
          @preview-command="applyPreviewCommand"
        >
          <template v-if="$slots.ganttTaskRow" #taskRow="slotProps">
            <slot name="ganttTaskRow" v-bind="slotProps" />
          </template>
          <template v-if="$slots.ganttTaskBar" #taskBar="slotProps">
            <slot name="ganttTaskBar" v-bind="slotProps" />
          </template>
          <template #tooltip="slotProps">
            <slot name="ganttTooltip" v-bind="slotProps">
              {{ slotProps.text }}
            </slot>
          </template>
        </GanttChart>
        <ScheduleChart
          v-else
          :fixture="scheduleFixture"
          :selected="scheduleSelection"
          :zoom="zoom"
          :show-capacity="showCapacity"
          :dependency-mode="dependencyMode"
          :show-conflicts="showConflicts"
          :preview-by-id="effectivePreviewById"
          :query="query"
          @select="selectSchedule"
          @preview-command="applyPreviewCommand"
        >
          <template v-if="$slots.scheduleResourceRow" #resourceRow="slotProps">
            <slot name="scheduleResourceRow" v-bind="slotProps" />
          </template>
          <template v-if="$slots.scheduleCalendarHighlight" #calendarHighlight="slotProps">
            <slot name="scheduleCalendarHighlight" v-bind="slotProps" />
          </template>
          <template v-if="$slots.scheduleOperationBar" #operationBar="slotProps">
            <slot name="scheduleOperationBar" v-bind="slotProps" />
          </template>
          <template #tooltip="slotProps">
            <slot name="scheduleTooltip" v-bind="slotProps">
              {{ slotProps.text }}
            </slot>
          </template>
        </ScheduleChart>
      </div>

      <slot v-if="activeSelection" name="detail" :selection="activeSelection">
        <SchedulingDetailSheet
          :gantt-fixture="ganttFixture"
          :schedule-fixture="scheduleFixture"
          :selection="activeSelection"
          @clear="clearSelection"
        />
      </slot>
    </div>

    <slot name="legend">
      <SchedulingLegend />
    </slot>
  </section>
</template>

<style scoped>
.scheduling-workspace {
  display: grid;
  gap: 12px;
  width: 100%;
  min-width: 0;
}

.scheduling-workspace__main {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 12px;
  align-items: stretch;
}

.scheduling-workspace__main--with-detail {
  grid-template-columns: minmax(0, 1fr) minmax(280px, 320px);
}

.scheduling-workspace__chart {
  min-width: 0;
}

@media (max-width: 960px) {
  .scheduling-workspace__main,
  .scheduling-workspace__main--with-detail {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
