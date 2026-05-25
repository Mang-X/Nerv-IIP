<script setup lang="ts">
import { Button, Input } from '@nerv-iip/ui'
import {
  CalendarDays,
  ChartGantt,
  Layers,
  Redo2,
  RotateCcw,
  Undo2,
  Workflow,
  ZoomIn,
  ZoomOut,
} from 'lucide-vue-next'

import type { SchedulingZoom } from '../time-scale/timeScale'
import type { SchedulingLinkMode, SchedulingWorkspaceMode } from './types'

interface Props {
  mode: SchedulingWorkspaceMode
  zoom: SchedulingZoom
  query: string
  dependencyMode: SchedulingLinkMode
  showBaselines: boolean
  showCapacity: boolean
  showConflicts: boolean
  canUndo: boolean
  canRedo: boolean
}

interface Emits {
  'update:mode': [value: SchedulingWorkspaceMode]
  'update:zoom': [value: SchedulingZoom]
  'update:query': [value: string]
  'update:dependencyMode': [value: SchedulingLinkMode]
  'update:showBaselines': [value: boolean]
  'update:showCapacity': [value: boolean]
  'update:showConflicts': [value: boolean]
  undo: []
  redo: []
  reset: []
  commit: []
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const zoomOrder: SchedulingZoom[] = ['day', 'week', 'month']
const dependencyModeOrder: SchedulingLinkMode[] = ['none', 'selection', 'all']

function zoomBy(delta: number) {
  const index = zoomOrder.indexOf(props.zoom)
  const nextIndex = Math.min(Math.max(index + delta, 0), zoomOrder.length - 1)
  emit('update:zoom', zoomOrder[nextIndex])
}

function cycleDependencyMode() {
  const index = dependencyModeOrder.indexOf(props.dependencyMode)
  emit('update:dependencyMode', dependencyModeOrder[(index + 1) % dependencyModeOrder.length])
}
</script>

<template>
  <div class="scheduling-toolbar" data-test="scheduling-toolbar">
    <div class="scheduling-toolbar__group" aria-label="View mode">
      <Button
        :variant="mode === 'gantt' ? 'default' : 'outline'"
        size="sm"
        type="button"
        @click="emit('update:mode', 'gantt')"
      >
        <ChartGantt />
        Gantt
      </Button>
      <Button
        :variant="mode === 'schedule' ? 'default' : 'outline'"
        size="sm"
        type="button"
        @click="emit('update:mode', 'schedule')"
      >
        <CalendarDays />
        Schedule
      </Button>
    </div>

    <div class="scheduling-toolbar__group" aria-label="Zoom controls">
      <Button variant="outline" size="icon-sm" type="button" aria-label="Zoom out" @click="zoomBy(1)">
        <ZoomOut />
      </Button>
      <span class="scheduling-toolbar__zoom">{{ zoom }}</span>
      <Button variant="outline" size="icon-sm" type="button" aria-label="Zoom in" @click="zoomBy(-1)">
        <ZoomIn />
      </Button>
    </div>

    <div class="scheduling-toolbar__search">
      <Input
        :model-value="query"
        data-test="scheduling-search"
        placeholder="Search tasks, orders, resources"
        aria-label="Search schedule"
        @update:model-value="emit('update:query', String($event))"
      />
    </div>

    <div class="scheduling-toolbar__group scheduling-toolbar__group--wrap" aria-label="Layer toggles">
      <Button
        :variant="dependencyMode === 'none' ? 'outline' : 'secondary'"
        size="sm"
        type="button"
        :aria-label="`Dependency links: ${dependencyMode}`"
        @click="cycleDependencyMode"
      >
        <Workflow />
        Links: {{ dependencyMode }}
      </Button>
      <Button
        :variant="showBaselines ? 'secondary' : 'outline'"
        size="sm"
        type="button"
        @click="emit('update:showBaselines', !showBaselines)"
      >
        <Layers />
        Baselines
      </Button>
      <Button
        :variant="showCapacity ? 'secondary' : 'outline'"
        size="sm"
        type="button"
        @click="emit('update:showCapacity', !showCapacity)"
      >
        <Layers />
        Capacity
      </Button>
      <Button
        :variant="showConflicts ? 'secondary' : 'outline'"
        size="sm"
        type="button"
        @click="emit('update:showConflicts', !showConflicts)"
      >
        Conflicts
      </Button>
    </div>

    <div class="scheduling-toolbar__group" aria-label="Command history">
      <Button
        variant="outline"
        size="icon-sm"
        type="button"
        aria-label="Undo preview change"
        :disabled="!canUndo"
        @click="emit('undo')"
      >
        <Undo2 />
      </Button>
      <Button
        variant="outline"
        size="icon-sm"
        type="button"
        aria-label="Redo preview change"
        :disabled="!canRedo"
        @click="emit('redo')"
      >
        <Redo2 />
      </Button>
      <Button variant="outline" size="icon-sm" type="button" aria-label="Reset preview" @click="emit('reset')">
        <RotateCcw />
      </Button>
      <Button
        variant="default"
        size="sm"
        type="button"
        data-test="commit-preview"
        @click="emit('commit')"
      >
        Commit
      </Button>
    </div>
  </div>
</template>

<style scoped>
.scheduling-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid hsl(var(--border, 214 32% 91%));
  border-radius: 8px;
  background: hsl(var(--background, 0 0% 100%));
}

.scheduling-toolbar__group {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.scheduling-toolbar__group--wrap {
  flex-wrap: wrap;
}

.scheduling-toolbar__zoom {
  min-width: 44px;
  color: #334155;
  font-size: 12px;
  font-weight: 700;
  text-align: center;
  text-transform: capitalize;
}

.scheduling-toolbar__search {
  flex: 1 1 220px;
  min-width: 180px;
}
</style>
