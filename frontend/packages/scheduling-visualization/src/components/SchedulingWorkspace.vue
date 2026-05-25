<script setup lang="ts">
import { computed, shallowRef } from 'vue'

import type { GanttFixture, GanttSelection } from '../model/gantt'
import type { ScheduleFixture, ScheduleSelection } from '../model/schedule'
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
}

const props = defineProps<Props>()

const fallbackGanttFixture = createMockGanttFixture()
const fallbackScheduleFixture = createMockScheduleFixture()
const commandStack = createSchedulingCommandStack()

const mode = shallowRef<SchedulingWorkspaceMode>('gantt')
const zoom = shallowRef<SchedulingZoom>('day')
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

function toggleExpandedTask(taskId: string) {
  expandedTaskIds.value = expandedTaskIds.value.includes(taskId)
    ? expandedTaskIds.value.filter((id) => id !== taskId)
    : [...expandedTaskIds.value, taskId]
}

function selectGantt(selection: GanttSelection) {
  activeSelection.value = { source: 'gantt', selection }
}

function selectSchedule(selection: ScheduleSelection) {
  activeSelection.value = { source: 'schedule', selection }
}

function clearSelection() {
  activeSelection.value = undefined
}
</script>

<template>
  <section class="scheduling-workspace" data-test="scheduling-workspace">
    <SchedulingToolbar
      :mode="mode"
      :zoom="zoom"
      :show-dependencies="showDependencies"
      :show-baselines="showBaselines"
      :show-capacity="showCapacity"
      :show-conflicts="showConflicts"
      :can-undo="commandStack.canUndo.value"
      :can-redo="commandStack.canRedo.value"
      @update:mode="mode = $event"
      @update:zoom="zoom = $event"
      @update:show-dependencies="showDependencies = $event"
      @update:show-baselines="showBaselines = $event"
      @update:show-capacity="showCapacity = $event"
      @update:show-conflicts="showConflicts = $event"
      @undo="commandStack.undo()"
      @redo="commandStack.redo()"
      @reset="commandStack.reset()"
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
          :preview-by-id="commandStack.previewById.value"
          @select="selectGantt"
          @toggle-expand="toggleExpandedTask"
        />
        <ScheduleChart
          v-else
          :fixture="scheduleFixture"
          :selected="scheduleSelection"
          :zoom="zoom"
          :show-capacity="showCapacity"
          :show-conflicts="showConflicts"
          :preview-by-id="commandStack.previewById.value"
          @select="selectSchedule"
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

