<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'
import { AlertTriangle, LockKeyhole } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'

import type { LeaferSurface } from '../canvas/leaferTypes'
import type { ScheduleFixture, ScheduleOperation, ScheduleRow, ScheduleSelection } from '../model/schedule'
import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom } from '../time-scale/timeScale'
import { createLeaferSurface } from '../canvas/createLeaferSurface'
import { groupScheduleRows } from '../model/schedule'
import { buildScheduleScene } from '../renderers/buildScheduleScene'
import { renderSceneToLeafer } from '../renderers/renderSceneToLeafer'
import { createTimeScale } from '../time-scale/timeScale'

interface Props {
  fixture: ScheduleFixture
  selected?: ScheduleSelection
  zoom?: SchedulingZoom
  showCapacity?: boolean
  showConflicts?: boolean
  today?: string
  previewById?: Record<string, SchedulingPreviewWindow>
  width?: number
  rowHeight?: number
}

interface OperationPosition {
  operation: ScheduleOperation
  top: number
  left: number
  width: number
}

interface Emits {
  select: [selection: ScheduleSelection]
}

const labelWidth = 230

const props = withDefaults(defineProps<Props>(), {
  zoom: 'day',
  showCapacity: true,
  showConflicts: true,
  today: '2026-05-06T00:00:00.000Z',
  previewById: () => ({}),
  width: 960,
  rowHeight: 52,
})
const emit = defineEmits<Emits>()

const surfaceHost = useTemplateRef<HTMLDivElement>('surfaceHost')
const surface = shallowRef<LeaferSurface>()
const isDisposed = shallowRef(false)

const rows = computed<ScheduleRow[]>(() =>
  groupScheduleRows(props.fixture.resources, props.fixture.operations),
)
const scene = computed(() =>
  buildScheduleScene({
    fixture: props.fixture,
    width: props.width,
    rowHeight: props.rowHeight,
    zoom: props.zoom,
    showCapacity: props.showCapacity,
    showConflicts: props.showConflicts,
    today: props.today,
    previewById: props.previewById,
  }),
)
const chartHeight = computed(() => `${scene.value.height}px`)
const summary = computed(() => ({
  resources: props.fixture.resources.length,
  operations: props.fixture.operations.length,
  overloads: props.fixture.capacityBands.filter((band) => band.isOverloaded).length,
}))
const operationPositions = computed<OperationPosition[]>(() => {
  const scale = createTimeScale({
    start: props.fixture.rangeStart,
    end: props.fixture.rangeEnd,
    width: props.width - labelWidth,
    zoom: props.zoom,
  })
  const rowIndexByResource = new Map(rows.value.map((row, index) => [row.id, index]))

  return props.fixture.operations.flatMap((operation) => {
    const rowIndex = rowIndexByResource.get(operation.resourceId)
    if (rowIndex === undefined) {
      return []
    }

    const preview = props.previewById[operation.id]
    const start = preview?.start ?? operation.start
    const end = preview?.end ?? operation.end
    const left = labelWidth + scale.dateToX(start)
    const endX = labelWidth + scale.dateToX(end)

    return [{
      operation,
      top: rowIndex * props.rowHeight + 10,
      left,
      width: Math.max(endX - left, 72),
    }]
  })
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

  if (!surface.value) {
    const nextSurface = await createLeaferSurface(host, props.width, scene.value.height)
    if (isDisposed.value) {
      nextSurface.dispose()
      return
    }
    surface.value = nextSurface
  }

  renderSceneToLeafer(surface.value, scene.value)
}

onMounted(syncSurface)
watch(scene, syncSurface, { flush: 'post' })
onBeforeUnmount(() => {
  isDisposed.value = true
  surface.value?.dispose()
  surface.value = undefined
})
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

    <div class="scheduling-chart__viewport" :style="{ height: chartHeight }">
      <div
        ref="surfaceHost"
        aria-hidden="true"
        class="scheduling-chart__surface"
        :style="{ width: `${width}px`, height: chartHeight }"
      />

      <div class="scheduling-chart__rows" :style="{ height: chartHeight, minWidth: `${width}px` }">
        <button
          v-for="row in rows"
          :key="row.id"
          class="schedule-resource"
          :class="{ 'schedule-resource--selected': isSelectedResource(row) }"
          type="button"
          :data-test="`schedule-resource-${row.id}`"
          :style="{ height: `${rowHeight}px` }"
          @click="emit('select', { kind: 'resource', id: row.id })"
        >
          <span class="schedule-resource__code">{{ row.workCenterCode }}</span>
          <span class="schedule-resource__name">{{ row.name }}</span>
          <span class="schedule-resource__calendar">{{ row.calendarLabel }}</span>
        </button>

        <button
          v-for="position in operationPositions"
          :key="position.operation.id"
          class="schedule-operation"
          :class="{ 'schedule-operation--selected': isSelectedOperation(position.operation) }"
          type="button"
          :data-test="`schedule-operation-${position.operation.id}`"
          :style="{
            top: `${position.top}px`,
            left: `${position.left}px`,
            width: `${position.width}px`,
          }"
          @click.stop="emit('select', { kind: 'operation', id: position.operation.id })"
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
  overflow: auto hidden;
  min-height: 52px;
}

.scheduling-chart__surface {
  position: absolute;
  inset: 0 auto auto 0;
}

.scheduling-chart__rows {
  position: relative;
  width: 100%;
}

.schedule-resource {
  display: grid;
  grid-template-columns: 78px minmax(0, 1fr);
  grid-template-rows: 1fr 1fr;
  align-items: center;
  width: 230px;
  padding: 7px 12px;
  border: 0;
  border-bottom: 1px solid rgba(226, 232, 240, 0.82);
  background: rgba(248, 250, 252, 0.9);
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
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: center;
  gap: 5px;
  min-height: 32px;
  padding: 5px 8px;
  border: 1px solid rgba(29, 78, 216, 0.38);
  border-radius: 7px;
  background: rgba(219, 234, 254, 0.82);
  color: #0f172a;
  cursor: pointer;
  font: inherit;
  text-align: left;
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
