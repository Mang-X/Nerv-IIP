<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'
import { AlertTriangle, ChevronDown, ChevronRight, LockKeyhole } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'

import type { LeaferSurface } from '../canvas/leaferTypes'
import type { GanttFixture, GanttRow, GanttSelection } from '../model/gantt'
import type { SchedulingPreviewCommand, SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom } from '../time-scale/timeScale'
import TimelineAxis from './TimelineAxis.vue'
import { createLeaferSurface } from '../canvas/createLeaferSurface'
import { filterGanttFixture } from '../state/filterFixtures'
import { flattenGanttTasks } from '../model/gantt'
import { buildGanttScene } from '../renderers/buildGanttScene'
import { renderSceneToLeafer } from '../renderers/renderSceneToLeafer'
import {
  buildGanttBarPositions,
  buildTimelineTicks,
  calculateTimelineContentWidth,
  shiftWindowByPixels,
} from '../time-scale/timelineLayout'
import { calculateVisibleRowRange } from '../time-scale/visibleRange'

interface Props {
  fixture: GanttFixture
  expandedTaskIds?: string[]
  selected?: GanttSelection
  zoom?: SchedulingZoom
  showDependencies?: boolean
  showBaselines?: boolean
  showConflicts?: boolean
  today?: string
  previewById?: Record<string, SchedulingPreviewWindow>
  query?: string
  width?: number
  rowHeight?: number
  maxViewportHeight?: number
}

interface Emits {
  select: [selection: GanttSelection]
  toggleExpand: [taskId: string]
  previewCommand: [command: SchedulingPreviewCommand]
}

const labelWidth = 220

const props = withDefaults(defineProps<Props>(), {
  expandedTaskIds: () => [],
  zoom: 'day',
  showDependencies: true,
  showBaselines: true,
  showConflicts: true,
  today: '2026-05-06T00:00:00.000Z',
  previewById: () => ({}),
  query: '',
  width: 960,
  rowHeight: 44,
  maxViewportHeight: 360,
})
const emit = defineEmits<Emits>()

const surfaceHost = useTemplateRef<HTMLDivElement>('surfaceHost')
const viewport = useTemplateRef<HTMLDivElement>('viewport')
const surface = shallowRef<LeaferSurface>()
const surfaceSize = shallowRef<{ width: number, height: number }>()
const isDisposed = shallowRef(false)
const scrollTop = shallowRef(0)
const scrollLeft = shallowRef(0)
const activeDrag = shallowRef<{
  taskId: string
  startX: number
  currentDeltaX: number
  before: SchedulingPreviewWindow
}>()

const expandedIds = computed(() => new Set(props.expandedTaskIds))
const filteredFixture = computed(() => filterGanttFixture(props.fixture, props.query))
const rows = computed<GanttRow[]>(() => flattenGanttTasks(filteredFixture.value.tasks, expandedIds.value))
const chartWidth = computed(() =>
  calculateTimelineContentWidth({
    start: filteredFixture.value.rangeStart,
    end: filteredFixture.value.rangeEnd,
    zoom: props.zoom,
    labelWidth,
    minWidth: props.width,
  }),
)
const livePreviewById = computed<Record<string, SchedulingPreviewWindow>>(() => {
  const drag = activeDrag.value
  if (!drag) {
    return props.previewById
  }

  return {
    ...props.previewById,
    [drag.taskId]: shiftWindowByPixels({
      start: drag.before.start,
      end: drag.before.end,
      deltaX: drag.currentDeltaX,
      rangeStart: filteredFixture.value.rangeStart,
      rangeEnd: filteredFixture.value.rangeEnd,
      width: chartWidth.value - labelWidth,
      zoom: props.zoom,
    }),
  }
})
const scene = computed(() =>
  buildGanttScene({
    fixture: filteredFixture.value,
    expandedTaskIds: expandedIds.value,
    width: chartWidth.value,
    rowHeight: props.rowHeight,
    zoom: props.zoom,
    showDependencies: props.showDependencies,
    showBaselines: props.showBaselines,
    showConflicts: props.showConflicts,
    today: props.today,
    previewById: livePreviewById.value,
  }),
)
const chartHeight = computed(() => `${scene.value.height}px`)
const viewportHeight = computed(() => Math.min(scene.value.height, props.maxViewportHeight))
const viewportHeightStyle = computed(() => `${viewportHeight.value}px`)
const visibleRange = computed(() =>
  calculateVisibleRowRange({
    scrollTop: scrollTop.value,
    viewportHeight: viewportHeight.value,
    rowHeight: props.rowHeight,
    rowCount: rows.value.length,
    overscan: 3,
  }),
)
const visibleRows = computed(() =>
  rows.value.slice(visibleRange.value.startIndex, visibleRange.value.endIndex).map((row, offset) => ({
    row,
    index: visibleRange.value.startIndex + offset,
  })),
)
const timelineTicks = computed(() =>
  buildTimelineTicks({
    start: filteredFixture.value.rangeStart,
    end: filteredFixture.value.rangeEnd,
    width: chartWidth.value - labelWidth,
    labelWidth,
    zoom: props.zoom,
  }),
)
const barPositions = computed(() => {
  const visibleTaskIds = new Set(visibleRows.value.map((item) => item.row.id))
  return buildGanttBarPositions({
    fixture: filteredFixture.value,
    rows: rows.value,
    width: chartWidth.value,
    rowHeight: props.rowHeight,
    zoom: props.zoom,
    labelWidth,
    previewById: livePreviewById.value,
  }).filter((position) => visibleTaskIds.has(position.task.id))
})
const summary = computed(() => {
  const conflictTaskIds = new Set(filteredFixture.value.conflicts.map((conflict) => conflict.taskId))
  return {
    rows: rows.value.length,
    conflicts: rows.value.filter((row) => conflictTaskIds.has(row.id)).length,
    dependencies: filteredFixture.value.dependencies.length,
  }
})

