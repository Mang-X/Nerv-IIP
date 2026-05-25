<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'
import { AlertTriangle, LockKeyhole } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'

import type { LeaferSurface } from '../canvas/leaferTypes'
import type { ScheduleFixture, ScheduleOperation, ScheduleRow, ScheduleSelection } from '../model/schedule'
import type { SchedulingPreviewCommand, SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom } from '../time-scale/timeScale'
import TimelineAxis from './TimelineAxis.vue'
import { createLeaferSurface } from '../canvas/createLeaferSurface'
import { filterScheduleFixture } from '../state/filterFixtures'
import { groupScheduleRows } from '../model/schedule'
import { buildScheduleScene } from '../renderers/buildScheduleScene'
import { renderSceneToLeafer } from '../renderers/renderSceneToLeafer'
import {
  buildScheduleOperationPositions,
  buildTimelineTicks,
  calculateTimelineContentWidth,
  shiftWindowByPixels,
} from '../time-scale/timelineLayout'
import { calculateVisibleRowRange } from '../time-scale/visibleRange'

interface Props {
  fixture: ScheduleFixture
  selected?: ScheduleSelection
  zoom?: SchedulingZoom
  showCapacity?: boolean
  showConflicts?: boolean
  today?: string
  previewById?: Record<string, SchedulingPreviewWindow>
  query?: string
  width?: number
  rowHeight?: number
  maxViewportHeight?: number
}

interface Emits {
  select: [selection: ScheduleSelection]
  previewCommand: [command: SchedulingPreviewCommand]
}

const labelWidth = 230

const props = withDefaults(defineProps<Props>(), {
  zoom: 'day',
  showCapacity: true,
  showConflicts: true,
  today: '2026-05-06T00:00:00.000Z',
  previewById: () => ({}),
  query: '',
  width: 960,
  rowHeight: 52,
  maxViewportHeight: 360,
})
const emit = defineEmits<Emits>()

const surfaceHost = useTemplateRef<HTMLDivElement>('surfaceHost')
const viewport = useTemplateRef<HTMLDivElement>('viewport')
const scrollPlane = useTemplateRef<HTMLDivElement>('scrollPlane')
const surface = shallowRef<LeaferSurface>()
const surfaceSize = shallowRef<{ width: number, height: number }>()
const isDisposed = shallowRef(false)
const scrollTop = shallowRef(0)
const scrollLeft = shallowRef(0)
const activeDrag = shallowRef<{
  operationId: string
  startX: number
  currentDeltaX: number
  currentResourceId: string
  before: SchedulingPreviewWindow
}>()

