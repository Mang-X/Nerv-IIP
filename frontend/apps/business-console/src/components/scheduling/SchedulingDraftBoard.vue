<script setup lang="ts">
import {
  GanttChart,
  ResourceSchedulerBoard,
  SchedulingToolbar,
  SchedulingLegend,
  TaskDetailPanel,
  resolveTimeScale,
  type EngineCommand,
  type TimeScale,
  type ScheduleModel,
  type TaskDragPayload,
} from '@nerv-iip/scheduling'
import type { BusinessConsoleSchedulingMaterialShortageSummary } from '@nerv-iip/api-client'
import SchedulingMaterialShortageSummary from './SchedulingMaterialShortageSummary.vue'
import type { WorkingSchedulePendingOperation } from '@/composables/useWorkingScheduleDraft'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import type { EntityPickerOption } from '@nerv-iip/ui'
import {
  NvButton,
  NvEntityPicker,
  NvInput,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { WORK_CENTER_FAMILY_LIST } from '@/data/workCenterFamilies'

const props = defineProps<{
  model?: ScheduleModel
  materialShortageSummary?: BusinessConsoleSchedulingMaterialShortageSummary[] | null
  pendingOperations?: WorkingSchedulePendingOperation[]
  readOnly?: boolean
  /**
   * 本次会话内落库成功的 override 工序键（`orderId:operationId`）。
   * 仅为会话内乐观回显：override 目前没有读接口，刷新或换会话后无法回读，徽标即消失。
   * followUp: 待后端补 override 查询 facade 后，改为服务端回读、跨会话回显。
   */
  persistedOperationKeys?: string[]
  /** 持久化请求进行中（禁用所有持久锁定按钮，避免并发重复提交）。 */
  persistPending?: boolean
}>()
const emit = defineEmits<{
  move: [payload: TaskDragPayload]
  update: [taskId: string, patch: { resourceId?: string; startUtc?: string; endUtc?: string }]
  lock: [taskId: string, locked: boolean]
  lockedAttempt: [taskId: string]
  moveToPending: [taskId: string]
  restorePending: [taskId: string]
  persistOverride: [taskId: string]
}>()
const view = shallowRef('gantt')
const scale = shallowRef<TimeScale>('auto')
const ganttRef = shallowRef<InstanceType<typeof GanttChart>>()
const resourceRef = shallowRef<InstanceType<typeof ResourceSchedulerBoard>>()
const activeBoard = computed(() => (view.value === 'gantt' ? ganttRef.value : resourceRef.value))
const selectedTaskId = shallowRef('')
const selectedTask = computed(() =>
  props.model?.tasks.find((task) => task.id === selectedTaskId.value),
)
const detailTitle = computed(() =>
  selectedTask.value?.blockKind
    ? '资源时间块详情'
    : selectedTask.value?.type === 'order'
      ? '工单详情'
      : '工序详情',
)
const search = shallowRef('')
const matchCursor = shallowRef(0)
const searchMatches = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query) return []
  return (props.model?.tasks ?? []).filter(
    (task) =>
      task.type === 'operation' &&
      !task.blockKind &&
      [
        task.orderId,
        task.operationId,
        task.text,
        task.resourceId,
        task.workCenterId,
        task.dimensions?.workCenter?.label,
        task.product,
      ].some((value) => value?.toLowerCase().includes(query)),
  )
})
const legendCategories = computed(() => {
  const used = new Set(
    (props.model?.tasks ?? [])
      .filter((task) => task.type === 'operation')
      .map((task) => task.colorKey),
  )
  return WORK_CENTER_FAMILY_LIST.filter((family) => used.has(family.key)).map(({ key, label }) => ({
    key,
    label,
  }))
})
function sendCommand(command: EngineCommand) {
  activeBoard.value?.command(command)
}
function setScale(value: TimeScale) {
  scale.value = value
  sendCommand({ kind: 'scaleTo', scale: value })
}
function zoom(direction: -1 | 1) {
  const scales: TimeScale[] = ['hour', 'day', 'week', 'month']
  const current = resolveTimeScale(scale.value, props.model?.horizon)
  setScale(scales[Math.max(0, Math.min(scales.length - 1, scales.indexOf(current) + direction))])
}
function revealMatch(index: number) {
  const matches = searchMatches.value
  if (!matches.length) return
  const next = ((index % matches.length) + matches.length) % matches.length
  matchCursor.value = next + 1
  selectedTaskId.value = matches[next].id
  sendCommand({ kind: 'revealTask', taskId: selectedTaskId.value })
  sendCommand({ kind: 'selectTask', taskId: selectedTaskId.value })
}
// 切换图面时应用当前查阅状态，草案本身始终来自父页面。
watch(
  [activeBoard, searchMatches],
  () => {
    sendCommand({ kind: 'setSearchHighlight', taskIds: searchMatches.value.map((task) => task.id) })
    matchCursor.value = 0
    if (searchMatches.value.length) {
      const selectedIndex = searchMatches.value.findIndex(
        (task) => task.id === selectedTaskId.value,
      )
      revealMatch(selectedIndex < 0 ? 0 : selectedIndex)
    } else if (selectedTask.value) {
      sendCommand({ kind: 'revealTask', taskId: selectedTaskId.value })
      sendCommand({ kind: 'selectTask', taskId: selectedTaskId.value })
    }
  },
  { flush: 'post' },
)
watch(
  () => props.model,
  () => {
    if (!selectedTask.value) selectedTaskId.value = ''
  },
)
watch(
  () => props.model?.meta.planId,
  () => {
    search.value = ''
    selectedTaskId.value = ''
  },
)
// 物料风险（软约束）：已排但缺料的工序，开工前必须先备料。
const materialRisks = computed(() => props.model?.materialRisks ?? [])
// 设备数据风险（软约束）：排在状态未知设备上的工序，开工前需人工确认设备可用。
const equipmentRisks = computed(() => props.model?.equipmentRisks ?? [])
// 改派资源只能在本排程的资源泳道之间挑，与资源排产板拖拽换泳道同一口径。
const resourceOptions = computed<EntityPickerOption[]>(() =>
  (props.model?.resources ?? []).map((resource) => ({
    value: resource.id,
    label: resource.text || resource.id,
  })),
)
</script>