function isSelected(row: GanttRow) {
  return props.selected?.kind === 'task' && props.selected.id === row.id
}

function rowHasConflict(row: GanttRow) {
  return (row.conflictIds?.length ?? 0) > 0
}

async function syncSurface() {
  const host = surfaceHost.value
  if (!host) {
    return
  }

  if (
    surface.value
    && (surfaceSize.value?.width !== scene.value.width || surfaceSize.value.height !== scene.value.height)
  ) {
    surface.value.dispose()
    surface.value = undefined
    surfaceSize.value = undefined
  }

  if (!surface.value) {
    const nextSurface = await createLeaferSurface(host, scene.value.width, scene.value.height)
    if (isDisposed.value) {
      nextSurface.dispose()
      return
    }
    surface.value = nextSurface
    surfaceSize.value = { width: scene.value.width, height: scene.value.height }
  }

  renderSceneToLeafer(surface.value, scene.value)
}

function selectRow(row: GanttRow) {
  emit('select', { kind: 'task', id: row.id })
}

function toggleRow(row: GanttRow) {
  if (row.hasChildren) {
    emit('toggleExpand', row.id)
  }
}

function onScroll(event: Event) {
  const target = event.currentTarget as HTMLElement
  scrollTop.value = target.scrollTop
  scrollLeft.value = target.scrollLeft
}

function resetHorizontalScroll() {
  if (viewport.value) {
    viewport.value.scrollLeft = 0
  }

  scrollLeft.value = 0
}

