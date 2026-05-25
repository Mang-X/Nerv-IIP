<script setup lang="ts">
import { computed, shallowRef } from 'vue'

import type { GanttFixture, GanttSelection } from '../model/gantt'
import type { ScheduleFixture, ScheduleSelection } from '../model/schedule'
import type { SchedulingPreviewCommand, SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom } from '../time-scale/timeScale'
import type { SchedulingWorkspaceMode, SchedulingWorkspaceSelection } from './types'
import GanttChart from './GanttChart.vue'
import ScheduleChart from './ScheduleChart.vue'
import SchedulingDetailSheet from './SchedulingDetailSheet.vue'
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

const fallbackGanttFixture = createMockGanttFixture()
const fallbackScheduleFixture = createMockScheduleFixture()
const commandStack = createSchedulingCommandStack()

const mode = shallowRef<SchedulingWorkspaceMode>(props.initialMode ?? 'gantt')
const zoom = shallowRef<SchedulingZoom>('day')
const query = shallowRef('')
const showDependencies = shallowRef(true)
const showBaselines = shallowRef(true)
const showCapacity = shallowRef(true)
const showConflicts = shallowRef(true)
const expandedTaskIds = shallowRef<string[]>(['phase-engineering'])
const activeSelection = shallowRef<SchedulingWorkspaceSelection>()

const ganttFixture = computed(() => props.ganttFixture ?? fallbackGanttFixture)
const scheduleFixture = computed(() => props.scheduleFixture ?? fallbackScheduleFixture)
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
</script>

<template>
  <section class="scheduling-workspace" data-test="scheduling-workspace">
    <SchedulingToolbar
      :mode="mode"
      :zoom="zoom"
      :query="query"
      :show-dependencies="showDependencies"
      :show-baselines="showBaselines"
      :show-capacity="showCapacity"
      :show-conflicts="showConflicts"
      :can-undo="commandStack.canUndo.value"
      :can-redo="commandStack.canRedo.value"
      @update:mode="mode = $event"
      @update:zoom="zoom = $event"
      @update:query="query = $event"
      @update:show-dependencies="showDependencies = $event"
      @update:show-baselines="showBaselines = $event"
      @update:show-capacity="showCapacity = $event"
      @update:show-conflicts="showConflicts = $event"
      @undo="commandStack.undo()"
      @redo="commandStack.redo()"
      @reset="resetPreview"
      @commit="commitPreview"
    />

    <div class="scheduling-workspace__main">
      <div class="scheduling-workspace__chart">
        <GanttChart
          v-if="mode === 'gantt'"
          :fixture="ganttFixture"
          :expanded-task-ids="expandedTaskIds"
          :selected="ganttSelection"
          :zoom="zoom"
          :show-dependencies="showDependencies"
          :show-baselines="showBaselines"
          :show-conflicts="showConflicts"
          :preview-by-id="effectivePreviewById"
          :query="query"
          @select="selectGantt"
          @toggle-expand="toggleExpandedTask"
          @preview-command="applyPreviewCommand"
        />
        <ScheduleChart
          v-else
          :fixture="scheduleFixture"
          :selected="scheduleSelection"
          :zoom="zoom"
          :show-capacity="showCapacity"
          :show-conflicts="showConflicts"
          :preview-by-id="effectivePreviewById"
          :query="query"
          @select="selectSchedule"
          @preview-command="applyPreviewCommand"
        />
      </div>

      <SchedulingDetailSheet
        :gantt-fixture="ganttFixture"
        :schedule-fixture="scheduleFixture"
        :selection="activeSelection"
        @clear="clearSelection"
      />
    </div>
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
  grid-template-columns: minmax(0, 1fr) minmax(280px, 320px);
  gap: 12px;
  align-items: stretch;
}

.scheduling-workspace__chart {
  min-width: 0;
}

@media (max-width: 960px) {
  .scheduling-workspace__main {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