<template>
  <section class="grid gap-3 rounded-lg border bg-card p-4" data-testid="scheduling-draft-board">
    <header>
      <h2 class="font-semibold">排程草案工作区</h2>
      <p class="text-sm text-muted-foreground">甘特拖拽、资源泳道和表格编辑共享同一份草稿状态。</p>
    </header>
    <section
      class="grid gap-2 rounded-md border bg-muted/20 p-3"
      data-testid="operation-pending-pool"
    >
      <div class="flex items-center justify-between gap-2">
        <h3 class="text-sm font-semibold">工序待排池</h3>
        <span class="text-xs text-muted-foreground"
          >{{ pendingOperations?.length ?? 0 }} 道工序</span
        >
      </div>
      <p v-if="!pendingOperations?.length" class="text-sm text-muted-foreground">
        暂无未排、移回或受失效影响的工序。
      </p>
      <ul v-else class="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
        <li
          v-for="item in pendingOperations"
          :key="item.id"
          class="flex items-center justify-between gap-2 rounded-md border bg-card p-2 text-sm"
        >
          <div class="min-w-0">
            <p class="truncate font-medium">{{ item.orderId }} · {{ item.operationId }}</p>
            <p class="truncate text-xs text-muted-foreground">
              {{
                item.source === 'removed'
                  ? '规划员移回'
                  : item.source === 'invalidated'
                    ? '失效影响'
                    : '求解未排'
              }}
              ·
              {{
                item.message ||
                (item.reasonCode
                  ? describeScheduleInvalidationReason(item.reasonCode)
                  : '待重新排程')
              }}
            </p>
          </div>
          <NvButton
            v-if="item.canRestore && item.taskId"
            size="sm"
            variant="outline"
            type="button"
            :disabled="readOnly"
            @click="emit('restorePending', item.taskId)"
            >恢复</NvButton
          >
        </li>
      </ul>
    </section>
    <SchedulingMaterialShortageSummary v-if="model" :shortages="materialShortageSummary ?? []" />
    <!--
      物料风险横幅：齐套是开工门槛不是排产门槛。缺料工单照排进方案，
      这里显式告诉规划员「哪些工序开工前必须先备料」，避免拿着方案去发布却被 MES 齐套门拦下。
    -->
    <section
      v-if="materialRisks.length"
      class="grid gap-1.5 rounded-md border border-warning/40 bg-warning/10 px-3 py-2.5 text-sm"
      data-testid="scheduling-material-risks"
    >
      <p class="font-semibold">{{ materialRisks.length }} 道工序有物料风险 · 需在开工前完成备料</p>
      <ul class="grid gap-1 text-xs">
        <li v-for="risk in materialRisks" :key="`${risk.orderId}:${risk.operationId}`">
          {{ risk.orderId }} · {{ risk.operationId }} —
          <template v-if="risk.shortages.length">
            {{ risk.shortages.map((s) => `${s.materialId} 缺 ${s.shortageQuantity}`).join('、') }}
          </template>
          <template v-else>{{ risk.message }}</template>
        </li>
      </ul>
    </section>
    <!--
      设备数据风险横幅：「不知道」不等于「不可用」。无快照/快照过期的设备照排，
      但必须显式告诉规划员哪些工序的设备状态是盲区，开工前要人工确认。
    -->
    <section
      v-if="equipmentRisks.length"
      class="grid gap-1.5 rounded-md border border-border bg-muted/40 px-3 py-2.5 text-sm"
      data-testid="scheduling-equipment-risks"
    >
      <p class="font-semibold">
        {{ equipmentRisks.length }} 道工序的设备状态未知 · 开工前请人工确认设备可用
      </p>
      <ul class="grid gap-1 text-xs text-muted-foreground">
        <li v-for="risk in equipmentRisks" :key="`${risk.orderId}:${risk.operationId}`">
          {{ risk.orderId }} · {{ risk.operationId }} — {{ risk.message }}
        </li>
      </ul>
    </section>
    <div
      v-if="!model"
      class="flex min-h-48 items-center justify-center rounded-md border border-dashed text-sm text-muted-foreground"
    >
      选择待排工单并生成首版方案后开始编辑。
    </div>
    <NvTabs v-else v-model="view">
      <NvTabsList>
        <NvTabsTrigger value="gantt">工单甘特</NvTabsTrigger>
        <NvTabsTrigger value="resource">资源排产板</NvTabsTrigger>
        <NvTabsTrigger value="table">表格编辑</NvTabsTrigger>
      </NvTabsList>
      <SchedulingToolbar
        v-if="view !== 'table'"
        :scale="scale"
        :read-only="Boolean(readOnly)"
        :can-undo="false"
        :can-redo="false"
        :dirty="false"
        :busy="false"
        :can-edit="false"
        :can-repreview="false"
        :can-release="false"
        searchable
        :search="search"
        :match-count="searchMatches.length"
        :match-index="matchCursor"
        @scale-change="setScale"
        @zoom-in="zoom(-1)"
        @zoom-out="zoom(1)"
        @today="sendCommand({ kind: 'scrollToToday' })"
        @fit="sendCommand({ kind: 'fitToScreen' })"
        @update:search="search = $event"
        @search-prev="revealMatch(matchCursor - 2)"
        @search-next="revealMatch(matchCursor)"
      />
      <div class="flex flex-col gap-3 xl:flex-row">
        <div class="min-w-0 flex-1">
          <NvTabsContent value="gantt" class="h-[34rem] overflow-hidden rounded-md border">
            <GanttChart
              ref="ganttRef"
              :scale="scale"
              :model="model"
              @task-select="selectedTaskId = $event"
              :read-only="readOnly"
              @task-drag-end="emit('move', $event)"
              @locked-drag-attempt="emit('lockedAttempt', $event)"
            />
          </NvTabsContent>
          <NvTabsContent value="resource" class="h-[34rem] overflow-hidden rounded-md border">
            <ResourceSchedulerBoard
              ref="resourceRef"
              :scale="scale"
              :model="model"
              @task-select="selectedTaskId = $event"
              :read-only="readOnly"
              @task-drag-end="emit('move', $event)"
              @locked-drag-attempt="emit('lockedAttempt', $event)"
            />
          </NvTabsContent>
          <NvTabsContent value="table" class="max-h-[34rem] overflow-auto rounded-md border">
            <table class="w-full text-sm">
              <thead class="sticky top-0 z-10 bg-muted text-left [&_th]:whitespace-nowrap">
                <tr>
                  <th class="p-2">工单 / 工序</th>
                  <th class="p-2">实际排程段</th>
                  <th class="p-2">资源</th>
                  <th class="p-2">开始</th>
                  <th class="p-2">结束</th>
                  <th class="p-2">物料</th>
                  <th class="p-2">设备状态</th>
                  <th class="p-2">锁定</th>
                  <th class="p-2">待排</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="task in model.tasks.filter(
                    (item) => item.type === 'operation' && !item.blockKind,
                  )"
                  :key="task.id"
                  class="border-t"
                >
                  <td class="p-2 font-medium">{{ task.orderId }} · {{ task.operationId }}</td>
                  <td class="p-2">
                    <p
                      v-for="(segment, index) in task.segments"
                      :key="index"
                      class="whitespace-nowrap text-xs"
                    >
                      第 {{ index + 1 }} 段 · {{ formatDateTime(segment.startUtc) }} 至
                      {{ formatDateTime(segment.endUtc) }}
                    </p>
                    <p
                      v-if="(task.segments?.length ?? 0) > 1"
                      class="text-xs text-muted-foreground"
                    >
                      分段时间由重新排程确定；可使用草案锁定保留各段。
                    </p>
                    <span v-else class="text-xs text-muted-foreground">连续排程</span>
                  </td>
                  <td class="p-2">
                    <NvEntityPicker
                      class="min-w-40"
                      :disabled="readOnly || task.locked"
                      :model-value="task.resourceId"
                      :options="resourceOptions"
                      title="选择资源"
                      placeholder="选择资源"
                      empty-text="本排程没有可用资源"
                      :show-code="false"
                      :aria-label="`${task.orderId} · ${task.operationId} 的资源`"
                      @update:model-value="emit('update', task.id, { resourceId: $event })"
                    />
                  </td>
                  <td class="p-2">
                    <NvInput
                      class="h-8 min-w-48"
                      :disabled="readOnly || task.locked || (task.segments?.length ?? 0) > 1"
                      :model-value="task.startUtc"
                      @update:model-value="emit('update', task.id, { startUtc: String($event) })"
                    />
                  </td>
                  <td class="p-2">
                    <NvInput
                      class="h-8 min-w-48"
                      :disabled="readOnly || task.locked || (task.segments?.length ?? 0) > 1"
                      :model-value="task.endUtc"
                      @update:model-value="emit('update', task.id, { endUtc: String($event) })"
                    />
                  </td>
                  <td class="p-2">
                    <span
                      v-if="task.materialRisk"
                      class="inline-flex items-center rounded border border-warning/50 bg-warning/10 px-1.5 text-xs font-semibold text-warning"
                      :title="task.materialRisk.message"
                      >缺料待备</span
                    >
                    <span v-else class="text-xs text-muted-foreground">齐套</span>
                  </td>
                  <td class="p-2">
                    <span
                      v-if="task.equipmentRisk"
                      class="inline-flex items-center rounded border border-border bg-muted px-1.5 text-xs font-semibold text-muted-foreground"
                      :title="task.equipmentRisk.message"
                      >状态未知</span
                    >
                    <span v-else class="text-xs text-muted-foreground">正常</span>
                  </td>
                  <td class="p-2">
                    <div class="flex flex-wrap items-center gap-1.5">
                      <NvButton
                        size="sm"
                        :variant="task.locked ? 'secondary' : 'outline'"
                        type="button"
                        :disabled="readOnly"
                        @click="emit('lock', task.id, !task.locked)"
                        >{{ task.locked ? '解锁' : '锁定' }}</NvButton
                      >
                      <NvButton
                        size="sm"
                        variant="outline"
                        type="button"
                        :disabled="
                          readOnly ||
                          persistPending ||
                          !task.resourceId ||
                          (task.segments?.length ?? 0) > 1
                        "
                        :title="
                          (task.segments?.length ?? 0) > 1
                            ? '多段工序请使用草案锁定保留各段；暂不支持持久锁定'
                            : task.resourceId
                              ? '固定该工序的资源与起止时间，之后重新排程也保持不变'
                              : '该工序未分配资源，先指定资源再持久锁定'
                        "
                        @click="emit('persistOverride', task.id)"
                        >持久锁定</NvButton
                      >
                      <NvStatusBadge
                        v-if="
                          persistedOperationKeys?.includes(`${task.orderId}:${task.operationId}`)
                        "
                        label="已持久锁定"
                        tone="success"
                        title="该工序的资源与起止时间已固定，之后重新排程也保持不变。刷新页面后此标记不再显示，但锁定仍然有效。"
                      />
                    </div>
                  </td>
                  <td class="p-2">
                    <NvButton
                      size="sm"
                      variant="ghost"
                      type="button"
                      :disabled="readOnly || task.locked"
                      @click="emit('moveToPending', task.id)"
                      >移回待排</NvButton
                    >
                  </td>
                </tr>
              </tbody>
            </table>
          </NvTabsContent>
        </div>
        <aside
          v-if="selectedTask"
          class="max-h-[34rem] overflow-y-auto rounded-md border xl:w-[21rem] xl:flex-none"
          data-testid="scheduling-draft-task-detail"
          :aria-label="detailTitle"
        >
          <div class="flex items-center justify-between gap-2 px-4 pt-3">
            <h3 class="text-sm font-semibold">{{ detailTitle }}</h3>
            <NvButton
              size="sm"
              variant="ghost"
              type="button"
              :aria-label="`关闭${detailTitle}`"
              @click="selectedTaskId = ''"
              >关闭</NvButton
            >
          </div>
          <TaskDetailPanel
            :task="selectedTask"
            :read-only="readOnly"
            @toggle-lock="(taskId, locked) => emit('lock', taskId, locked)"
          />
        </aside>
      </div>
      <SchedulingLegend
        v-if="view !== 'table'"
        :model="model"
        :scale="scale"
        :view="view === 'gantt' ? 'order' : 'resource'"
        :categories="legendCategories"
      />
    </NvTabs>
  </section>
</template>