const filteredFixture = computed(() => filterScheduleFixture(props.fixture, props.query))
const rows = computed<ScheduleRow[]>(() =>
  groupScheduleRows(filteredFixture.value.resources, filteredFixture.value.operations),
)
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
    [drag.operationId]: {
      ...shiftWindowByPixels({
        start: drag.before.start,
        end: drag.before.end,
        deltaX: drag.currentDeltaX,
        rangeStart: filteredFixture.value.rangeStart,
        rangeEnd: filteredFixture.value.rangeEnd,
        width: chartWidth.value - labelWidth,
        zoom: props.zoom,
      }),
      resourceId: drag.currentResourceId,
    },
  }
})
const scene = computed(() =>
  buildScheduleScene({
    fixture: filteredFixture.value,
    width: chartWidth.value,
    rowHeight: props.rowHeight,
    zoom: props.zoom,
    showCapacity: props.showCapacity,
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
const summary = computed(() => ({
  resources: filteredFixture.value.resources.length,
  operations: filteredFixture.value.operations.length,
  overloads: filteredFixture.value.capacityBands.filter((band) => band.isOverloaded).length,
}))
const timelineTicks = computed(() =>
  buildTimelineTicks({
    start: filteredFixture.value.rangeStart,
    end: filteredFixture.value.rangeEnd,
    width: chartWidth.value - labelWidth,
    labelWidth,
    zoom: props.zoom,
  }),
)
const operationPositions = computed(() => {
  const visibleResourceIds = new Set(visibleRows.value.map((item) => item.row.id))
  return buildScheduleOperationPositions({
    fixture: filteredFixture.value,
    rows: rows.value,
    width: chartWidth.value,
    rowHeight: props.rowHeight,
    zoom: props.zoom,
    labelWidth,
    previewById: livePreviewById.value,
  }).filter((position) => visibleResourceIds.has(position.resourceId))
})

function isSelectedResource(row: ScheduleRow) {
  return props.selected?.kind === 'resource' && props.selected.id === row.id
}

function isSelectedOperation(operation: ScheduleOperation) {
  return props.selected?.kind === 'operation' && props.selected.id === operation.id
}

function operationHasConflict(operation: ScheduleOperation) {
  return (operation.conflictIds?.length ?? 0) > 0
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

onMounted(syncSurface)
watch(scene, syncSurface, { flush: 'post' })
watch(() => props.zoom, resetHorizontalScroll, { flush: 'post' })
onBeforeUnmount(() => {
  isDisposed.value = true
  surface.value?.dispose()
  surface.value = undefined
})

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

function startDrag(operation: ScheduleOperation, event: PointerEvent) {
  if (event.currentTarget instanceof HTMLElement && typeof event.currentTarget.setPointerCapture === 'function') {
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  activeDrag.value = {
    operationId: operation.id,
    startX: event.clientX,
    currentDeltaX: 0,
    currentResourceId: props.previewById[operation.id]?.resourceId ?? operation.resourceId,
    before: props.previewById[operation.id] ?? {
      start: operation.start,
      end: operation.end,
      resourceId: operation.resourceId,
    },
  }
}

function getResourceIdFromPointer(event: PointerEvent, fallbackResourceId: string) {
  const host = scrollPlane.value
  if (!host || rows.value.length === 0) {
    return fallbackResourceId
  }

  const rect = host.getBoundingClientRect()
  const relativeY = event.clientY - rect.top
  const rowIndex = Math.min(Math.max(Math.floor(relativeY / props.rowHeight), 0), rows.value.length - 1)

  return rows.value[rowIndex]?.id ?? fallbackResourceId
}

function updateActiveDrag(event: PointerEvent) {
  const drag = activeDrag.value
  if (!drag) {
    return
  }

  activeDrag.value = {
    ...drag,
    currentDeltaX: event.clientX - drag.startX,
    currentResourceId: getResourceIdFromPointer(event, drag.currentResourceId),
  }
}

function moveDrag(event: PointerEvent) {
  if (!activeDrag.value) {
    return
  }

  updateActiveDrag(event)
  event.preventDefault()
}

function finishDrag(operation: ScheduleOperation, event: PointerEvent) {
  if (
    event.currentTarget instanceof HTMLElement
    && typeof event.currentTarget.hasPointerCapture === 'function'
    && event.currentTarget.hasPointerCapture(event.pointerId)
  ) {
    event.currentTarget.releasePointerCapture(event.pointerId)
  }

  const drag = activeDrag.value
  updateActiveDrag(event)
  const finalDrag = activeDrag.value
  activeDrag.value = undefined
  if (!drag || !finalDrag || drag.operationId !== operation.id) {
    return
  }

  const deltaX = finalDrag.currentDeltaX
  const resourceChanged = finalDrag.currentResourceId !== (drag.before.resourceId ?? operation.resourceId)
  if (Math.abs(deltaX) < 4 && !resourceChanged) {
    return
  }

  emit('previewCommand', {
    id: `preview-${operation.id}-${Date.now()}`,
    targetId: operation.id,
    kind: 'move',
    before: drag.before,
    after: {
      ...shiftWindowByPixels({
        start: drag.before.start,
        end: drag.before.end,
        deltaX,
        rangeStart: filteredFixture.value.rangeStart,
        rangeEnd: filteredFixture.value.rangeEnd,
        width: chartWidth.value - labelWidth,
        zoom: props.zoom,
      }),
      resourceId: finalDrag.currentResourceId,
    },
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
</script>

<template>
  <section class="scheduling-chart scheduling-chart--schedule" data-test="schedule-chart">
    <header class="scheduling-chart__header">
      <div class="scheduling-chart__title-block">
        <p class="scheduling-chart__eyebrow">Schedule</p>
        <h3 class="scheduling-chart__title">
          {{ fixture.name }}
        </h3>
      </div>
      <div class="scheduling-chart__meta">
        <Badge variant="secondary">{{ summary.resources }} resources</Badge>
        <Badge variant="outline">{{ summary.operations }} ops</Badge>
        <Badge :variant="summary.overloads > 0 ? 'destructive' : 'secondary'">
          {{ summary.overloads }} overloads
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
      data-test="schedule-viewport"
      :style="{ height: viewportHeightStyle }"
      @scroll="onScroll"
    >
      <div
        ref="scrollPlane"
        class="scheduling-chart__scroll-plane"
        data-test="schedule-scroll-plane"
        :style="{ width: `${chartWidth}px`, height: chartHeight }"
      >
        <div
          ref="surfaceHost"
          aria-hidden="true"
          class="scheduling-chart__surface"
          :style="{ width: `${chartWidth}px`, height: chartHeight }"
        />

      <div class="scheduling-chart__rows" :style="{ height: chartHeight, minWidth: `${chartWidth}px` }">
        <button
          v-for="item in visibleRows"
          :key="item.row.id"
          class="schedule-resource"
          :class="{ 'schedule-resource--selected': isSelectedResource(item.row) }"
          type="button"
          :data-test="`schedule-resource-${item.row.id}`"
          :style="{ height: `${rowHeight}px`, top: `${item.index * rowHeight}px`, left: `${scrollLeft}px` }"
          @click="emit('select', { kind: 'resource', id: item.row.id })"
        >
          <span class="schedule-resource__code">{{ item.row.workCenterCode }}</span>
          <span class="schedule-resource__name">{{ item.row.name }}</span>
          <span class="schedule-resource__calendar">{{ item.row.calendarLabel }}</span>
        </button>

        <button
          v-for="position in operationPositions"
          :key="position.operation.id"
          class="schedule-operation"
          :class="{
            'schedule-operation--selected': isSelectedOperation(position.operation),
            'schedule-operation--dragging': activeDrag?.operationId === position.operation.id,
          }"
          type="button"
          :data-test="`schedule-operation-${position.operation.id}`"
          :data-preview-resource-id="position.resourceId"
          :style="{
            top: `${position.top}px`,
            left: `${position.left}px`,
            width: `${position.width}px`,
            height: `${position.height}px`,
          }"
          @click.stop="emit('select', { kind: 'operation', id: position.operation.id })"
          @pointerdown.stop="startDrag(position.operation, $event)"
          @pointermove.stop="moveDrag"
          @pointerup.stop="finishDrag(position.operation, $event)"
          @pointercancel.stop="cancelDrag"
        >
          <span class="schedule-operation__title">{{ position.operation.workOrderCode }}</span>
          <span class="schedule-operation__subtitle">{{ position.operation.name }}</span>
          <LockKeyhole
            v-if="position.operation.isLocked"
            class="schedule-operation__icon"
            aria-label="Locked"
          />
          <AlertTriangle
            v-if="operationHasConflict(position.operation)"
            class="schedule-operation__icon schedule-operation__icon--warning"
            aria-label="Has conflict"
          />
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
  min-height: 52px;
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

.schedule-resource {
  position: absolute;
  z-index: 4;
  box-sizing: border-box;
  display: grid;
  grid-template-columns: 78px minmax(0, 1fr);
  grid-template-rows: 1fr 1fr;
  align-items: center;
  width: 230px;
  padding: 7px 12px;
  border: 0;
  border-bottom: 1px solid rgba(226, 232, 240, 0.82);
  background: rgba(248, 250, 252, 0.9);
  box-shadow: 1px 0 0 rgba(226, 232, 240, 0.9);
  color: #0f172a;
  cursor: pointer;
  font: inherit;
  text-align: left;
}

.schedule-resource:hover {
  background: rgba(14, 165, 233, 0.08);
}

.schedule-resource--selected {
  background: rgba(37, 99, 235, 0.12);
  box-shadow: inset 3px 0 0 #2563eb;
}

.schedule-resource__code {
  grid-row: 1 / 3;
  color: #1d4ed8;
  font-size: 12px;
  font-weight: 750;
}

.schedule-resource__name,
.schedule-resource__calendar {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.schedule-resource__name {
  font-size: 13px;
  font-weight: 650;
}

.schedule-resource__calendar {
  color: #64748b;
  font-size: 11px;
}

.schedule-operation {
  position: absolute;
  z-index: 3;
  box-sizing: border-box;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: center;
  gap: 5px;
  padding: 5px 8px;
  border: 1px solid rgba(29, 78, 216, 0.38);
  border-radius: 7px;
  background: rgba(219, 234, 254, 0.82);
  color: #0f172a;
  cursor: grab;
  font: inherit;
  text-align: left;
  touch-action: none;
  transition:
    border-color 120ms ease,
    background 120ms ease,
    box-shadow 120ms ease,
    transform 120ms ease;
  will-change: top, left;
}

.schedule-operation:hover {
  border-color: #2563eb;
  background: rgba(191, 219, 254, 0.96);
}

.schedule-operation--selected {
  border-color: #0f172a;
  background: #dbeafe;
  box-shadow: 0 0 0 2px rgba(15, 23, 42, 0.18);
}

.schedule-operation--dragging {
  z-index: 5;
  border-color: #2563eb;
  background: #dbeafe;
  box-shadow:
    0 10px 24px rgba(15, 23, 42, 0.16),
    0 0 0 2px rgba(37, 99, 235, 0.18);
  cursor: grabbing;
}

.schedule-operation__title,
.schedule-operation__subtitle {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.schedule-operation__title {
  color: #1e3a8a;
  font-size: 12px;
  font-weight: 750;
}

.schedule-operation__subtitle {
  grid-column: 1 / 2;
  color: #334155;
  font-size: 11px;
}

.schedule-operation__icon {
  width: 13px;
  height: 13px;
  color: #64748b;
}

.schedule-operation__icon--warning {
  color: #dc2626;
}

@media (max-width: 720px) {
  .scheduling-chart__header {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
