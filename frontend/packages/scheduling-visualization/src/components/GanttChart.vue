<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'
import { AlertTriangle, ChevronDown, ChevronRight, LockKeyhole } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'

import type { LeaferSurface } from '../canvas/leaferTypes'
import type { GanttFixture, GanttRow, GanttSelection } from '../model/gantt'
import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom } from '../time-scale/timeScale'
import { createLeaferSurface } from '../canvas/createLeaferSurface'
import { flattenGanttTasks } from '../model/gantt'
import { buildGanttScene } from '../renderers/buildGanttScene'
import { renderSceneToLeafer } from '../renderers/renderSceneToLeafer'

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
  width?: number
  rowHeight?: number
}

interface Emits {
  select: [selection: GanttSelection]
  toggleExpand: [taskId: string]
}

const props = withDefaults(defineProps<Props>(), {
  expandedTaskIds: () => [],
  zoom: 'day',
  showDependencies: true,
  showBaselines: true,
  showConflicts: true,
  today: '2026-05-06T00:00:00.000Z',
  previewById: () => ({}),
  width: 960,
  rowHeight: 44,
})
const emit = defineEmits<Emits>()

const surfaceHost = useTemplateRef<HTMLDivElement>('surfaceHost')
const surface = shallowRef<LeaferSurface>()
const isDisposed = shallowRef(false)

const expandedIds = computed(() => new Set(props.expandedTaskIds))
const rows = computed<GanttRow[]>(() => flattenGanttTasks(props.fixture.tasks, expandedIds.value))
const scene = computed(() =>
  buildGanttScene({
    fixture: props.fixture,
    expandedTaskIds: expandedIds.value,
    width: props.width,
    rowHeight: props.rowHeight,
    zoom: props.zoom,
    showDependencies: props.showDependencies,
    showBaselines: props.showBaselines,
    showConflicts: props.showConflicts,
    today: props.today,
    previewById: props.previewById,
  }),
)
const chartHeight = computed(() => `${scene.value.height}px`)
const summary = computed(() => {
  const conflictTaskIds = new Set(props.fixture.conflicts.map((conflict) => conflict.taskId))
  return {
    rows: rows.value.length,
    conflicts: rows.value.filter((row) => conflictTaskIds.has(row.id)).length,
    dependencies: props.fixture.dependencies.length,
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

function selectRow(row: GanttRow) {
  emit('select', { kind: 'task', id: row.id })
}

function toggleRow(row: GanttRow) {
  if (row.hasChildren) {
    emit('toggleExpand', row.id)
  }
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

    <div class="scheduling-chart__viewport" :style="{ height: chartHeight }">
      <div
        ref="surfaceHost"
        aria-hidden="true"
        class="scheduling-chart__surface"
        :style="{ width: `${width}px`, height: chartHeight }"
      />

      <div class="scheduling-chart__rows" :style="{ height: chartHeight }">
        <button
          v-for="row in rows"
          :key="row.id"
          class="gantt-row"
          :class="{ 'gantt-row--selected': isSelected(row) }"
          type="button"
          :data-test="`gantt-row-${row.id}`"
          :style="{ minHeight: `${rowHeight}px` }"
          @click="selectRow(row)"
        >
          <span class="gantt-row__main" :style="{ paddingLeft: `${row.depth * 16}px` }">
            <button
              v-if="row.hasChildren"
              class="gantt-row__expand"
              type="button"
              :aria-label="expandedIds.has(row.id) ? 'Collapse task group' : 'Expand task group'"
              @click.stop="toggleRow(row)"
            >
              <ChevronDown v-if="expandedIds.has(row.id)" />
              <ChevronRight v-else />
            </button>
            <span v-else class="gantt-row__spacer" />
            <span class="gantt-row__name">{{ row.name }}</span>
          </span>
          <span class="gantt-row__code">{{ row.code }}</span>
          <span class="gantt-row__status">{{ row.status }}</span>
          <LockKeyhole v-if="row.isLocked" class="gantt-row__icon" aria-label="Locked" />
          <AlertTriangle
            v-if="rowHasConflict(row)"
            class="gantt-row__icon gantt-row__icon--warning"
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
  min-height: 44px;
}

.scheduling-chart__surface {
  position: absolute;
  inset: 0 auto auto 0;
}

.scheduling-chart__rows {
  position: relative;
  width: 100%;
  min-width: 720px;
}

.gantt-row {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) 96px 86px 24px 24px;
  align-items: center;
  width: 100%;
  padding: 0 12px;
  border: 0;
  border-bottom: 1px solid rgba(226, 232, 240, 0.82);
  background: transparent;
  color: #0f172a;
  cursor: pointer;
  font: inherit;
  text-align: left;
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

.gantt-row__code,
.gantt-row__status {
  color: #475569;
  font-size: 12px;
}

.gantt-row__icon {
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
    grid-template-columns: minmax(190px, 1fr) 72px 74px 20px 20px;
    padding-inline: 10px;
  }
}
</style>