function startDrag(task: GanttRow, event: PointerEvent) {
  if (event.currentTarget instanceof HTMLElement && typeof event.currentTarget.setPointerCapture === 'function') {
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  activeDrag.value = {
    taskId: task.id,
    startX: event.clientX,
    currentDeltaX: 0,
    before: props.previewById[task.id] ?? { start: task.start, end: task.end },
  }
}

function moveDrag(event: PointerEvent) {
  if (!activeDrag.value) {
    return
  }

  activeDrag.value = {
    ...activeDrag.value,
    currentDeltaX: event.clientX - activeDrag.value.startX,
  }
  event.preventDefault()
}

function finishDrag(task: GanttRow, event: PointerEvent) {
  if (
    event.currentTarget instanceof HTMLElement
    && typeof event.currentTarget.hasPointerCapture === 'function'
    && event.currentTarget.hasPointerCapture(event.pointerId)
  ) {
    event.currentTarget.releasePointerCapture(event.pointerId)
  }

  const drag = activeDrag.value
  activeDrag.value = undefined
  if (!drag || drag.taskId !== task.id) {
    return
  }

  const deltaX = event.clientX - drag.startX
  if (Math.abs(deltaX) < 4) {
    return
  }

  emit('previewCommand', {
    id: `preview-${task.id}-${Date.now()}`,
    targetId: task.id,
    kind: 'move',
    before: drag.before,
    after: shiftWindowByPixels({
      start: drag.before.start,
      end: drag.before.end,
      deltaX,
      rangeStart: filteredFixture.value.rangeStart,
      rangeEnd: filteredFixture.value.rangeEnd,
      width: chartWidth.value - labelWidth,
      zoom: props.zoom,
    }),
  })
}

function cancelDrag(event: PointerEvent) {
  if (
    event.currentTarget instanceof HTMLElement
    && typeof event.currentTarget.hasPointerCapture === 'function'
    && event.currentTarget.hasPointerCapture(event.pointerId)
  ) {
    event.currentTarget.releasePointerCapture(event.pointerId)
  }

  activeDrag.value = undefined
}

onMounted(syncSurface)
watch(scene, syncSurface, { flush: 'post' })
watch(() => props.zoom, resetHorizontalScroll, { flush: 'post' })
onBeforeUnmount(() => {
  isDisposed.value = true
  surface.value?.dispose()
  surface.value = undefined
})
</script>

<template>
  <section class="scheduling-chart scheduling-chart--gantt" data-test="gantt-chart">
    <header class="scheduling-chart__header">
      <div class="scheduling-chart__title-block">
        <p class="scheduling-chart__eyebrow">Gantt</p>
        <h3 class="scheduling-chart__title">
          {{ fixture.name }}
        </h3>
      </div>
      <div class="scheduling-chart__meta">
        <Badge variant="secondary">{{ summary.rows }} rows</Badge>
        <Badge variant="outline">{{ summary.dependencies }} links</Badge>
        <Badge :variant="summary.conflicts > 0 ? 'destructive' : 'secondary'">
          {{ summary.conflicts }} conflicts
        </Badge>
      </div>
    </header>

    <TimelineAxis
      :ticks="timelineTicks"
      :width="chartWidth"
      :label-width="labelWidth"
      :scroll-left="scrollLeft"
    />

    <div
      ref="viewport"
      class="scheduling-chart__viewport"
      data-test="gantt-viewport"
      :style="{ height: viewportHeightStyle }"
      @scroll="onScroll"
    >
      <div class="scheduling-chart__scroll-plane" :style="{ width: `${chartWidth}px`, height: chartHeight }">
        <div
          ref="surfaceHost"
          aria-hidden="true"
          class="scheduling-chart__surface"
          :style="{ width: `${chartWidth}px`, height: chartHeight }"
        />

        <div class="scheduling-chart__rows" :style="{ height: chartHeight }">
        <button
          v-for="item in visibleRows"
          :key="item.row.id"
          class="gantt-row"
          :class="{ 'gantt-row--selected': isSelected(item.row) }"
          type="button"
          :data-test="`gantt-row-${item.row.id}`"
          :style="{ height: `${rowHeight}px`, top: `${item.index * rowHeight}px`, left: `${scrollLeft}px` }"
          @click="selectRow(item.row)"
        >
          <span class="gantt-row__main" :style="{ paddingLeft: `${item.row.depth * 16}px` }">
            <button
              v-if="item.row.hasChildren"
              class="gantt-row__expand"
              type="button"
              :aria-label="expandedIds.has(item.row.id) ? 'Collapse task group' : 'Expand task group'"
              @click.stop="toggleRow(item.row)"
            >
              <ChevronDown v-if="expandedIds.has(item.row.id)" />
              <ChevronRight v-else />
            </button>
            <span v-else class="gantt-row__spacer" />
            <span class="gantt-row__name">{{ item.row.name }}</span>
          </span>
          <span class="gantt-row__code">{{ item.row.code }}</span>
          <span class="gantt-row__status">{{ item.row.status }}</span>
          <LockKeyhole v-if="item.row.isLocked" class="gantt-row__icon" aria-label="Locked" />
          <AlertTriangle
            v-if="rowHasConflict(item.row)"
            class="gantt-row__icon gantt-row__icon--warning"
            aria-label="Has conflict"
          />
        </button>

        <button
          v-for="position in barPositions"
          :key="`bar-${position.task.id}`"
          class="gantt-bar-overlay"
          :class="{ 'gantt-bar-overlay--dragging': activeDrag?.taskId === position.task.id }"
          type="button"
          :data-test="`gantt-bar-${position.task.id}`"
          :style="{
            top: `${position.top}px`,
            left: `${position.left}px`,
            width: `${position.width}px`,
          }"
          @click.stop="selectRow(position.task)"
          @pointerdown.stop="startDrag(position.task, $event)"
          @pointermove.stop="moveDrag"
          @pointerup.stop="finishDrag(position.task, $event)"
          @pointercancel.stop="cancelDrag"
        >
          {{ position.task.code }}
        </button>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.scheduling-chart {
  overflow: hidden;
  border: 1px solid hsl(var(--border, 214 32% 91%));
  border-radius: 8px;
  background: hsl(var(--background, 0 0% 100%));
  color: hsl(var(--foreground, 222 47% 11%));
}

.scheduling-chart__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border-bottom: 1px solid hsl(var(--border, 214 32% 91%));
  background: linear-gradient(180deg, #f8fafc 0%, #ffffff 100%);
}

.scheduling-chart__title-block {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.scheduling-chart__eyebrow {
  margin: 0;
  color: #475569;
  font-size: 12px;
  font-weight: 600;
}

.scheduling-chart__title {
  margin: 0;
  overflow: hidden;
  color: #0f172a;
  font-size: 15px;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.scheduling-chart__meta {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
}

.scheduling-chart__viewport {
  position: relative;
  overflow: auto;
  min-height: 44px;
}

.scheduling-chart__scroll-plane {
  position: relative;
}

.scheduling-chart__surface {
  position: absolute;
  inset: 0 auto auto 0;
  pointer-events: none;
}

.scheduling-chart__rows {
  position: relative;
  width: 100%;
}

.gantt-row {
  position: absolute;
  z-index: 4;
  box-sizing: border-box;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 18px 18px;
  grid-template-rows: 1fr 1fr;
  align-items: center;
  width: 220px;
  padding: 5px 10px;
  border: 0;
  border-right: 1px solid rgba(226, 232, 240, 0.9);
  border-bottom: 1px solid rgba(226, 232, 240, 0.82);
  background: rgba(255, 255, 255, 0.92);
  box-shadow: 1px 0 0 rgba(226, 232, 240, 0.9);
  color: #0f172a;
  cursor: pointer;
  font: inherit;
  text-align: left;
}

.gantt-bar-overlay {
  position: absolute;
  z-index: 3;
  box-sizing: border-box;
  display: flex;
  align-items: center;
  height: 22px;
  padding-inline: 7px;
  border: 1px solid rgba(30, 64, 175, 0.48);
  border-radius: 7px;
  background: rgba(219, 234, 254, 0.9);
  color: #1e3a8a;
  cursor: grab;
  font-size: 11px;
  font-weight: 750;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  touch-action: none;
  transition:
    border-color 120ms ease,
    background 120ms ease,
    box-shadow 120ms ease;
}

.gantt-bar-overlay:active {
  cursor: grabbing;
}

.gantt-bar-overlay--dragging {
  z-index: 5;
  border-color: #2563eb;
  background: #dbeafe;
  box-shadow:
    0 10px 24px rgba(15, 23, 42, 0.16),
    0 0 0 2px rgba(37, 99, 235, 0.18);
  cursor: grabbing;
}

.gantt-row:hover {
  background: rgba(14, 165, 233, 0.08);
}

.gantt-row--selected {
  background: rgba(37, 99, 235, 0.12);
  box-shadow: inset 3px 0 0 #2563eb;
}

.gantt-row__main {
  display: flex;
  grid-column: 1 / 2;
  grid-row: 1 / 2;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.gantt-row__expand {
  display: inline-grid;
  place-items: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: #334155;
  cursor: pointer;
}

.gantt-row__expand:hover {
  background: rgba(15, 23, 42, 0.08);
}

.gantt-row__expand svg,
.gantt-row__icon {
  width: 14px;
  height: 14px;
}

.gantt-row__spacer {
  width: 22px;
  height: 1px;
}

.gantt-row__name,
.gantt-row__code,
.gantt-row__status {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.gantt-row__name {
  font-size: 13px;
  font-weight: 650;
}

.gantt-row__code {
  grid-column: 1 / 2;
  grid-row: 2 / 3;
  padding-left: 28px;
}

.gantt-row__status {
  display: none;
}

.gantt-row__code,
.gantt-row__status {
  color: #475569;
  font-size: 12px;
}

.gantt-row__icon {
  grid-row: 1 / 3;
  color: #64748b;
}

.gantt-row__icon--warning {
  color: #d97706;
}

@media (max-width: 720px) {
  .scheduling-chart__header {
    align-items: flex-start;
    flex-direction: column;
  }

  .gantt-row {
    grid-template-columns: minmax(0, 1fr) 18px 18px;
    width: 220px;
    padding-inline: 10px;
  }
}
</style>
